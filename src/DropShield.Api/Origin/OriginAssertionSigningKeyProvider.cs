using System.Security.Cryptography;
using DropShield.Api.Options;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Origin;

public sealed class OriginAssertionSigningKeyProvider
{
    private readonly OriginAssertionSigningKey _activeKey;

    public OriginAssertionSigningKeyProvider(
        IOptions<DropShieldOptions> options,
        ILogger<OriginAssertionSigningKeyProvider> logger)
    {
        var assertionOptions = options.Value.OriginAssertions;
        if (!assertionOptions.Enabled)
        {
            _activeKey = OriginAssertionSigningKey.Disabled;
            return;
        }

        if (!string.IsNullOrWhiteSpace(assertionOptions.SigningKey))
        {
            _activeKey = new OriginAssertionSigningKey(
                assertionOptions.KeyId,
                Convert.FromBase64String(assertionOptions.SigningKey));
            return;
        }

        _activeKey = new OriginAssertionSigningKey(
            "ephemeral",
            RandomNumberGenerator.GetBytes(32));
        logger.LogWarning(
            "Ephemeral origin assertion signing key in use. Assertions will become invalid after restart and are not suitable for multi-instance deployment.");
    }

    public OriginAssertionSigningKey GetActiveKey() => _activeKey;
}

public sealed record OriginAssertionSigningKey(string KeyId, byte[] Material)
{
    public static OriginAssertionSigningKey Disabled { get; } = new("disabled", []);
}
