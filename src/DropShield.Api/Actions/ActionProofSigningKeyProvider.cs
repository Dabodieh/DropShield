using System.Security.Cryptography;
using DropShield.Api.Options;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Actions;

public sealed class ActionProofSigningKeyProvider
{
    private readonly ActionProofSigningKey _activeKey;

    public ActionProofSigningKeyProvider(
        IOptions<DropShieldOptions> options,
        ILogger<ActionProofSigningKeyProvider> logger)
    {
        var proofOptions = options.Value.ActionProofs;
        if (!proofOptions.Enabled)
        {
            _activeKey = ActionProofSigningKey.Disabled;
            return;
        }

        if (!string.IsNullOrWhiteSpace(proofOptions.SigningKey))
        {
            _activeKey = new ActionProofSigningKey(
                proofOptions.KeyId,
                Convert.FromBase64String(proofOptions.SigningKey));
            return;
        }

        _activeKey = new ActionProofSigningKey(
            "ephemeral",
            RandomNumberGenerator.GetBytes(32));
        logger.LogWarning(
            "Ephemeral action proof signing key in use. Proofs will become invalid after restart and are not suitable for multi-instance deployment.");
    }

    public ActionProofSigningKey GetActiveKey() => _activeKey;
}

public sealed record ActionProofSigningKey(string KeyId, byte[] Material)
{
    public static ActionProofSigningKey Disabled { get; } = new("disabled", []);
}
