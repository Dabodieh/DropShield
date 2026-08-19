# Cart, checkout, and inventory protection

Once a session is admitted, DropShield still needs to stop a captured cart/checkout request
from being replayed, and to prevent overselling a scarce drop. These two controls run in
order, after admission proof and before origin forwarding:

```text
valid admission proof
        ↓
one-time action proof (cart/checkout only)
        ↓
inventory reservation (cart reserves, checkout commits)
        ↓
DemoStore
```

## Action proof (replay protection)

Admission proof means "this session may participate in the drop." It does not stop a captured
`POST /api/cart` or `POST /api/checkout` request from being resubmitted. Action proof is a
separate, short-lived, one-time token scoped to a single action.

```text
POST /api/action-proofs/cart or /checkout   (requires valid admission proof)
        ↓
short-lived signed action token returned
        ↓
POST /api/cart or /api/checkout with X-DropShield-Action header
        ↓
validate scope → atomically consume → forward
```

The action name comes from the issuance route, not a client-supplied claim — a cart proof
can't authorize checkout or vice versa. Read routes are unaffected.

Action proof reuses the same compact `v1.<payload>.<hmac>` convention, HMAC-SHA256, UTF-8,
Base64Url, UTC timestamps, and fixed-time validation as admission proof, with its own claim
set: version, key ID, drop, derived session binding, action name, a random 256-bit action ID,
issued-at, expiry. The random action ID is never persisted directly — validation derives an
HMAC replay key, and only that derived value enters replay state.

`InMemory` mode keeps a locked, expiry-pruned bounded marker set for one-process development.
Redis mode uses `SET NX PX` (via StackExchange.Redis): the first instance to set the marker
wins; concurrent or later attempts fail. Marker lifetime is the remaining token lifetime plus
a configured cleanup margin. Redis keys are `dropshield:v1:replay:<derived-id>`, value `1`,//
expiry-only.

Failure contract:

| Condition | Response |
|---|---|
| Missing/invalid admission proof | HTTP 403 `admission_required` |
| Missing, malformed, expired, wrong-drop/session/action proof | HTTP 403 `action_authorization_required` |
| Already-consumed proof | HTTP 409 `action_already_used` |
| Replay state unavailable / bounded state exhausted | HTTP 503 `state_unavailable` |

All of these happen before DemoStore forwarding. Consumption happens before the origin call:
if DemoStore processes a mutation but the client loses the response, resubmitting the same
proof correctly returns a replay conflict rather than double-applying the mutation. Recovering
that case for the client would need idempotency keys and stored-result semantics, which
DropShield doesn't implement.

**Known limitation — proof consumption precedes origin completion.** Because the action proof
is atomically consumed before the origin request is made (not after it succeeds), a *transient*
origin failure — a timeout, a 5xx, a dropped connection — also burns the proof, even though no
mutation reached the origin. The shopper must obtain a new admission and action proof to retry,
rather than the same proof simply being retried. This is an availability/UX limitation, not a
security defect: it does not allow replay, does not allow bypassing admission, and does not
allow a stale or forged proof to succeed. A production design carrying real purchase risk would
likely want a more sophisticated retry/compensation strategy — for example a pending/committed
reservation state with idempotency-keyed origin calls, so a retried request can resume rather
than restart. That is future design work, not implemented here.

## Inventory reservation

DropShield maintains a configurable synthetic reservation pool for the protected drop. It is
a local proof-of-concept control — not Adobe Commerce's or any retailer's authoritative
inventory system.

```json
{
  "DropShield": {
    "InventoryReservation": {
      "Enabled": true,
      "InitialStock": 500,
      "ReservationTtlSeconds": 300,
      "MaximumInMemoryReservations": 100000
    }
  }
}
```

A valid cart action (already past admission and one-time action proof) reserves one unit. The
ledger tracks `available`, `reserved`, and `committed`, preserving
`available + reserved + committed = InitialStock`. A session holds at most one active
reservation per drop; a second cart attempt returns HTTP 409 `reservation_exists`. Out-of-stock
returns HTTP 409 `out_of_stock` and never reaches DemoStore. If forwarding a cart reservation
fails, DropShield performs a best-effort compensating release.

Checkout requires the calling session's active reservation (HTTP 409 `reservation_required`
otherwise); a successful DemoStore checkout commits the unit, a failed one leaves the
reservation intact until it expires or a later checkout succeeds. There's no distributed
transaction with DemoStore: a successful origin checkout followed by a reservation-state
failure cannot be made atomically consistent here, so DropShield reports that as a failure
rather than claiming coordination it doesn't have. Reservation TTL is constrained not to
exceed the admission-session TTL, so a reservation can't outlive the session that created it.

Reservations are owned by an HMAC-derived admission-session value — no raw cookies, tokens, or
customer data. `InMemory` uses one lock around pruning and every transition, with
`TimeProvider`-driven lazy expiry. Redis uses a co-located hash for the three counters plus a
sorted set of HMAC-derived owners scored by expiry; one Lua operation prunes expired members,
returns their units to `available`, then performs the requested transition atomically.

`GET /internal/inventory` (Development/Testing diagnostics gate) exposes aggregate counts
only. `/internal/metrics` adds fixed reservation counters: created, reused, released, expired,
committed, out-of-stock-rejected, state failures.

For a real integration, authoritative stock allocation must stay coordinated with the
commerce platform's own inventory/ERP systems — see [Adobe Commerce](adobe-commerce.md). This
ledger is not a production source of truth.
