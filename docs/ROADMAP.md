# Roadmap

## Implemented

- Per-client and aggregate rate limiting, with optional Redis-backed distributed state.
- Bounded internal observability (`/internal/metrics`).
- Waiting-room admission with signed HMAC admission proof.
- One-time action proof (replay protection) for cart/checkout.
- Synthetic inventory reservation for the protected drop.
- Behavioural risk scoring.
- Adobe Commerce / Magento 2 connector with signed origin assertions.
- Fastly reference edge adapter ([docs/fastly.md](fastly.md)) — not evidence of any specific
  retailer's production edge.

See [architecture](ARCHITECTURE.md) for how these fit together, and the topic docs it links to
for each control's detail.

## Future work

- **Additional edge-provider integrations** (Cloudflare, Akamai) — same
  `integrations/<provider>/` pattern as the Fastly reference adapter, per
  [ADR-001](adr/ADR-001-edge-provider-neutral.md).
- **Edge-aware client identity** — trusting Fastly's documented client-IP header from a verified
  Fastly connection instead of the raw socket address; see the Client identity section of
  [docs/fastly.md](fastly.md).
- **Retailer-specific demonstration profile** — a configuration profile built from confirmed
  platform research, not a claim of production deployment.
- **Key rotation** for admission/action-proof/origin-assertion signing keys (the `kid` claim
  already supports this; only a previous-key verification ring is missing).
- **Idempotency keys** for cart/checkout, to let a client safely retry a lost response instead
  of hitting the replay-conflict path.

None of this is scheduled or implemented. This project does not target or test any specific
retailer's production systems.
