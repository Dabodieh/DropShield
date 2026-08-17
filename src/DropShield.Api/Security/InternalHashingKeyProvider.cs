using System.Security.Cryptography;
using DropShield.Api.Options;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Security;

/// <summary>
/// Provides the shared key for internal, non-bearer HMAC partitioning (reservation
/// ownership, behavioural actor identity). See <see cref="InternalHashingOptions"/>.
/// </summary>
public sealed class InternalHashingKeyProvider
{
    private readonly byte[] _material;

    public InternalHashingKeyProvider(
        IOptions<DropShieldOptions> options,
        ILogger<InternalHashingKeyProvider> logger)
    {
        var configured = options.Value.InternalHashing.SigningKey;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            _material = Convert.FromBase64String(configured);
            return;
        }

        _material = RandomNumberGenerator.GetBytes(32);
        logger.LogWarning(
            "Ephemeral internal hashing key in use. Reservation and behavioural partitions will not be consistent across restarts or instances.");
    }

    public byte[] Material => _material;
}
