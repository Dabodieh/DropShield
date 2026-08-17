namespace DropShield.Api.Actions;

public interface IReplayState
{
    ValueTask<ReplayConsumeResult> TryConsumeAsync(
        string replayKey,
        TimeSpan timeToLive,
        CancellationToken cancellationToken);
}

public sealed record ReplayConsumeResult(bool IsConsumed)
{
    public static ReplayConsumeResult Consumed { get; } = new(true);

    public static ReplayConsumeResult AlreadyConsumed { get; } = new(false);
}

public sealed class ReplayStateUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);
