// DropShield reference snippet. Type: recv, priority: 5 (runs before cache lookup and before
// Adobe's default priority-50 snippets, so it can force a pass-to-origin decision ahead of
// normal caching logic). Coexists with Adobe Commerce's generated VCL; it does not replace it.
//
// Every route DropShield.Api exposes is a request it needs to see and finish before Commerce
// runs: catalogue/stock reads it rate-limits, and REST/GraphQL/storefront cart-add it wraps in
// a signed Origin Assertion. Fastly cannot tell an ordinary POST /graphql query from a
// protected cart mutation from HTTP routing information alone — the body has to be inspected,
// and DropShield.Api (GraphQlCartMutationInspector) is what does that. So this snippet routes
// the whole shared POST /graphql endpoint to DropShield rather than trying to split it here.
//
// req.http.Fastly-DropShield-Edge is internal edge metadata, not a DropShield.Api header: it is
// only used later in this VCL (see deliver-strip-internal-headers.vcl) and is stripped before
// the response reaches the client.

if (req.url ~ "^/api/" ||
    (req.method == "POST" && (
        req.url ~ "^/graphql(?:[?].*)?$" ||
        req.url ~ "^/checkout/cart/add(?:[?].*)?$" ||
        req.url ~ "^/rest/(?:default/)?V1/guest-carts/[A-Za-z0-9_-]{1,128}/(?:items|payment-information)(?:[?].*)?$"
    )) ||
    req.url ~ "^/health$") {

    set req.http.Fastly-DropShield-Edge = "1";
    set req.backend = F_dropshield;

    # Protected mutation and inventory routes must reach DropShield.Api uncached and
    # unconditionally on every request; see pass-protected.vcl for the cache/pass decision.
    return(pass);
}

if (req.url ~ "^/internal/") {
    error 404 "Not Found";
}

// Everything else (storefront pages, static assets, ordinary catalogue browsing not routed
// through DropShield) is left to Adobe Commerce's existing Fastly configuration and caching
// rules, unmodified by this snippet.
