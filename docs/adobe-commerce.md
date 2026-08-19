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
The contract documents both legacy DemoStore literals and the Commerce REST templates. Commerce
REST assertions deliberately bind the **concrete** request path, including the opaque guest-cart
ID (for example `POST /rest/V1/guest-carts/abc/items`); the documented `{cartId}` templates are
not signed literally. `OriginAssertionRouteContractTest` (PHP) checks that the connector derives
the method/path from Magento's actual request, while `OriginAssertionContractTests` (C#) checks
the strict matcher templates against the shared contract.

### DropShield.Api side

- `Origin/OriginAssertionService` issues and validates assertions; `Origin/OriginAssertionSigningKeyProvider`
  holds a dedicated key (`DropShield:OriginAssertions:SigningKey`, Base64, >= 32 random bytes),
  separate from the admission token key.
- `OriginMode` is explicit. The default `DemoStore` behaviour is retained. `AdobeCommerce` only
  exposes these additional protected routes: `POST /rest/V1/guest-carts/{cartId}/items`,
  `POST /rest/default/V1/guest-carts/{cartId}/items`, and their corresponding
  `payment-information` checkout routes. Unsupported Magento REST paths remain 404 at
  DropShield; this is not a general Commerce proxy.
- `DemoStoreForwarder` issues an assertion only after the existing middleware chain (rate limit,
  admission, admission proof, action proof, replay consumption, reservation) has allowed a
  protected operation. In Commerce mode it forwards only a narrow safe header set and Commerce
  session cookies, strips `DropShield.*` cookies and client-supplied assertion material, and
  returns `Set-Cookie`, `Location`, and Magento store/vary response headers needed by a browser.
  Issuance failure fails closed (503).
- The existing `/api/products/{drop}/stock` admission entry point remains DropShield-owned in
  Commerce mode, backed by its reservation capacity. It is not translated into a fictitious
  Magento catalogue endpoint.
- `/graphql` is a shared endpoint (catalogue queries, customer data, and cart-add for both
  protected and ordinary SKUs all arrive there), so it is not unconditionally treated as a
  protected mutation. `Traffic/GraphQlCartMutationInspector` inspects the request body once
  (the `{query, variables, operationName}` JSON envelope) to determine whether the document
  invokes `addSimpleProductsToCart`, `addVirtualProductsToCart`, or `addProductsToCart` for the
  configured protected drop;
  the result is cached on the
  request (`TrafficRequestObservation.IsProtectedGraphQlCartMutation`) so every downstream
  policy check reads the same decision instead of re-parsing. This is a bounded structural
  check, not a GraphQL parser — it does not validate or execute the document, only detects the
  supported protected cart-add mutation shape. `/checkout/cart/add` has no such ambiguity and is
  always
  treated as a mutation, matching REST's existing `/api/cart` precedent.
- `TrafficMetricsMiddleware` unconditionally strips any client-supplied
  `X-DropShield-Origin-Assertion` header before route classification, so a forged header can
  never reach the origin. Protected Commerce and GraphQL bodies are read once with a configurable
  4 KiB–1 MiB cap (default 256 KiB); oversized protected bodies receive 413 before forwarding.
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
  (`Plugin/QuoteGraphQlAddProductsToCartPlugin`) for the legacy
  `addSimpleProductsToCart` / `addVirtualProductsToCart` resolver path.
- `Magento\QuoteGraphQl\Model\AddProductsToCart::execute` — `beforeExecute` plugin
  (`Plugin/ModernGraphQlAddProductsToCartPlugin`) for Mage-OS 3.0.0's modern
  `addProductsToCart` mutation. This is separate from the legacy class; leaving it on the old
  interceptor would silently leave the recommended mutation unguarded.
- `Magento\Checkout\Model\AddProductToCart::execute` — `beforeExecute` plugin
  (`Plugin/CheckoutAddProductToCartPlugin`). What the storefront `checkout/cart/add` controller
  calls; a third, distinct class from both of the above.

All cart plugins route into the same shared `OriginAssertionGuard`/`ProtectedDropResolver`
logic — no GraphQL-specific crypto validator was added. Each uses the actual incoming method and
path for the route claim, so REST guest-cart identifiers, GraphQL, and storefront request shapes
cannot be substituted for each other.

