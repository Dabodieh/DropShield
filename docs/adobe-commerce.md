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
The contract's `routes` object is the single source of truth for the canonical route literals
each transport binds to: `"POST /api/cart"`, `"POST /api/checkout"`, `"POST /graphql"`, and
`"POST /checkout/cart/add"` — all four are now issued by `DropShield.Api` and validated by the
connector. Every plugin's `ROUTE` constant is checked against this file in
`OriginAssertionRouteContractTest` (PHP), and all four are also checked against
`TrafficRouteClassifier.GetRouteTemplate` (C#) in `OriginAssertionContractTests`, so a future
rename on either side fails a test instead of only failing at runtime.

### DropShield.Api side

- `Origin/OriginAssertionService` issues and validates assertions; `Origin/OriginAssertionSigningKeyProvider`
  holds a dedicated key (`DropShield:OriginAssertions:SigningKey`, Base64, >= 32 random bytes),
  separate from the admission token key.
- `TrafficRouteClassifier` recognises four mutation routes: `POST /api/cart`,
  `POST /api/checkout`, `POST /graphql`, `POST /checkout/cart/add`. `DemoStoreForwarder` issues
  an assertion for all four only after the full existing middleware chain (rate limit,
  admission, admission proof, action proof, replay consumption, reservation) has already
  allowed the request. Issuance failure fails closed (503); the mutation is never forwarded
  without proof.
- `/graphql` is a shared endpoint (catalogue queries, customer data, and cart-add for both
  protected and ordinary SKUs all arrive there), so it is not unconditionally treated as a
  protected mutation. `Traffic/GraphQlCartMutationInspector` inspects the request body once
  (the `{query, variables, operationName}` JSON envelope) to determine whether the document
  invokes `addProductsToCart` for the configured protected drop; the result is cached on the
  request (`TrafficRequestObservation.IsProtectedGraphQlCartMutation`) so every downstream
  policy check reads the same decision instead of re-parsing. This is a bounded structural
  check, not a GraphQL parser — it does not validate or execute the document, only detects the
  one supported mutation shape. `/checkout/cart/add` has no such ambiguity and is always
  treated as a mutation, matching REST's existing `/api/cart` precedent.
- `TrafficMetricsMiddleware` unconditionally strips any client-supplied
  `X-DropShield-Origin-Assertion` header before route classification, so a forged header can
  never reach the origin — verified for all four mutation routes.
- The raw request body is read once via `Traffic/RequestBodyReader` (buffered, stream rewound
  after each read) and forwarded unchanged; the same bytes are hashed into the assertion and
  sent to the origin. Nothing parses and reserializes the body — GraphQL JSON and storefront
  form data are both forwarded exactly as received.
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
├── Plugin/       (cart-add and checkout interception, one plugin per transport)
└── Test/Unit/    (PHPUnit)
```

### Extension points

REST, GraphQL, and the storefront controller do **not** converge on one shared cart-add service
contract — each is a genuinely separate Magento code path, confirmed by reading the actual
Mage-OS 3.0.0 source before picking an extension point (not assumed). Checkout is the one
operation that *does* converge on a single call.

Cart-add, one plugin per transport:

- `Magento\Quote\Api\CartItemRepositoryInterface::save` — `beforeSave` plugin
  (`Plugin/CartItemRepositoryPlugin`). What the REST cart-items endpoint
  (`POST /rest/V1/{guest-,}carts/.../items`) calls.
- `Magento\QuoteGraphQl\Model\Cart\AddProductsToCart::execute` — `beforeExecute` plugin
  (`Plugin/QuoteGraphQlAddProductsToCartPlugin`). What the `addSimpleProductsToCart` /
  `addConfigurableProductsToCart` GraphQL resolvers call; this class mutates the `Quote` object
  directly and saves it via `CartRepositoryInterface`, never reaching
  `CartItemRepositoryInterface::save`.
- `Magento\Checkout\Model\AddProductToCart::execute` — `beforeExecute` plugin
  (`Plugin/CheckoutAddProductToCartPlugin`). What the storefront `checkout/cart/add` controller
  calls; a third, distinct class from both of the above.

All three plugins route into the same shared `OriginAssertionGuard`/`ProtectedDropResolver`
logic — no GraphQL-specific security logic and no second assertion validator were added. Each
plugin only differs in which SKUs it can see and which literal `route` claim it checks
(`"POST /api/cart"`, `"POST /graphql"`, `"POST /checkout/cart/add"` respectively), since the
assertion's route binding must match the transport that actually received the request.

Checkout, one plugin covers all three transports:

- `Magento\Quote\Api\CartManagementInterface::placeOrder` — `beforePlaceOrder` plugin
  (`Plugin/CartManagementPlugin`). REST guest/customer checkout, the GraphQL `placeOrder`
  mutation, and storefront one-page checkout (via
  `PaymentInformationManagementInterface::savePaymentInformationAndPlaceOrder`) all genuinely
  converge on this one call to convert a quote into an order. Confirmed by runtime testing:
  REST and GraphQL checkout are both correctly rejected without a valid assertion and correctly
  succeed with one. This plugin was not changed by the cart-add fix.

Every plugin is a `before` plugin, not `around`: each only needs to validate-then-throw or pass
through, so the least invasive supported mechanism was used throughout.

No double enforcement: the three cart-add plugins sit on three disjoint classes with no shared
call path, so a single incoming request can only ever trigger exactly one of them. Confirmed by
runtime testing — REST cart-add, REST checkout, and GraphQL checkout all still behave exactly
as before the cart-add fix.

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
  the transport-appropriate plugin (`CartItemRepositoryPlugin` for REST,
  `QuoteGraphQlAddProductsToCartPlugin` for GraphQL, `CheckoutAddProductToCartPlugin` for the
  storefront) throws `AuthorizationRequiredException` before the item is added.
- Checkout: if the quote being placed contains a protected SKU and the request carries no valid
  origin assertion, `CartManagementPlugin` throws before the order is placed, for all three
  transports. Quotes with no protected SKU are unaffected.
- Error contract: always `DropShield authorization required.` via a `LocalizedException`
  subclass. REST and storefront checkout surface it through Magento's normal error handling;
  GraphQL wraps a plain `LocalizedException` as a generic `"Internal server error"` rather than
  a `graphql-input`-category error (confirmed by runtime testing) — the request is still
  correctly rejected and no mutation occurs, but the client-facing message is less specific on
  GraphQL than on REST/storefront. No cryptographic detail, key material, or assertion content
  is ever included in the message or logged.

### Configuration

`Stores > Configuration > DropShield Connector`: Enabled, Protected SKUs, internal header name,
signing key ID, Base64 shared secret (stored via Magento's encrypted config backend), clock
tolerance. The shared secret must match `DropShield:OriginAssertions:SigningKey` and must be set
through environment-scoped deployment configuration, never committed or exported in plain
`app/etc/config.php`.

### REST / GraphQL / storefront coverage

| Surface | Cart-add | Checkout |
|---|---|---|
| REST (`guest-carts/{id}/items`, `guest-carts/{id}/payment-information`) | Protected — verified over HTTP | Protected — verified over HTTP |
| GraphQL (`addSimpleProductsToCart`, `placeOrder`) | Protected — verified over HTTP | Protected — verified over HTTP |
| Storefront (`checkout/cart/add`, one-page checkout) | Plugin confirmed to attach and reject via direct service invocation (real DI, real `AddProductToCart`); full HTTP round-trip (including a valid-assertion success case) not verified — Magento's storefront form-key CSRF layer blocked an end-to-end curl-based test; unrelated to the connector | Uses the same `CartManagementInterface::placeOrder` call as REST/GraphQL, so expected protected; not independently runtime-tested |

GraphQL cart-add is covered by `QuoteGraphQlAddProductsToCartPlugin` (see "Extension points").
Its origin assertion binds to the *real* GraphQL request: `route = "POST /graphql"` and
`bodyHash` over the raw GraphQL request body (the query/variables JSON), not a fabricated
REST-shaped payload — the route/body binding was verified to match exactly, not weakened.

**DropShield.Api now issues assertions shaped for both routes.** `TrafficRouteClassifier`
recognises `POST /graphql` (`TrafficRoute.GraphQlCartAdd`) and `POST /checkout/cart/add`
(`TrafficRoute.StorefrontCartAdd`), and `DemoStoreForwarder` issues an assertion for both after
the same pipeline that already gates `/api/cart` (rate policy, admission, action proof, replay,
reservation). Since `/graphql` is a shared endpoint serving catalogue queries, customer data,
and cart-add for both protected and ordinary SKUs, `GraphQlCartMutationInspector` inspects the
request body once (JSON envelope `{query, variables, operationName}`) to determine whether the
document is an `addProductsToCart` mutation targeting the configured protected drop — a bounded
structural check, not a GraphQL parser or execution engine. Ordinary GraphQL traffic on the same
endpoint never enters the protected pipeline and is forwarded unassisted. `/checkout/cart/add`
has no such ambiguity (it is only ever a cart-add attempt), so it is treated unconditionally,
matching REST's existing precedent for `/api/cart`. See `docs/traffic-control.md` if the
pipeline order needs review — this fix does not change it, only which routes enter it.

## Verified against a live Magento instance

Tested against **Mage-OS 3.0.0** (Magento Open Source-compatible community fork, based on
Magento 2.4.9), installed keylessly via `markshust/docker-magento` pointed at
`https://repo.mage-os.org/` (no Adobe Marketplace account or auth keys used, per project
policy), PHP 8.5.6. Disposable, local-only; no k6, no third-party traffic.

