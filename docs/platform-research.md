# Platform research

## Purpose and boundary

This document records public, non-intrusive research relevant to a representative high-demand ecommerce retailer scenario as of 17 August 2026. It separates evidence from inference and proposed DropShield design. It is a methodology and a set of publicly sourced, generalized architectural inputs — not a description of any specific retailer's private network, a penetration-test report, or authorisation to test any retailer's systems.

No exact platform patch version, edge-routing topology, security-product entitlement, payment topology, or current state of every historical integration is claimed for any named organisation.

## Evidence classifications

| Classification | Meaning |
|---|---|
| **CONFIRMED PLATFORM** | Multiple strong public sources directly identify the platform or relationship. |
| **DOCUMENTED HISTORICALLY** | An implementation partner documented the component at a point in time; continued use is not assumed. |
| **CURRENT BUT NOT FULLY VERIFIED** | Recent public evidence suggests continued relevance but does not prove the complete current configuration. |
| **ARCHITECTURAL INFERENCE** | A reasoned conclusion based on vendor architecture or public evidence, not a confirmed deployment fact. |
| **PROPOSED DROPSHIELD DESIGN** | A future DropShield direction, not an observed retailer capability. |
| **UNKNOWN** | Public evidence is insufficient to make the claim. |

Confidence and classification are separate: a historical implementation can be documented with high confidence while its current deployment state remains unknown.

## Evidence and confidence table

The following table reflects publicly available case studies for a reference high-demand ecommerce retailer. Component names are generalized; the underlying evidence-quality methodology is unchanged.

| Component or claim | Classification | Confidence | Evidence and limits |
|---|---|---:|---|
| Adobe Commerce / Magento 2 | **CONFIRMED PLATFORM** | Very high | A public implementation-partner case study documents replacement of a home-grown platform with Adobe Commerce. A second public case study documents Magento 2 Cloud capabilities and Adobe Commerce Cloud. Storefront behaviour and recruitment evidence are corroborative only. The exact deployed patch version is **UNKNOWN**. |
| Microsoft Dynamics 365 | **DOCUMENTED HISTORICALLY** and **CURRENT BUT NOT FULLY VERIFIED** | High | Public case studies document D365 ERP integration; recent recruitment evidence also indicates ongoing organisational relevance. This supports treating downstream ERP, inventory, and order integration load as important. It does not prove a synchronous D365 call on every storefront request. |
| Algolia | **DOCUMENTED HISTORICALLY** and **CURRENT BUT NOT FULLY VERIFIED** | High | A public case study identifies Algolia as the implemented search platform; recruitment evidence suggests continued relevance. Search/discovery traffic must therefore be modelled separately from commerce transaction traffic. Exact current data flows are **UNKNOWN**. |
| Logistics/shipping integration | **DOCUMENTED HISTORICALLY** | Medium-high | A public case study documents named-day and next-day shipping checkout integration. Current configuration is not independently confirmed. |
| Buy-now-pay-later payment provider | **DOCUMENTED HISTORICALLY** | Medium-high | A public case study documents a BNPL payment integration. Current payment topology is not independently confirmed. |
| Email marketing platform | **DOCUMENTED HISTORICALLY** | Medium-high | A public case study documents its use in customer-facing marketing. Continued use is not assumed. |
| CRM platform | **DOCUMENTED HISTORICALLY** | Medium-high | A public case study documents its use in customer-facing activity. Product, scope, and continued use are not assumed. |
| Third-party ticketing/customer-query system | **DOCUMENTED HISTORICALLY** | Medium-high | A public case study documents an integrated system but does not establish its current product or topology. |
| Address/geolocation integration | **DOCUMENTED HISTORICALLY** | Medium-high | A public case study documents automatic address completion with geolocation. Current provider and implementation are **UNKNOWN**. |
| Dropship/vendor integration | **DOCUMENTED HISTORICALLY** | Medium-high | A public case study documents a vendor integration and order flow. Current implementation details are **UNKNOWN**. |
| Payment provider (original implementation) | **DOCUMENTED HISTORICALLY** | High for original implementation | A public case study identifies the payment provider used during the original Adobe Commerce implementation. Current exact payment topology is not independently confirmed. |
| Adobe Commerce Edge / CDN | **ARCHITECTURAL INFERENCE** | High for standard Adobe architecture; unconfirmed for any specific retailer | Adobe documents Fastly as required for Adobe Commerce on Cloud Infrastructure staging and production environments. A public case study documents Adobe Commerce Cloud capabilities for the reference retailer scenario. Exact production edge routing for any specific retailer remains **UNKNOWN**. |
| Fastly | **ARCHITECTURAL INFERENCE** | Likely where standard Adobe Commerce Cloud architecture applies | Fastly is part of Adobe's documented Commerce on Cloud Infrastructure architecture. This does not conclusively establish any specific retailer's current DNS, routing, service configuration, or exclusive edge provider. |
| Cloudflare as authoritative CDN/reverse proxy | **UNKNOWN** | Not confirmed | No evidence reviewed establishes this claim for any specific retailer. DropShield documentation must not present Cloudflare as confirmed retailer infrastructure. References to Cloudflare-hosted assets or allowed domains would not prove authoritative edge routing. |
| Adobe Advanced Security | **UNKNOWN** for any specific retailer | Product capability confirmed; entitlement unknown | Adobe documents Advanced Security as an additional-cost product for Adobe Commerce on Cloud Infrastructure. Any specific retailer's licensing, onboarding, rules, and enabled state have not been established. |
| Advanced Rate Limiting, Bot Management, and Layer-7 DDoS protection | **UNKNOWN** for any specific retailer | Product capabilities confirmed | Adobe documents these as Advanced Security capabilities. Their availability depends on product, licensing, onboarding, and configuration; they must not be described as enabled for any specific retailer without retailer evidence. |
| DropShield edge and Adobe Commerce protection layers | **PROPOSED DROPSHIELD DESIGN** | Design decision | This is the intended future architecture, not a representation of any specific retailer's private topology. |