Checkout, one plugin covers all three transports:

- `Magento\Quote\Api\CartManagementInterface::placeOrder` — `beforePlaceOrder` plugin
  (`Plugin/CartManagementPlugin`). REST guest/customer checkout, the GraphQL `placeOrder`
  mutation, and storefront one-page checkout (via
  `PaymentInformationManagementInterface::savePaymentInformationAndPlaceOrder`) all genuinely
  converge on this one call to convert a quote into an order. The narrow DropShield profile
  sends the REST guest checkout route; GraphQL checkout remains connector-capable but is not
  gateway-exposed. This plugin was not changed by the cart-add fix.

Every plugin is a `before` plugin, not `around`: each only needs to validate-then-throw or pass
through, so the least invasive supported mechanism was used throughout.

No double enforcement: the REST, legacy GraphQL, modern GraphQL, and storefront cart-add
plugins sit on distinct extension points. The two GraphQL plugins cover separate Mage-OS 3.0.0
service paths; a single supported mutation reaches one of them.

Not yet covered by the reference connector: multi-shipment/multi-address checkout flows that
bypass `CartManagementInterface::placeOrder` entirely, and any custom checkout extension that
places orders through a different, non-standard path. Treat any such flow as unprotected until
verified.

### Protected drop configuration

The connector supports multiple saved drop definitions and exactly one enabled protected drop at
a time. Operators use **Marketing > Protected Drops** to select existing catalogue products;
the connector persists only drop metadata and product entity-ID assignments. It does not copy
catalogue data. See [Protected drops](protected-drops.md).

The connector exposes its active mapping through authenticated `GET
/V1/dropshield/protection-manifest`, protected by the dedicated
`DropShield_Connector::protection_manifest` Web API ACL. DropShield.Api reads that manifest into
an in-process cache. A mismatch between the active connector drop and a gateway assertion fails
closed because the assertion drop claim is still verified locally by the connector.

The manifest response is a Magento webapi Data object, not a hand-serialized array; its field
names on the wire are Magento's standard REST snake_case (`version`, `generated_at`,
`active_drop`, and within it `id`/`products`/`product_id`/`sku`), confirmed against a real Mage-OS
REST response — not the `camelCase` shape a naive reading of the field names might suggest.
`AdobeCommerceProtectedDropCatalog.Parse` on the DropShield.Api side reads this exact shape, and
also treats a missing `active_drop` key (which is how Magento's webapi serializer represents a
null value — it omits the key rather than emitting `"active_drop":null`) the same as an explicit
null: no active drop, not a parse failure.

Admin protected-drop management (grid, create/edit, product search by SKU and name, product
selection persistence, duplicate-assignment prevention, the one-active-drop invariant including a
concurrent-enable race, ACL-restricted-role denial, remove/re-add/disable lifecycle with no
DropShield.Api restart, and FK cascade delete when a Magento product is deleted while assigned) is
RUNTIME VERIFIED over real HTTP against Mage-OS 3.0.0. See [Protected drops](protected-drops.md).

### Cart and checkout enforcement

- Cart: if the SKU being added is protected and the request carries no valid origin assertion,
  the transport-appropriate plugin (`CartItemRepositoryPlugin` for REST,
  `QuoteGraphQlAddProductsToCartPlugin` or `ModernGraphQlAddProductsToCartPlugin` for GraphQL,
  `CheckoutAddProductToCartPlugin` for the storefront) throws
  `AuthorizationRequiredException` before the item is added.
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
| REST (`guest-carts/{id}/items`, `guest-carts/{id}/payment-information`) | SUPPORTED — RUNTIME VERIFIED over HTTP through DropShield | SUPPORTED — RUNTIME VERIFIED over HTTP through DropShield |
| GraphQL (`addSimpleProductsToCart`, `addProductsToCart`) | SUPPORTED — RUNTIME VERIFIED over HTTP through DropShield | UNSUPPORTED by the narrow gateway; the `placeOrder` connector path exists but is not gateway-exposed |
| GraphQL (`addVirtualProductsToCart`) | SUPPORTED — automated transport/connector coverage; focused real-Mage-OS virtual-product runtime check remains unverified | UNSUPPORTED by the narrow gateway |
| Storefront (`checkout/cart/add`, one-page checkout) | UNVERIFIED full HTTP success path — plugin attaches and fails closed via direct service invocation, but form-key CSRF blocked the valid-assertion curl test | UNVERIFIED — expected to use `CartManagementInterface::placeOrder`, but not independently runtime-tested |

