# Protected performance — Phase 3

These are measurements from actual local executions on 17 August 2026. The environment matches [`BASELINE_PERFORMANCE.md`](BASELINE_PERFORMANCE.md): Windows 11 Pro, AMD Ryzen 7 9800X3D, 16 logical processors, 31.7 GiB RAM, .NET 8.0.30, and k6 2.2.0 in Docker Desktop.

All traffic targeted `DropShield.Api` at `localhost:5257`, which forwarded allowed requests to the local DemoStore at `localhost:5058`. No third-party target was used.

## Normal traffic control

| Metric | Direct Phase 2 | Protected Phase 3 |
|---|---:|---:|
| Incoming requests | 86 | 86 |
| Origin requests | 86 | 86 |
| 429 responses | 0 | 0 |
| Throughput | 2.46 req/s | 2.46 req/s allowed |
| p50 | 1.06 ms | 1.54 ms allowed |
| p95 | 61.65 ms | 63.26 ms allowed |
| p99 | 64.23 ms | 66.08 ms allowed |
| Stock p95 | 64.08 ms | 65.72 ms allowed |

No normal request was rate-limited. Measured gateway overhead was +0.48 ms at overall p50 and +1.61 ms at overall p95. Random journey timing and the 50 ms stock delay mean these are run-level comparisons, not isolated microbenchmarks.

## Aggressive stock polling

The primary protected run used 10 VUs for 30 seconds with a 50 ms explicit poll interval to compensate for fast-rejection feedback.

| Metric | Direct Phase 2 | Protected Phase 3 |
|---|---:|---:|
| Incoming stock requests | 4,820 | 4,260 |
| Forwarded stock requests | 4,820 | 1,400 |
| 429 responses | 0 | 2,860 |
| Allowed/origin throughput | 160.60 req/s | 46.60 req/s |
| Rejected throughput | 0 | 95.20 req/s |
| Allowed p50 / p95 / p99 | 62.08 / 63.61 / 64.49 ms | 57.90 / 61.44 / 64.45 ms |
| Rejected p50 / p95 / p99 | — | 0.96 / 1.38 / 1.70 ms |

- Within-run stock suppression: **67.14%** (`2,860 / 4,260`).
- Origin request reduction versus the direct polling baseline: **70.95%** (`1 - 1,400 / 4,820`).

An intentionally unpaced diagnostic generated 244,232 attempts, of which 242,782 (99.41%) were rejected and 1,450 forwarded. It demonstrates that immediate 429s accelerate a closed-loop fixed-VU client; it is not used as the fair comparison result.

## POKEMON_DROP_PROTECTED

All runs retained the Phase 2 VU stages and 70/20/10 traffic allocation.

| Profile | Incoming | Forwarded | 429 | 429 rate | Incoming rate | Origin rate | Stock incoming | Stock forwarded | Stock suppression |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| SMALL | 6,922 | 4,017 | 2,905 | 41.97% | 125.86 req/s | 73.04 req/s | 5,310 | 2,464 | 53.60% |
| MEDIUM | 36,827 | 14,606 | 22,221 | 60.34% | 472.15 req/s | 187.26 req/s | 29,213 | 7,316 | 74.96% |
| STRESS | 103,618 | 31,012 | 72,606 | 70.07% | 1,057.36 req/s | 316.46 req/s | 83,304 | 11,680 | 85.98% |

Allowed and rejected latency must be interpreted separately:

| Profile | Allowed p50 | Allowed p95 | Allowed p99 | Rejected p50 | Rejected p95 | Rejected p99 |
|---|---:|---:|---:|---:|---:|---:|
| SMALL | 54.81 ms | 62.92 ms | 65.60 ms | 0.78 ms | 1.15 ms | 1.46 ms |
| MEDIUM | 49.65 ms | 62.48 ms | 65.56 ms | 0.89 ms | 1.75 ms | 4.47 ms |
| STRESS | 0.94 ms | 62.33 ms | 65.99 ms | 0.89 ms | 1.94 ms | 3.32 ms |

STRESS allowed p50 is below 1 ms because allowed catalogue and cart responses outnumber the aggregate-capped stock responses. It does not mean the 50 ms stock dependency became faster.

## Before DropShield versus after DropShield

| Metric | SMALL baseline / protected | MEDIUM baseline / protected | STRESS baseline / protected |
|---|---:|---:|---:|
| Client-side incoming rate | 140.17 / 125.86 req/s | 467.43 / 472.15 req/s | 993.22 / 1,057.36 req/s |
| Origin request rate | 140.17 / 73.04 req/s | 467.43 / 187.26 req/s | 993.22 / 316.46 req/s |
| Origin request volume | 7,709 / 4,017 | 36,458 / 14,606 | 97,329 / 31,012 |
| Origin traffic reduction | 47.89% | 59.94% | 68.14% |
| 429 responses | 0 / 2,905 | 0 / 22,221 | 0 / 72,606 |
| Protected stock origin rate | — / 44.80 req/s | — / 93.80 req/s | — / 119.19 req/s |

The Phase 2 JSON summaries did not record an exact stock-request counter, so mixed baseline stock rates are left blank rather than inferred. Phase 3 stock reduction is calculated exactly from DropShield's incoming and forwarded counters.

## Legitimate-shaped traffic impact

The standalone normal scenario had a 0% 429 rate. Under aggregate mixed-drop pressure:

| Profile | Normal requests | Normal 429 | Normal 429 rate | Flash requests | Flash 429 | Flash 429 rate |
|---|---:|---:|---:|---:|---:|---:|
| SMALL | 1,032 | 0 | 0% | 304 | 0 | 0% |
| MEDIUM | 4,935 | 299 | 6.06% | 1,495 | 171 | 11.44% |
| STRESS | 12,898 | 1,998 | 15.49% | 3,757 | 957 | 25.47% |

All of these MEDIUM/STRESS customer denials were protected stock checks caused by the aggregate origin ceiling; product-list and product-detail browsing remained fully forwarded. They are a real false-positive/availability tradeoff in this static PoC policy and should inform later policy refinement. Phase 3 does not classify those clients as bots.

## Defensible conclusion

DropShield reduced high-frequency stock requests reaching the synthetic origin by 53.60% to 85.98% across the protected mixed profiles, and reduced total origin traffic by 47.89% to 68.14%. The normal control run was unaffected, but the static aggregate ceiling rejected some legitimate-shaped stock checks at MEDIUM and STRESS.

The benchmark does **not** show that DropShield prevented server collapse: Phase 2 did not observe a collapse. It shows measured origin-traffic suppression and the latency/availability tradeoff of a first fixed policy.

Raw k6 summaries and paired DropShield counter snapshots are in [`load-tests/results`](../load-tests/results/).
