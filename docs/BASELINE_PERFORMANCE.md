# Unprotected baseline performance

These are executed measurements of the synthetic, unprotected `DropShield.DemoStore`, not estimates and not Hamleys telemetry. All generated traffic remained on the developer workstation: k6 ran in Docker Desktop and reached the host DemoStore through `host.docker.internal`.

## Test environment

| Item | Value |
|---|---|
| Test date | 17 August 2026 |
| Operating system | Microsoft Windows 11 Pro 64-bit, 10.0.26200 |
| Processor | AMD Ryzen 7 9800X3D 8-Core Processor |
| Logical processors | 16 |
| Total RAM | 31.7 GiB |
| .NET SDK | 8.0.424 |
| .NET runtime | 8.0.30, x64 |
| k6 | 2.2.0, official `grafana/k6:2.2.0` container |
| DemoStore build | Release, host .NET process at `http://localhost:5058` |
| Application logging | Default level overridden to `Warning` for the run |
| Inventory configuration | 500 available; `StockLookupDelayMilliseconds` = 50 |

Each run used the same application process. Summary rates include k6's full elapsed execution period, including configured ramp, inter-stage, and graceful-stop time where applicable.

## Executed scenario summary

| Scenario | Active configuration | Requests | Success / failed | Error rate | Avg | p50 | p90 | p95 | p99 | Throughput |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Smoke | 1 VU, 1 iteration | 4 | 4 / 0 | 0% | 15.25 ms | 0.85 ms | 41.32 ms | 49.97 ms | 56.89 ms | 61.95 req/s* |
| Normal customer SMALL | 5 VUs, 30s | 86 | 86 / 0 | 0% | 17.73 ms | 1.06 ms | 60.80 ms | 61.65 ms | 64.23 ms | 2.46 req/s |
| Flash crowd SMALL | 2 baseline → 20 peak, 40s | 1,342 | 1,342 / 0 | 0% | 26.58 ms | 0.84 ms | 61.59 ms | 63.33 ms | 65.35 ms | 33.24 req/s |
| Stock polling SMALL | 10 VUs, 30s, no poll pause | 4,820 | 4,820 / 0 | 0% | 62.15 ms | 62.08 ms | 63.28 ms | 63.61 ms | 64.49 ms | 160.60 req/s |
| POKEMON_DROP_BASELINE SMALL | 10 / 25 / 50 / 75 VUs, 10s stages | 7,709 | 7,709 / 0 | 0% | 48.02 ms | 61.15 ms | 62.87 ms | 63.28 ms | 64.75 ms | 140.17 req/s |
| POKEMON_DROP_BASELINE MEDIUM | 10 / 50 / 150 / 300 VUs, 15s stages | 36,458 | 36,458 / 0 | 0% | 48.29 ms | 60.81 ms | 62.52 ms | 63.10 ms | 64.86 ms | 467.43 req/s |
| POKEMON_DROP_BASELINE STRESS | 25 / 100 / 300 / 600 VUs, 20s stages | 97,329 | 97,329 / 0 | 0% | 48.47 ms | 60.67 ms | 62.74 ms | 63.33 ms | 64.83 ms | 993.22 req/s |

*The single-iteration smoke rate is not a capacity measurement.

## POKEMON_DROP_BASELINE key experiment

The final controlled run used the STRESS profile after SMALL and MEDIUM completed without instability. Each stage ran for 20 seconds with a five-second slot gap and allocated VUs approximately 70% customer, 20% poller, and 10% cart-oriented traffic.

| Stage | Concurrent VUs | Requests | Approx. active-window throughput** | Error rate | Overall p50 | Overall p95 | Overall p99 | Stock p50 | Stock p95 | Stock p99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| A | 25 | 2,407 | 120.4 req/s | 0% | 61.34 ms | 63.69 ms | 65.55 ms | 61.72 ms | 63.88 ms | 65.68 ms |
| B | 100 | 9,610 | 480.5 req/s | 0% | 60.86 ms | 63.30 ms | 64.81 ms | 61.25 ms | 63.44 ms | 64.99 ms |
| C | 300 | 28,454 | 1,422.7 req/s | 0% | 60.66 ms | 63.09 ms | 64.85 ms | 61.07 ms | 63.32 ms | 64.97 ms |
| D | 600 | 56,858 | 2,842.9 req/s | 0% | 60.62 ms | 63.39 ms | 64.78 ms | 61.13 ms | 63.57 ms | 65.02 ms |

**Request volume divided by the configured 20-second active stage. A small number of in-flight customer iterations may finish during the three-second graceful-stop window, so this is a stage comparison estimate rather than k6's full-run rate.

The complete STRESS run reached 600 VUs, 77,498 iterations (790.85 iterations/s over the full run), and 97,329 requests (993.22 requests/s over the full 98-second run) with no HTTP errors.

## Observations

- The first clear cost is the stock endpoint's intentional 50 ms asynchronous dependency wait. Its median was about 61 ms and p99 about 65 ms across progressive stages, while product and cart medians remained below 1 ms.
- Ten sequential aggressive pollers sustained 160.60 stock requests/s, close to the concurrency divided by observed per-request latency. More poller VUs multiply concurrent inventory work even though each request appears inexpensive.
- Throughput increased with VUs, but latency did not materially increase and no saturation or error knee appeared by the bounded 600-VU ceiling. On this machine, the app handled the async-wait workload without evidence that would justify a more extreme run.
- Mixed overall p50 is close to stock p50 because most customer and poller paths include stock. Human-scale sleeps raise iteration duration but are not included in HTTP latency.
- These absolute values include Windows host networking plus Docker Desktop load generation. They are useful only as a same-environment baseline for later protected runs.

## Limitations and interpretation

`Task.Delay` represents asynchronous I/O latency. It does not model CPU exhaustion, a bounded database connection pool, locks, remote service quotas, or downstream failures. Therefore this benchmark demonstrates concurrency multiplication and a fixed expensive endpoint, but not a real commerce platform's saturation point. No synthetic database or retailer-specific behavior was added, and the DemoStore was deliberately not optimised after measurement.

The absence of errors is not proof of production capacity. Future phases should rerun the identical scripts on the same environment and compare throughput, latency, and error behavior after each protection mechanism is introduced.

## Raw results

The exact k6 summary exports are versioned under [`load-tests/results`](../load-tests/results): smoke, SMALL normal/flash/polling, and SMALL/MEDIUM/STRESS mixed-drop runs.
