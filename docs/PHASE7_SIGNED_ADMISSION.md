# Phase 7 — Signed admission tokens

Phase 7 adds browser-held, cryptographically verifiable proof that DropShield admitted a session to one protected drop. It is not authentication, a login, payment proof, an inventory reservation, or a cart/checkout replay-control mechanism.

## Flow and boundary

```text
protected request
        ↓
per-client abuse policy
        ↓
admission-token check (when a token is present)
        ↓
Phase 6 admission state / active lease check
   ├─ admitted → issue proof when missing → DemoStore
   └─ waiting/full/state failure → existing 202/503 behavior
```

Missing proof intentionally continues to the Phase 6 evaluation flow, allowing an admitted session to receive its first proof. A malformed, modified, expired, wrong-drop, wrong-session, unsupported-version, or unknown-key token returns HTTP 403 with the safe `{ "error": "admission_required" }` contract and never reaches DemoStore. The token cookie is deleted on that response.

The Phase 6 state evaluation still happens for a valid token. This refreshes the active-session lease and prevents a signed token from extending admission beyond server-side capacity semantics. Redis is therefore still used for distributed admission state where configured, but no token is stored in Redis and signature verification is local.

## Token format and claims

The compact wire format is:

```text
v1.<base64url UTF-8 JSON payload>.<base64url HMAC>
```

The HMAC input is the literal `v1.payload` prefix and payload segment. The JSON payload contains only:

- `v` — payload format version, currently `1`;
- `kid` — active signing-key identifier, included for future rotation;
- `drop` — protected product/drop identifier;
- `session` — a Base64Url, HMAC-derived session binding;
- `iat` and `exp` — UTC Unix-second issued and expiry timestamps.

Raw session cookies, IP addresses, synthetic identities, customer names, emails, addresses, payment values, and token values are excluded.

## Cryptography and bindings

DropShield uses .NET `HMACSHA256` with a Base64-configured signing key of at least 32 random bytes. It uses explicit UTF-8, secure random generation for ephemeral keys, UTC through `TimeProvider`, Base64Url encoding, and `CryptographicOperations.FixedTimeEquals` for signature and session-binding comparisons. It does not use JWT/OIDC, custom ciphers, encryption, SHA-1, MD5, or plain SHA-256 signatures.

The session binding is a domain-separated HMAC-SHA256 derivation of the opaque Phase 6 session identifier. Validation derives the expected binding from the current session cookie and compares it to the signed claim. Copying proof into another admission session therefore fails. The signed `drop` claim prevents a proof issued for one protected product from applying to another.

The configured lifetime is 60 seconds in the PoC and is startup-validated not to exceed the Phase 6 active-session TTL. Tokens are reusable during that short lifetime: Phase 7 intentionally does not add one-time use, cart-action nonce consumption, replay caches, idempotency keys, or reservations.

## Cookie transport

`DropShield.Admission` is distinct from the Phase 6 `DropShield.Session` cookie. It is HttpOnly, SameSite=Lax, essential, Secure for HTTPS requests, and scoped to the configured protected-stock path. Local HTTP remains usable because Secure is set only for HTTPS. `DemoStoreClient` constructs a new outbound request and copies no inbound cookies, so neither session nor admission token is forwarded to DemoStore.

## Key management and multi-instance behavior

Set the Base64 key and identifier through configuration providers such as environment variables, user secrets, or a deployment secret store; never put actual key material in source control:

```powershell
$env:DropShield__AdmissionTokens__KeyId = '2026-08-primary'
$env:DropShield__AdmissionTokens__SigningKey = [Convert]::ToBase64String(
    [Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

Production and Redis mode require an explicit valid key at startup. Redis/multi-instance deployments must use the same key and key identifier in every DropShield process, allowing instance B to verify a token minted by instance A. Missing or weak material fails options validation before the application starts.

In Development or Testing with InMemory state only, a missing key creates an ephemeral random 32-byte startup key and logs a warning without printing it. Tokens become invalid after restart and cannot verify across instances. This exists only for convenient local development; it is not a production fallback.

`kid` makes the format rotation-ready. This phase accepts one active key only; a bounded previous-key verifier ring is deliberately deferred because the token lifetime is short and a full secret-rotation workflow would expand the phase.

## Metrics and logging

The controlled internal metrics snapshot adds fixed aggregate `admissionTokens` counters: `issued`, `validations`, `validationFailures`, and `expired`. It exposes no token values, session bindings, raw cookies, or key information. Logs contain only fixed validation categories at Debug and the non-secret ephemeral-key warning; they never contain token, cookie, binding, or signing-key values.

## Threat model and limits

This phase makes client-side changes to an admission claim, expiry, product scope, signature, or a copied token in another session ineffective. It rejects expired proof and prevents invalid proof from reaching the origin.

It does not mitigate malware stealing a complete active browser session, bot classification, account farming, residential proxies, cart-action replay, purchase-limit circumvention, or inventory hoarding after valid admission. Those remain future phases.
