using DropShield.Api.Options;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Actions;

public sealed class InMemoryReplayState(
    TimeProvider timeProvider,
    IOptions<DropShieldOptions> options) : IReplayState
{
    private readonly object _sync = new();
    private readonly Dictionary<string, DateTimeOffset> _consumed = new(StringComparer.Ordinal);
    private readonly int _maximumMarkers = options.Value.ActionProofs.MaximumInMemoryMarkers;

    public ValueTask<ReplayConsumeResult> TryConsumeAsync(
        string replayKey,
        TimeSpan timeToLive,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (timeToLive <= TimeSpan.Zero)
        {
            return ValueTask.FromResult(ReplayConsumeResult.AlreadyConsumed);
        }

        lock (_sync)
        {
            var now = timeProvider.GetUtcNow();
            foreach (var expired in _consumed
                         .Where(entry => entry.Value <= now)
                         .Select(entry => entry.Key)
                         .ToArray())
            {
                _consumed.Remove(expired);
            }

            if (_consumed.ContainsKey(replayKey))
            {
                return ValueTask.FromResult(ReplayConsumeResult.AlreadyConsumed);
            }

            if (_consumed.Count >= _maximumMarkers)
            {
                throw new ReplayStateUnavailableException(
                    "In-memory replay state capacity is exhausted.",
                    new InvalidOperationException("Replay marker capacity reached."));
            }

            _consumed.Add(replayKey, now + timeToLive);
            return ValueTask.FromResult(ReplayConsumeResult.Consumed);
        }
    }
}
