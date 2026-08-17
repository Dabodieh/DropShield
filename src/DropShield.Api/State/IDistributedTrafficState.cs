namespace DropShield.Api.State;

public interface IDistributedTrafficState
{
    ValueTask<DistributedTrafficLease> TryAcquireAsync(
        DistributedTrafficRequest request,
        CancellationToken cancellationToken);

    ValueTask<DistributedStateHealth> GetHealthAsync(CancellationToken cancellationToken);
}

public sealed record DistributedTrafficRequest(
    TrafficPolicyKind Policy,
    TrafficLimitScope Scope,
    string? ClientPartition,
    int PermitLimit,
    TimeSpan Window);

public sealed record DistributedTrafficLease(
    bool IsAcquired,
    TimeSpan RetryAfter);

public sealed record DistributedStateHealth(bool IsAvailable, string Status);

public enum TrafficPolicyKind
{
    Stock,
    Cart,
    Checkout,
}

public enum TrafficLimitScope
{
    PerClient,
    Aggregate,
}

public sealed class DistributedTrafficStateUnavailableException(
    string message,
    Exception innerException)
    : Exception(message, innerException);