Confirmed end to end over real HTTP against the running instance:

- Ordinary SKU added to cart with no assertion header (REST and GraphQL) — succeeds, connector
  does not intervene.
- Protected SKU added to cart with no assertion header (REST and GraphQL) — rejected,
  `DropShield authorization required.` (GraphQL surfaces it as a generic error; see "Cart and
  checkout enforcement" above).
- Protected SKU added to cart with a valid signed assertion (REST and GraphQL) — succeeds;
  confirmed the item is genuinely present on the quote via a follow-up `cart` query, not just a
  successful mutation response.
- Protected checkout (REST guest `payment-information`) with no assertion — rejected; with a
  valid assertion — succeeds, a real order is placed and persists (verified via
  `GET /rest/V1/orders/{id}`).
- Protected checkout via the GraphQL `placeOrder` mutation with no assertion — rejected, order
  count unchanged.
- Storefront cart-add: the real `Magento\Checkout\Model\AddProductToCart` service (resolved
  through Magento's own DI, so the plugin is genuinely attached) rejected a protected SKU with
  no assertion when invoked directly, bypassing only the HTTP/session/CSRF layer. A full
  browser-equivalent HTTP round-trip was not achieved — see the coverage table.

Two real defects were found and fixed by this testing, independent of the coverage gap above:

1. **`Config::getSigningKeyBase64()` returned Magento's raw encrypted config value, not the
   decrypted key.** The `signing_key` field uses Magento's `Encrypted` config backend, which
   only decrypts automatically when read through the admin config-section model — every other
   reader (this connector included) must call `EncryptorInterface::decrypt()` explicitly, the
   same pattern core modules use for their own encrypted config values. Without this fix, every
   assertion validation failed regardless of correctness. Fixed by decrypting in `Config.php`.
2. **`OriginAssertionGuard` and both plugins type-hinted the request parameter as
   `Magento\Framework\App\RequestInterface`**, which does not declare `getHeader()`,
   `getContent()`, or `getMethod()` — the three methods the guard actually calls. This worked
   at runtime only because Magento's DI always resolves the interface to the concrete
   `Magento\Framework\App\Request\Http`, which does implement them; it is not guaranteed by the
   type declaration and broke unit-test mocking under a current PHPUnit version (mocking an
   interface for a method it doesn't declare is now a hard error). Fixed by type-hinting
   `Http` directly, matching common practice for Magento classes that need HTTP-specific
   request methods.

Both fixes remain in place and were not regressed by the GraphQL/storefront cart-add fix. Full
suite: 23/23 passing (17 from the original validation pass, plus 6 new: 4 for the two new
plugins, 2 for the extended route contract), run against the real Magento-provided PHPUnit and
autoloader inside the test instance above.

The connector's PHPUnit suite is not run in this repository's CI: it depends on Magento's own
autoloader and test bootstrap, which only exist inside an installed Magento/Mage-OS instance.
Reproducing that in CI would mean installing a full Magento stack on every run, which is outside
what this project's CI aims to do. The suite has been run against a real instance as described
above; running it again requires the same local Mage-OS setup.

## Limitations

- Storefront cart-add's full HTTP round-trip (including a valid-assertion success case through
  the actual controller) remains unverified — see the coverage table above. Storefront checkout
  is expected to behave like REST/GraphQL since it shares the same
  `CartManagementInterface::placeOrder` call, but this has not been directly observed either.
- Multi-shipment/multi-address checkout and any non-standard order-placement path remain
  unverified — see "Extension points" above.
- Composer constraints (`magento/framework ^103.0`, `magento/module-quote ^101.0`,
  `magento/module-checkout ^100.4`) target currently supported Magento 2 / Adobe Commerce
  component versions; the runtime test above needed `--ignore-platform-req=php` because Mage-OS
  3.0.0 itself requires PHP 8.3-8.5 while this connector's `composer.json` declares `~8.1.0
  ||~8.2.0||~8.3.0` — the two ranges only overlap at 8.3. No incompatibility with PHP 8.4/8.5 was
  found in practice; the declared constraint is narrower than necessary and worth widening in a
  separate change.
- Webhooks and App Builder are not used; documented only as possible future options for
  Commerce lifecycle events, not a current dependency.
