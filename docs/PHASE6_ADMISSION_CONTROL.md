# Phase 6 — Virtual Waiting Room / Admission Control

Phase 6 separates abusive request rate from origin shopper capacity.

```text
protected stock request
        ↓
per-client rate policy
   ├─ excessive polling → HTTP 429
   └─ eligible request
              ↓
        admission state
   ├─ capacity/batch available → origin
   ├─ eligible but not yet admitted → HTTP 202 waiting
   └─ bounded room full/state unavailable → HTTP 503
```

No queue position, HTML waiting-room UI, signed admission token, reservation, bot score, or ecommerce integration is introduced.

## Configuration

The committed PoC values are illustrative local settings:

```json
{
  "DropShield": {
    "Admission": {
      "Enabled": true,
      "ProtectedProduct": "pokemon-etb",
      "MaximumActiveSessions": 200,
      "AdmissionBatchSize": 20,
      "MaximumWaitingSessions": 2000,
      "SessionTtlSeconds": 300,
      "WaitingTtlSeconds": 600,
      "RetryAfterSeconds": 5
    }
  }
}
```

The protected product must already appear in `DropShield:ProtectedProducts`. Batch size cannot exceed active capacity. Capacities, TTLs, and retry intervals are startup-validated. These values are not recommendations or claims about Hamleys or another retailer.

## Request and response behavior

Admission applies only to `GET /api/products/{configured-drop}/stock`. The same route is both the entry request and polling request; no separate status endpoint is needed.

An admitted request receives the unchanged DemoStore response. A waiting request receives HTTP 202 and `Retry-After`:

```json
{
  "status": "waiting",
  "drop": "pokemon-etb",
  "retryAfterSeconds": 5
}
```

The payload deliberately omits position, estimated wait, queue size, and customer identity. A waiting request is never forwarded.

If the bounded waiting set is full, a new session receives HTTP 503 `waiting_room_full`. If required Redis state is unavailable, the existing fail-closed HTTP 503 `state_unavailable` contract applies. Neither case falls back to local state in Redis mode.

## Rate and admission separation

Per-client protected-stock, cart, and checkout limits remain rate controls. Aggressive polling still produces the Phase 3 HTTP 429 body.

When admission is active for the configured drop, the protected-stock aggregate fixed window is bypassed for that request. `MaximumActiveSessions` and `AdmissionBatchSize` become the origin-capacity mechanism. When admission is disabled, the original aggregate stock limiter remains unchanged.

This prevents a legitimate session from being rejected by an aggregate limiter before the waiting-room decision can be made.

## Session identity

DropShield creates a random 256-bit lowercase hexadecimal `DropShield.Session` cookie. It is:

- HttpOnly;
- SameSite=Lax;
- scoped to `/`;
- marked Secure for HTTPS requests;
- bounded by the longer configured admission TTL.

The cookie is only an opaque lookup identifier. It does not contain an admission claim, customer identity, token, queue position, or expiry. It is deliberately unsigned in Phase 6 and can therefore be copied or replayed; Phase 7 owns signed admission proof.

## Provider behavior

`StateProvider: InMemory` uses one locked bounded state structure and is appropriate only for one-process local development.

`StateProvider: Redis` uses the existing singleton connection and HMAC key. Multiple DropShield instances share the same active capacity, waiting order, waiting expiry, and batch allowance.

## Redis model and atomicity

Each configured drop uses five co-located namespaced keys:

```text
{prefix}:admission:{drop}:active
{prefix}:admission:{drop}:waiting:order
{prefix}:admission:{drop}:waiting:expiry
{prefix}:admission:{drop}:sequence
{prefix}:admission:{drop}:batch
```

Braces keep the keys in one Redis Cluster hash slot for a future clustered deployment. Sorted-set members are HMAC-SHA256 derivatives of the opaque session ID. Raw cookies, IPs, synthetic identities, headers, bodies, authentication data, carts, payments, and customer data are not stored.

One Lua operation uses Redis server time and atomically:

1. prunes expired active and waiting members;
2. refreshes an already-active session;
3. adds a new session only if the waiting bound permits it;
4. preserves its original FIFO sequence while polls refresh waiting expiry;
5. resets or increments the fixed admission-batch window;
6. checks active capacity, batch allowance, and waiting rank;
7. moves the eligible current session from waiting to active.

Only the polling session is promoted; DropShield does not reserve active slots for disconnected waiters. Concurrent instances cannot exceed the shared active or batch limits through a read/modify/write race.

## Expiry and progression

Active entries use a sliding `SessionTtlSeconds` refreshed by admitted protected-stock requests. Waiting entries preserve join order but refresh `WaitingTtlSeconds` on a valid poll. Abandoned entries are pruned by score and all collection keys have bounded TTLs.

Up to `AdmissionBatchSize` sessions can enter during each `RetryAfterSeconds` fixed interval, subject to `MaximumActiveSessions`. Once an active session expires and a new batch interval begins, eligible live waiters can be progressively admitted as they poll.

This is best-effort FIFO, not a strict fairness or exact-position service. Clock, disconnect, retry, and network behavior make such promises inappropriate for this phase.

## Observability

Phase 4 metrics add fixed per-instance admission counters:

- `admitted` — admission decisions allowed to continue;
- `waiting` — HTTP 202 waiting decisions;
- `queueFull` — bounded overflow decisions.

They count request decisions, not unique sessions or distributed queue depth. State failures remain in `stateFailures`. Metrics never expose session identifiers or queue contents.

## Limitations and future boundary

- Unsigned session cookies are replayable and are not proof of admission.
- InMemory admission is not consistent across application instances.
- Redis deployment, credential lifecycle, clustering, TLS, and multi-region behavior are not productionized.
- Configuration changes require restart and must be consistent across instances.
- Admission protects the configured stock route only; cart/checkout admission continuity is future work.
- No position, wait-time estimate, UI, early disconnect signal, explicit leave operation, or administrative drop lifecycle exists.
- Phase 7 signed admission, Phase 8 replay protection, Phase 9 reservation, Phase 10 bot scoring, and Phase 11 commerce integration remain unimplemented.

References: [Redis `ZRANK`](https://redis.io/docs/latest/commands/zrank/), [Redis key expiry](https://redis.io/docs/latest/commands/expire/), and [StackExchange.Redis scripting](https://stackexchange.github.io/StackExchange.Redis/Scripting.html).
