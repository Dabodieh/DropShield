namespace DropShield.Api.Origin;

public interface IOriginAssertionService
{
    string Issue(string drop, string action, string method, string route, ReadOnlySpan<byte> body);

    OriginAssertionValidationResult Validate(
        string assertion,
        string drop,
        string action,
        string method,
        string route,
        ReadOnlySpan<byte> body);
}

public sealed record OriginAssertionValidationResult(bool IsValid, OriginAssertionValidationFailure Failure)
{
    public static OriginAssertionValidationResult Valid() => new(true, OriginAssertionValidationFailure.None);

    public static OriginAssertionValidationResult Invalid(OriginAssertionValidationFailure failure) =>
        new(false, failure);
}

public enum OriginAssertionValidationFailure
{
    None,
    Malformed,
    UnsupportedVersion,
    UnknownKeyId,
    InvalidSignature,
    Expired,
    WrongMethod,
    WrongRoute,
    WrongDrop,
    WrongAction,
    BodyMismatch,
}
