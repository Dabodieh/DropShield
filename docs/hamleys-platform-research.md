# Hamleys platform research

## Purpose and boundary

This document records public, non-intrusive research relevant to the Hamleys use case as of 17 August 2026. It separates evidence from inference and proposed DropShield design. It is not a description of Hamleys' private network, a penetration-test report, or authorisation to test Hamleys systems.

No exact Magento patch version, edge-routing topology, security-product entitlement, payment topology, or current state of every historical integration is claimed.

## Evidence classifications

| Classification | Meaning |
|---|---|
| **CONFIRMED PLATFORM** | Multiple strong public sources directly identify the platform or relationship. |
| **DOCUMENTED HISTORICALLY** | An implementation partner documented the component at a point in time; continued use is not assumed. |
| **CURRENT BUT NOT FULLY VERIFIED** | Recent public evidence suggests continued relevance but does not prove the complete current configuration. |
| **ARCHITECTURAL INFERENCE** | A reasoned conclusion based on vendor architecture or public evidence, not a confirmed Hamleys deployment fact. |
| **PROPOSED DROPSHIELD DESIGN** | A future DropShield direction, not an observed retailer capability. |
| **UNKNOWN** | Public evidence is insufficient to make the claim. |

Confidence and classification are separate: a historical implementation can be documented with high confidence while its current deployment state remains unknown.

## Evidence and confidence table

| Component or claim | Classification | Confidence | Evidence and limits |
|---|---|---:|---|
| Adobe Commerce / Magento 2 | **CONFIRMED PLATFORM** | Very high | SQLI says Hamleys replaced a home-grown platform with Adobe Commerce. Krish documents Magento 2 Cloud capabilities and Adobe Commerce Cloud. Current storefront behaviour and recruitment evidence are corroborative only. The exact deployed patch version is **UNKNOWN**. |
| Microsoft Dynamics 365 | **DOCUMENTED HISTORICALLY** and **CURRENT BUT NOT FULLY VERIFIED** | High | SQLI and Krish document D365 ERP integration; recent Hamleys recruitment also indicates ongoing organisational relevance. This supports treating downstream ERP, inventory, and order integration load as important. It does not prove a synchronous D365 call on every storefront request. |
| Algolia | **DOCUMENTED HISTORICALLY** and **CURRENT BUT NOT FULLY VERIFIED** | High | Krish identifies Algolia as the implemented search platform; recruitment evidence in the research brief suggests continued relevance. Search/discovery traffic must therefore be modelled separately from commerce transaction traffic. Exact current data flows are **UNKNOWN**. |
| GFS | **DOCUMENTED HISTORICALLY** | Medium-high | Krish documents GFS checkout integration for named-day and next-day shipping. Current configuration is not independently confirmed. |
| Klarna | **DOCUMENTED HISTORICALLY** | Medium-high | Krish documents Klarna integration. Current payment topology is not independently confirmed. |
| Mailchimp | **DOCUMENTED HISTORICALLY** | Medium-high | Krish documents its use in customer-facing marketing. Continued use is not assumed. |
| Salesforce | **DOCUMENTED HISTORICALLY** | Medium-high | Krish documents its use in customer-facing activity. Product, scope, and continued use are not assumed. |
| Third-party ticketing/customer-query system | **DOCUMENTED HISTORICALLY** | Medium-high | Krish documents an integrated system but does not establish its current product or topology. |
| Address/geolocation integration | **DOCUMENTED HISTORICALLY** | Medium-high | Krish documents automatic address completion with geolocation. Current provider and implementation are **UNKNOWN**. |
| Dropship/vendor integration | **DOCUMENTED HISTORICALLY** | Medium-high | Krish documents a vendor integration and order flow. Current implementation details are **UNKNOWN**. |
| Adyen | **DOCUMENTED HISTORICALLY** | High for original implementation | SQLI identifies Adyen as the payment provider during the original Adobe Commerce implementation. Current exact payment topology is not independently confirmed. |
| Adobe Commerce Edge / CDN | **ARCHITECTURAL INFERENCE** | High for standard Adobe architecture; unconfirmed for Hamleys | Adobe documents Fastly as required for Adobe Commerce on Cloud Infrastructure staging and production environments. Krish documents Adobe Commerce Cloud capabilities for Hamleys. Exact Hamleys production edge routing remains **UNKNOWN**. |
| Fastly | **ARCHITECTURAL INFERENCE** | Likely where standard Adobe Commerce Cloud architecture applies | Fastly is part of Adobe's documented Commerce on Cloud Infrastructure architecture. This does not conclusively establish Hamleys' current DNS, routing, service configuration, or exclusive edge provider. |
| Cloudflare as Hamleys' authoritative CDN/reverse proxy | **UNKNOWN** | Not confirmed | No evidence reviewed establishes this claim. DropShield documentation must not present Cloudflare as confirmed Hamleys infrastructure. References to Cloudflare-hosted assets or allowed domains would not prove authoritative edge routing. |
| Adobe Advanced Security | **UNKNOWN** for Hamleys | Product capability confirmed; entitlement unknown | Adobe documents Advanced Security as an additional-cost product for Adobe Commerce on Cloud Infrastructure. Hamleys' licensing, onboarding, rules, and enabled state have not been established. |
| Advanced Rate Limiting, Bot Management, and Layer-7 DDoS protection | **UNKNOWN** for Hamleys | Product capabilities confirmed | Adobe documents these as Advanced Security capabilities. Their availability depends on product, licensing, onboarding, and configuration; they must not be described as enabled for Hamleys without retailer evidence. |
| DropShield edge and Adobe Commerce protection layers | **PROPOSED DROPSHIELD DESIGN** | Design decision | This is the intended future architecture, not a representation of Hamleys' private topology. |

