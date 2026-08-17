# ADR-003: Optional Redis-backed traffic-policy state

- Status: Accepted
- Date: 17 August 2026

## Context

The Phase 3 fixed-window limiters are correct for one process but multiply their effective allowance when several independent DropShield instances receive the same traffic. Phase 5 needs shared atomic enforcement while retaining a zero-dependency local workflow.

## Decision

Keep the existing ASP.NET Core limiter unchanged for `InMemory` mode. In `Redis` mode, use a focused `IDistributedTrafficState` implementation backed by StackExchange.Redis and an atomic Lua fixed-window operation.

Redis mode distributes protected-stock client and aggregate limits plus cart and checkout client limits. Per-client Redis keys use HMAC-SHA256 partitions. Keys expire shortly after their current window. Redis unavailability fails closed with HTTP 503 and never falls back silently to local state.

Phase 4 telemetry remains per-instance and in memory because enforcement state and operational metrics have different lifecycle and aggregation requirements.

## Consequences

- Local development remains Redis-free by default.
- Multiple Redis-mode instances share one effective policy allowance.
- The custom Redis evaluator and built-in InMemory limiter must remain behaviorally aligned through shared configuration and regression tests.
- Redis becomes a required availability dependency only when explicitly selected.
- Deployment must supply and protect the Redis connection credentials and client-identity HMAC key.
- Redis-mode protected-stock rejections have exact per-client versus aggregate attribution; built-in InMemory attribution remains chain-level.

## Alternatives considered

### Always require Redis

Rejected. It would make the controlled local PoC and ordinary automated suite unnecessarily dependent on infrastructure.

### Automatically fall back to local limits

Rejected. A Redis outage would silently weaken the configured global allowance in a multi-instance deployment.

### Replace the InMemory limiter too

Rejected for this phase. The built-in limiter is working and provides the baseline behavior; replacing it would create avoidable regression risk.

### Non-atomic Redis GET/SET

Rejected because concurrent instances can lose increments and exceed the configured allowance.
