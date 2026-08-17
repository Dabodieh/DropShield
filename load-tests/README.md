# DropShield load tests

This directory contains k6 traffic-pattern simulations that can target either the unprotected synthetic DemoStore or the protected DropShield entry point. They do not model observed retailer traffic.

## Safety boundary

Scripts default to `http://localhost:5058`. `TARGET_BASE_URL` accepts only `localhost`, `127.0.0.1`, `[::1]`, or the Docker Desktop host alias `host.docker.internal`, with an optional port. Paths, credentials, query strings, fragments, unsupported schemes, and every other hostname are rejected during script initialisation.

Never weaken this guard to target Hamleys, another retailer, or third-party infrastructure. Only run these tests on localhost or infrastructure that you own and have deliberately placed under test.

## Requirements and target startup

The documented workflow uses the pinned official `grafana/k6:2.2.0` Docker image, so k6 is not an application runtime dependency. From the repository root, build and start the DemoStore in Release mode:

```powershell
dotnet build DropShield.sln --configuration Release
$env:Logging__LogLevel__Default = 'Warning'
dotnet run --project src/DropShield.DemoStore --configuration Release --no-build --no-launch-profile --urls http://localhost:5058
```

The warning log level matches the recorded baseline and prevents successful placeholder cart requests from turning console I/O into part of the measurement. It does not change API behaviour.

In a second PowerShell terminal at the repository root, resolve the bind mount once:

```powershell
$loadTests = (Resolve-Path .\load-tests).Path
```

Docker Desktop exposes the locally running DemoStore to k6 as `http://host.docker.internal:5058`.

## Run the scenarios

Smoke test (one iteration, four GET requests, response-body assertions):

```powershell
docker run --rm -e TARGET_BASE_URL=http://host.docker.internal:5058 --mount "type=bind,source=$loadTests,target=/scripts" grafana/k6:2.2.0 run --summary-export /scripts/results/smoke.json /scripts/smoke.js
```

Normal customer traffic:

```powershell
docker run --rm -e TARGET_BASE_URL=http://host.docker.internal:5058 -e PROFILE=SMALL --mount "type=bind,source=$loadTests,target=/scripts" grafana/k6:2.2.0 run --summary-export /scripts/results/normal-small.json /scripts/normal-traffic.js
```

Flash-crowd traffic:

```powershell
docker run --rm -e TARGET_BASE_URL=http://host.docker.internal:5058 -e PROFILE=SMALL --mount "type=bind,source=$loadTests,target=/scripts" grafana/k6:2.2.0 run --summary-export /scripts/results/flash-small.json /scripts/flash-crowd.js
```

Aggressive automated stock polling:

```powershell
docker run --rm -e TARGET_BASE_URL=http://host.docker.internal:5058 -e PROFILE=SMALL --mount "type=bind,source=$loadTests,target=/scripts" grafana/k6:2.2.0 run --summary-export /scripts/results/polling-small.json /scripts/bot-like-stock-polling.js
```

Mixed Pokémon drop baseline:

```powershell
docker run --rm -e TARGET_BASE_URL=http://host.docker.internal:5058 -e PROFILE=SMALL --mount "type=bind,source=$loadTests,target=/scripts" grafana/k6:2.2.0 run --summary-export /scripts/results/pokemon-drop-baseline-small.json /scripts/mixed-drop.js
```

Set `PROFILE=MEDIUM` or `PROFILE=STRESS` only after smaller runs are stable. Absolute numbers are machine-dependent; compare unprotected and future protected runs on the same machine and configuration.

If k6 is installed directly, the same scripts can run without Docker. Their safe default points to the locally launched DemoStore:

```powershell
k6 run load-tests/smoke.js
```

## Standard profiles

| Scenario | SMALL | MEDIUM | STRESS |
|---|---|---|---|
| Normal customer | 5 VUs, 30s | 20 VUs, 60s | 50 VUs, 90s |
| Flash crowd | 2 baseline / 20 peak, 40s | 5 / 50, 70s | 10 / 100, 100s |
| Stock polling | 10 VUs, 30s | 30 VUs, 45s | 60 VUs, 60s |
| Mixed drop stages A–D | 10 / 25 / 50 / 75 VUs, 10s each | 10 / 50 / 150 / 300 VUs, 15s each | 25 / 100 / 300 / 600 VUs, 20s each |

The mixed profile allocates VUs as 70% customer traffic, 20% aggressive stock polling, and 10% cart-oriented activity by default. Customer traffic itself mixes slower normal journeys and shorter flash-crowd journeys.

## Configuration

All scripts support `TARGET_BASE_URL` and `PROFILE`. Relevant overrides are:

