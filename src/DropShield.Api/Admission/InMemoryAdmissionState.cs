namespace DropShield.Api.Admission;

public sealed class InMemoryAdmissionState(TimeProvider timeProvider) : IAdmissionState
{
    private readonly object _sync = new();
    private readonly Dictionary<string, DropState> _drops = new(StringComparer.OrdinalIgnoreCase);

    public ValueTask<AdmissionDecision> EvaluateAsync(
        AdmissionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            var now = timeProvider.GetUtcNow();
            if (!_drops.TryGetValue(request.Drop, out var state))
            {
                state = new DropState();
                _drops.Add(request.Drop, state);
            }

            PruneExpired(state, now);

            if (state.Active.ContainsKey(request.SessionId))
            {
                state.Active[request.SessionId] = now + request.SessionTtl;
                return ValueTask.FromResult(AdmissionDecision.Admitted);
            }

            if (!state.Waiting.TryGetValue(request.SessionId, out var waiting))
            {
                if (state.Waiting.Count >= request.MaximumWaitingSessions)
                {
                    return ValueTask.FromResult(new AdmissionDecision(
                        AdmissionStatus.Full,
                        request.RetryAfter));
                }

                waiting = new WaitingEntry(++state.Sequence, now + request.WaitingTtl);
            }
            else
            {
                waiting = waiting with { ExpiresAt = now + request.WaitingTtl };
            }

            state.Waiting[request.SessionId] = waiting;

            var batchWindow = GetBatchWindow(now, request.RetryAfter);
            if (state.BatchWindow != batchWindow)
            {
                state.BatchWindow = batchWindow;
                state.BatchAdmissions = 0;
            }

            var availableCapacity = request.MaximumActiveSessions - state.Active.Count;
            var availableBatch = request.AdmissionBatchSize - state.BatchAdmissions;
            var eligibleCount = Math.Min(availableCapacity, availableBatch);
            var rank = state.Waiting.Values.Count(entry => entry.Sequence < waiting.Sequence);

            if (eligibleCount > 0 && rank < eligibleCount)
            {
                state.Waiting.Remove(request.SessionId);
                state.Active[request.SessionId] = now + request.SessionTtl;
                state.BatchAdmissions++;
                return ValueTask.FromResult(AdmissionDecision.Admitted);
            }

            return ValueTask.FromResult(new AdmissionDecision(
                AdmissionStatus.Waiting,
                request.RetryAfter));
        }
    }

    private static void PruneExpired(DropState state, DateTimeOffset now)
    {
        foreach (var sessionId in state.Active
                     .Where(entry => entry.Value <= now)
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            state.Active.Remove(sessionId);
        }

        foreach (var sessionId in state.Waiting
                     .Where(entry => entry.Value.ExpiresAt <= now)
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            state.Waiting.Remove(sessionId);
        }
    }

    private static long GetBatchWindow(DateTimeOffset now, TimeSpan window) =>
        now.ToUnixTimeMilliseconds() / Math.Max(1, (long)window.TotalMilliseconds);

    private sealed class DropState
    {
        public Dictionary<string, DateTimeOffset> Active { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, WaitingEntry> Waiting { get; } = new(StringComparer.Ordinal);

        public long Sequence { get; set; }

        public long BatchWindow { get; set; } = -1;

        public int BatchAdmissions { get; set; }
    }

    private sealed record WaitingEntry(long Sequence, DateTimeOffset ExpiresAt);
}
