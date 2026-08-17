# Phase 5 — Distributed State / Redis

Phase 5 removes the single-process assumption from traffic-policy state while preserving the existing local path.

```text
InMemory mode
client → DropShield instance → ASP.NET Core fixed-window limiter → DemoStore

Redis mode
                         ┌─ DropShield A ─┐
client / load balancer ──┼─ DropShield B ─┼→ shared Redis windows → DemoStore
                         └─ DropShield C ─┘
```

This phase added shared rate-window state, not a customer-facing protection feature. Phase 6 subsequently reuses the same provider selection and Redis connection for bounded admission state.

## Provider selection

`DropShield:StateProvider` accepts:

- `InMemory` — the unchanged Phase 3 ASP.NET Core chained fixed-window limiter. Redis is neither constructed nor required.
- `Redis` — a focused DropShield evaluator backed by `IDistributedTrafficState` and StackExchange.Redis 3.1.0.

Both paths reuse the same protected-product list, policy enablement, permit limits, windows, client partition provider, evaluation order, JSON 429 body, and `Retry-After` behavior.

Example Redis configuration:

```json
{
  "DropShield": {
    "StateProvider": "Redis",
    "Redis": {
      "ConnectionString": "127.0.0.1:6379",
      "Database": 0,
      "KeyPrefix": "dropshield:v1",
      "IdentityHashKey": "provided-through-secret-configuration",
      "ConnectTimeoutMilliseconds": 1000,
      "OperationTimeoutMilliseconds": 1000
    }
  }
}
```

The committed default remains `InMemory`, and the committed HMAC key is empty. Redis mode requires at least 32 characters supplied through environment or secret configuration.

## Distributed policy scope

Redis mode shares all Phase 3 rate state:

1. protected-stock per-client fixed window;
2. protected-stock aggregate fixed window;
3. cart per-client fixed window;
4. checkout per-client fixed window.

Catalogue browsing and unprotected-product stock routes remain unlimited as before. Each protected-stock request acquires its client window first and then its aggregate window, matching the existing chained evaluation order.

## Atomic fixed windows

`IDistributedTrafficState.TryAcquireAsync` accepts a fixed policy/scope request and returns an acquired decision plus retry duration. The Redis implementation evaluates one Lua script atomically:

1. read Redis server time;
2. calculate the current fixed-window ID;
3. reset the stored hash when its window ID changes, or atomically increment its count;
4. set key expiry;
5. compare the count with the configured permit limit;
6. return the decision and time remaining in the window.

Using Redis server time avoids splitting windows because application-instance clocks differ. Lua avoids the unsafe `GET → local increment → SET` race. StackExchange.Redis caches the script and normally uses `EVALSHA` after first evaluation.

The per-client and aggregate acquisitions are separate atomic operations. If the client acquisition succeeds but the aggregate acquisition fails, the client count remains consumed for that window, consistent with the conservative behavior of the existing chained limiter.

## Keys, identity, and expiry

Key shapes are fixed:

```text
{prefix}:rate:stock:aggregate
{prefix}:rate:stock:client:{hmac-sha256}
{prefix}:rate:cart:client:{hmac-sha256}
{prefix}:rate:checkout:client:{hmac-sha256}
```

The HMAC input is the existing internal client partition (`test:...` in controlled tests or `ip:...` fallback). The configured HMAC key derives a deterministic SHA-256 digest before Redis sees the key. Raw source IPs and synthetic test identities are not stored in Redis keys or values.

Each Redis value is a two-field hash containing only `window` and `count`. Its TTL is the current window remainder plus a one-second cleanup allowance. Repeated requests update the TTL to the same approximate window-end boundary rather than extending state indefinitely. Cardinality is therefore bounded by active policy/client partitions.

Changing `IdentityHashKey` changes per-client partitions. Production secret rotation would need a coordinated strategy and is not designed in this PoC.

## Local endpoint validation

Redis mode accepts only endpoints using:

- `localhost`;
- `127.0.0.1` or another loopback representation;
- `::1`;
- `host.docker.internal`.

External Redis endpoints fail startup validation in the current PoC. Connection strings may be supplied through environment variables, but credentials must not be committed or logged. Key prefixes, database numbers, HMAC-key length, and timeouts are also validated.

## Failure behavior

Redis mode is fail-closed:

```text
Redis policy state unavailable
        ↓
HTTP 503
{
  "error": "state_unavailable",
  "message": "Traffic policy state is temporarily unavailable."
}
```

The request is not forwarded and is not mislabeled as HTTP 429 or an origin failure. There is no automatic fallback to the local limiter because that would silently multiply allowances across instances. StackExchange.Redis is configured with `AbortOnConnectFail=false` so its singleton connection can reconnect in the background, but policy calls remain fail-closed until Redis is available.

Warnings contain only method and fixed route category; connection strings, Redis keys, client identities, and credentials are excluded.

## Health

`GET /health` remains small:

```json
{
  "status": "healthy",
  "service": "DropShield.Api",
  "stateProvider": "InMemory",
  "state": "available"
}
```

- InMemory returns HTTP 200 without contacting Redis.
- Available Redis returns HTTP 200 with `stateProvider: Redis` and `state: available`.
- Unavailable required Redis returns HTTP 503 with `status: unhealthy` and `state: unavailable`.

## Observability distinction

Redis holds enforcement state only. Phase 4 counters, status outcomes, latency histograms, and rolling rates remain per-instance and in memory.

Phase 5 adds `stateFailures` to aggregate/fixed-route traffic counters. Redis-mode rejections can be attributed exactly as `perClient` or `aggregate`; InMemory protected-stock rejections retain the honest `protectedStockChained` category because the built-in chained lease does not identify its rejecting child.

The project does not implement distributed metrics aggregation, durable telemetry, Prometheus, or a dashboard.

## Local development

Start one disposable Redis container bound only to loopback:

```powershell
docker run --rm --name dropshield-redis-phase5 -p 127.0.0.1:6379:6379 redis:8.8.1-alpine
```

In the DropShield terminal, create an ephemeral development HMAC key and enable Redis mode:

```powershell
$env:DropShield__StateProvider = 'Redis'
$env:DropShield__Redis__ConnectionString = '127.0.0.1:6379'
$env:DropShield__Redis__IdentityHashKey = [Convert]::ToHexString(
    [Security.Cryptography.RandomNumberGenerator]::GetBytes(32))

dotnet run --project src/DropShield.Api
```

Stop the API and Redis container after the demonstration. The default InMemory workflow requires none of these settings.

## Verification and limitations

Phase-gate verification uses one small Redis container and a focused test that creates two independent Redis state clients against the same namespace. Concurrent operations prove one combined allowance and verify TTL cleanup. Separate HTTP integration tests use two gateway instances sharing a deterministic fake implementation so forwarding and 429 behavior are exercised without making the ordinary suite depend on Redis.

Known limitations:

- Redis is a single required dependency in Redis mode; cluster, sentinel, TLS, and managed-service deployment are not exercised.
- Connection credentials and HMAC key lifecycle are deployment responsibilities.
- No multi-region clock/network behavior is modeled.
- Policy configuration must remain consistent across instances.
- Metrics remain per instance.
- Redis key cleanup is expiry-based, not an audit history.
- No performance benchmark was run; historical Phase 2/3 evidence remains unchanged.

References: [StackExchange.Redis configuration](https://stackexchange.github.io/StackExchange.Redis/Configuration), [StackExchange.Redis scripting](https://stackexchange.github.io/StackExchange.Redis/Scripting.html), and the [official Redis Docker image](https://hub.docker.com/_/redis).
