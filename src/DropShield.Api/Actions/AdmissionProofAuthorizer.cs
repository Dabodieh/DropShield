using DropShield.Api.Admission;
using DropShield.Api.Options;
using DropShield.Api.Traffic;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Actions;

public sealed class AdmissionProofAuthorizer(
    AdmissionSessionProvider sessionProvider,
    IAdmissionTokenService admissionTokenService,
    IOptions<DropShieldOptions> options,
    TrafficMetrics metrics)
{
    private readonly DropShieldOptions _options = options.Value;

    public AdmissionProofAuthorizationResult Authorize(HttpContext context)
    {
        var sessionId = sessionProvider.GetOrCreate(context);
        if (!context.Request.Cookies.TryGetValue(
                _options.AdmissionTokens.CookieName,
                out var token) || string.IsNullOrWhiteSpace(token))
        {
            return AdmissionProofAuthorizationResult.Required;
        }

        var validation = admissionTokenService.Validate(
            token,
            _options.Admission.ProtectedProduct,
            sessionId);
        metrics.RecordAdmissionTokenValidation(validation);
        return validation.IsValid
            ? new AdmissionProofAuthorizationResult(true, sessionId)
            : AdmissionProofAuthorizationResult.Required;
    }
}

public sealed record AdmissionProofAuthorizationResult(bool IsAuthorized, string? SessionId)
{
    public static AdmissionProofAuthorizationResult Required { get; } = new(false, null);
}
