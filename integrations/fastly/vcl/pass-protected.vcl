// DropShield reference snippet. Type: pass, priority: 10.
//
// Every route sent to the DropShield backend is either a protected mutation (cart, checkout,
// GraphQL, storefront cart-add) or scarce-stock/read state that must not be served from a
// stale cache. recv-route.vcl already forces return(pass) for all of them, so this snippet's
// job is to confirm no downstream logic re-enables caching for these requests, and to set the
// origin connection timeout appropriate for a synchronous protection check.

if (req.http.Fastly-DropShield-Edge) {
    set bereq.http.Host = dropshield_backend_host;
}
