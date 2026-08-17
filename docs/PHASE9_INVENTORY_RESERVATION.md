# Phase 9 — Inventory Reservation

DropShield now maintains a configurable synthetic reservation pool for the protected
`pokemon-etb` drop. It is a local PoC control, not Hamleys', Adobe Commerce's, or any
retailer's authoritative inventory system.

## Lifecycle

A valid admitted cart action with a valid one-time action proof reserves one unit.
The ledger tracks `available`, `reserved`, and `committed`, preserving
`available + reserved + committed = InitialStock`. A session has at most one active
reservation per drop. A second valid cart action returns HTTP 409 `reservation_exists`.

Reservations are owned by an HMAC-derived admission-session value; raw session cookies,
tokens, addresses, and customer data are not placed in reservation state. The default
pool is configured through `DropShield:InventoryReservation`:

```json
{
  "Enabled": true,
  "InitialStock": 500,
  "ReservationTtlSeconds": 300,
  "MaximumInMemoryReservations": 100000
}
```

The reservation TTL is constrained not to exceed the admission-session lifetime, so a
reservation does not become an admission bypass.

## Request handling

The protected path is rate limit, admission proof, action-proof consumption, reservation,
then DemoStore forwarding. Invalid, replayed, or rate-limited mutations never allocate
stock. Out-of-stock cart requests return HTTP 409 `out_of_stock` and do not reach
DemoStore. If cart forwarding fails after a new reservation, DropShield performs a
best-effort compensating release.

Checkout requires the calling admission session's active reservation; otherwise it returns
HTTP 409 `reservation_required`. A successful synthetic origin checkout commits the unit.
A failed checkout retains the reservation until it expires or a later successful checkout.

There is no distributed transaction with DemoStore. In particular, a successful origin
checkout followed by a reservation-state failure cannot be made atomically consistent by
this PoC; DropShield fails closed rather than claiming ACID coordination. The existing
action proof is still consumed before the origin response, so lost-response retry recovery
remains a future idempotency concern.

## State implementations

The InMemory implementation uses one lock around pruning and every state transition. It is
bounded and uses `TimeProvider` for lazy expiry.

Redis uses a co-located, versioned-prefix hash for the three counters and a sorted set of
HMAC-derived owners scored by expiry. Every Lua operation uses Redis server time, removes
expired sorted-set members, returns their units to `available`, then performs the requested
reserve/read/release/commit transition atomically. It does not depend on Redis keyspace
notifications. Redis stores no raw identity, cookie, admission token, action token, cart,
or customer data.

## Diagnostics

`GET /internal/inventory` is available only under the existing Development/Testing internal
diagnostics policy. It exposes aggregate current counts only. `/internal/metrics` adds fixed
reservation counters: created, reused, released, expired, committed, out-of-stock rejected,
and state failures.

For a real commerce integration, authoritative allocation must remain coordinated with Adobe
Commerce and the retailer's inventory/ERP systems. This synthetic ledger must not be treated
as a production source of truth.
