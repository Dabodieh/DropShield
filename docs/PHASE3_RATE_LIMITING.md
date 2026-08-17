# Phase 3 — Traffic Policy / Rate Limiting

Phase 3 makes `DropShield.Api` the protected local entry point while leaving `DropShield.DemoStore` directly accessible and deliberately unprotected.

```text
k6 → DropShield.Api :5257 → traffic policy → DemoStore :5058
                              ├─ denied → 429
                              └─ allowed → forward
```

This is a provider-neutral traffic-control primitive. It is not an Adobe Commerce, Fastly, Cloudflare, or Akamai integration.

## Protected routes

DropShield forwards only these route shapes:

- `GET /api/products`
- `GET /api/products/{productId}`
- `GET /api/products/{productId}/stock`
- `POST /api/cart`
- `POST /api/checkout`

It is not a generic arbitrary HTTP proxy. Catalogue browsing is forwarded without rate limiting. Stock policy applies only when `{productId}` appears in `DropShield:ProtectedProducts`; the current protected product is `pokemon-etb`.

## Algorithms and evaluation order

The implementation uses .NET 8 `System.Threading.RateLimiting` primitives through ASP.NET Core rate-limiting middleware. Two partitioned fixed-window limiters are chained:

1. a per-client limiter for protected stock, cart, and checkout;
2. one aggregate partition for protected stock across all clients.

Fixed windows were chosen because PoC limits are easy to configure, reproduce, and explain. Each limiter has `QueueLimit` set to zero, so denied requests return immediately rather than waiting inside DropShield. This avoids implementing the future waiting-room phase accidentally.

The fixed-window boundary can permit short bursts when a window resets. The values below are demonstration settings, not recommended retailer production values.

## Configuration

Default `appsettings.json` values:

| Policy | Per-client allowance | Aggregate allowance |
|---|---:|---:|
| Protected stock | 5 requests / 1 second | 200 requests / 1 second |
| Cart | 2 requests / 1 second | None |
| Checkout | 1 request / 5 seconds | None |

Other configuration includes:

- `DropShield:Enabled`
- `DropShield:OriginBaseUrl` — default `http://localhost:5058`
- `DropShield:OriginTimeoutSeconds` — default 10
- `DropShield:ProtectedProducts`
- `DropShield:SyntheticClientIdentity`
- `DropShield:InternalMetrics`

Options are validated when the application starts. The origin permits only HTTP(S) URLs using `localhost`, `127.0.0.1`, `::1`, or `host.docker.internal`, with no credentials, path, query, or fragment. Invalid limits and unsafe origins stop startup.

## Client partitioning

For controlled load tests, k6 sends a stable per-VU `X-DropShield-Test-Client` value. DropShield trusts this header only when it is explicitly enabled and the host environment is `Development` or `Testing`. Enabling it in another environment fails configuration validation. The header is not forwarded to DemoStore.

When the mechanism is disabled or the header is absent, this PoC uses the immediate source IP as a fallback partition. It does not process arbitrary forwarded-IP headers.

> IP address alone is not considered a sufficient long-term identity mechanism for production bot mitigation.

The synthetic header is test instrumentation, not authentication, proof of identity, or a production security boundary. Production identity design remains out of scope.

## Responses and failure behavior

A denied request receives HTTP 429, a `Retry-After` header, and:

```json
{
  "error": "rate_limited",
  "message": "Too many requests. Please try again shortly."
}
```

The response does not expose limiter state, thresholds, origin details, or stack traces.

- Policy evaluation is fail-closed in this local process: an unexpected evaluation failure is not forwarded as though allowed.
- An unavailable or timed-out DemoStore returns HTTP 502 with `upstream_unavailable`; it is never misreported as HTTP 429.
- No retry, cache, queue, circuit breaker, or distributed fallback was introduced.

## Forwarding evidence

An in-memory counter records incoming, forwarded, and rate-limited counts per known route. When explicitly enabled, `GET /internal/metrics` reads the snapshot and `POST /internal/metrics/reset` resets counters. These routes return 404 outside Development/Testing or when disabled.

The counters are development evidence, not the Phase 4 observability architecture. They contain no client identifiers or request payloads. A forwarded count means DropShield attempted the local origin call; in the recorded healthy-origin benchmarks it corresponds to traffic sent to DemoStore.

## Benchmark methodology

Protected runs reused the Phase 2 SMALL, MEDIUM, and STRESS VU stages and the same 70% customer / 20% aggressive polling / 10% cart-oriented allocation. `PROTECTED_MODE=true` enabled synthetic identity and treated 429 as an expected policy outcome rather than an unexpected script error.

The direct origin's 50 ms stock response naturally paced each Phase 2 polling VU. Fast 429 responses remove that service-time pacing and otherwise cause a fixed VU to retry thousands of times per second. Official protected comparison runs therefore set the already-configurable `POLL_INTERVAL_SECONDS=0.05`. This preserves roughly comparable offered polling cadence without changing VUs or traffic allocation. Separate unpaced diagnostic artifacts demonstrate the feedback effect and are not used for the primary before/after table.

See [`PROTECTED_PERFORMANCE.md`](PROTECTED_PERFORMANCE.md) for actual results.

## Production limitations

- All limiter and counter state is in memory and scoped to one process.
- There is no distributed coordination, Redis, durable state, or multi-instance fairness.
- The source-IP fallback is intentionally simplistic.
- Fixed windows can burst at boundaries and do not classify behavior.
- The PoC forwards a small explicit route set and does not implement full proxy header/body semantics.
- The synthetic origin models asynchronous I/O latency, not a real commerce system's resource constraints.

> Phase 3 performs traffic-rate control, not bot classification.

No CAPTCHA, fingerprinting, behavioral scoring, queue, waiting room, signed token, inventory reservation, purchase limit, adaptive admission, or ecommerce-provider integration is present.
