using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DropShield.Api.Options;
using DropShield.Api.Security;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Origin;

public sealed class OriginAssertionService(
    OriginAssertionSigningKeyProvider signingKeyProvider,
    IOptions<DropShieldOptions> options,
    TimeProvider timeProvider) : IOriginAssertionService
{
    private const string Version = "v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
    };

    private readonly OriginAssertionOptions _options = options.Value.OriginAssertions;

    public string Issue(string drop, string action, string method, string route, ReadOnlySpan<byte> body)
    {
        var key = signingKeyProvider.GetActiveKey();
        var issuedAt = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var payload = new OriginAssertionPayload(
            1,
            key.KeyId,
            drop,
            action,
            method.ToUpperInvariant(),
            route,
            Base64Url.Encode(SHA256.HashData(body)),
            Base64Url.Encode(RandomNumberGenerator.GetBytes(16)),
            issuedAt,
            checked(issuedAt + _options.LifetimeSeconds));
        var payloadPart = Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        var signingInput = $"{Version}.{payloadPart}";
        var signature = HMACSHA256.HashData(key.Material, Encoding.UTF8.GetBytes(signingInput));
        return $"{signingInput}.{Base64Url.Encode(signature)}";
    }

    public OriginAssertionValidationResult Validate(
        string assertion,
        string drop,
        string action,
        string method,
        string route,
        ReadOnlySpan<byte> body)
    {
        if (string.IsNullOrWhiteSpace(assertion) || assertion.Length > 2_048)
        {
            return OriginAssertionValidationResult.Invalid(OriginAssertionValidationFailure.Malformed);
        }

        var parts = assertion.Split('.', StringSplitOptions.None);
        if (parts.Length != 3 || !string.Equals(parts[0], Version, StringComparison.Ordinal))
        {
            return OriginAssertionValidationResult.Invalid(OriginAssertionValidationFailure.UnsupportedVersion);
        }

        if (!Base64Url.TryDecode(parts[1], out var payloadBytes) ||
            !Base64Url.TryDecode(parts[2], out var actualSignature))
        {
            return OriginAssertionValidationResult.Invalid(OriginAssertionValidationFailure.Malformed);
        }

        OriginAssertionPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<OriginAssertionPayload>(payloadBytes, JsonOptions);
        }
        catch (JsonException)
        {
            return OriginAssertionValidationResult.Invalid(OriginAssertionValidationFailure.Malformed);
        }

        var key = signingKeyProvider.GetActiveKey();
        if (payload is null || payload.V != 1)
        {
            return OriginAssertionValidationResult.Invalid(OriginAssertionValidationFailure.UnsupportedVersion);
        }

        if (!string.Equals(payload.Kid, key.KeyId, StringComparison.Ordinal))
        {
            return OriginAssertionValidationResult.Invalid(OriginAssertionValidationFailure.UnknownKeyId);
        }

        var expectedSignature = HMACSHA256.HashData(
            key.Material,
            Encoding.UTF8.GetBytes($"{Version}.{parts[1]}"));
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, actualSignature))
        {
            return OriginAssertionValidationResult.Invalid(OriginAssertionValidationFailure.InvalidSignature);
        }

        var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        if (payload.Iat < 0 || payload.Exp <= payload.Iat ||
            payload.Exp - payload.Iat > _options.LifetimeSeconds || now >= payload.Exp)
        {
            return OriginAssertionValidationResult.Invalid(OriginAssertionValidationFailure.Expired);
        }

        if (!string.Equals(payload.Method, method.ToUpperInvariant(), StringComparison.Ordinal))
        {
            return OriginAssertionValidationResult.Invalid(OriginAssertionValidationFailure.WrongMethod);
        }

        if (!string.Equals(payload.Route, route, StringComparison.Ordinal))
        {
            return OriginAssertionValidationResult.Invalid(OriginAssertionValidationFailure.WrongRoute);
        }

        if (!string.Equals(payload.Drop, drop, StringComparison.Ordinal))
        {
            return OriginAssertionValidationResult.Invalid(OriginAssertionValidationFailure.WrongDrop);
        }

        if (!string.Equals(payload.Action, action, StringComparison.Ordinal))
        {
            return OriginAssertionValidationResult.Invalid(OriginAssertionValidationFailure.WrongAction);
        }

        var expectedBodyHash = SHA256.HashData(body);
        if (!Base64Url.TryDecode(payload.BodyHash, out var actualBodyHash) ||
            !CryptographicOperations.FixedTimeEquals(expectedBodyHash, actualBodyHash))
        {
            return OriginAssertionValidationResult.Invalid(OriginAssertionValidationFailure.BodyMismatch);
        }

        return OriginAssertionValidationResult.Valid();
    }

    private sealed record OriginAssertionPayload(
        int V,
        string Kid,
        string Drop,
        string Action,
        string Method,
        string Route,
        string BodyHash,
        string Jti,
        long Iat,
        long Exp);
}
