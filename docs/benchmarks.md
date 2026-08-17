# Benchmarks

These are executed k6 measurements on one developer workstation, not estimates and not
retailer telemetry. All traffic stayed local: k6 ran in Docker Desktop and reached the host
APIs through `host.docker.internal`. Raw k6 summary exports are versioned under
[`load-tests/results`](../load-tests/results/); this document interprets them.

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
| Inventory configuration | 500 available; `StockLookupDelayMilliseconds` = 50 |

## Unprotected baseline (DemoStore direct)

| Scenario | Active configuration | Requests | Error rate | Avg | p50 | p90 | p95 | p99 | Throughput |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Smoke | 1 VU, 1 iteration | 4 | 0% | 15.25 ms | 0.85 ms | 41.32 ms | 49.97 ms | 56.89 ms | 61.95 req/s* |
| Normal customer SMALL | 5 VUs, 30s | 86 | 0% | 17.73 ms | 1.06 ms | 60.80 ms | 61.65 ms | 64.23 ms | 2.46 req/s |
| Flash crowd SMALL | 2 baseline → 20 peak, 40s | 1,342 | 0% | 26.58 ms | 0.84 ms | 61.59 ms | 63.33 ms | 65.35 ms | 33.24 req/s |
| Stock polling SMALL | 10 VUs, 30s, no poll pause | 4,820 | 0% | 62.15 ms | 62.08 ms | 63.28 ms | 63.61 ms | 64.49 ms | 160.60 req/s |
| Mixed drop SMALL | 10/25/50/75 VUs, 10s stages | 7,709 | 0% | 48.02 ms | 61.15 ms | 62.87 ms | 63.28 ms | 64.75 ms | 140.17 req/s |
| Mixed drop MEDIUM | 10/50/150/300 VUs, 15s stages | 36,458 | 0% | 48.29 ms | 60.81 ms | 62.52 ms | 63.10 ms | 64.86 ms | 467.43 req/s |
| Mixed drop STRESS | 25/100/300/600 VUs, 20s stages | 97,329 | 0% | 48.47 ms | 60.67 ms | 62.74 ms | 63.33 ms | 64.83 ms | 993.22 req/s |

*Single-iteration smoke rate is not a capacity measurement.

The mixed-drop STRESS profile ran four 20-second stages (25/100/300/600 concurrent VUs) with a
70% customer / 20% aggressive-poller / 10% cart-oriented traffic split:

| Stage | VUs | Requests | Throughput** | p50 | p95 | p99 |
|---|---:|---:|---:|---:|---:|---:|
| A | 25 | 2,407 | 120.4 req/s | 61.34 ms | 63.69 ms | 65.55 ms |
| B | 100 | 9,610 | 480.5 req/s | 60.86 ms | 63.30 ms | 64.81 ms |
| C | 300 | 28,454 | 1,422.7 req/s | 60.66 ms | 63.09 ms | 64.85 ms |
| D | 600 | 56,858 | 2,842.9 req/s | 60.62 ms | 63.39 ms | 64.78 ms |

**Request volume divided by the 20-second active stage window; an estimate, not k6's full-run
rate.

The full STRESS run reached 600 VUs, 77,498 iterations (790.85/s), and 97,329 requests
(993.22 req/s over the full 98-second run) with zero HTTP errors. The stock endpoint's
intentional 50 ms async wait dominates latency (median ~61 ms, p99 ~65 ms across stages) while
product and cart medians stay under 1 ms; throughput scales with VUs without a saturation
knee appearing by 600 VUs on this machine. `Task.Delay` models I/O wait, not CPU exhaustion,
connection-pool limits, or database locks, so this shows concurrency multiplication against a
fixed expensive endpoint — not a real commerce platform's saturation point.

## Protected (through DropShield)

Same environment; all traffic targeted `DropShield.Api` at `localhost:5257`, which forwarded
allowed requests to DemoStore at `localhost:5058`.

### Normal traffic

