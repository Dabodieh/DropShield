using DropShield.Api.Options;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Traffic;

public sealed class TrafficPolicyOptionsSetup(IOptions<DropShieldOptions> options)
    : IConfigureOptions<RateLimiterOptions>
{
    public void Configure(RateLimiterOptions rateLimiterOptions) =>
        TrafficPolicy.Configure(rateLimiterOptions, options.Value);
}
