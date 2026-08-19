# Architecture

## Positioning

DropShield is an edge-first, ecommerce-aware flash-drop protection architecture intended to
integrate with Adobe Commerce and a retailer's existing edge/CDN infrastructure. The core
stays edge-provider-neutral: provider-specific capabilities belong behind future adapters,
not inside DropShield's policy and commerce concepts.

## Current implementation

```text
local client → DropShield.Api → per-client rate policy → proof + admission → action consume → reserve/commit → DropShield.DemoStore
                    │                  │                                                           │
                    │                  └─ abuse 429                                                 └─ admitted, waiting 202, or safe proof/replay/reservation rejection
                    └─ InMemory or shared Redis state + per-instance metrics
```

`DropShield.DemoStore` and the protection pipeline are separate processes. See
[traffic control](traffic-control.md), [transaction protection](transaction-protection.md),
[behavioural scoring](behavioural-scoring.md), and the
[Adobe Commerce connector](adobe-commerce.md) for how each stage works. A Fastly reference edge
adapter exists — see [docs/fastly.md](fastly.md); Cloudflare and Akamai remain future work.

The observability layer (see [observability](observability.md)) records only fixed route
categories, aggregate outcomes, bounded latency histograms, and a short rolling-rate window —
no request or identity history, no provider-specific telemetry dependency.

Redis, where enabled, is an optional implementation detail behind focused traffic,
admission-state, replay-state, reservation, and behaviour-state boundaries. It stores
expiry-driven fixed-window counts, bounded HMAC-derived session members, short-lived derived
replay markers, and reservation/behaviour counters — never carts, real inventory, customer
identity, request contents, cookies, tokens, or telemetry. Signed tokens are locally verified
and never stored in Redis.

## Conceptual architecture (future direction)

```text
                    INTERNET
                        │
                        ▼
             ┌────────────────────┐
             │ EXISTING EDGE/CDN  │
             │                    │
             │ Fastly / supported │
             │ edge integration   │
             └─────────┬──────────┘
                       │
                       ▼
             ┌────────────────────┐
             │ DROPSHIELD EDGE    │
             │ POLICY LAYER       │
             │                    │
             │ • traffic shaping  │
             │ • rate controls    │
             │ • admission        │
             │ • bot controls     │
             └─────────┬──────────┘
                       │
                       ▼
              ADOBE COMMERCE
                 MAGENTO 2
                       │
         ┌─────────────┼─────────────┐
         │             │             │
         ▼             ▼             ▼
      Algolia       Commerce      Integration
      Search         State          Layer
                                      │
                                      ▼
                              Microsoft D365
                              ERP / inventory /
                               order ecosystem
```

The boxes are logical responsibilities, not necessarily separate physical hops or products.
This diagram does not represent any specific retailer's private network topology; where
public retailer research informs it, see [Platform research](platform-research.md).

## Protection domains

### Edge protection

The edge domain orchestrates or augments traffic shaping, rate controls, queue/admission
policy, gross automation filtering, and API abuse controls, so abusive traffic is rejected
before the application tier does expensive work:

```text
Bot / Excess Traffic → Edge/CDN → DropShield traffic policy → Reject / Challenge / Queue
```

DropShield should not rely on a Magento module as the primary volumetric limiter, because the
Commerce application would already have consumed origin resources before rejection.

A Fastly reference adapter exists at [integrations/fastly](../integrations/fastly) ([design
doc](fastly.md)), showing DropShield sitting behind an Adobe Commerce Fastly deployment without
making Fastly part of DropShield's core architecture. The core stays provider-neutral; Fastly is
the first reference adapter only because Adobe documents it as the standard Adobe Commerce Cloud
edge. Cloudflare and Akamai adapters are not implemented.

### Adobe Commerce integration

The application domain provides ecommerce-aware controls: Commerce-managed protected-drop
selection and authenticated manifest synchronisation,
purchase and cart policy, inventory reservation policy, checkout admission enforcement, and
Commerce lifecycle integration. See [Adobe Commerce](adobe-commerce.md) for what's implemented
today.

```text
Verified / admitted traffic → Adobe Commerce → application-specific DropShield controls → Cart / inventory / checkout
```

## Design principles

1. **Control abusive volume early** — reject, challenge, shape, or queue before expensive
   origin work where the edge supports it.
2. **Orchestrate before reinventing** — prefer augmenting existing platform security controls
   over replacing mature vendor capabilities.
3. **Stay provider-neutral at the core** — Fastly, Cloudflare, Akamai, or other integrations
   must not redefine core drop-policy semantics.
4. **Be ecommerce-aware** — protect business operations and transaction state, not only
   request counts.
5. **Separate discovery from transactions** — search traffic and transactional traffic can
   have different costs, dependencies, and policies.
6. **Require evidence for retailer-specific claims** — general threat patterns become
   retailer-specific claims only when retailer telemetry confirms them.
7. **Assume endpoints are known** — security must hold even when product, cart, REST,
   GraphQL, and checkout routes are all public knowledge.
8. **Test only with authorisation** — automated traffic targets only the synthetic DemoStore,
   localhost, operator-owned infrastructure, or explicitly authorised systems.

## Existing Adobe/Fastly capabilities

DropShield should evaluate and reuse available platform controls before adding overlapping
mechanisms:

| Capability | Adobe platform status |
|---|---|
| Fastly CDN for Adobe Commerce on Cloud Infrastructure | Documented standard architecture for staging and production |
| Fastly-powered WAF | Documented Adobe Commerce on Cloud Infrastructure capability |
| ACLs and custom VCL | Documented Fastly/Adobe configuration capabilities |
| Basic rate-limiting mechanisms | Documented through the Fastly CDN module/ecosystem |
| Adobe Advanced Security | Additional-cost Adobe product |
| Advanced Rate Limiting, Bot Management, Layer-7 DDoS protection | Advanced Security capabilities |

DropShield's proposition is not "invent rate limiting" — it's drop-aware business
configuration, orchestration, and application controls around existing Adobe Commerce and
edge-security infrastructure. Translating a launch/SKU/purchase-limit/queue configuration into
safe edge and Adobe Commerce controls is future work.

## Security non-foundations

Proof-of-work puzzles, dynamic endpoint masquerading, random checkout routes, and honeypot
submission URLs are not foundational DropShield requirements. Any future research into such
techniques must remain supplemental and never substitute for authentication, authorisation,
admission, validation, rate controls, or transaction integrity.

## Synthetic DemoStore role

`DropShield.DemoStore` is a controlled synthetic ecommerce test harness for developing and
benchmarking DropShield concepts without directing test traffic at third parties. Its .NET
implementation is not a representation of any retailer's implementation language or private
architecture.

## Related records

- [Platform research](platform-research.md) — public retailer-research evidence
  boundary used as one input to this architecture.
- [ADR-001: Edge-provider-neutral core](adr/ADR-001-edge-provider-neutral.md)
- [ADR-002: Adobe Commerce as first planned target](adr/ADR-002-adobe-commerce-target.md)
