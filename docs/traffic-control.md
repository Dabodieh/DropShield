# Traffic control and admission

DropShield sits in front of `DropShield.DemoStore` and applies, in order: per-client and
aggregate rate limits, then (when enabled for a drop) waiting-room admission with signed
proof.

```text
protected request
        ↓
per-client rate policy ── excessive polling → HTTP 429
        ↓
admission state (if enabled for this drop)
   ├─ capacity/batch available → origin
   ├─ eligible but not yet admitted → HTTP 202 waiting
   └─ bounded room full/state unavailable → HTTP 503
        ↓
admission-proof check (if a token is present)
```

## Protected routes

DropShield forwards only:

- `GET /api/products`
- `GET /api/products/{productId}`
- `GET /api/products/{productId}/stock`
- `POST /api/cart`
- `POST /api/checkout`
- `POST /api/action-proofs/cart`
- `POST /api/action-proofs/checkout`
- `POST /graphql`
- `POST /checkout/cart/add`

In `OriginMode=AdobeCommerce`, the narrow profile additionally forwards only `POST`
`/rest[/default]/V1/guest-carts/{cartId}/items` and
`/rest[/default]/V1/guest-carts/{cartId}/payment-information`; other Commerce REST paths are
not proxied.

It is not a generic HTTP proxy. Catalogue browsing is forwarded without rate limiting. Stock,
cart, and checkout policy applies only when the authenticated Commerce protection-manifest cache
resolves the SKU or product entity ID. `ProtectedProducts` remains DemoStore-only synthetic
configuration; it is not authoritative in Adobe Commerce mode.
`POST /graphql` is a shared endpoint: DropShield inspects its JSON envelope only to identify a
protected `addSimpleProductsToCart`, `addVirtualProductsToCart`, or `addProductsToCart` mutation
for the configured drop, while ordinary GraphQL traffic remains outside the protected mutation
pipeline.

## Rate limiting

Two partitioned fixed-window limiters, chained: a per-client limiter for protected stock,
cart, and checkout, and one aggregate partition for protected stock across all clients (used
only when admission is disabled for that drop — see below). Each limiter has `QueueLimit`
zero, so denied requests return immediately rather than queueing inside DropShield.

Default `appsettings.json` values:

| Policy | Per-client allowance | Aggregate allowance |
|---|---:|---:|
| Protected stock | 5 requests / 1 second | 200 requests / 1 second |
| Cart | 2 requests / 1 second | None |
| Checkout | 1 request / 5 seconds | None |

Fixed windows can permit short bursts at a window reset. These are demonstration settings,
not production values.

A denied request receives HTTP 429, `Retry-After`, and:

```json
{ "error": "rate_limited", "message": "Too many requests. Please try again shortly." }
```

An unavailable or timed-out DemoStore returns HTTP 502 `upstream_unavailable`; it is never
reported as HTTP 429.

### Client partitioning

For controlled load tests, k6 sends a stable per-VU `X-DropShield-Test-Client` value.
DropShield trusts this header only when explicitly enabled and the environment is
`Development` or `Testing`; it is never forwarded to DemoStore. Otherwise DropShield
partitions by source IP. IP address alone is not a sufficient identity mechanism for
production bot mitigation — this is test instrumentation, not authentication.

## Distributed state (Redis)

`DropShield:StateProvider` selects `InMemory` (the default, using the built-in ASP.NET Core
chained limiter) or `Redis` (a focused `IDistributedTrafficState` evaluator using
StackExchange.Redis and one atomic Lua fixed-window script). Both paths share the same
thresholds, windows, evaluation order, and response contract.

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

The committed default is `InMemory`; the committed HMAC key is empty. Redis mode requires at
least 32 characters supplied through environment or secret configuration, and only accepts
`localhost`, `127.0.0.1`, `::1`, or `host.docker.internal` endpoints.

The Lua script reads Redis server time, resets or increments the window hash, sets expiry,
and compares against the permit limit — one atomic round trip, avoiding a `GET`/increment/`SET`
race. Redis key shapes:

```text
{prefix}:rate:stock:aggregate
{prefix}:rate:stock:client:{hmac-sha256}
{prefix}:rate:cart:client:{hmac-sha256}
{prefix}:rate:checkout:client:{hmac-sha256}
```

The HMAC input is the internal client partition value; raw IPs and test identities never
enter Redis keys. Each value is a two-field hash (`window`, `count`) with TTL set to the
window remainder plus a one-second allowance, so cardinality stays bounded by active
partitions.

Redis mode is fail-closed: an unavailable dependency returns HTTP 503 `state_unavailable`
rather than forwarding the request or silently falling back to a weaker local limit.
StackExchange.Redis uses `AbortOnConnectFail=false` so the connection can reconnect in the
background, but policy calls stay fail-closed until Redis answers.

## Waiting-room admission

Rate limiting alone can't distinguish an eligible shopper from an aggressive client once the
shared allowance is exhausted. When admission is enabled for a drop, it replaces the
protected-stock aggregate window with a bounded active/waiting capacity decision, so eligible
excess sessions wait instead of being rejected outright.

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

