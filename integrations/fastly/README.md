# Fastly reference integration

This is a reference Fastly integration for Adobe Commerce deployments. Fastly is part of
Adobe Commerce's documented cloud architecture; Hamleys' current edge provider has not been
independently confirmed. Adobe documents Fastly as the standard, required edge for Adobe
Commerce on Cloud Infrastructure staging and production — see
[ADR-001](../../docs/adr/ADR-001-edge-provider-neutral.md) and
[Hamleys platform research](../../docs/hamleys-platform-research.md) for what is and is not
established about any specific retailer's routing.

See [docs/fastly.md](../../docs/fastly.md) for the full design: routing model, GraphQL
treatment, caching, header/trust model, and limitations. This README covers only the snippets
themselves.

## What's here

```
integrations/fastly/
  README.md
  edge-routes.json       route contract checked against TrafficRouteClassifier in tests
  vcl/
    recv-coarse-rate-limit.vcl
    recv-route.vcl
    recv-strip-client-headers.vcl
    pass-protected.vcl
    deliver-strip-internal-headers.vcl
```

These are custom VCL snippets meant to be added to an existing Adobe Commerce Fastly service
alongside Adobe's generated VCL — not a replacement configuration. Nothing here is a working
Fastly service definition; there are no service IDs, API tokens, or real hostnames anywhere in
this directory.

## Snippet types and ordering

| File | Type | Priority | Purpose |
|---|---|---|---|
| `recv-coarse-rate-limit.vcl` | `recv` | 4 | Coarse IP-based flood protection for DropShield.Api, ahead of routing |
| `recv-route.vcl` | `recv` | 5 | Routes DropShield-owned paths to the DropShield backend and forces pass (no cache); denies `/internal/*` |
| `recv-strip-client-headers.vcl` | `recv` | 6 | Strips client-forgeable `X-DropShield-*` headers, sets the trusted edge key |
| `pass-protected.vcl` | `pass` | 10 | Sets the DropShield backend `Host` header for the passed request |
| `deliver-strip-internal-headers.vcl` | `deliver` | 10 | Removes internal routing/edge metadata from the client-facing response |

Adobe Commerce's own Magento-Fastly module snippets run at priority 50. Everything here runs
before that (lower priority number = earlier), so DropShield's routing/pass decision is made
before Adobe's default caching and ACL logic would otherwise apply.

## Backends

Two logical backends: `F_dropshield` (DropShield.Api) and Adobe Commerce's existing backend
(already defined by Adobe's generated VCL — untouched here). `F_dropshield`'s hostname and the
edge trust shared key are configuration, not part of these snippets — set them via a Fastly
edge dictionary (`dropshield_edge_config`) and backend definition when deploying, using your
own DropShield.Api hostname and a freshly generated key. Do not commit either value.

## Local validation

No live Fastly service is used or required. Validation is static and test-time only:

- `EdgeRoutesContractTests` (tests/DropShield.Tests) checks `edge-routes.json` against
  `TrafficRouteClassifier`'s route templates and the shared `origin-assertion-v1.json` contract,
  and checks `recv-route.vcl` contains the documented path prefixes/denials.
- `EdgeTrustMiddlewareTests` checks DropShield.Api's independent rejection of a missing or
  forged edge key, with and without the edge trust check enabled.

Neither test uploads VCL to Fastly or opens a network connection to any third party.
