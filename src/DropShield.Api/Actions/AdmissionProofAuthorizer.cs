using DropShield.Api.Admission;
using DropShield.Api.Options;
using DropShield.Api.Catalog;
using DropShield.Api.State;
using DropShield.Api.Traffic;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Actions;

/// <summary>
/// Authorizes protected mutations (cart/checkout/action-proof issuance) by validating the
/// signed admission proof and then re-confirming the session still holds an active admission
/// lease. Signature validity alone is not sufficient: a token remains cryptographically valid
/// for its full lifetime even if the underlying admission entitlement was revoked earlier
/// (session pruning, reduced capacity, admission-state reset). This mirrors the same live
/// check <see cref="AdmissionControlMiddleware"/> performs for the stock route.
/// </summary>
public sealed class AdmissionProofAuthorizer(
    AdmissionSessionProvider sessionProvider,
    IAdmissionTokenService admissionTokenService,
    AdmissionEvaluator admissionEvaluator,
    IOptions<DropShieldOptions> options,
    IProtectedDropCatalog catalog,
    TrafficMetrics metrics)
{
    private readonly DropShieldOptions _options = options.Value;

    public async ValueTask<AdmissionProofAuthorizationResult> AuthorizeAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var sessionId = sessionProvider.GetOrCreate(context);
        if (!context.Request.Cookies.TryGetValue(
                _options.AdmissionTokens.CookieName,
                out var token) || string.IsNullOrWhiteSpace(token))
        {
            return AdmissionProofAuthorizationResult.Required;
        }

        var dropId = context.Features.Get<TrafficRequestObservation>()?.ProtectedDropId ??
                     catalog.GetActiveDrop()?.DropId;
        if (string.IsNullOrEmpty(dropId) || !catalog.Status.IsUsable)
        {
            return AdmissionProofAuthorizationResult.Required;
        }

        var validation = admissionTokenService.Validate(
            token,
            dropId,
            sessionId);
        metrics.RecordAdmissionTokenValidation(validation);
        if (!validation.IsValid)
        {
            return AdmissionProofAuthorizationResult.Required;
        }

        AdmissionDecision decision;
        try
        {
            decision = await admissionEvaluator.EvaluateAsync(dropId, sessionId, cancellationToken);
        }
        catch (DistributedTrafficStateUnavailableException)
        {
            return AdmissionProofAuthorizationResult.StateUnavailable;
        }

        metrics.RecordAdmission(decision.Status);
        return decision.Status == AdmissionStatus.Admitted
            ? new AdmissionProofAuthorizationResult(true, false, sessionId)
            : AdmissionProofAuthorizationResult.Required;
    }
}

public sealed record AdmissionProofAuthorizationResult(bool IsAuthorized, bool IsStateUnavailable, string? SessionId)
{
    public static AdmissionProofAuthorizationResult Required { get; } = new(false, false, null);

    public static AdmissionProofAuthorizationResult StateUnavailable { get; } = new(false, true, null);
}
