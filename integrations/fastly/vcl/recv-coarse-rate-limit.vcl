// DropShield reference snippet. Type: recv, priority: 4 (runs before recv-route.vcl so an
// obvious volumetric flood is rejected before DropShield.Api is reached at all).
//
// This is coarse, IP-based, protocol-level flood protection for DropShield.Api itself — not a
// reimplementation of DropShield's own per-client/aggregate rate limits, which stay
// ecommerce-aware and authoritative inside DropShield.Api (see docs/traffic-control.md). The
// threshold below is illustrative only and is not a recommendation for any specific deployment
// or retailer; a real deployment must size this from its own traffic baseline.
//
// ratelimit.check_rate signature: (client key, rate-counter name, max ops, time window in
// seconds, penalty-box name, penalty-box duration). See:
// https://www.fastly.com/documentation/reference/vcl/functions/rate-limiting/ratelimit-check-rate/

if (req.url ~ "^/api/" || req.url ~ "^/graphql$" || req.url ~ "^/checkout/cart/add$") {
    if (ratelimit.check_rate(client.ip, "dropshield_edge_flood", 200, 10, "dropshield_edge_penaltybox", 60s)) {
        error 429 "Too Many Requests";
    }
}
