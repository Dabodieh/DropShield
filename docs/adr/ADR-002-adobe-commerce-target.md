# ADR-002: Adobe Commerce as the first planned ecommerce target

## Status

Accepted

## Date

17 August 2026

## Context

DropShield needs an initial real ecommerce platform target to make future protection contracts concrete. Public SQLI and Krish TechnoLabs case studies establish Adobe Commerce / Magento 2 as the relevant platform for the Hamleys use case. They also document an ecosystem that includes Algolia search, Microsoft Dynamics 365, and several commerce integrations.

The existing .NET proof of concept is already a safe synthetic harness. Its language does not need to match Magento/PHP because it is not intended to reproduce Hamleys' private implementation.

## Decision

Adobe Commerce / Magento 2 will be the first planned real ecommerce platform integration for DropShield.

DropShield's .NET core and synthetic DemoStore will be preserved. A future Adobe Commerce integration may use an adapter, extension, or Magento module that communicates with or configures DropShield functionality. The integration can own Adobe-specific lifecycle hooks, protected-SKU configuration, cart and checkout enforcement, reservation policy, and administration while the provider-neutral core retains shared policy semantics.

The concrete process boundary, protocol, deployment model, packaging, and PHP/.NET responsibilities remain future design decisions. This ADR does not create an Adobe Commerce project and does not authorise implementation in the current phase.

## Constraints

- Do not guess or pin a Hamleys Magento patch version from public evidence.
- Do not assume every storefront request synchronously calls D365 or any other integration.
- Distinguish Algolia search/discovery traffic from Adobe Commerce transaction traffic.
- Apply volumetric controls at the edge where possible; do not rely on a Magento module as the first rejection point.
- Reuse or orchestrate existing Adobe/Fastly capabilities before duplicating them.
- Keep customer, payment, order, and inventory data exposure to the minimum required for a future policy.
- Test integrations only against local, owned, or explicitly authorised systems.

## Alternatives considered

### Remain platform-generic indefinitely

Rejected because a concrete integration target is needed to validate lifecycle, cart, inventory, checkout, and administration assumptions. Provider-neutral core design remains required, but it does not remove the need for platform-specific adapters.

### Rewrite DropShield in PHP

Rejected. A Magento integration may contain PHP, but no evidence requires the entire protection system or synthetic harness to share Magento's implementation language.

### Choose a different first ecommerce platform

Deferred. Other integrations can follow, but Adobe Commerce has the strongest direct relevance to the Hamleys use case.

### Model the .NET DemoStore as Hamleys

Rejected. The DemoStore is a controlled test harness, not a replica of Hamleys' technology, dependencies, data model, or network topology.

## Consequences

### Positive

- Future requirements can be grounded in a documented target platform.
- The proof of concept remains safe and useful for repeatable local benchmarks.
- Adobe-specific integration concerns remain outside provider-neutral core concepts.
- Existing Adobe and edge security capabilities become explicit integration inputs.

### Negative

- The first production-oriented adapter will require Adobe Commerce and PHP expertise.
- Exact integration seams cannot be finalised without a supported Adobe test environment and version matrix.
- Platform-specific behavior will require separate compatibility and upgrade testing.

### Risks and mitigations

- **Overfitting to public Hamleys evidence:** inferred topology could enter code as fact. Mitigation: use the research classifications and validate with retailer telemetry and authorised configuration access.
- **Duplicating Adobe/Fastly controls:** DropShield could add redundant enforcement. Mitigation: capability inventory and orchestration-first design before implementation.
- **Late origin rejection:** an application module could become the primary limiter. Mitigation: preserve the two protection domains and require an edge enforcement plan for volumetric controls.
- **Tight coupling to a Magento version:** APIs may change across releases. Mitigation: establish a supported-version matrix during the future integration design.

## Migration plan

No migration is required. Keep `DropShield.Api`, `DropShield.DemoStore`, and `DropShield.Tests` unchanged. Design the Adobe Commerce integration in its roadmap phase after contracts, security boundaries, supported versions, and an authorised test environment are known.

## Validation criteria

- Future Adobe-specific code remains outside provider-neutral core policy models.
- The integration design documents supported Adobe Commerce versions and lifecycle seams before implementation.
- The DemoStore remains runnable without Adobe Commerce or third-party services.
- No production Hamleys URL appears in automated tests, load tests, fuzzers, scrapers, or endpoint-discovery tooling.
- Edge and application enforcement responsibilities remain explicit.

## Related decisions

- [ADR-001: Edge-provider-neutral core](ADR-001-edge-provider-neutral.md)
- [DropShield architecture direction](../ARCHITECTURE.md)
- [Hamleys platform research](../hamleys-platform-research.md)