Admission applies to `GET /api/products/{configured-drop}/stock`, which doubles as both entry
and status poll — there's no separate queue-status route. A waiting response is HTTP 202 and
never forwarded:

```json
{ "status": "waiting", "drop": "pokemon-etb", "retryAfterSeconds": 5 }
```

The payload deliberately omits position, estimated wait, and identity. A full bounded waiting
set returns HTTP 503 `waiting_room_full`; state unavailability returns the same
`state_unavailable` contract as rate limiting. Neither falls back to local state in Redis
mode.

DropShield issues a random 256-bit `DropShield.Session` cookie (HttpOnly, SameSite=Lax,
Secure on HTTPS and outside Development/Testing) as an opaque lookup key — it carries no admission claim and can be copied or
replayed on its own; server-side state and the signed proof below are what actually gate
access.

### Redis admission model

Each drop uses five co-located keys (`{...}` brace-scoping keeps them in one Redis Cluster
hash slot):

```text
{prefix}:admission:{drop}:active
{prefix}:admission:{drop}:waiting:order
{prefix}:admission:{drop}:waiting:expiry
{prefix}:admission:{drop}:sequence
{prefix}:admission:{drop}:batch
```

Sorted-set members are HMAC-SHA256 derivatives of the session ID — no raw cookies, IPs, or
customer data. One Lua operation, atomically: prunes expired active/waiting members, refreshes
an already-active session, admits a new session only within the waiting bound while
preserving FIFO order, resets or increments the batch window, and promotes the next eligible
waiter when active capacity and batch allowance permit. This is best-effort FIFO among live
pollers, not a strict fairness guarantee — clock, disconnect, and retry behavior make an exact
position promise inappropriate.

## Signed admission proof

An opaque session cookie identifies state but isn't proof the browser was actually admitted.
Once admission evaluation succeeds, DropShield issues a compact HMAC-SHA256 token as
browser-held, locally verifiable proof, carried in a dedicated `DropShield.Admission`
HttpOnly cookie (separate from `DropShield.Session`; SameSite=Lax, Secure on HTTPS and outside
Development/Testing, scoped to `/` because action-proof, GraphQL, and storefront mutation routes
have no narrower common browser path).

Format: `v1.<base64url UTF-8 JSON payload>.<base64url HMAC>`. The HMAC covers the literal
`v1.payload` prefix and payload. Claims are limited to `v` (version), `kid` (signing-key ID,
for future rotation), `drop`, `session` (HMAC-derived session binding), `iat`, `exp`. No raw
cookies, IPs, or customer data.

Missing proof falls through to admission evaluation, so a newly admitted session receives its
first token. Malformed, tampered, expired, wrong-drop, wrong-session, unsupported-version, or
unknown-key proof returns HTTP 403 `admission_required` and is never forwarded; the cookie is
deleted on that response. A *valid* token still re-evaluates server-side admission state
before forwarding — a signature alone can't outlive the active-session lease, and the
re-evaluation also refreshes it. This live check runs for every protected path that a token
gates: the stock route (`AdmissionControlMiddleware`), and cart/checkout/action-proof issuance
(`AdmissionProofAuthorizer`, used by `ActionProofMiddleware` and the `/api/action-proofs/*`
endpoints) — a token's own signature/expiry is never sufficient on its own for a mutating
request.

Uses .NET `HMACSHA256`, UTF-8, Base64Url, `TimeProvider` for UTC timestamps, and
`CryptographicOperations.FixedTimeEquals` for signature and session-binding comparisons — no
JWT/OIDC, no custom ciphers. The session binding is a domain-separated HMAC derivation of the
opaque session identifier, so copying a token into another session fails validation, and the
signed `drop` claim prevents cross-drop reuse.

Token lifetime (60 seconds in the PoC config) is validated at startup not to exceed the
admission session TTL. Tokens are intentionally reusable during that window — one-time
consumption is a property of action proof (see [transaction protection](transaction-protection.md)),
not admission proof.

### Key management

```powershell
$env:DropShield__AdmissionTokens__KeyId = '2026-08-primary'
$env:DropShield__AdmissionTokens__SigningKey = [Convert]::ToBase64String(
    [Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

Production and Redis/multi-instance deployments require an explicit shared Base64 key of at
least 32 random bytes at startup — missing or weak material fails options validation before
the app starts. Development/Testing with `InMemory` state generates an ephemeral 32-byte
startup key when none is configured (never logged); tokens then invalidate on restart and
can't be verified across instances. This is a local-development convenience only.

`kid` exists for future key rotation; only one active verification key is supported today.

## Threat model and limits

Signed proof makes client-side changes to admission claims, expiry, drop scope, or a copied
token in another session ineffective, and it rejects expired proof before it reaches the
origin. It does not mitigate malware stealing a whole active browser session, bot
classification, account farming, residential proxies, cart-action replay, purchase-limit
circumvention, or inventory hoarding after valid admission — see
[transaction protection](transaction-protection.md) and
[behavioural scoring](behavioural-scoring.md) for those.

All limiter, admission, and rate state is aggregate and per-window; none of it stores request
bodies, cookies, tokens, or customer data. See [observability](observability.md) for how
outcomes are measured, and [benchmarks](benchmarks.md) for measured before/after throughput.
