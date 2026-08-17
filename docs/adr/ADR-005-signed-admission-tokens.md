# ADR-005: Use scoped HMAC admission proof with server-side lease checks

- Status: Accepted
- Date: 17 August 2026

## Context

Phase 6 uses an opaque session cookie and server-side admission state. The cookie identifies state but is not proof that the browser was admitted, can be copied, and cannot be verified locally across instances. DropShield needs short-lived proof bound to a protected drop and existing admission session without introducing a general authentication/JWT platform or weakening active-capacity semantics.

## Decision

Use a compact versioned `v1.payload.signature` token, with Base64Url UTF-8 JSON and HMAC-SHA256 over `v1.payload`. Claims are limited to payload version, key identifier, drop, HMAC-derived session binding, issued-at, and expiry. A dedicated HttpOnly cookie transports the token.

Every protected request with a token validates it locally using fixed-time comparisons. It then still evaluates Phase 6 admission state before forwarding. Missing proof proceeds to admission evaluation and an admitted session receives proof; invalid proof returns a safe admission-required response and is never forwarded.

Require a shared Base64 key of at least 32 bytes in Redis and non-controlled environments. Development/Testing InMemory may generate an ephemeral startup key. Include `kid` for future key rotation but keep one active verification key in this phase.

## Consequences

- Tampered, expired, wrong-drop, and wrong-session proof fails without a Redis token lookup.
- A token cannot by itself outlive or renew active admission capacity: every protected path that consumes a token (the stock route, and cart/checkout/action-proof issuance) re-evaluates live server-side admission state before proceeding, not only signature and expiry. Redis remains relevant for that state in multi-instance deployments.
- Same-key instances can verify each other's tokens.
- Browser-held proof remains vulnerable when an attacker steals the browser's whole live session.
- Tokens are deliberately reusable during their short validity period. Phase 8 replay controls, reservation, and transaction policy remain out of scope.

## Alternatives considered

### Sign only `admitted=true`

Rejected because it lacks drop and session scope and would make copied proof broadly reusable.

### JWT/OIDC infrastructure

Rejected because this is not identity or authentication and a compact fixed-purpose HMAC format has a smaller dependency and attack surface.

### Eliminate admission-state checks after token validation

Rejected because a signed token could outlive a server-side lease and cause active-capacity drift.

### Store issued tokens in Redis

Rejected because signature verification is naturally stateless and Redis is reserved for admission capacity, waiting state, and lease progression.
