# Fastly reference integration

This is a reference Fastly integration for Adobe Commerce deployments. Fastly is part of
Adobe Commerce's documented cloud architecture; Hamleys' current edge provider has not been
independently confirmed. Adobe documents Fastly as the standard, required edge for Adobe
Commerce on Cloud Infrastructure staging and production ([Adobe Commerce custom VCL
snippets](https://experienceleague.adobe.com/en/docs/commerce-on-cloud/user-guide/cdn/custom-vcl-snippets/fastly-vcl-custom-snippets),
[Fastly rate limiting](https://www.fastly.com/documentation/guides/concepts/rate-limiting/)),
which is why Fastly is the first reference adapter — see
[ADR-001](adr/ADR-001-edge-provider-neutral.md). See also
[Hamleys platform research](hamleys-platform-research.md) for what is and is not established
about any specific retailer's edge routing.

Snippets and config: [integrations/fastly](../integrations/fastly). This document is the design
rationale; that directory is the implementation.

## Where Fastly sits

```text
Client -> Fastly -> DropShield.Api -> Adobe Commerce -> DropShield_Connector
```

Fastly is an edge transport/enforcement adapter, not a second implementation of DropShield's
policy engine. Admission, behavioural scoring, replay protection, stock reservation, action
proofs, and origin-assertion signing all remain inside `DropShield.Api`, exactly as before this
integration. Nothing here moves DropShield logic into VCL, and Fastly-specific types do not
appear in DropShield.Api's core code — see `integrations/fastly` for the boundary.

## Routing model

`DropShield.Api` is already an explicit-route gateway: every route it exposes terminates in
`DemoStoreForwarder`, which itself forwards to Adobe Commerce. There is no generic
reverse-proxy behaviour to guard against extending, so the reference design routes every
DropShield-owned path to the DropShield backend by path match, and leaves everything else
(storefront pages, static assets, ordinary catalogue browsing not covered by these routes) to
Adobe Commerce's existing Fastly configuration, untouched:

| Path | Routed to DropShield |
|---|---|
| `GET /api/products`, `/api/products/{id}`, `/api/products/{id}/stock` | yes |
| `POST /api/cart`, `POST /api/checkout` | yes |
| `POST /api/action-proofs/{action}` | yes |
| `POST /graphql` | yes, unconditionally (see GraphQL below) |
| `POST /checkout/cart/add` | yes |
| `POST /rest[/default]/V1/guest-carts/{cartId}/items` | yes |
| `POST /rest[/default]/V1/guest-carts/{cartId}/payment-information` | yes |
| `GET /health` | yes |
| `/internal/*` | denied at the edge (see Internal diagnostics below) |
| everything else | left to Adobe Commerce's existing Fastly config |

The full list is `integrations/fastly/edge-routes.json`, checked in `EdgeRoutesContractTests`
against `TrafficRouteClassifier`'s route templates and against
`contracts/origin-assertion-v1.json`, so the C# route, the Fastly route, and the origin
assertion route cannot silently diverge.

## GraphQL

`POST /graphql` is a shared Magento endpoint: catalogue queries, customer/account operations,
and cart-add mutations for both protected and ordinary SKUs all arrive on the same path. VCL
routing operates on HTTP-level request information — method, path, headers — and Fastly does
not parse GraphQL operation semantics from the request body as part of that. Path-only routing
therefore cannot distinguish a protected `addSimpleProductsToCart`,
`addVirtualProductsToCart`, or `addProductsToCart` mutation for the configured drop from an
ordinary GraphQL query on the same endpoint.

The reference design routes the entire `POST /graphql` transport to DropShield.Api rather than
attempting that distinction at the edge. `GraphQlCartMutationInspector`, already exercised by
`GraphQlAndStorefrontCartTests`, makes the ecommerce-aware decision: a protected mutation gets a
signed origin assertion and enters the protected-mutation pipeline; ordinary GraphQL traffic
(catalogue/customer queries, cart-add for non-protected SKUs) is forwarded unassorted, same as
today without Fastly in front. This does add every GraphQL request — not just protected
mutations — to DropShield.Api's request path; the coarse edge rate limit below exists partly to
bound that before it reaches DropShield.

## REST and storefront cart-add

The Adobe Commerce profile routes only the guest-cart REST item-add and payment-information paths
listed above to DropShield; unsupported `/rest/*` paths remain on Commerce's ordinary backend.
DemoStore REST cart (`POST /api/cart`) and checkout (`POST /api/checkout`) also terminate in
DropShield.Api. Storefront cart-add
(`POST /checkout/cart/add`) is included in the edge route list on the same unconditional basis
as REST cart, matching how `DemoStoreForwarder` already treats it — this route has no
ordinary/non-cart traffic to disambiguate from, unlike GraphQL.

## Backend model

Two logical backends: the DropShield backend (`F_dropshield` in the snippets) and the existing
Adobe Commerce backend, already defined by Adobe's generated VCL and left untouched. No real
hostnames appear in the committed snippets — the DropShield backend hostname is configuration,
set via a Fastly edge dictionary at deploy time.

## Caching and pass behaviour

Every route sent to the DropShield backend uses `return(pass)` in `recv-route.vcl`: cart,
checkout, GraphQL, and storefront cart-add must never be served from cache, and stock reads for
a protected drop must not risk serving stale scarce-stock state. The simplest correct rule for
this reference design is that nothing DropShield-owned is cached at the edge at all — there is
no scenario here where caching a subset of these routes would be both safe and worth the added
complexity. Static assets and ordinary storefront content are not touched by this rule and keep
whatever caching behaviour Adobe Commerce's existing Fastly configuration already gives them.

## Header/trust model

DropShield.Api already refuses to trust a client-supplied `X-DropShield-Origin-Assertion`
header (`TrafficMetricsMiddleware` strips it before any handler sees it); this integration
follows the same pattern for the edge itself.

An optional shared-secret header (`X-DropShield-Edge-Key` by default, `DropShield:EdgeTrust` in
configuration) lets DropShield.Api confirm a request came through the fronting edge.
`recv-strip-client-headers.vcl` strips any client-supplied `X-DropShield-*` header and sets the
real edge key from a Fastly edge dictionary; `deliver-strip-internal-headers.vcl` removes it
again (and the internal routing marker header) from the response so it never reaches the
client. `EdgeTrustMiddleware` in DropShield.Api independently strips and checks the header
itself — a security guarantee here does not depend solely on VCL stripping, since DropShield.Api
may be reachable directly (see Direct access below).

The edge trust key is a dedicated Base64-encoded secret of at least 32 random bytes with one
job: proving "this request passed through the edge." The edge dictionary sends that configured
Base64 value verbatim. It is never reused for admission tokens, action proofs, origin
assertions, or the internal HMAC hashing key — `DropShieldOptionsValidator` compares decoded
material and rejects configuration that reuses it. `EdgeTrust` is disabled by default, matching
the project's current direct-access PoC deployment model; a production deployment fronted by
Fastly should enable it.

## Direct access

Two bypass paths matter here:

**Client bypasses Fastly and reaches DropShield.Api directly.** With `EdgeTrust` enabled, this
request is rejected before any protection logic runs, because it cannot present the edge key.
With `EdgeTrust` disabled (the current default), this is the same direct-access model the
project already runs under everywhere else — DropShield.Api's own rate limiting, admission, and
other controls still apply; only the edge-specific coarse flood protection is skipped.

**Client bypasses both Fastly and DropShield.Api, reaching Adobe Commerce directly.** This is
unaffected by the Fastly integration: the Adobe Commerce connector's Origin Assertion validation
(`OriginAssertionGuard`, see [docs/adobe-commerce.md](adobe-commerce.md)) already rejects a
protected mutation with no valid assertion, regardless of whether Fastly or DropShield.Api were
involved. This reference VCL does not provide network-level origin isolation by itself — a real
deployment should still restrict which hosts can reach the Commerce origin where its
infrastructure allows, the same caveat that already exists in the Adobe Commerce connector docs.

## Client identity

This integration does not change how DropShield.Api derives client identity
(`ClientIdentityProvider` uses `HttpContext.Connection.RemoteIpAddress` directly; it does not
read `X-Forwarded-For` or any Fastly-supplied client-IP header today). A production deployment
that puts Fastly in front of DropShield.Api will see Fastly's own connection IP as the remote
address unless DropShield.Api is updated to trust Fastly's documented client-IP header
(`Fastly-Client-IP`) from a verified Fastly connection — that is future work, called out here
rather than silently left inconsistent.

DropShield cookies remain `Secure` whenever the public request is HTTPS or the API runs outside
Development/Testing. This deliberately remains true when a trusted edge terminates TLS and uses
plain HTTP to the local DropShield hop; DropShield does not trust arbitrary forwarded-proto
headers from clients.

## Internal diagnostics

`recv-route.vcl` returns a 404 for `/internal/*` at the edge as defense in depth. This does not
replace DropShield.Api's own gate (`InternalDiagnosticsAreAvailable` in `Program.cs`, which
already restricts `/internal/metrics` and `/internal/inventory` to Development/Testing
environments) — that gate remains authoritative regardless of what the edge does.

## Coarse edge rate limiting

`recv-coarse-rate-limit.vcl` applies a small IP-based flood check
(`ratelimit.check_rate`, [Fastly rate limiting
reference](https://www.fastly.com/documentation/reference/vcl/functions/rate-limiting/ratelimit-check-rate/))
ahead of routing, scoped to DropShield-owned paths. Its only job is protecting DropShield.Api
from an obvious volumetric flood before ecommerce-aware policy runs. The example threshold is
illustrative only, not a recommendation for any specific deployment or retailer, and is not a
substitute for DropShield's own per-client and aggregate rate limits
([traffic control](traffic-control.md)), which remain authoritative for ecommerce-aware
decisions.

## Local validation

No live Fastly account, service, or VCL upload is used. Validation is static and test-time:

- `EdgeRoutesContractTests` — the edge route list matches `TrafficRouteClassifier` and the
  origin-assertion route contract; `recv-route.vcl` contains the documented path prefixes and
  the `/internal/` denial.
- `EdgeTrustMiddlewareTests` — DropShield.Api rejects a missing or forged edge key independently
  of VCL, with and without the edge trust check enabled.

## Adobe Commerce compatibility

These are custom VCL snippets meant to be layered onto an existing Adobe Commerce Fastly
service, not a replacement configuration. Adobe's own Magento-Fastly module snippets run at
priority 50; the snippets here run at lower priority numbers (4–10), so DropShield's
routing/pass decision is made before Adobe's default caching and ACL logic would otherwise
apply, without overwriting it. See `integrations/fastly/README.md` for the full snippet-by-
snippet ordering table.

This integration does not modify the Adobe Commerce connector. REST cart/checkout protection,
the explicitly supported GraphQL cart forms, and cross-language Origin Assertion validation are
established by the connector itself and are unaffected here — see
[docs/adobe-commerce.md](adobe-commerce.md).

## Limitations

- No live Fastly deployment has been exercised; validation is static/contract-level only.
- Client-IP handling is not yet edge-aware (see Client identity above).
- The coarse edge rate-limit threshold is a placeholder, not a tuned production value.
- Only Fastly is implemented. Cloudflare and Akamai adapters would follow the same
  `integrations/<provider>/` pattern but are not started — see [ADR-001](adr/ADR-001-edge-provider-neutral.md).
