using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DropShield.Api.Admission;
using DropShield.Api.Options;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Actions;

public sealed class ActionTokenService(
    AdmissionSigningKeyProvider signingKeyProvider,
    IOptions<DropShieldOptions> options,
    TimeProvider timeProvider) : IActionTokenService
{
    private const string Version = "v1";
    private const string SessionBindingPrefix = "DropShield.Action.Session.v1:";
    private const string ReplayKeyPrefix = "DropShield.Action.Replay.v1:";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
    };

    private readonly ActionProofOptions _options = options.Value.ActionProofs;

    public string Issue(string drop, string sessionId, ActionKind action)
    {
        var key = signingKeyProvider.GetActiveKey();
        var issuedAt = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var payload = new ActionTokenPayload(
            1,
            key.KeyId,
            drop,
            Base64UrlEncode(DeriveSessionBinding(key.Material, sessionId)),
            GetActionName(action),
            Base64UrlEncode(RandomNumberGenerator.GetBytes(32)),
            issuedAt,
            checked(issuedAt + _options.LifetimeSeconds));
        var payloadPart = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        var signingInput = $"{Version}.{payloadPart}";
        var signature = HMACSHA256.HashData(key.Material, Encoding.UTF8.GetBytes(signingInput));
        return $"{signingInput}.{Base64UrlEncode(signature)}";
    }

    public ActionTokenValidationResult Validate(
        string token,
        string drop,
        string sessionId,
        ActionKind action)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4_096)
        {
            return ActionTokenValidationResult.Invalid(ActionTokenValidationFailure.Malformed);
        }

        var parts = token.Split('.', StringSplitOptions.None);
        if (parts.Length != 3 || !string.Equals(parts[0], Version, StringComparison.Ordinal))
        {
            return ActionTokenValidationResult.Invalid(
                ActionTokenValidationFailure.UnsupportedVersion);
        }

        if (!TryBase64UrlDecode(parts[1], out var payloadBytes) ||
            !TryBase64UrlDecode(parts[2], out var actualSignature))
        {
            return ActionTokenValidationResult.Invalid(ActionTokenValidationFailure.Malformed);
        }

        ActionTokenPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ActionTokenPayload>(payloadBytes, JsonOptions);
        }
        catch (JsonException)
        {
            return ActionTokenValidationResult.Invalid(ActionTokenValidationFailure.Malformed);
        }

        var key = signingKeyProvider.GetActiveKey();
        if (payload is null || payload.V != 1)
        {
            return ActionTokenValidationResult.Invalid(
                ActionTokenValidationFailure.UnsupportedVersion);
        }

        if (!string.Equals(payload.Kid, key.KeyId, StringComparison.Ordinal))
        {
            return ActionTokenValidationResult.Invalid(ActionTokenValidationFailure.UnknownKeyId);
        }

        var expectedSignature = HMACSHA256.HashData(
            key.Material,
            Encoding.UTF8.GetBytes($"{Version}.{parts[1]}"));
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, actualSignature))
        {
            return ActionTokenValidationResult.Invalid(
                ActionTokenValidationFailure.InvalidSignature);
        }

        var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        if (payload.Iat < 0 || payload.Exp <= payload.Iat ||
            payload.Exp - payload.Iat > _options.LifetimeSeconds || now >= payload.Exp)
        {
            return ActionTokenValidationResult.Invalid(ActionTokenValidationFailure.Expired);
        }

        if (!string.Equals(payload.Drop, drop, StringComparison.Ordinal))
        {
            return ActionTokenValidationResult.Invalid(ActionTokenValidationFailure.WrongDrop);
        }

        if (!string.Equals(payload.Action, GetActionName(action), StringComparison.Ordinal))
        {
            return ActionTokenValidationResult.Invalid(ActionTokenValidationFailure.WrongAction);
        }

        if (!TryBase64UrlDecode(payload.Session, out var actualBinding) ||
            !TryBase64UrlDecode(payload.Jti, out var actionId) || actionId.Length != 32)
        {
            return ActionTokenValidationResult.Invalid(ActionTokenValidationFailure.Malformed);
        }

        var expectedBinding = DeriveSessionBinding(key.Material, sessionId);
        if (!CryptographicOperations.FixedTimeEquals(expectedBinding, actualBinding))
        {
            return ActionTokenValidationResult.Invalid(ActionTokenValidationFailure.WrongSession);
        }

        var replayKey = Convert.ToHexString(HMACSHA256.HashData(
            key.Material,
            Encoding.UTF8.GetBytes($"{ReplayKeyPrefix}{payload.Jti}"))).ToLowerInvariant();
        return ActionTokenValidationResult.Valid(
            replayKey,
            TimeSpan.FromSeconds(payload.Exp - now));
    }

    private static string GetActionName(ActionKind action) => action switch
    {
        ActionKind.Cart => "cart",
        ActionKind.Checkout => "checkout",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

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

    private sealed record ActionTokenPayload(
        int V,
        string Kid,
        string Drop,
        string Session,
        string Action,
        string Jti,
        long Iat,
        long Exp);
}
