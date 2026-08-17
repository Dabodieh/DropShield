# Adobe Commerce integration

DropShield's protection primitives (rate limiting, admission, admission proof, action proof,
replay protection, reservation state, behavioural scoring, distributed state) run entirely in
`DropShield.Api`. Adobe Commerce / Magento 2 owns catalogue, quotes/carts, customer/session
semantics, checkout, and orders. `DropShield_Connector` is a thin Magento module that lets
Commerce verify a request already passed DropShield, without reimplementing any DropShield
control in PHP.

## Architecture

```text
Client -> DropShield.Api (rate/admission/proof/replay/reservation/behaviour)
       -> origin assertion issued (only after all checks pass)
       -> Adobe Commerce (DropShield_Connector verifies the assertion)
       -> normal Commerce cart/checkout processing
```

Edge-provider integration (Fastly/Cloudflare/Akamai) is out of scope here. A production
deployment should still restrict direct network access to the Commerce origin where
infrastructure permits; the connector is application-level defence in depth, not a replacement
for that.

## Origin assertion v1

A DropShield-internal, short-lived, HMAC-SHA256-signed proof that a protected cart/checkout
mutation already passed every upstream DropShield control. It is unrelated to browser-facing
admission/action tokens and is never accepted from a client.

Wire format: `v1.<base64url-payload>.<base64url-signature>`

Payload fields: `v, kid, drop, action, method, route, bodyHash, jti, iat, exp`. `bodyHash` is
`base64url(SHA-256(raw forwarded body))`, binding the assertion to the exact mutation it was
issued for. Excluded: customer names, email, address, payment details, raw cookies, IP
addresses, admission tokens, action tokens.

