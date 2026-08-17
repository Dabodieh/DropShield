using System.Security.Cryptography;
using System.Text;
using DropShield.Api.Options;
using Microsoft.Extensions.Options;

namespace DropShield.Api.State;

public sealed class RedisTrafficKeyBuilder(IOptions<DropShieldOptions> options)
{
    private readonly RedisStateOptions _options = options.Value.Redis;

    public string Build(DistributedTrafficRequest request)
    {
        var policy = request.Policy.ToString().ToLowerInvariant();
        var scope = request.Scope == TrafficLimitScope.Aggregate
            ? "aggregate"
            : "client";

        if (request.Scope == TrafficLimitScope.Aggregate)
        {
            return $"{_options.KeyPrefix}:rate:{policy}:{scope}";
        }

        if (string.IsNullOrWhiteSpace(request.ClientPartition))
        {
            throw new ArgumentException(
                "A client partition is required for a per-client limit.",
                nameof(request));
        }

        var keyBytes = Encoding.UTF8.GetBytes(_options.IdentityHashKey);
        var identityBytes = Encoding.UTF8.GetBytes(request.ClientPartition);
        var digest = HMACSHA256.HashData(keyBytes, identityBytes);
        return $"{_options.KeyPrefix}:rate:{policy}:{scope}:{Convert.ToHexString(digest).ToLowerInvariant()}";
    }
}
