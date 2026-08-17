using System.Security.Cryptography;
using DropShield.Api.Options;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Admission;

public sealed class AdmissionSigningKeyProvider
{
    private readonly AdmissionSigningKey _activeKey;

    public AdmissionSigningKeyProvider(
        IOptions<DropShieldOptions> options,
        IHostEnvironment environment,
        ILogger<AdmissionSigningKeyProvider> logger)
    {
        var tokenOptions = options.Value.AdmissionTokens;
        if (!tokenOptions.Enabled)
        {
            _activeKey = AdmissionSigningKey.Disabled;
            return;
        }

        if (!string.IsNullOrWhiteSpace(tokenOptions.SigningKey))
        {
            _activeKey = new AdmissionSigningKey(
                tokenOptions.KeyId,
                Convert.FromBase64String(tokenOptions.SigningKey));
            return;
        }

        _activeKey = new AdmissionSigningKey(
            "ephemeral",
            RandomNumberGenerator.GetBytes(32));
        logger.LogWarning(
            "Ephemeral admission signing key in use. Tokens will become invalid after restart and are not suitable for multi-instance deployment.");
    }

    public AdmissionSigningKey GetActiveKey() => _activeKey;
}

public sealed record AdmissionSigningKey(string KeyId, byte[] Material)
{
    public static AdmissionSigningKey Disabled { get; } = new("disabled", []);
}