Full contract and a deterministic cross-language test vector: `contracts/origin-assertion-v1.json`.
The contract's `routes` object is the single source of truth for the canonical `"POST /api/cart"`
/ `"POST /api/checkout"` route literals that must match byte-for-byte between
`TrafficRouteClassifier.GetRouteTemplate` (C#) and each plugin's `ROUTE` constant (PHP) — both
sides have a test asserting their literal against this contract file, so a future rename on
either side fails a test instead of only failing at runtime against a real Magento instance.

### DropShield.Api side

- `Origin/OriginAssertionService` issues and validates assertions; `Origin/OriginAssertionSigningKeyProvider`
  holds a dedicated key (`DropShield:OriginAssertions:SigningKey`, Base64, >= 32 random bytes),
  separate from the admission token key.
- Assertions are issued only in `DemoStoreForwarder`, only for `Cart`/`Checkout` routes, only
  after the full existing middleware chain (rate limit, admission, admission proof, action
  proof, replay consumption, reservation) has already allowed the request. Issuance failure
  fails closed (503); the mutation is never forwarded without proof.
- `TrafficMetricsMiddleware` unconditionally strips any client-supplied
  `X-DropShield-Origin-Assertion` header before route classification, so a forged header can
  never reach the origin.
- Lifetime is configurable and bounded to 1-30 seconds (`DropShield:OriginAssertions:LifetimeSeconds`).

## Adobe Commerce connector

`integrations/adobe-commerce/DropShield_Connector` — a Composer-installable Magento 2 module,
namespace `DropShield\Connector`.

```text
DropShield_Connector/
├── composer.json
├── registration.php
├── etc/{module.xml,di.xml,config.xml,acl.xml,adminhtml/system.xml}
├── Model/        (config, protected-drop resolver, pure crypto validator, guard)
├── Plugin/       (cart and checkout interception)
└── Test/Unit/    (PHPUnit)
```

### Extension points

- `Magento\Quote\Api\CartItemRepositoryInterface::save` — `beforeSave` plugin
  (`Plugin/CartItemRepositoryPlugin`). This is the documented public service contract behind
  both the REST cart-items endpoint and GraphQL add-to-cart resolvers.
- `Magento\Quote\Api\CartManagementInterface::placeOrder` — `beforePlaceOrder` plugin
  (`Plugin/CartManagementPlugin`). REST order placement, the GraphQL `placeOrder` mutation, and
  storefront one-page checkout (via `PaymentInformationManagementInterface::savePaymentInformationAndPlaceOrder`)
  all converge on this call to convert a quote into an order, so a single interception point
  covers all three surfaces without three separate implementations.

Both are `before` plugins, not `around`: they only need to validate-then-throw or pass through,
so the least invasive supported mechanism was chosen deliberately.

Not yet covered by the reference connector: multi-shipment/multi-address checkout flows that
bypass `CartManagementInterface::placeOrder` entirely, and any custom checkout extension that
places orders through a different, non-standard path. Treat any such flow as unprotected until
verified.

### Protected SKU configuration

This connector supports exactly one active protected drop, matching DropShield.Api's
single-value `Admission:ProtectedProduct`. `Stores > Configuration > DropShield Connector >
General > Protected Drop ID` configures that drop identifier, and `Protected SKUs`
(comma-separated) configures every SKU that maps to it — multiple SKUs can share the one drop,
but the connector does not sign or validate assertions for more than one drop at a time.
`pokemon-etb` is sample data for the PoC only. `ProtectedDropResolver` is the single place both
decisions are made (which SKUs are protected, and what drop ID they map to); ordinary SKUs are
never routed through DropShield semantics.

A protected SKU whose configured Drop ID does not match DropShield.Api's
`Admission:ProtectedProduct` will fail closed: DropShield never signs an assertion for a drop
it isn't configured to protect, so every mutation for a misconfigured SKU is permanently
rejected. This is a configuration error, not a security gap — check both sides agree on the
same drop identifier before relying on multi-SKU protection.

### Cart and checkout enforcement

- Cart: if the SKU being added is protected and the request carries no valid origin assertion,
  `CartItemRepositoryPlugin` throws `AuthorizationRequiredException` before the item is saved.
- Checkout: if the quote being placed contains a protected SKU and the request carries no valid
  origin assertion, `CartManagementPlugin` throws before the order is placed. Quotes with no
  protected SKU are unaffected.
- Error contract: always `DropShield authorization required.` via a `LocalizedException`
  subclass, so REST, GraphQL, and storefront checkout each surface it through their normal
  Magento error handling. No cryptographic detail, key material, or assertion content is ever
  included in the message or logged.

### Configuration

`Stores > Configuration > DropShield Connector`: Enabled, Protected SKUs, internal header name,
signing key ID, Base64 shared secret (stored via Magento's encrypted config backend), clock
tolerance. The shared secret must match `DropShield:OriginAssertions:SigningKey` and must be set
through environment-scoped deployment configuration, never committed or exported in plain
`app/etc/config.php`.

### REST / GraphQL / storefront coverage

Because both extension points are on `Quote` service contracts rather than specific controllers,
REST (`/V1/carts/mine/items`, `/V1/carts/mine/order`), GraphQL (add-to-cart resolvers,
`placeOrder`), and storefront one-page checkout all converge on the same enforcement. Coverage
has not been independently verified against a running Magento instance (see Limitations).

## Limitations

- No local Magento/Adobe Commerce runtime was available or brought up during development. The
  PHP crypto core (`OriginAssertionValidator`) was verified directly against the same
  deterministic test vector the C# suite uses, executed in a disposable `php:8.2-cli` container
  — it accepted the valid vector and correctly rejected a tampered token and a body-hash
  mismatch. Full PHPUnit execution through Composer was blocked locally by a transient GitHub
  codeload rate limit inside that container, not by a problem in the module. Runtime Magento
  wiring (plugin registration actually firing, admin config screen, REST/GraphQL end-to-end) is
  therefore unverified and should be validated against a real instance before any further use.
- Composer constraints target currently supported Magento 2 / Adobe Commerce component versions
  (`magento/framework ^103.0`, `magento/module-quote ^101.0`, `magento/module-checkout ^100.4`);
  no claim of compatibility with older/EOL Magento releases is made.
- Webhooks and App Builder are not used; documented only as possible future options for
  Commerce lifecycle events, not a current dependency.
- Multi-shipment/multi-address checkout and any non-standard order-placement path are not
  covered — see "Extension points" above.
