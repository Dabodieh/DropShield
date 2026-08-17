using DropShield.Api.Admission;

namespace DropShield.Tests.Support;

/// <summary>
/// Admits every session normally until <see cref="Revoke"/> is called, after which every
/// evaluation reports <see cref="AdmissionStatus.Waiting"/>. Models a session whose active
/// admission lease was revoked server-side (capacity reduction, pruning, state reset) after
/// the client already holds a signature-valid, unexpired admission token.
/// </summary>
internal sealed class RevocableAdmissionState : IAdmissionState
{
    private volatile bool _revoked;

    public void Revoke() => _revoked = true;

    public ValueTask<AdmissionDecision> EvaluateAsync(
        AdmissionRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(_revoked
            ? new AdmissionDecision(AdmissionStatus.Waiting, request.RetryAfter)
            : AdmissionDecision.Admitted);
}
