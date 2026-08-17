// DropShield reference snippet. Type: deliver, priority: 10.
//
// Never let DropShield.Api's internal routing/edge metadata leak to the client response.
// Backend name, internal hostnames, and the edge trust header must not be observable outside
// the edge <-> DropShield hop.

unset resp.http.Fastly-DropShield-Edge;
unset resp.http.X-DropShield-Edge-Key;
unset resp.http.X-Powered-By;
unset resp.http.Server;
