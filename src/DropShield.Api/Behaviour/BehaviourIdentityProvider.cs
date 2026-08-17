using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DropShield.Api.Admission;
using DropShield.Api.Security;
using DropShield.Api.Traffic;

namespace DropShield.Api.Behaviour;

public sealed partial class BehaviourIdentityProvider(
    InternalHashingKeyProvider hashingKeys,
    ClientIdentityProvider clientIdentityProvider)
{
    public string GetActor(HttpContext context)
    {
        var source = context.Request.Cookies.TryGetValue(AdmissionSessionProvider.CookieName, out var sessionId) &&
                     sessionId is not null &&
                     SessionIdPattern().IsMatch(sessionId)
            ? $"session:{sessionId}"
            : $"client:{clientIdentityProvider.GetPartitionKey(context)}";

        return Convert.ToHexString(HMACSHA256.HashData(
            hashingKeys.Material,
            Encoding.UTF8.GetBytes($"DropShield.Behaviour.Actor.v1:{source}"))).ToLowerInvariant();
    }

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex SessionIdPattern();
}
