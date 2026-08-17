# Phase 8 — Cart and checkout replay protection

Phase 8 prevents a captured protected cart or checkout mutation from being submitted more than once. It adds action proof; it does not add inventory reservation, purchase limits, order persistence, payment behavior, or general idempotency.

## Proofs and flow

Admission proof remains reusable and means the current session may participate in the protected drop. An action proof is separate, short-lived, scoped to the same drop/session and one server-selected action, and is consumed once.

```text
POST /api/action-proofs/cart or /checkout
    valid Phase 7 admission cookie + proof required
        ↓
short-lived signed action token returned
        ↓
POST /api/cart or /api/checkout with X-DropShield-Action
        ↓
validate action scope → atomically consume → DemoStore
```

The action name comes from the issuance route; clients cannot request an arbitrary claim. Cart proof cannot authorize checkout and vice versa. Read routes remain unchanged.

## Token and consumption

Action proof uses the Phase 7 compact `v1.<base64url-payload>.<base64url-HMAC>` convention, HMAC-SHA256, UTF-8, Base64Url, UTC timestamps, fixed-time validation, and the shared configured signing key. Claims are version, key ID, drop, derived session binding, action name, random 256-bit action ID, issued-at, and expiry.

The random action ID is generated with .NET cryptographic randomness. It is never persisted directly. Validation derives an HMAC replay key, and only that derived value is consumed.

InMemory mode keeps a locked, expiry-pruned bounded marker set for one-process development. Redis mode uses one `SET NX PX` equivalent through StackExchange.Redis: the first instance sets the short-lived marker; concurrent or later attempts fail. Marker lifetime is the remaining action-token lifetime plus the configured cleanup margin. Redis keys are namespaced as `dropshield:v1:replay:<derived-id>` and contain only `1` with expiry.

## Response and failure contracts

- Missing or invalid admission proof: HTTP 403 `admission_required`.
- Missing, malformed, expired, changed, wrong-drop, wrong-session, or wrong-action proof: HTTP 403 `action_authorization_required`.
- Already consumed proof: HTTP 409 `action_already_used`.
- Replay state unavailable or locally bounded state exhausted: HTTP 503 `state_unavailable`.

All failures happen before DemoStore forwarding. Per-client cart/checkout rate controls run first and also cover the corresponding action-proof issuance endpoint.

## Metrics and limits

Internal metrics add aggregate `actionProofs` fields for cart/checkout tokens issued, validations, validation failures, cart/checkout actions consumed, replay rejection, and replay-state unavailability. They include no token, action ID, session, binding, or customer data. Logs use only fixed failure categories and never include proofs or identifiers.

Consumption occurs before origin forwarding. If the origin processes a mutation but the client loses the response, repeating the same action proof correctly returns replay conflict. A later phase would need idempotency keys and stored-result semantics to offer stronger retry recovery.
