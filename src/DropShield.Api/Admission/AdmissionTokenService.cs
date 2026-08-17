using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DropShield.Api.Options;
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
            Base64UrlEncode(DeriveSessionBinding(key.Material, sessionId)),
            issuedAt,
            checked(issuedAt + _options.LifetimeSeconds));
        var payloadPart = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        var signingInput = $"{Version}.{payloadPart}";
        var signature = HMACSHA256.HashData(key.Material, Encoding.UTF8.GetBytes(signingInput));
        return $"{signingInput}.{Base64UrlEncode(signature)}";
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

        if (!TryBase64UrlDecode(parts[1], out var payloadBytes) ||
            !TryBase64UrlDecode(parts[2], out var actualSignature))
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

        if (!TryBase64UrlDecode(payload.Session, out var actualBinding))
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

    private static string Base64UrlEncode(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static bool TryBase64UrlDecode(string value, out byte[] decoded)
    {
        decoded = [];
        if (string.IsNullOrEmpty(value) || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            return false;
        }

        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 = (base64.Length % 4) switch
            {
                0 => base64,
                2 => base64 + "==",
                3 => base64 + "=",
                _ => string.Empty,
            };
            if (base64.Length == 0)
            {
                return false;
            }

            decoded = Convert.FromBase64String(base64);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed record AdmissionTokenPayload(
        int V,
        string Kid,
        string Drop,
        string Session,
        long Iat,
        long Exp);
}
