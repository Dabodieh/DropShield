// DropShield reference snippet. Type: recv, priority: 6 (runs immediately after
// recv-route.vcl so a forged header never reaches vcl_pass/vcl_miss).
//
// A direct client cannot be trusted to supply DropShield's own internal headers. Strip any
// client-supplied value before this request is ever forwarded, then set the trusted edge value
// fresh. DropShield.Api independently rejects a missing/incorrect edge key (see
// EdgeTrustMiddleware) rather than relying solely on this snippet — defense in depth in case
// DropShield.Api is reachable directly.
//
// dropship_edge_key is a Fastly edge dictionary entry, not a signing key: it exists only to
// prove "this request came through the edge," and is never reused for admission, action proof,
// or origin assertion signing (see docs/fastly.md).

if (req.http.Fastly-DropShield-Edge) {
    unset req.http.X-DropShield-Edge-Key;
    unset req.http.X-DropShield-Origin-Assertion;
    unset req.http.X-DropShield-Action;
    unset req.http.X-DropShield-Test-Client;

    set req.http.X-DropShield-Edge-Key = table.lookup(dropshield_edge_config, "edge_key");
}
