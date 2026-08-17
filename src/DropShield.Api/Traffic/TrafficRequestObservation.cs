namespace DropShield.Api.Traffic;

public sealed class TrafficRequestObservation(bool isProtectedStock)
{
    private long _originDurationTicks;

    public bool IsProtectedStock { get; } = isProtectedStock;

    public TimeSpan OriginDuration =>
        TimeSpan.FromTicks(Interlocked.Read(ref _originDurationTicks));

    public void SetOriginDuration(TimeSpan duration) =>
        Interlocked.Exchange(ref _originDurationTicks, Math.Max(0, duration.Ticks));
}
