# DropShield

DropShield is a defensive proof-of-concept intended to demonstrate techniques for keeping ecommerce platforms responsive during high-demand product launches and excessive automated traffic. Its long-term direction is an edge-first, ecommerce-aware protection architecture that can integrate with Adobe Commerce and a retailer's existing edge/CDN infrastructure without coupling the core to one edge provider.

## Important safety boundary

All automated traffic generation, load testing, endpoint experimentation, and abuse simulation must target only:

- localhost;
- the synthetic `DropShield.DemoStore`;
- infrastructure owned by the project operator; or
- systems for which explicit testing permission has been granted.

This project must not be used to send abusive traffic to third-party websites. Public architectural research does not constitute authorisation to security-test Hamleys. Do not add Hamleys production URLs to load tests, integration tests, API fuzzers, scrapers, bot simulations, or automated endpoint discovery. Hamleys and any other retailer are conceptual use cases only and are not affiliated with DropShield.

## Current phase

**Phase 6 — Virtual Waiting Room / Admission Control**

The completed Phase 1–5 paths remain reproducible. Phase 6 adds bounded admission state so eligible sessions can wait outside the origin and be progressively admitted. InMemory remains available for one-process local development; Redis mode coordinates admission across instances.

Per-client rate limits remain the abuse boundary. Waiting clients receive JSON HTTP 202 rather than an exact queue position, while admitted clients reach the synthetic origin. Signed admission tokens, cart replay protection, inventory reservation, bot classification, adaptive admission, Adobe Commerce integration, and edge-provider integration have not been implemented.

## Architecture direction

DropShield is not a Cloudflare-native system for Hamleys. The core is edge-provider-neutral, with future provider-specific capabilities isolated behind adapters. Adobe Commerce / Magento 2 is the first planned real ecommerce integration because public evidence establishes it as the relevant platform for the Hamleys use case.

For Hamleys-specific discussion, use this evidence boundary:

> **Adobe Commerce Edge / CDN**  
> Likely Fastly where standard Adobe Commerce Cloud architecture applies.  
> Exact Hamleys production configuration: unconfirmed.

Cloudflare is not confirmed as Hamleys' authoritative edge provider. Hamleys' exact Fastly routing and Adobe Advanced Security licensing or enablement are also unknown.

See:

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — future edge/application separation and design principles.
- [`docs/HAMLEYS_PLATFORM_RESEARCH.md`](docs/HAMLEYS_PLATFORM_RESEARCH.md) — evidence classifications, confidence levels, sources, and known unknowns.
- [`docs/adr/ADR-001-edge-provider-neutral.md`](docs/adr/ADR-001-edge-provider-neutral.md) — provider-neutral core decision.
- [`docs/adr/ADR-002-adobe-commerce-target.md`](docs/adr/ADR-002-adobe-commerce-target.md) — Adobe Commerce target decision.

## Projects

- `src/DropShield.Api` — protected local entry point with explicit forwarding, selectable InMemory/Redis policy and admission state, and per-instance observability.
- `src/DropShield.DemoStore` — synthetic ecommerce backend with health, product, stock, cart, and checkout endpoints.
- `tests/DropShield.Tests` — xUnit integration tests for both APIs.
- `load-tests` — localhost-only k6 smoke, customer, flash-crowd, aggressive-polling, and mixed-drop scenarios.

## Run locally

Requirements: .NET 8 SDK.

```powershell
dotnet restore
dotnet build DropShield.sln
dotnet test DropShield.sln
```

Start each API in a separate terminal:

```powershell
dotnet run --project src/DropShield.Api
dotnet run --project src/DropShield.DemoStore
```

Default local URLs:

- DropShield API: `http://localhost:5257/health`
- Demo Store: `http://localhost:5058/health`
- Demo Store stock: `http://localhost:5058/api/products/pokemon-etb/stock`

The demo store's intentional inventory lookup delay is configured in `src/DropShield.DemoStore/appsettings.json`.

## Phase 2 — Baseline Load Testing

Start the DemoStore in one PowerShell terminal:

```powershell
$env:Logging__LogLevel__Default = 'Warning'
dotnet run --project src/DropShield.DemoStore --configuration Release --no-launch-profile --urls http://localhost:5058
```

In another terminal at the repository root, set the Docker bind mount and run any scenario:

```powershell
$loadTests = (Resolve-Path .\load-tests).Path

# Smoke
docker run --rm -e TARGET_BASE_URL=http://host.docker.internal:5058 --mount "type=bind,source=$loadTests,target=/scripts" grafana/k6:2.2.0 run /scripts/smoke.js

# Normal customer traffic
docker run --rm -e TARGET_BASE_URL=http://host.docker.internal:5058 -e PROFILE=SMALL --mount "type=bind,source=$loadTests,target=/scripts" grafana/k6:2.2.0 run /scripts/normal-traffic.js

# Flash crowd
docker run --rm -e TARGET_BASE_URL=http://host.docker.internal:5058 -e PROFILE=SMALL --mount "type=bind,source=$loadTests,target=/scripts" grafana/k6:2.2.0 run /scripts/flash-crowd.js

# Aggressive bot-like stock polling
docker run --rm -e TARGET_BASE_URL=http://host.docker.internal:5058 -e PROFILE=SMALL --mount "type=bind,source=$loadTests,target=/scripts" grafana/k6:2.2.0 run /scripts/bot-like-stock-polling.js

# Mixed Pokémon drop baseline
docker run --rm -e TARGET_BASE_URL=http://host.docker.internal:5058 -e PROFILE=SMALL --mount "type=bind,source=$loadTests,target=/scripts" grafana/k6:2.2.0 run /scripts/mixed-drop.js
```

