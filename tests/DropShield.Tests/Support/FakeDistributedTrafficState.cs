using System.Collections.Concurrent;
using DropShield.Api.State;

namespace DropShield.Tests.Support;

internal sealed class FakeDistributedTrafficState : IDistributedTrafficState
{
    private readonly ConcurrentDictionary<string, Counter> _counters = new();

    public bool IsAvailable { get; set; } = true;

    public ValueTask<DistributedTrafficLease> TryAcquireAsync(
        DistributedTrafficRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsAvailable)
        {
            throw new DistributedTrafficStateUnavailableException(
                "Synthetic shared state is unavailable.",
                new TimeoutException());
        }

        var key = $"{request.Policy}:{request.Scope}:{request.ClientPartition}";
        var counter = _counters.GetOrAdd(key, _ => new Counter());
        lock (counter)
        {
            var now = DateTimeOffset.UtcNow;
            if (now >= counter.WindowEndsAt)
            {
                counter.Count = 0;
                counter.WindowEndsAt = now.Add(request.Window);
            }

            counter.Count++;
            return ValueTask.FromResult(new DistributedTrafficLease(
                counter.Count <= request.PermitLimit,
                counter.WindowEndsAt - now));
        }
    }

    public ValueTask<DistributedStateHealth> GetHealthAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new DistributedStateHealth(
            IsAvailable,
            IsAvailable ? "available" : "unavailable"));
    }

    private sealed class Counter
    {
        public int Count { get; set; }

        public DateTimeOffset WindowEndsAt { get; set; } = DateTimeOffset.MinValue;
    }
}
