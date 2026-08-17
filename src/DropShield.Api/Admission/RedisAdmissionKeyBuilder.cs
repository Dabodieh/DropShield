using System.Security.Cryptography;
using System.Text;
using DropShield.Api.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace DropShield.Api.Admission;

public sealed class RedisAdmissionKeyBuilder(IOptions<DropShieldOptions> options)
{
    private readonly RedisStateOptions _options = options.Value.Redis;

    public RedisAdmissionKeys Build(string drop)
    {
        var root = $"{_options.KeyPrefix}:admission:{{{drop.ToLowerInvariant()}}}";
        return new RedisAdmissionKeys(
            $"{root}:active",
            $"{root}:waiting:order",
            $"{root}:waiting:expiry",
            $"{root}:sequence",
            $"{root}:batch");
    }

    public string HashSession(string sessionId)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_options.IdentityHashKey);
        var sessionBytes = Encoding.UTF8.GetBytes(sessionId);
        var digest = HMACSHA256.HashData(keyBytes, sessionBytes);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}

public sealed record RedisAdmissionKeys(
    RedisKey Active,
    RedisKey WaitingOrder,
    RedisKey WaitingExpiry,
    RedisKey Sequence,
    RedisKey Batch)
{
    public RedisKey[] ToArray() =>
        [Active, WaitingOrder, WaitingExpiry, Sequence, Batch];
}
