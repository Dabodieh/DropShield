# Phase 4 — Observability

Phase 4 makes the existing DropShield gateway observable without changing its Phase 3 forwarding or rate-limit decisions. Instrumentation is in-process, bounded, and available through the controlled internal JSON endpoint.

```text
known commerce request
        ↓
metrics middleware starts end-to-end timer and records incoming
        ↓
unchanged Phase 3 chained rate limiter
        ├─ rejected → record 429 and rejection category
        └─ allowed  → forwarder records origin attempt, latency, and failure
        ↓
metrics middleware records final status and DropShield-only latency
```

No external collector, dashboard, database, Prometheus server, Grafana deployment, or OpenTelemetry pipeline is introduced.

## Endpoint and lifecycle

`GET /internal/metrics` returns the current in-memory snapshot. `POST /internal/metrics/reset` clears collection counters, histograms, and the rolling window while preserving the application instance start time.

Both endpoints are available only when `DropShield:InternalMetrics:Enabled` is true and the environment is Development or Testing. They return HTTP 404 in Production or when disabled. They have no authentication and are therefore internal PoC diagnostics, not a production management surface.

Metrics start at zero for every application process. Restarting the process loses all counters, latency history, and rolling-rate history. Resetting changes `collectionStartedAt` but not `startedAt` or application uptime.

The endpoint itself and `/health` are excluded from commerce traffic counters, so reading metrics does not inflate them.

## Response schema

The response has this stable top-level shape:

```json
{
  "startedAt": "2026-08-17T10:00:00+00:00",
  "collectionStartedAt": "2026-08-17T10:00:00+00:00",
  "uptimeSeconds": 42,
  "traffic": {},
  "rateLimitReasons": {},
  "admission": {},
  "admissionTokens": {},
  "statusCodes": {},
  "latencyMilliseconds": {},
  "recentRates": {},
  "protectedStock": {},
  "routes": {}
}
```

Phase 6 adds fixed `admission` request-decision counters: `admitted`, `waiting`, and `queueFull`. These are per-instance operational observations, not distributed queue depth or unique-session counts. They contain no session identifiers or queue positions.

Phase 7 adds fixed `admissionTokens` counters: `issued`, `validations`, `validationFailures`, and `expired`. They have no per-session, per-key, token-value, or validation-reason labels.

`traffic`, `protectedStock`, and each route's `traffic` object expose:

- `incoming`
- `forwarded`
- `rateLimited`
- `upstreamFailures`
- `internalFailures`
- `stateFailures` — shared policy-state dependency failures added in Phase 5;
- `forwardingPercentage`
- `rejectionPercentage`
- `originTrafficReductionPercentage`

Origin traffic reduction is the percentage of recognized incoming commerce requests not forwarded. From Phase 6 this includes waiting and bounded-overflow decisions as well as rate limits and state failures. `rejectionPercentage` remains specifically the HTTP 429 rate-limit share. A forwarded request that later fails at the origin remains forwarded and is also counted as an upstream failure.

`protectedStock` is a fixed aggregate for requests whose product ID is in `DropShield:ProtectedProducts`. It does not create a label for each SKU.

## Fixed route categories

Only these categories and templates are retained:

| Key | Template |
|---|---|
| `products` | `GET /api/products` |
| `product` | `GET /api/products/{productId}` |
| `stock` | `GET /api/products/{productId}/stock` |
| `cart` | `POST /api/cart` |
| `checkout` | `POST /api/checkout` |

Arbitrary URL paths, product IDs, client IDs, and headers never become metric labels. Unknown and internal endpoints are not included in the commerce snapshot.

## Status outcomes

Global and per-route status counters expose:

- `success2xx`
- `clientError4xx`
- `rateLimited429`
- `serverError5xx`
- `badGateway502`
- `other`

The specific 429 and 502 counters are subsets of their corresponding 4xx and 5xx categories.

## Latency measurement

Global latency summaries use milliseconds and expose `count`, `average`, `p50`, `p95`, and `p99` for:

- `endToEnd` — time inside the gateway from the metrics middleware until the response path completes;
- `origin` — time spent sending the allowed request to DemoStore and copying its response, or waiting for a failed/timed-out origin attempt;
- `dropShieldProcessing` — end-to-end time minus the recorded origin duration.

Average uses accumulated observed duration. Percentiles are bounded-histogram estimates using 24 fixed buckets from 0.1 ms through 10 seconds plus an overflow bucket. No individual request samples are stored, so memory does not grow with traffic. Percentiles are bucket upper-bound estimates rather than exact raw-sample percentiles.

## Rate-limit reason attribution

The response exposes:

- `perClient` — accurately attributed cart and checkout rejections, because those routes have no aggregate limiter;
- `protectedStockChained` — a protected-stock rejection from the existing per-client-plus-aggregate chain;
- `aggregate` — an exactly attributed Redis-mode aggregate rejection added in Phase 5;
- `unattributed` — any future rejection that cannot safely fit the other categories.

ASP.NET Core's chained rejection lease supplies retry metadata but does not identify which child limiter rejected a protected-stock request. Phase 4 therefore does not guess whether an individual protected-stock 429 came from the per-client or aggregate child. Obtaining perfect attribution would require replacing or materially restructuring the working Phase 3 chain, which is outside this phase.

## Recent rates

`recentRates` exposes `incomingPerSecond`, `forwardedPerSecond`, and `rateLimitedPerSecond` over a fixed ten-second in-memory rolling window. `sampleSeconds` reports how much of that window has elapsed since process start or the last reset.

The implementation uses ten reusable one-second buckets behind a short critical section. Memory is fixed, history expires automatically, and no request-level history is retained.

## Structured logging

- successful origin forwarding: Debug, using method, fixed route category, and origin status;
- rate limiting: Debug, using method, fixed route category, and safe attribution category;
- origin connection failure or timeout: Warning;
- unexpected DropShield processing failure: Error.

High-frequency success and rejection events are not logged at Information level. Logs do not include client IPs, synthetic identities, headers, cookies, bodies, credentials, or arbitrary raw paths.

## Privacy, security, and operational limits

- All data is aggregate and in memory.
- Counters use atomic operations; the rolling window uses a fixed bounded structure.
- Snapshots are assembled while traffic may continue, so closely related values can differ slightly during highly concurrent reads.
- Reset is intended for controlled demonstrations and is not transactionally coordinated with in-flight requests.
- The endpoint has no production authentication and is deliberately unavailable in Production.
- Metrics are single-instance and reset on restart; they provide no distributed view or durable audit record.
- No client behavior, identity, fingerprint, customer journey, or request payload is retained.

`System.Diagnostics.Metrics`, OpenTelemetry, and Prometheus exporters were not added. The current requirement is a small structured PoC snapshot, and adding parallel instruments or an exporter pipeline would create dependencies and operations not required in Phase 4. The fixed internal service can later feed a standard telemetry adapter without changing the current endpoint contract.
