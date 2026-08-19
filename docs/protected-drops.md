# Protected drops

Adobe Commerce remains the catalogue, price, image, category, and inventory authority. DropShield stores only a protected-drop identifier and assignments to existing Commerce product entity IDs.

## Operator workflow

1. Create and manage products in Adobe Commerce normally.
2. Open **Marketing > Protected Drops**.
3. Create or edit a protected drop, then search the Commerce catalogue by SKU or product name.
4. Select the existing products and save the drop.
5. Enable the drop when it is ready. An enabled drop must have at least one product.
6. DropShield.Api refreshes the authenticated Commerce protection manifest. No gateway restart is needed.

Removing a product or disabling a drop becomes effective at the gateway after the next manifest refresh. The Commerce connector reads its local persisted configuration directly and remains the final origin-side enforcement point.

## Current PoC constraints

- Multiple saved drop definitions are supported, but **only one protected drop may be enabled at a time**. The invariant is server-side and transactionally serialised (a `SELECT ... FOR UPDATE` lock row), confirmed safe under a near-concurrent enable race against two drops in runtime testing.
- The manifest is eventually consistent. The gateway keeps the last valid snapshot for the configured stale period. Before its first successful load, or after that period expires, potentially protected Commerce mutations fail closed with HTTP 503. Both boundaries (never-loaded, and stale-after-successful-load) were runtime-verified against a live gateway instance.
- The synthetic DropShield reservation ledger is not Magento MSI or Commerce inventory.
- A Commerce integration token or minimal-ACL admin token with only `DropShield_Connector::protection_manifest` is required for `GET /V1/dropshield/protection-manifest`. Its bearer token belongs in secret/environment configuration and is never logged.
- Deleting a Magento product that is assigned to a protected drop cascades (`ON DELETE CASCADE`) to remove the assignment row automatically; the drop definition itself is not deleted and can end up with zero assigned products, which the admin UI's own save path would refuse to let happen for an *enabled* drop but does not retroactively re-check.
- This is not a live retailer deployment. Production secret management, Magento deployment, and edge validation remain infrastructure-specific work.

## Runtime verification status

Admin drop management (grid load, ACL enforcement for a restricted role, create/edit, SKU and
name product search, product selection persistence across reopen, duplicate-assignment
de-duplication, one-active-drop invariant including a concurrent-enable attempt, remove/re-add/
disable lifecycle observed by DropShield.Api through its normal refresh cycle with no restart,
and FK cascade delete) is RUNTIME VERIFIED over real HTTP against a local, disposable Mage-OS
3.0.0 instance. Full end-to-end protected REST and GraphQL cart-add through DropShield.Api (real
admission token, real action proof, real origin assertion, real Commerce persistence) is also
RUNTIME VERIFIED. Storefront `checkout/cart/add` remains UNVERIFIED for the same form-key CSRF
reason documented in [Adobe Commerce integration](adobe-commerce.md).
