namespace DropShield.Api.Admission;

public interface IAdmissionState
{
    ValueTask<AdmissionDecision> EvaluateAsync(
        AdmissionRequest request,
        CancellationToken cancellationToken);
}

public sealed record AdmissionRequest(
    string Drop,
    string SessionId,
    int MaximumActiveSessions,
    int AdmissionBatchSize,
    int MaximumWaitingSessions,
    TimeSpan SessionTtl,
    TimeSpan WaitingTtl,
    TimeSpan RetryAfter);

public sealed record AdmissionDecision(
    AdmissionStatus Status,
    TimeSpan RetryAfter)
{
    public static AdmissionDecision Admitted { get; } = new(
        AdmissionStatus.Admitted,
        TimeSpan.Zero);
}

public enum AdmissionStatus
{
    Admitted,
    Waiting,
    Full,
}