## Confirmed and documented platform findings

### Adobe Commerce / Magento 2

Adobe Commerce / Magento 2 is the primary planned ecommerce platform target for the Hamleys use case. SQLI documents the replacement of Hamleys' previous home-grown site with Adobe Commerce and a launch on 11 December 2020. Krish subsequently documents Magento 2 Cloud and Adobe Commerce Cloud work, including code audit, version upgrade, performance and checkout optimisation.

The evidence does not justify guessing a current Magento patch version or assuming that every component described during implementation remains unchanged.

### Microsoft Dynamics 365

Dynamics 365 participates in Hamleys' ERP, inventory, and order ecosystem, making downstream integration load an important architectural consideration. Public evidence does not establish the precise consistency model, cache strategy, synchronisation cadence, or whether any particular storefront request invokes D365 synchronously.

### Algolia

Algolia is documented as Hamleys' search platform. DropShield must distinguish search and discovery traffic from transactional commerce traffic: search activity may exercise Algolia and catalogue-delivery paths differently from cart, inventory, customer, order, and checkout paths in Adobe Commerce.

### Other documented integrations

GFS, Klarna, Mailchimp, Salesforce, a third-party customer-query/ticketing system, address/geolocation functionality, and dropship/vendor integration are documented by Krish. SQLI separately documents Adyen in the original implementation. These are historical implementation facts, not a guarantee of the current vendor set or topology.

## Edge and Adobe security interpretation

Use the following wording for the Hamleys use case:

> **Adobe Commerce Edge / CDN**  
> Likely Fastly where standard Adobe Commerce Cloud architecture applies.  
> Exact Hamleys production configuration: unconfirmed.

Adobe's documentation establishes platform capabilities including Fastly CDN integration, a Fastly-powered WAF, traffic ACLs, custom VCL, and basic rate-limiting mechanisms. Adobe also documents Advanced Security with Advanced Rate Limiting, Bot Management, and Layer-7 DDoS protection as an additional-cost capability.

Vendor documentation proves that these capabilities exist. It does not prove that Hamleys has purchased, enabled, or configured every capability.

