namespace DropShield.Api.Admission;

public interface IAdmissionTokenService
{
    string Issue(string drop, string sessionId);

    AdmissionTokenValidationResult Validate(string token, string drop, string sessionId);
}

public sealed record AdmissionTokenValidationResult(
    bool IsValid,
    AdmissionTokenValidationFailure? Failure)
{
    public static AdmissionTokenValidationResult Valid { get; } = new(true, null);

    public static AdmissionTokenValidationResult Invalid(
        AdmissionTokenValidationFailure failure) => new(false, failure);
}

public enum AdmissionTokenValidationFailure
{
    Malformed,
    InvalidSignature,
    Expired,
    WrongDrop,
    WrongSession,
    UnsupportedVersion,
    UnknownKeyId,
}
