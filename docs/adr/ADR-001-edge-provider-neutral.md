# ADR-001: Edge-provider-neutral core

## Status

Accepted

## Date

17 August 2026

## Context

DropShield is intended to protect ecommerce launches before excessive or abusive traffic consumes expensive origin resources. Effective enforcement will often depend on capabilities exposed by an existing CDN, WAF, or edge provider.

The exact provider varies by retailer. For a representative high-demand retailer running Adobe Commerce / Magento 2, exact current production edge routing has not been conclusively established from public evidence alone. Adobe documents Fastly as standard and required in Adobe Commerce on Cloud Infrastructure staging and production architecture, making Fastly likely where that standard architecture applies. This is not proof of any specific retailer's exact routing.

Baking a Cloudflare-specific model into the core would encode an unsupported assumption and constrain other deployments. Baking Fastly directly into the core would turn a strong platform inference into unnecessary core coupling.

## Decision

DropShield's core architecture will remain edge-provider-neutral.

Core concepts—including drops, protected resources, admission, purchase policy, traffic-policy intent, and enforcement outcomes—must not depend on one provider's rule schema or API. Provider-specific functionality will be implemented through future adapters or integrations.

Possible conceptual integration names include:

- `DropShield.Edge.Fastly`
- `DropShield.Edge.Cloudflare`
- `DropShield.Edge.Akamai`

These names communicate boundaries only. No projects or adapters will be created until a later phase establishes requirements and contracts.

The provider abstraction must expose capabilities rather than pretend every provider has identical features. A future adapter may report support for rate controls, ACLs, edge dictionaries, custom logic, challenges, queueing, logging, or other features, allowing orchestration to fail safely when a requested policy cannot be enforced.

## Constraints

- Enforcement should occur before Magento/PHP application work where the required edge capability exists.
- Provider adapters must preserve deny-by-default behavior for protection policies they claim to enforce.
- Secrets, service identifiers, and provider credentials must not enter shared core configuration or source control.
- Provider neutrality must not become a lowest-common-denominator design that hides material capability differences.
- No retailer-specific adapter work is authorised by this decision.

## Alternatives considered

### Cloudflare-native core

Rejected because Cloudflare has not been confirmed as any specific target retailer's authoritative CDN, reverse proxy, or edge runtime. It would also constrain non-Cloudflare retailers.

### Fastly-native core

Rejected as a core architecture choice. Fastly is the strongest first-provider candidate where standard Adobe Commerce Cloud architecture applies, but retailer-specific routing is unconfirmed and other target retailers may use different providers.

### Origin-only protection in Adobe Commerce

Rejected as the primary traffic-control architecture because Magento/PHP and origin infrastructure would perform substantial work before rejecting abusive volume. Application controls remain necessary for ecommerce semantics but complement rather than replace early edge controls.

## Consequences

### Positive

- Retailer deployments can integrate with their existing edge/CDN provider.
- The core policy model is not constrained by an unsupported retailer-specific assumption.
- Provider capabilities can be reused and orchestrated instead of duplicated.
- Tests can use provider-neutral fakes without external edge accounts.

### Negative

- Capability discovery and adapter conformance will require explicit contracts.
- Provider differences may make policy translation lossy or unsupported.
- Each adapter will need separate operational, security, and integration testing.

### Risks and mitigations

- **False portability:** a generic interface could conceal provider differences. Mitigation: use explicit capability reporting and reject unsupported policies.
- **Configuration drift:** provider state could diverge from DropShield intent. Mitigation: future reconciliation, audit records, idempotent application, and drift reporting.
- **Credential exposure:** adapters will require sensitive credentials. Mitigation: future secret-store integration, least privilege, rotation, and redacted logs.
- **Provider lock-in through the first adapter:** implementation details may leak into core semantics. Mitigation: conformance tests and review against this ADR before accepting shared-contract changes.

## Migration plan

No code migration is required. Phase 1 contains no provider integration. Future provider projects must depend on core contracts rather than introduce provider SDK types into core models.

## Validation criteria

- Core projects do not reference a provider SDK or provider-specific rule type.
- Provider-specific configuration remains inside its adapter boundary.
- A core drop policy can be evaluated without an external provider account.
- Unsupported provider capabilities produce an explicit safe failure rather than silent partial enforcement.

## Related decisions

- [ADR-002: Adobe Commerce as the first planned ecommerce target](ADR-002-adobe-commerce-target.md)
- [DropShield architecture direction](../ARCHITECTURE.md)
- [Platform research](../platform-research.md)