## Current recruitment evidence

Recruitment evidence is useful corroboration of organisational technology relevance, not proof of runtime topology. The supplied research identifies current/public ecommerce recruitment references to Adobe Commerce, Algolia, and Microsoft D365. A recent Hamleys IT Technical Specialist advert independently references D365 administration and Store Commerce POS. Job adverts can expire, be copied, or describe desired rather than deployed systems, so they remain **CURRENT BUT NOT FULLY VERIFIED** evidence.

## Threat-model evidence boundary

The following are general ecommerce threats or hypotheses DropShield may eventually address; they are not established facts about Hamleys:

| Scenario | Status for Hamleys | Evidence needed before retailer-specific claims |
|---|---|---|
| High-frequency inventory polling | Potential threat | Edge, application, and inventory telemetry correlated by route and client signals. |
| Cart hoarding | General ecommerce attack pattern | Cart lifecycle, reservation, expiry, customer, and stock telemetry. |
| Automated checkout or payment attempts | Potential threat | Checkout and payment-provider telemetry with fraud and timing analysis. |
| Distributed automation using proxy networks | Potential threat | Edge telemetry and bot-classification evidence. |
| REST or GraphQL abuse | Hypothesis requiring confirmation | Route-level logs, schema exposure review, and retailer-authorised testing. |
| D365 contention caused by storefront traffic | Hypothesis requiring confirmation | Integration traces, queue depth, database telemetry, and dependency timings. |

DropShield has not penetration-tested Hamleys. Public research is not authorisation to perform endpoint discovery, scraping, fuzzing, load testing, or abuse simulation against Hamleys.

## Sources and what they support

### SQLI Hamleys case study

[Hamleys: A new digital experience for an iconic toy retailer](https://www.sqli.com/int-en/case-studies/hamleys-new-digital-experience-iconic-toy-retailer)

Supports Adobe Commerce selection, replacement of the home-grown platform, Microsoft Dynamics 365 integration, Adyen's role during the original implementation, and the December 2020 launch.

### Krish TechnoLabs Hamleys Adobe Commerce case study

[Building blocks of perfection in the toy world with exceptional commerce](https://www.krishtechnolabs.com/casestudies/hamleys/)

Supports Magento 2 Cloud capabilities, Adobe Commerce Cloud, Algolia, Microsoft D365, ERP/inventory connectivity, GFS, Klarna, dropship/vendor integration, Mailchimp, Salesforce, customer-query ticketing, address/geolocation functionality, code audit/version work, performance work, and checkout/payment/shipping optimisation.

### Current Hamleys recruitment evidence

[Recent Hamleys D365 recruitment listing indexed by Indeed](https://uk.indeed.com/q-microsoft-dynamics-365-commerce-jobs.html)

Supports current organisational relevance of D365. The wider Adobe Commerce and Algolia recruitment references described in the supplied research are treated only as corroboration, not proof of exact infrastructure.

### Adobe Experience League documentation

- [Configure Fastly services](https://experienceleague.adobe.com/en/docs/commerce-on-cloud/user-guide/cdn/setup-fastly/fastly-configuration)
- [Fastly services overview](https://experienceleague.adobe.com/en/docs/commerce-on-cloud/user-guide/cdn/fastly)
- [Web Application Firewall](https://experienceleague.adobe.com/en/docs/commerce-on-cloud/user-guide/cdn/fastly-waf-service)
- [Getting started with custom VCL](https://experienceleague.adobe.com/en/docs/commerce-on-cloud/user-guide/cdn/custom-vcl-snippets/fastly-vcl-custom-snippets)
- [Adobe Commerce Advanced Security](https://experienceleague.adobe.com/en/docs/commerce-on-cloud/user-guide/cdn/advanced-security)

These sources support capabilities of Adobe Commerce on Cloud Infrastructure itself. They are not evidence that Hamleys has enabled every feature.

