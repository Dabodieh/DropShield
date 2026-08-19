# Observability

DropShield exposes an in-process, bounded operational snapshot without changing any
forwarding or policy decision. No external collector, dashboard, database, Prometheus server,
Grafana deployment, or OpenTelemetry pipeline is involved — `System.Diagnostics.Metrics` and
an exporter pipeline were deliberately left out; the current requirement is a small structured
snapshot, and the internal service can feed a standard telemetry adapter later without
changing this contract.

```text
known commerce request
        ↓
metrics middleware starts a timer, records incoming
        ↓
rate limiter
   ├─ rejected → record 429 and rejection category
   └─ allowed  → forwarder records origin attempt, latency, failure
        ↓
metrics middleware records final status and DropShield-only latency
```

## Endpoint and lifecycle

`GET /internal/metrics` returns the current snapshot; `POST /internal/metrics/reset` clears
counters, histograms, and the rolling window while preserving the process start time. Both
return HTTP 404 outside Development/Testing or when `DropShield:InternalMetrics:Enabled` is
false — they have no authentication and are internal diagnostics, not a production management
surface. Metrics start at zero per process and are lost on restart. `/health` and the metrics
endpoint itself are excluded from the commerce counters they report.

## Response shape

```json
{
  "startedAt": "2026-08-17T10:00:00+00:00",
  "collectionStartedAt": "2026-08-17T10:00:00+00:00",
  "uptimeSeconds": 42,
  "traffic": {},
  "rateLimitReasons": {},
  "admission": {},
  "admissionTokens": {},
  "actionProofs": {},
  "statusCodes": {},
  "latencyMilliseconds": {},
  "recentRates": {},
  "protectedStock": {},
  "routes": {}
}
```

- `traffic`, `protectedStock`, and each route's object: `incoming`, `forwarded`,
  `rateLimited`, `upstreamFailures`, `internalFailures`, `stateFailures`,
  `forwardingPercentage`, `rejectionPercentage`, `originTrafficReductionPercentage`.
  `originTrafficReductionPercentage` covers everything not forwarded (rate limits, waiting,
  bounded overflow, state failures); `rejectionPercentage` is specifically the 429 share. A
  forwarded request that later fails at the origin is still counted forwarded and separately
  as an upstream failure.
- `admission`: `admitted`, `waiting`, `queueFull` — request decisions, not unique sessions or
  distributed queue depth.
- `admissionTokens`: `issued`, `validations`, `validationFailures`, `expired`.
- `actionProofs`: cart/checkout issuance, validation/failure totals, consumption, replay
  rejection, replay-state unavailability.
- Reservation counters (see [transaction protection](transaction-protection.md)): created,
  reused, released, expired, committed, out-of-stock-rejected, state failures.
- Behavioural counters (see [behavioural scoring](behavioural-scoring.md)): observation,
  score-band, restriction, state-failure counts.

None of these carry client identifiers, session/action bindings, token values, or request
payloads.

### Fixed route categories

| Key | Template |
|---|---|
| `products` | `GET /api/products` |
| `product` | `GET /api/products/{productId}` |
| `stock` | `GET /api/products/{productId}/stock` |
| `cart` | `POST /api/cart` |
| `checkout` | `POST /api/checkout` |
| `actionProof` | `POST /api/action-proofs/{action}` |
| `graphqlCartAdd` | `POST /graphql` |
| `storefrontCartAdd` | `POST /checkout/cart/add` |
| `commerceRestCart` | `POST /rest[/default]/V1/guest-carts/{cartId}/items` |
| `commerceRestCheckout` | `POST /rest[/default]/V1/guest-carts/{cartId}/payment-information` |

Arbitrary paths, product IDs, client IDs, and headers never become metric labels.
`protectedStock` is one fixed aggregate for `DropShield:ProtectedProducts` members, not a
per-SKU label.

### Status outcomes

Global and per-route: `success2xx`, `clientError4xx`, `rateLimited429`, `serverError5xx`,
`badGateway502`, `other` (429/502 are subsets of the 4xx/5xx totals).

### Latency

`count`, `average`, `p50`, `p95`, `p99` in milliseconds for `endToEnd`, `origin`, and
`dropShieldProcessing` (end-to-end minus origin). Percentiles come from a 24-bucket bounded
histogram (0.1 ms–10 s plus overflow) — bucket-boundary estimates, not exact raw-sample
percentiles, so memory doesn't grow with traffic.

### Rate-limit attribution

- `perClient` — cart/checkout rejections, exactly attributed (no aggregate limiter on those
  routes).
- `protectedStockChained` — a protected-stock rejection from the InMemory per-client+aggregate
  chain. ASP.NET Core's chained rejection lease doesn't identify which child limiter rejected
  the request, so this stays an honest combined category rather than a guess.
- `aggregate` — a Redis-mode aggregate rejection, exactly attributed.
- `unattributed` — reserved for a future rejection type that doesn't fit the others.

### Recent rates

`incomingPerSecond`, `forwardedPerSecond`, `rateLimitedPerSecond` over a fixed ten-second
rolling window (ten reusable one-second buckets behind a short critical section); `sampleSeconds`
reports how much of the window has elapsed since start or reset.

## Logging

Successful forwarding and rate limiting log at Debug with method, fixed route category, and
(for rejections) attribution category. Origin connection failure/timeout logs at Warning;
unexpected DropShield failures at Error. High-frequency success/rejection events are not
logged at Information. No client IPs, identities, headers, cookies, bodies, credentials, or
raw paths appear in logs.

## Limits

Metrics are single-instance, in memory, and reset on restart — there is no distributed
aggregation or durable audit record. Counters use atomic operations; the rolling window uses a
fixed bounded structure. Snapshots are assembled while traffic continues, so closely related
values can differ slightly under heavy concurrency. Reset is for controlled demonstrations and
isn't transactionally coordinated with in-flight requests.
