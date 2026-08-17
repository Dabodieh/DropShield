namespace DropShield.Api.Actions;

public interface IActionTokenService
{
    string Issue(string drop, string sessionId, ActionKind action);

    ActionTokenValidationResult Validate(
        string token,
        string drop,
        string sessionId,
        ActionKind action);
}

public sealed record ActionTokenValidationResult(
    bool IsValid,
    ActionTokenValidationFailure? Failure,
    string? ReplayKey,
    TimeSpan RemainingLifetime)
{
    public static ActionTokenValidationResult Valid(string replayKey, TimeSpan remainingLifetime) =>
        new(true, null, replayKey, remainingLifetime);

    public static ActionTokenValidationResult Invalid(ActionTokenValidationFailure failure) =>
        new(false, failure, null, TimeSpan.Zero);
}

public enum ActionTokenValidationFailure
{
    Malformed,
    InvalidSignature,
    Expired,
    WrongDrop,
    WrongSession,
    WrongAction,
    UnsupportedVersion,
    UnknownKeyId,
}
