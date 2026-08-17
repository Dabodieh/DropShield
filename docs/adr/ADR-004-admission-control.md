# ADR-004: Separate admission capacity from abuse rate limiting

- Status: Accepted
- Date: 17 August 2026

## Context

The Phase 3 protected-stock aggregate limiter protects one origin request rate but cannot distinguish an otherwise eligible shopper from an aggressive client once the shared allowance is exhausted. Phase 6 needs to hold eligible sessions outside the origin and progressively admit them without replacing per-client abuse controls or implementing Phase 7 signed proof.

## Decision

Run per-client rate limiting before admission. When admission is enabled for its configured protected product, skip the old protected-stock aggregate rate window and use a separate `IAdmissionState` capacity decision instead.

Admission uses a bounded active-session set, bounded waiting set, session and waiting TTLs, FIFO join order, and a fixed admission-batch interval. The InMemory provider supports one-process development. Redis uses one Lua operation and co-located sorted-set/hash keys to coordinate pruning, queue insertion, batch enforcement, and promotion across instances.

An opaque random HttpOnly cookie identifies the polling session. It contains no admission status. Redis sees only an HMAC-SHA256 member. Waiting responses are HTTP 202 with a retry interval and no exact position.

## Consequences

- Aggressive per-client polling still receives the existing HTTP 429 response.
- Eligible excess sessions receive waiting responses without reaching the origin.
- Active and waiting state disappear after bounded inactivity and cannot grow beyond configured limits.
- Redis mode provides cross-instance consistency; InMemory mode is intentionally instance-local.
- The unsigned cookie can be copied or replayed and is not an admission credential. Phase 7 must add signed admission proof before treating client-carried state as trustworthy.
- Queue order is best-effort FIFO among live pollers; no exact queue position or strict fairness guarantee is exposed.

## Alternatives considered

### Keep the aggregate limiter as the only capacity control

Rejected because legitimate excess traffic would continue receiving indiscriminate 429 responses rather than waiting.

### Keep both aggregate rate and admission capacity enabled

Rejected for the configured drop because the aggregate limiter could reject a session before admission evaluates it, creating conflicting capacity boundaries.

### Issue signed admission tokens now

Rejected because signing, verification, replay handling, and key rotation belong to Phase 7.

### Store an unbounded permanent queue

Rejected because abandoned sessions would accumulate and create unsafe memory/state growth.
