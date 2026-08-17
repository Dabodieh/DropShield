# DropShield architecture direction

## Positioning

DropShield is an edge-first, ecommerce-aware flash-drop protection architecture designed to integrate with Adobe Commerce and a retailer's existing edge/CDN infrastructure.

The core remains edge-provider-neutral. Provider-specific capabilities belong behind future adapters rather than inside DropShield's core policy and commerce concepts.

This document primarily describes future direction. Phase 3 implements provider-neutral traffic-rate controls, Phase 4 adds bounded internal observability, Phase 5 adds optional Redis-backed shared enforcement state, Phase 6 adds bounded waiting/admission decisions, Phase 7 adds scoped signed admission proof, Phase 8 adds one-time cart/checkout action proof, Phase 9 adds a synthetic inventory reservation ledger, and Phase 10 adds conservative short-lived behavioural scoring. Adobe Commerce and edge-provider integrations remain future work.

## Current local implementation

The working PoC keeps the synthetic origin and protection boundary separate:

```text
local client → DropShield.Api → per-client rate policy → proof + admission → action consume → reserve/commit → DropShield.DemoStore
                    │                  │                                                           │
                    │                  └─ abuse 429                                                 └─ admitted, waiting 202, or safe proof/replay/reservation rejection
                    └─ InMemory or shared Redis state + per-instance metrics
```

The observability layer records only fixed route categories, aggregate outcomes, bounded latency histograms, and a short rolling-rate window. It does not retain request or identity histories and does not introduce a provider-specific telemetry dependency. See [Phase 4 observability](PHASE4_OBSERVABILITY.md).

Redis is an optional implementation detail behind focused traffic, admission-state, replay-state, and synthetic-reservation boundaries. It stores expiry-driven fixed-window counts, bounded HMAC-derived active/waiting session members, short-lived derived replay markers, and a synthetic reservation counter hash with an expiry sorted set. Signed tokens are locally verified and are not stored in Redis. It does not store carts, real retailer inventory, customer identity, request contents, cookies, tokens, or telemetry. See [Phase 5 distributed state](PHASE5_DISTRIBUTED_STATE.md), [Phase 6 admission control](PHASE6_ADMISSION_CONTROL.md), [Phase 7 signed admission](PHASE7_SIGNED_ADMISSION.md), [Phase 8 replay protection](PHASE8_REPLAY_PROTECTION.md), and [Phase 9 inventory reservation](PHASE9_INVENTORY_RESERVATION.md).

## Conceptual architecture

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

The boxes are logical responsibilities, not necessarily separate physical hops or products. The diagram does not represent Hamleys' exact private network topology. For the Hamleys use case, Fastly is likely where standard Adobe Commerce Cloud architecture applies, but exact production routing is unconfirmed.

## Protection domains

### Edge protection

The future edge domain may orchestrate or augment:

- traffic shaping and rate controls;
- queue and admission policy;
- gross automation filtering;
- request and admission-token validation;
- API abuse controls;
- drop-specific route and traffic policies.

Abusive or excessive traffic should ideally be controlled before Magento/PHP performs expensive application work:

```text
Bot / Excess Traffic
        ↓
Edge/CDN
        ↓
DropShield traffic policy
        ↓
Reject / Challenge / Queue
```

DropShield should not rely on a Magento module as the primary volumetric limiter because PHP and the Commerce application would already have consumed origin resources before rejection.

### Adobe Commerce integration

The future application domain may provide ecommerce-aware controls such as:

- protected SKU and drop configuration;
- purchase and cart policies;
- inventory reservation policy;
- customer and order correlation;
- checkout admission enforcement;
- Adobe Commerce lifecycle integration and administration.

Accepted traffic can then follow an application-aware path:

```text
Verified / admitted traffic
        ↓
Adobe Commerce
        ↓
Application-specific DropShield controls
        ↓
Cart / inventory / checkout
```

The concrete module, adapter, API, and deployment boundaries remain future design decisions.

## Design principles

1. **Control abusive volume early.** Reject, challenge, shape, or queue traffic before expensive origin work where the edge supports it.
2. **Orchestrate before reinventing.** Prefer orchestration and augmentation of existing platform security controls over replacement of mature vendor capabilities.
3. **Remain provider-neutral at the core.** Fastly, Cloudflare, Akamai, or other provider integrations must not redefine core drop policy semantics.
4. **Be ecommerce-aware.** Protect business operations and transaction state, not only IP-address request counts.
5. **Separate discovery from transactions.** Algolia-backed search traffic and Adobe Commerce transactional traffic can have different costs, dependencies, and policies.
6. **Require evidence for retailer-specific claims.** General threat patterns become Hamleys-specific claims only when retailer telemetry confirms them.
7. **Assume endpoints are known.** Security must remain effective when attackers know product, cart, REST, GraphQL, and checkout routes.
8. **Test only with authorisation.** Automated traffic is restricted to the synthetic DemoStore, localhost, operator-owned infrastructure, or explicitly authorised systems.
9. **Deliver incrementally.** Roadmap discussion does not authorise implementation ahead of the current phase.

## Existing Adobe/Fastly capabilities

DropShield must evaluate and reuse available platform controls before adding overlapping mechanisms.

| Capability | Adobe platform status | Hamleys-specific status |
|---|---|---|
| Fastly CDN for Adobe Commerce on Cloud Infrastructure | Documented standard architecture for staging and production | Exact routing unconfirmed |
| Fastly-powered WAF | Documented Adobe Commerce on Cloud Infrastructure capability | Exact Hamleys configuration unconfirmed |
| ACLs and custom VCL | Documented Fastly/Adobe configuration capabilities | Use and configuration unknown |
| Basic rate-limiting mechanisms | Documented through the Fastly CDN module/ecosystem | Use and configuration unknown |
| Adobe Advanced Security | Additional-cost Adobe product | Licensing and enablement unknown |
| Advanced Rate Limiting | Advanced Security capability | Unknown |
| Bot Management | Advanced Security capability | Unknown |
| Layer-7 DDoS protection | Advanced Security capability | Unknown |

DropShield's future proposition is therefore not merely “invent rate limiting.” It is to provide drop-aware business configuration, orchestration, and application controls around existing Adobe Commerce and edge-security infrastructure.

A future business configuration might identify a launch, protected SKUs, purchase limit, traffic policy, queue policy, inventory-route policy, cart policy, and checkout admission policy. Translating that configuration into safe edge and Adobe Commerce controls is future work.

## Security non-foundations

Proof-of-work puzzles, dynamic endpoint masquerading, random checkout routes, and honeypot submission URLs are not foundational DropShield requirements. Any future research into such techniques must have a defensible purpose, remain supplemental, and never become a substitute for authentication, authorisation, admission, validation, rate controls, or transaction integrity.

## Synthetic DemoStore role

`DropShield.DemoStore` is a controlled synthetic ecommerce test harness for developing and benchmarking DropShield concepts without directing test traffic at third parties. Its .NET implementation is intentionally not a representation of Hamleys' implementation language or private architecture.

## Related records

- [Hamleys platform research](HAMLEYS_PLATFORM_RESEARCH.md)
- [ADR-001: Edge-provider-neutral core](adr/ADR-001-edge-provider-neutral.md)
- [ADR-002: Adobe Commerce as first planned target](adr/ADR-002-adobe-commerce-target.md)
