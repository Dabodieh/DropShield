using DropShield.Api.Options;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Admission;

public sealed class AdmissionEvaluator(
    IAdmissionState state,
    AdmissionSessionProvider sessionProvider,
    IOptions<DropShieldOptions> options)
{
    private readonly AdmissionOptions _options = options.Value.Admission;

    public ValueTask<AdmissionDecision> EvaluateAsync(
        HttpContext context,
        CancellationToken cancellationToken) =>
        state.EvaluateAsync(
            new AdmissionRequest(
                _options.ProtectedProduct,
                sessionProvider.GetOrCreate(context),
                _options.MaximumActiveSessions,
                _options.AdmissionBatchSize,
                _options.MaximumWaitingSessions,
                TimeSpan.FromSeconds(_options.SessionTtlSeconds),
                TimeSpan.FromSeconds(_options.WaitingTtlSeconds),
                TimeSpan.FromSeconds(_options.RetryAfterSeconds)),
            cancellationToken);
}
