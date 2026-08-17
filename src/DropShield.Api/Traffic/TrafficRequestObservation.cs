namespace DropShield.Api.Traffic;

public sealed class TrafficRequestObservation(bool isProtectedStock)
{
    private long _originDurationTicks;

    public bool IsProtectedStock { get; } = isProtectedStock;

    /// <summary>
    /// Set once, early in the pipeline, for GraphQL requests only: whether the request body
    /// contains a protected-drop addProductsToCart mutation. Cached here so the body is
    /// inspected exactly once per request and every downstream policy check (action proof,
    /// admission, forwarding) reads the same decision instead of re-parsing.
    /// </summary>
    public bool IsProtectedGraphQlCartMutation { get; set; }

    public TimeSpan OriginDuration =>
        TimeSpan.FromTicks(Interlocked.Read(ref _originDurationTicks));

    public void SetOriginDuration(TimeSpan duration) =>
        Interlocked.Exchange(ref _originDurationTicks, Math.Max(0, duration.Ticks));
}