The scripts reject non-local target hosts. See [`load-tests/README.md`](load-tests/README.md) for profiles, overrides, summary exports, and safety details. Executed results are in [`docs/BASELINE_PERFORMANCE.md`](docs/BASELINE_PERFORMANCE.md).

## Phase 3 — Protected local path

Start DemoStore as above. In a second terminal, start DropShield with development-only synthetic identity and metrics enabled by `appsettings.Development.json`:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Logging__LogLevel__Default = 'Warning'
dotnet run --project src/DropShield.Api --configuration Release --no-launch-profile --urls http://localhost:5257
```

Allowed requests follow `localhost:5257 → localhost:5058`; rate-limited requests receive JSON HTTP 429 and never reach DemoStore. Run the protected normal control:

```powershell
$loadTests = (Resolve-Path .\load-tests).Path
docker run --rm -e TARGET_BASE_URL=http://host.docker.internal:5257 -e PROTECTED_MODE=true -e PROFILE=SMALL --mount "type=bind,source=$loadTests,target=/scripts" grafana/k6:2.2.0 run /scripts/normal-traffic.js
```

Run a protected mixed comparison with polling cadence compensation:

```powershell
docker run --rm -e TARGET_BASE_URL=http://host.docker.internal:5257 -e PROTECTED_MODE=true -e PROFILE=SMALL -e POLL_INTERVAL_SECONDS=0.05 --mount "type=bind,source=$loadTests,target=/scripts" grafana/k6:2.2.0 run /scripts/mixed-drop.js
```

See [`docs/PHASE3_RATE_LIMITING.md`](docs/PHASE3_RATE_LIMITING.md) for architecture and policy behavior, and [`docs/PROTECTED_PERFORMANCE.md`](docs/PROTECTED_PERFORMANCE.md) for measured before/after results.

## Phase 4 — Internal observability

With DropShield running in Development, inspect the aggregate JSON snapshot:

```powershell
Invoke-RestMethod http://localhost:5257/internal/metrics
```

Reset the current collection for a controlled demonstration:

```powershell
Invoke-WebRequest -Method Post http://localhost:5257/internal/metrics/reset
```

These endpoints return 404 in Production or when internal metrics are disabled. They expose no client identifiers or request data. See [`docs/PHASE4_OBSERVABILITY.md`](docs/PHASE4_OBSERVABILITY.md) for the schema, metric definitions, bounded implementation, and attribution limits.

## Phase 5 — Optional Redis state

The committed default remains `InMemory` and requires no Redis service. For a controlled local Redis-mode run, start one loopback-only container:

```powershell
docker run --rm --name dropshield-redis-phase5 -p 127.0.0.1:6379:6379 redis:8.8.1-alpine
```

Then configure the API process without committing a secret:

```powershell
$env:DropShield__StateProvider = 'Redis'
$env:DropShield__Redis__ConnectionString = '127.0.0.1:6379'
$env:DropShield__Redis__IdentityHashKey = [Convert]::ToHexString(
    [Security.Cryptography.RandomNumberGenerator]::GetBytes(32))

dotnet run --project src/DropShield.Api
```

Redis unavailability is fail-closed: protected traffic receives HTTP 503 and is not forwarded or silently placed on a weaker local limiter. See [`docs/PHASE5_DISTRIBUTED_STATE.md`](docs/PHASE5_DISTRIBUTED_STATE.md) and [ADR-003](docs/adr/ADR-003-distributed-state-provider.md).

## Phase 6 — Waiting-room behavior

Admission is enabled for `pokemon-etb` in the committed PoC configuration. A protected-stock request first passes the per-client abuse limit. The first configured batch of sessions is admitted; eligible excess sessions receive:

```json
{
  "status": "waiting",
  "drop": "pokemon-etb",
  "retryAfterSeconds": 5
}
```

The response is HTTP 202 with `Retry-After`. Poll the same protected-stock URL using the returned `DropShield.Session` cookie. No exact queue position is promised. See [`docs/PHASE6_ADMISSION_CONTROL.md`](docs/PHASE6_ADMISSION_CONTROL.md) and [ADR-004](docs/adr/ADR-004-admission-control.md).

## Docker

Build the images from the repository root:

```powershell
docker build -f src/DropShield.Api/Dockerfile -t dropshield-api .
docker build -f src/DropShield.DemoStore/Dockerfile -t dropshield-demo-store .
```

Each container listens on port `8080`.
