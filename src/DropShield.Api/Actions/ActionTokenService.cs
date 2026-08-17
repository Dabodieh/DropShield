using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DropShield.Api.Options;
using DropShield.Api.Security;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Actions;

public sealed class ActionTokenService(
    ActionProofSigningKeyProvider signingKeyProvider,
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
            Base64Url.Encode(DeriveSessionBinding(key.Material, sessionId)),
            GetActionName(action),
            Base64Url.Encode(RandomNumberGenerator.GetBytes(32)),
            issuedAt,
            checked(issuedAt + _options.LifetimeSeconds));
        var payloadPart = Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        var signingInput = $"{Version}.{payloadPart}";
        var signature = HMACSHA256.HashData(key.Material, Encoding.UTF8.GetBytes(signingInput));
        return $"{signingInput}.{Base64Url.Encode(signature)}";
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

        if (!Base64Url.TryDecode(parts[1], out var payloadBytes) ||
            !Base64Url.TryDecode(parts[2], out var actualSignature))
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

        if (!Base64Url.TryDecode(payload.Session, out var actualBinding) ||
            !Base64Url.TryDecode(payload.Jti, out var actionId) || actionId.Length != 32)
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