## Confirmed and documented platform findings

### Adobe Commerce / Magento 2

Adobe Commerce / Magento 2 is the primary planned ecommerce platform target informed by public case-study evidence for a representative high-demand retailer scenario. A public implementation-partner case study documents replacement of a previous home-grown site with Adobe Commerce. A second public case study subsequently documents Magento 2 Cloud and Adobe Commerce Cloud work, including code audit, version upgrade, performance and checkout optimisation.

The evidence does not justify guessing a current Magento patch version or assuming that every component described during implementation remains unchanged for any specific retailer.

### Microsoft Dynamics 365

Dynamics 365 participates in the reference retailer's ERP, inventory, and order ecosystem in the public case-study evidence, making downstream integration load an important architectural consideration generally. Public evidence does not establish the precise consistency model, cache strategy, synchronisation cadence, or whether any particular storefront request invokes D365 synchronously.

### Algolia

Algolia is documented as the reference retailer's search platform in public case-study evidence. DropShield must distinguish search and discovery traffic from transactional commerce traffic: search activity may exercise Algolia and catalogue-delivery paths differently from cart, inventory, customer, order, and checkout paths in Adobe Commerce.

### Other documented integrations

Logistics/shipping, a BNPL payment provider, email marketing, CRM, a third-party customer-query/ticketing system, address/geolocation functionality, and dropship/vendor integration are documented in public case-study evidence. A separate case study documents the original payment provider. These are historical implementation facts, not a guarantee of any specific retailer's current vendor set or topology.

## Edge and Adobe security interpretation

Use the following wording pattern when discussing a specific retailer's likely edge posture:

> **Adobe Commerce Edge / CDN**  
> Likely Fastly where standard Adobe Commerce Cloud architecture applies.  
> Exact production configuration for any specific retailer: unconfirmed.

Adobe's documentation establishes platform capabilities including Fastly CDN integration, a Fastly-powered WAF, traffic ACLs, custom VCL, and basic rate-limiting mechanisms. Adobe also documents Advanced Security with Advanced Rate Limiting, Bot Management, and Layer-7 DDoS protection as an additional-cost capability.

Vendor documentation proves that these capabilities exist. It does not prove that any specific retailer has purchased, enabled, or configured every capability.

## Recruitment evidence as a research signal

Recruitment evidence is useful corroboration of organisational technology relevance, not proof of runtime topology. Public recruitment listings can reference Adobe Commerce, Algolia, and Microsoft D365 for a retailer's ecommerce team. Job adverts can expire, be copied, or describe desired rather than deployed systems, so they remain **CURRENT BUT NOT FULLY VERIFIED** evidence at best.

## Threat-model evidence boundary

The following are general ecommerce threats or hypotheses DropShield may eventually address; they are not established facts about any specific retailer:

| Scenario | Status | Evidence needed before retailer-specific claims |
|---|---|---|
| High-frequency inventory polling | Potential threat | Edge, application, and inventory telemetry correlated by route and client signals. |
| Cart hoarding | General ecommerce attack pattern | Cart lifecycle, reservation, expiry, customer, and stock telemetry. |
| Automated checkout or payment attempts | Potential threat | Checkout and payment-provider telemetry with fraud and timing analysis. |
| Distributed automation using proxy networks | Potential threat | Edge telemetry and bot-classification evidence. |
| REST or GraphQL abuse | Hypothesis requiring confirmation | Route-level logs, schema exposure review, and retailer-authorised testing. |
| ERP contention caused by storefront traffic | Hypothesis requiring confirmation | Integration traces, queue depth, database telemetry, and dependency timings. |

DropShield has not penetration-tested any retailer. Public research is not authorisation to perform endpoint discovery, scraping, fuzzing, load testing, or abuse simulation against any retailer.

## Methodology sources

This research draws on the kind of public case studies typically published by Adobe Commerce implementation partners (documenting platform migrations, integration ecosystems, and optimisation work) and Adobe's own Experience League documentation for Commerce on Cloud Infrastructure (CDN, Fastly, WAF, custom VCL, and Advanced Security capabilities). The methodology of separating confirmed/documented/inferred/unknown evidence, and never treating vendor platform-capability documentation as proof of a specific retailer's enabled configuration, applies to any retailer this research approach is used for — not to one organisation.

These sources support general platform capabilities and typical integration patterns for Adobe Commerce Cloud deployments. They are not evidence that any specific retailer has enabled every documented feature.
