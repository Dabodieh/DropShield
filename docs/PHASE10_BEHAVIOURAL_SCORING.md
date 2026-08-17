# Phase 10 — Behavioural Bot Scoring

Phase 10 adds an explainable, short-lived behavioural signal for the protected drop. It is
a demonstration policy, not an AI detector, production retailer configuration, long-term
reputation service, or identity/fingerprinting system.

## Evidence and score

Each HMAC-derived actor has a rolling 60-second evidence window. State expires after 120
seconds of inactivity. The score is recalculated from current bounded counts, never
unboundedly accumulated, and is capped at 100.

| Signal | Fixed contribution | Reason code |
| --- | ---: | --- |
| 8+ protected-stock requests | 20 | stock_polling |
| 80%+ stock requests across 10+ requests | 10 | stock_request_ratio |
| 3+ rate-limit responses | 20 | rate_limit_history |
| 2+ action-proof replay attempts | 25 | replay_activity |
| 3+ invalid action proofs | 15 | invalid_proof_activity |
| 4+ transaction requests | 10 | rapid_transaction_pattern |

The levels are normal (0–19), elevated (20–39), suspicious (40–69), and high
(70–100). Reason codes and scores are internal-only; public responses do not disclose
thresholds or activity history.

## Separate response policy

Scoring does not make enforcement decisions. The policy layer observes normal, elevated,
and suspicious scores without changing requests. A high score temporarily restricts only
action-proof, cart, and checkout requests with HTTP 429 behaviour_restricted; the
existing window automatically clears the restriction as evidence expires.

Rate limits, admission, signed proof, replay protection, and reservation controls remain
independent deterministic controls. If behavioural state is unavailable, the supplementary
policy allows the request and records an aggregate state-failure metric; it does not weaken
those existing controls.

## State and privacy

InMemory state uses bounded per-actor event lists and TimeProvider expiry. Redis uses one
TTL-bound sorted set per HMAC-derived actor, with Lua using Redis server time to prune and
count events atomically across instances. Redis stores only the derived actor key and random
event members; it does not retain raw client IDs, IPs, sessions, cookies, tokens, request
bodies, customer data, or a durable profile. Expiry timestamps are an internal Redis
implementation detail and are never exposed by diagnostics or public responses.

/internal/metrics exposes fixed aggregate observation, score-band, restriction, and
state-failure counts under the existing Development/Testing gate. There is intentionally
no actor lookup or score diagnostic endpoint.

## Limitations

This policy can miss slow or low-volume automation, while legitimate users can generate
suspicious-looking short bursts. It is therefore deliberately conservative and is not
evidence of bot identity. A real deployment would need retailer-approved policy tuning,
appeal/support processes, and authoritative commerce integration.
