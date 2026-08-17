namespace DropShield.Api.Behaviour;

public interface IBehaviourState
{
    ValueTask<BehaviourEvidence> RecordAsync(
        string actor,
        BehaviourEventType eventType,
        CancellationToken cancellationToken);

    ValueTask<BehaviourEvidence> GetAsync(string actor, CancellationToken cancellationToken);
}

public enum BehaviourEventType
{
    Request,
    StockRequest,
    RateLimited,
    ReplayRejected,
    InvalidProof,
    Transaction,
}

public sealed record BehaviourEvidence(
    int Requests,
    int StockRequests,
    int RateLimited,
    int ReplayRejected,
    int InvalidProof,
    int Transactions)
{
    public static BehaviourEvidence Empty { get; } = new(0, 0, 0, 0, 0, 0);
}

public sealed class BehaviourStateUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);
