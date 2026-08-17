using DropShield.Api.Options;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Behaviour;

public sealed class InMemoryBehaviourState(
    TimeProvider timeProvider,
    IOptions<DropShieldOptions> options) : IBehaviourState
{
    private readonly object _sync = new();
    private readonly Dictionary<string, List<BehaviourEvent>> _actors = new(StringComparer.Ordinal);
    private readonly BehaviourScoringOptions _options = options.Value.BehaviourScoring;

    public ValueTask<BehaviourEvidence> RecordAsync(
        string actor,
        BehaviourEventType eventType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            PruneExpiredActors();
            if (!_actors.TryGetValue(actor, out var events))
            {
                if (_actors.Count >= _options.MaximumInMemoryActors)
                {
                    throw Unavailable();
                }

                events = [];
                _actors.Add(actor, events);
            }

            Prune(events);
            if (events.Count >= _options.MaximumEventsPerActor)
            {
                events.RemoveAt(0);
            }

            events.Add(new BehaviourEvent(eventType, timeProvider.GetUtcNow()));
            return ValueTask.FromResult(ToEvidence(events));
        }
    }

    public ValueTask<BehaviourEvidence> GetAsync(string actor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            PruneExpiredActors();
            if (!_actors.TryGetValue(actor, out var events))
            {
                return ValueTask.FromResult(BehaviourEvidence.Empty);
            }

            Prune(events);
            return ValueTask.FromResult(ToEvidence(events));
        }
    }

    private void PruneExpiredActors()
    {
        var cutoff = timeProvider.GetUtcNow().AddSeconds(-_options.StateTtlSeconds);
        var expiredActors = _actors
            .Where(pair => pair.Value.Count == 0 || pair.Value[^1].At < cutoff)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var actor in expiredActors)
        {
            _actors.Remove(actor);
        }
    }

    private void Prune(List<BehaviourEvent> events)
    {
        var cutoff = timeProvider.GetUtcNow().AddSeconds(-_options.ObservationWindowSeconds);
        events.RemoveAll(@event => @event.At <= cutoff);
    }

    private static BehaviourEvidence ToEvidence(IEnumerable<BehaviourEvent> events) => new(
        events.Count(@event => @event.Type == BehaviourEventType.Request),
        events.Count(@event => @event.Type == BehaviourEventType.StockRequest),
        events.Count(@event => @event.Type == BehaviourEventType.RateLimited),
        events.Count(@event => @event.Type == BehaviourEventType.ReplayRejected),
        events.Count(@event => @event.Type == BehaviourEventType.InvalidProof),
        events.Count(@event => @event.Type == BehaviourEventType.Transaction));

    private static BehaviourStateUnavailableException Unavailable() => new(
        "In-memory behavioural state capacity is exhausted.",
        new InvalidOperationException("Behaviour actor capacity reached."));

    private sealed record BehaviourEvent(BehaviourEventType Type, DateTimeOffset At);
}
