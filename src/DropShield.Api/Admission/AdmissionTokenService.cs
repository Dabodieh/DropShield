using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DropShield.Api.Options;
using DropShield.Api.Security;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Admission;

public sealed class AdmissionTokenService(
    AdmissionSigningKeyProvider signingKeyProvider,
    IOptions<DropShieldOptions> options,
    TimeProvider timeProvider) : IAdmissionTokenService
{
    private const string Version = "v1";
    private const string SessionBindingPrefix = "DropShield.Admission.Session.v1:";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
    };

    private readonly AdmissionTokenOptions _options = options.Value.AdmissionTokens;

    public string Issue(string drop, string sessionId)
    {
        var key = signingKeyProvider.GetActiveKey();
        var issuedAt = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var payload = new AdmissionTokenPayload(
            1,
            key.KeyId,
            drop,
            Base64Url.Encode(DeriveSessionBinding(key.Material, sessionId)),
            issuedAt,
            checked(issuedAt + _options.LifetimeSeconds));
        var payloadPart = Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        var signingInput = $"{Version}.{payloadPart}";
        var signature = HMACSHA256.HashData(key.Material, Encoding.UTF8.GetBytes(signingInput));
        return $"{signingInput}.{Base64Url.Encode(signature)}";
    }

    public AdmissionTokenValidationResult Validate(string token, string drop, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4_096)
        {
            return AdmissionTokenValidationResult.Invalid(AdmissionTokenValidationFailure.Malformed);
        }

        var parts = token.Split('.', StringSplitOptions.None);
        if (parts.Length != 3 || !string.Equals(parts[0], Version, StringComparison.Ordinal))
        {
            return AdmissionTokenValidationResult.Invalid(
                AdmissionTokenValidationFailure.UnsupportedVersion);
        }

        if (!Base64Url.TryDecode(parts[1], out var payloadBytes) ||
            !Base64Url.TryDecode(parts[2], out var actualSignature))
        {
            return AdmissionTokenValidationResult.Invalid(AdmissionTokenValidationFailure.Malformed);
        }

        AdmissionTokenPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<AdmissionTokenPayload>(payloadBytes, JsonOptions);
        }
        catch (JsonException)
        {
            return AdmissionTokenValidationResult.Invalid(AdmissionTokenValidationFailure.Malformed);
        }

        if (payload is null || payload.V != 1 ||
            !string.Equals(payload.Kid, signingKeyProvider.GetActiveKey().KeyId, StringComparison.Ordinal))
        {
            return AdmissionTokenValidationResult.Invalid(
                payload?.V != 1
                    ? AdmissionTokenValidationFailure.UnsupportedVersion
                    : AdmissionTokenValidationFailure.UnknownKeyId);
        }

        var key = signingKeyProvider.GetActiveKey();
        var expectedSignature = HMACSHA256.HashData(
            key.Material,
            Encoding.UTF8.GetBytes($"{Version}.{parts[1]}"));
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, actualSignature))
        {
            return AdmissionTokenValidationResult.Invalid(
                AdmissionTokenValidationFailure.InvalidSignature);
        }

        var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        if (payload.Iat < 0 || payload.Exp <= payload.Iat ||
            payload.Exp - payload.Iat > _options.LifetimeSeconds || now >= payload.Exp)
        {
            return AdmissionTokenValidationResult.Invalid(AdmissionTokenValidationFailure.Expired);
        }

        if (!string.Equals(payload.Drop, drop, StringComparison.Ordinal))
        {
            return AdmissionTokenValidationResult.Invalid(AdmissionTokenValidationFailure.WrongDrop);
        }

        if (!Base64Url.TryDecode(payload.Session, out var actualBinding))
        {
            return AdmissionTokenValidationResult.Invalid(AdmissionTokenValidationFailure.Malformed);
        }

        var expectedBinding = DeriveSessionBinding(key.Material, sessionId);
        return CryptographicOperations.FixedTimeEquals(expectedBinding, actualBinding)
            ? AdmissionTokenValidationResult.Valid
            : AdmissionTokenValidationResult.Invalid(AdmissionTokenValidationFailure.WrongSession);
    }

    private static byte[] DeriveSessionBinding(byte[] key, string sessionId) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{SessionBindingPrefix}{sessionId}"));

    private sealed record AdmissionTokenPayload(
        int V,
        string Kid,
        string Drop,
        string Session,
        long Iat,
        long Exp);
}
