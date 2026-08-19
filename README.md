# DropShield

DropShield is a proof-of-concept traffic and scarce-stock protection layer for high-demand
ecommerce releases. It sits in front of an ecommerce origin and applies rate controls,
waiting-room admission, replay protection, and short-lived synthetic stock reservations before
protected requests are forwarded.

## Safety boundary

All automated traffic generation, load testing, endpoint experimentation, and abuse simulation
must target only localhost, the synthetic `DropShield.DemoStore`, infrastructure owned by the
project operator, or systems with explicit testing permission. This project must not be used
to send abusive traffic to third-party websites. Any retailer referenced in the documentation
(for architectural research purposes) is a conceptual use case only and is not affiliated with
DropShield, and no traffic has been sent to it.

## What's implemented

Rate limiting applies per-client and aggregate fixed-window limits, InMemory or Redis-backed.
Waiting-room admission holds eligible sessions in a bounded active/waiting capacity separate
from raw rate limiting, and issues signed HMAC-SHA256 admission proof once a session is let
in. Cart and checkout mutations require a one-time action proof so a captured request can't be
replayed, and a synthetic scarce-stock ledger reserves and commits inventory atomically.
Behavioural scoring adds a short-lived, explainable risk signal that can temporarily restrict
high-risk sessions. The Adobe Commerce connector is a companion Magento 2 module that verifies
a signed origin assertion before allowing cart/checkout writes through, so Commerce can't be
reached by skipping DropShield; REST and GraphQL cart/checkout protection have been exercised
against a real Mage-OS 3.0.0 runtime (see [Adobe Commerce](docs/adobe-commerce.md) for exact
coverage). A Fastly reference adapter shows how an edge provider can sit in front of DropShield
without becoming part of its core policy engine (see [Fastly](docs/fastly.md)). An internal,
bounded, aggregate-only metrics snapshot supports local development.

None of this is presented as production-ready. It is a working demonstration of the protection
model, backed by measured local benchmarks — see [benchmarks](docs/benchmarks.md).

## Run the demo

```powershell
dotnet run --project demo/DropShield.Demo --configuration Release
```

A scripted walkthrough of the full protection flow — normal traffic, excessive polling,
admission/waiting, action proof, replay rejection, reservation, and checkout — against a local
DemoStore and DropShield.Api instance. See [docs/demo.md](docs/demo.md) for prerequisites and
what it does and does not prove.

## Architecture

```text
local client → DropShield.Api → per-client rate policy → admission + proof → action consume → reserve/commit → DropShield.DemoStore
                    │                  │                                                           │
                    │                  └─ abuse 429                                                 └─ admitted, waiting 202, or safe proof/replay/reservation rejection
                    └─ InMemory or shared Redis state + per-instance metrics
```

Deeper documentation:

- [Architecture](docs/ARCHITECTURE.md) — overall design and edge-integration direction.
- [Traffic control](docs/traffic-control.md) — rate limiting, distributed state, waiting-room
  admission, signed admission proof.
- [Transaction protection](docs/transaction-protection.md) — one-time action proof and
  inventory reservation.
- [Behavioural scoring](docs/behavioural-scoring.md).
- [Observability](docs/observability.md) — the internal metrics endpoint.
- [Adobe Commerce](docs/adobe-commerce.md) — the origin-assertion contract and the Magento
  connector.
- [Fastly](docs/fastly.md) — reference edge adapter; not evidence of any retailer's production
  edge.
- [Demo](docs/demo.md) — the scripted local walkthrough above, stage by stage.
- [Benchmarks](docs/benchmarks.md) — measured unprotected vs. protected throughput/latency.
- [Roadmap](docs/ROADMAP.md).

## Projects

- `src/DropShield.Api` — the protection gateway: explicit route forwarding, selectable
  InMemory/Redis state, and per-instance observability.
- `src/DropShield.DemoStore` — a synthetic ecommerce backend (health, product, stock, cart,
  checkout) used as the protected origin for local development and benchmarking.
- `tests/DropShield.Tests` — xUnit integration tests for both APIs.
- `load-tests` — localhost-only k6 scenarios (smoke, normal customer, flash crowd, aggressive
  polling, mixed drop).
- `integrations/adobe-commerce/DropShield_Connector` — the Magento 2 connector.
- `demo/DropShield.Demo` — the local scripted demo runner (see [docs/demo.md](docs/demo.md)).
- `contracts/origin-assertion-v1.json` — the language-neutral origin-assertion wire format and
  a cross-language test vector.

## Run locally

Requirements: .NET 8 SDK.

```powershell
dotnet restore
dotnet build DropShield.sln
dotnet test DropShield.sln
```

Start each API in a separate terminal:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/DropShield.Api
dotnet run --project src/DropShield.DemoStore
```

Default local URLs:

- DropShield API: `http://localhost:5257/health`
- Demo Store: `http://localhost:5058/health`
- Demo Store stock: `http://localhost:5058/api/products/pokemon-etb/stock`

Development mode generates ephemeral, non-persistent signing keys for admission tokens, action
proofs, and origin assertions when none are configured; tokens invalidate on restart. Production
and Redis-mode deployments require explicit shared Base64 keys before startup — DropShield fails
closed rather than falling back to a weaker default:

```powershell
$env:DropShield__AdmissionTokens__KeyId = '2026-08-primary'
$env:DropShield__AdmissionTokens__SigningKey = [Convert]::ToBase64String(
    [Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

Admission tokens, action proofs, and origin assertions each use a dedicated signing key —
startup validation rejects any configuration that reuses one key across those purposes.

### Exercise the protected drop

Admission is enabled for `pokemon-etb` in the committed configuration. A protected-stock
request first passes the per-client rate limit, then admission. An eligible excess session gets:

```json
{ "status": "waiting", "drop": "pokemon-etb", "retryAfterSeconds": 5 }
```

as HTTP 202 — poll the same URL with the returned `DropShield.Session` cookie. An admitted
response also sets an HttpOnly `DropShield.Admission` proof cookie. With valid admission proof,
`POST /api/action-proofs/cart` or `/checkout` returns a one-time action token to attach as
`X-DropShield-Action` on the corresponding mutation; a valid cart action reserves one synthetic
unit, and checkout commits it only after the origin succeeds.

### Optional Redis state

The committed default is `InMemory` and needs no Redis service. For a local Redis-mode run:

```powershell
docker run --rm --name dropshield-redis -p 127.0.0.1:6379:6379 redis:8.8.1-alpine
$env:DropShield__StateProvider = 'Redis'
$env:DropShield__Redis__ConnectionString = '127.0.0.1:6379'
$env:DropShield__Redis__IdentityHashKey = [Convert]::ToHexString(
    [Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet run --project src/DropShield.Api
```

Redis unavailability is fail-closed: protected traffic gets HTTP 503, never a silent fallback
to a weaker local limit. See [traffic control](docs/traffic-control.md).

## Load testing and benchmarks

`load-tests/` contains k6 scenarios that can target either the unprotected DemoStore or the
protected DropShield entry point; see [`load-tests/README.md`](load-tests/README.md) for
commands and profiles. Executed results are in [docs/benchmarks.md](docs/benchmarks.md); raw k6
summaries are versioned under [`load-tests/results/`](load-tests/results/). Both scripts and
DropShield reject any target that isn't localhost, loopback, or `host.docker.internal`.

## Docker

```powershell
docker build -f src/DropShield.Api/Dockerfile -t dropshield-api .
docker build -f src/DropShield.DemoStore/Dockerfile -t dropshield-demo-store .
```

Each container listens on port `8080`.

## License

[MIT](LICENSE).