| Metric | Direct | Protected |
|---|---:|---:|
| Incoming requests | 86 | 86 |
| Origin requests | 86 | 86 |
| 429 responses | 0 | 0 |
| p50 | 1.06 ms | 1.54 ms |
| p95 | 61.65 ms | 63.26 ms |
| p99 | 64.23 ms | 66.08 ms |

No normal request was rate-limited. Measured gateway overhead: +0.48 ms at p50, +1.61 ms at
p95.

### Aggressive stock polling

10 VUs for 30 seconds with a 50 ms explicit poll interval, compensating for the closed-loop
effect where fast 429s otherwise let a fixed-VU client retry thousands of times per second:

| Metric | Direct | Protected |
|---|---:|---:|
| Incoming stock requests | 4,820 | 4,260 |
| Forwarded | 4,820 | 1,400 |
| 429 responses | 0 | 2,860 |
| Allowed throughput | 160.60 req/s | 46.60 req/s |
| Allowed p50/p95/p99 | 62.08/63.61/64.49 ms | 57.90/61.44/64.45 ms |
| Rejected p50/p95/p99 | — | 0.96/1.38/1.70 ms |

Within-run stock suppression: **67.14%** (2,860/4,260). Origin-request reduction vs. direct
polling: **70.95%** (1 − 1,400/4,820). A separate unpaced diagnostic run generated 244,232
attempts (99.41% rejected) purely to demonstrate the closed-loop feedback effect; it is not
used for the primary comparison above.

### Mixed drop

Same VU stages and 70/20/10 allocation as the baseline:

| Profile | Incoming | Forwarded | 429 | 429 rate | Stock incoming | Stock forwarded | Stock suppression |
|---|---:|---:|---:|---:|---:|---:|---:|
| SMALL | 6,922 | 4,017 | 2,905 | 41.97% | 5,310 | 2,464 | 53.60% |
| MEDIUM | 36,827 | 14,606 | 22,221 | 60.34% | 29,213 | 7,316 | 74.96% |
| STRESS | 103,618 | 31,012 | 72,606 | 70.07% | 83,304 | 11,680 | 85.98% |

Allowed vs. rejected latency must be read separately — rejections resolve in under 2 ms:

| Profile | Allowed p50 | Allowed p95 | Allowed p99 | Rejected p50 | Rejected p95 | Rejected p99 |
|---|---:|---:|---:|---:|---:|---:|
| SMALL | 54.81 ms | 62.92 ms | 65.60 ms | 0.78 ms | 1.15 ms | 1.46 ms |
| MEDIUM | 49.65 ms | 62.48 ms | 65.56 ms | 0.89 ms | 1.75 ms | 4.47 ms |
| STRESS | 0.94 ms | 62.33 ms | 65.99 ms | 0.89 ms | 1.94 ms | 3.32 ms |

(STRESS allowed p50 sits below 1 ms because allowed catalogue/cart responses outnumber the
aggregate-capped stock responses — the 50 ms stock dependency itself did not get faster.)

Origin traffic reduction overall: **47.89%** (SMALL), **59.94%** (MEDIUM), **68.14%**
(STRESS).

### Legitimate-traffic impact

Under aggregate mixed-drop pressure, some legitimate-shaped stock checks were also rejected by
the static aggregate ceiling (browsing itself stayed unaffected):

| Profile | Normal 429 rate | Flash-crowd 429 rate |
|---|---:|---:|
| SMALL | 0% | 0% |
| MEDIUM | 6.06% | 11.44% |
| STRESS | 15.49% | 25.47% |

This is a real false-positive/availability tradeoff of the first fixed rate-limit policy — it
does not classify these clients as bots.

## Interpretation

DropShield reduced high-frequency stock requests reaching the synthetic origin by 53.60–85.98%
across the mixed profiles, and total origin traffic by 47.89–68.14%. The unprotected baseline
never observed a saturation point or collapse on this hardware, so these numbers show measured
origin-traffic suppression and its availability tradeoff — not "DropShield prevented a
crash." Numbers are machine-dependent; compare protected vs. unprotected runs on the same
environment, not against a different machine or a different retailer's infrastructure.