- `VIRTUAL_USERS`, `DURATION`, and `POLL_INTERVAL_SECONDS` for fixed-VU scenarios;
- `BASELINE_VUS`, `VIRTUAL_USERS`, `BASELINE_DURATION`, `RAMP_DURATION`, `DURATION`, and `COOLDOWN_DURATION` for flash crowds;
- `CUSTOMER_PERCENT`, `POLLER_PERCENT`, `CART_PERCENT`, `NORMAL_CUSTOMER_SHARE`, and `CUSTOMER_CART_PROBABILITY` for mixed traffic;
- `STAGE_A_VUS` through `STAGE_D_VUS`, `STAGE_DURATION_SECONDS`, `STAGE_SLOT_SECONDS`, and `MAX_STAGE` for progressive mixed runs.

Percentages must total 100. Durations use k6 units such as `500ms`, `30s`, or `2m`. Invalid profiles, ranges, ratios, or targets fail before traffic starts.

## Measurements

k6 reports total and per-second requests, successes and failures, HTTP error rate, iterations, active/max VUs, and average/median/p90/p95/p99 latency. Custom trends separately capture health, product-list, product-detail, stock, and cart latency. The mixed benchmark adds stage tags so request counts, errors, overall latency, and stock latency can be compared from A through D.

Raw executed summaries are in [`results/`](results/) and the interpreted results are in [`docs/benchmarks.md`](../docs/benchmarks.md).

## Protected runs (through DropShield)

Start DemoStore on port 5058 and DropShield.Api in Development on port 5257 as documented in the root README. Reset development counters, wait for the one-second stock window to replenish, and run the normal control:

```powershell
Invoke-WebRequest -Method Post http://localhost:5257/internal/metrics/reset | Out-Null
Start-Sleep -Milliseconds 1200
docker run --rm -e TARGET_BASE_URL=http://host.docker.internal:5257 -e PROTECTED_MODE=true -e PROFILE=SMALL --mount "type=bind,source=$loadTests,target=/scripts" grafana/k6:2.2.0 run --summary-export /scripts/results/normal-protected-small.json /scripts/normal-traffic.js
Invoke-RestMethod http://localhost:5257/internal/metrics
```

Aggressive polling comparison:

```powershell
Invoke-WebRequest -Method Post http://localhost:5257/internal/metrics/reset | Out-Null
Start-Sleep -Milliseconds 1200
docker run --rm -e TARGET_BASE_URL=http://host.docker.internal:5257 -e PROTECTED_MODE=true -e PROFILE=SMALL -e POLL_INTERVAL_SECONDS=0.05 --mount "type=bind,source=$loadTests,target=/scripts" grafana/k6:2.2.0 run --summary-export /scripts/results/polling-protected-small.json /scripts/bot-like-stock-polling.js
Invoke-RestMethod http://localhost:5257/internal/metrics
```

Mixed protected benchmark; change `PROFILE` to `MEDIUM` or `STRESS` only after smaller runs remain stable:

```powershell
Invoke-WebRequest -Method Post http://localhost:5257/internal/metrics/reset | Out-Null
Start-Sleep -Milliseconds 1200
docker run --rm -e TARGET_BASE_URL=http://host.docker.internal:5257 -e PROTECTED_MODE=true -e PROFILE=SMALL -e POLL_INTERVAL_SECONDS=0.05 --mount "type=bind,source=$loadTests,target=/scripts" grafana/k6:2.2.0 run --summary-export /scripts/results/pokemon-drop-protected-small.json /scripts/mixed-drop.js
Invoke-RestMethod http://localhost:5257/internal/metrics
```

`PROTECTED_MODE=true` sends a stable `X-DropShield-Test-Client` value per k6 VU and classifies HTTP 429 as an expected policy outcome. DropShield trusts that header only in explicitly enabled Development/Testing environments. Direct (unprotected) commands remain unchanged and do not send the header.

The explicit 50 ms poll interval compensates for closed-loop feedback: against the direct origin, the 50 ms stock response naturally paces each VU, while a sub-millisecond 429 would otherwise allow thousands of retries per second. It does not change VU counts or the 70/20/10 mixed allocation.

Protected summaries include incoming, allowed, rate-limited, and stock-specific counters plus separate allowed/rejected latency trends. Authoritative origin-forwarding evidence comes from DropShield's development counters. Results are interpreted in [`docs/benchmarks.md`](../docs/benchmarks.md).

## Synthetic-backend limitation

The stock service uses a cancellation-aware asynchronous `Task.Delay` configured as 50 ms. This credibly represents wait time for an I/O dependency, but it does not consume a thread for 50 ms and does not model CPU saturation, database locks, connection-pool limits, Dynamics 365, or any retailer's private systems. No second synthetic mode was added because the phase requires measuring the existing application before changing its behaviour.
