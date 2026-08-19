namespace DropShield.Api.Traffic;

using DropShield.Api.Actions;

public sealed class TrafficRequestObservation(bool isProtectedStock)
{
    private long _originDurationTicks;

    public bool IsProtectedStock { get; } = isProtectedStock;

    /// <summary>
    /// Set once, early in the pipeline, for GraphQL requests only: whether the request body
    /// contains a protected-drop mutation from the narrow supported GraphQL cart-add set.
    /// Cached here so the body is inspected exactly once per request and every downstream
    /// policy check (action proof, admission, forwarding) reads the same decision instead of
    /// re-parsing.
    /// </summary>
    public bool IsProtectedGraphQlCartMutation { get; set; }

    public bool IsProtectedCommerceCartMutation { get; set; }

    public bool IsCommerceCheckoutMutation { get; set; }

    public byte[]? BufferedBody { get; set; }

    /// <summary>The manifest-resolved drop, never a shopper supplied identifier.</summary>
    public string? ProtectedDropId { get; set; }

    public ActionKind? ProtectedAction => IsCommerceCheckoutMutation
        ? ActionKind.Checkout
        : IsProtectedCommerceCartMutation || IsProtectedGraphQlCartMutation
            ? ActionKind.Cart
            : null;

    public TimeSpan OriginDuration =>
        TimeSpan.FromTicks(Interlocked.Read(ref _originDurationTicks));

    public void SetOriginDuration(TimeSpan duration) =>
        Interlocked.Exchange(ref _originDurationTicks, Math.Max(0, duration.Ticks));
}
