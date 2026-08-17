# Behavioural scoring

DropShield adds an explainable, short-lived behavioural signal for the protected drop. It's a
demonstration policy built from bounded recent evidence — not a machine-learning classifier,
long-term reputation system, or fingerprinting mechanism.

## Evidence and score

Each HMAC-derived actor gets a rolling 60-second evidence window, expiring after 120 seconds
of inactivity. The score is recalculated from current bounded counts (never unboundedly
accumulated) and capped at 100:

| Signal | Contribution | Reason code |
|---|---:|---|
| 8+ protected-stock requests | 20 | `stock_polling` |
| 80%+ stock requests across 10+ requests | 10 | `stock_request_ratio` |
| 3+ rate-limit responses | 20 | `rate_limit_history` |
| 2+ action-proof replay attempts | 25 | `replay_activity` |
| 3+ invalid action proofs | 15 | `invalid_proof_activity` |
| 4+ transaction requests | 10 | `rapid_transaction_pattern` |

Levels: normal (0–19), elevated (20–39), suspicious (40–69), high (70–100). Reason codes and
scores are internal only — public responses never disclose thresholds or activity history.

## Response policy

Scoring is observational, not enforcement, at normal/elevated/suspicious levels. A high score
temporarily restricts action-proof, cart, and checkout requests with HTTP 429
`behaviour_restricted`; the restriction clears automatically as evidence ages out of the
window. Rate limiting, admission, signed proof, replay protection, and reservation remain
independent deterministic controls — see [traffic control](traffic-control.md) and
[transaction protection](transaction-protection.md). If behavioural state is unavailable, this
supplementary policy fails open (allows the request, records a state-failure metric) rather
than weakening those other controls.

## State and privacy

`InMemory` uses bounded per-actor event lists with `TimeProvider` expiry. Redis uses one
TTL-bound sorted set per HMAC-derived actor; Lua prunes and counts events atomically using
Redis server time. Redis stores only the derived actor key and random event members — no raw
client IDs, IPs, sessions, cookies, tokens, request bodies, or durable profile.

`/internal/metrics` exposes fixed aggregate observation, score-band, restriction, and
state-failure counts under the existing Development/Testing gate. There's intentionally no
per-actor lookup or score diagnostic endpoint.

## Limitations

This policy can miss slow or low-volume automation, and legitimate users can generate
suspicious-looking short bursts. It's deliberately conservative and is not evidence of bot
identity by itself. A real deployment would need retailer-approved threshold tuning, an appeal
path, and integration with authoritative commerce systems.