GraphQL cart-add is covered by both GraphQL plugins (see "Extension points").
Its origin assertion binds to the *real* GraphQL request: `route = "POST /graphql"` and
`bodyHash` over the raw GraphQL request body (the query/variables JSON), not a fabricated
REST-shaped payload — the route/body binding was verified to match exactly, not weakened.

`/graphql` remains a shared endpoint, so `GraphQlCartMutationInspector` performs a bounded,
single-read inspection of the `{query, variables, operationName}` envelope for protected
`addSimpleProductsToCart`, `addVirtualProductsToCart`, and `addProductsToCart` requests.
Ordinary GraphQL traffic is forwarded without an assertion. GraphQL `placeOrder` is intentionally not exposed by the Commerce gateway:
the connector can protect it, but discerning whether the opaque cart contains a protected SKU at
the gateway would require broader quote lookup/proxy behaviour outside this narrow profile.

## Verified against a local Mage-OS instance

Tested against **Mage-OS 3.0.0**, installed keylessly through the reproducible
`markshust/docker-magento` stack and `https://repo.mage-os.org/`, with PHP 8.4.21. The instance
was disposable and local-only; no k6 or public traffic was used.

Confirmed end to end over real HTTP against the running instance:

- Direct local protected REST cart-add: rejected (400, `DropShield authorization required.`).
- Gateway REST cart-add: accepted with a valid DropShield-issued assertion.
- Direct local protected GraphQL `addSimpleProductsToCart`: rejected (GraphQL generic error,
  as Magento wraps the connector exception).
- Gateway GraphQL `addSimpleProductsToCart`: accepted and the returned cart contains
  `pokemon-etb`.
- Gateway GraphQL `addProductsToCart`: accepted and the returned cart contains `pokemon-etb`.
- Direct local protected REST checkout: rejected (400, `DropShield authorization required.`).
- Gateway REST guest `payment-information`: accepted and placed local order `1`.
- The connector PHPUnit suite ran inside this instance: 25 tests, 60 assertions, passing
  (PHPUnit reported 16 non-failing notices).

The connector PHPUnit suite is not run in this repository's .NET CI because it requires the
Magento/Mage-OS autoloader and test bootstrap. Re-running it requires the same local Mage-OS
setup; the shared contract file must be available beside the installed module for its
cross-language contract test.

## Limitations

- Storefront cart-add's full HTTP round-trip (including a valid-assertion success case through
  the actual controller) remains unverified — see the coverage table above. Storefront checkout
  is expected to behave like REST/GraphQL since it shares the same
  `CartManagementInterface::placeOrder` call, but this has not been directly observed either.
- Multi-shipment/multi-address checkout and any non-standard order-placement path remain
  unverified — see "Extension points" above.
- The narrow GraphQL profile supports `addVirtualProductsToCart` through the same legacy
  connector service as `addSimpleProductsToCart`; its gateway transport is automated-tested, but
  a focused virtual-product Mage-OS HTTP round-trip has not yet been observed.
- Composer constraints (`magento/framework ^103.0`, `magento/module-quote ^101.0`,
  `magento/module-checkout ^100.4`) target currently supported Magento 2 / Adobe Commerce
  component versions; the runtime test above needed `--ignore-platform-req=php` because Mage-OS
  3.0.0 itself requires PHP 8.3-8.5 while this connector's `composer.json` declares `~8.1.0
  ||~8.2.0||~8.3.0` — the two ranges only overlap at 8.3. No incompatibility with PHP 8.4/8.5 was
  found in practice; the declared constraint is narrower than necessary and worth widening in a
  separate change.
- Webhooks and App Builder are not used; documented only as possible future options for
  Commerce lifecycle events, not a current dependency.
