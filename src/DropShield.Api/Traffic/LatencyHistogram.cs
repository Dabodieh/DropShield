namespace DropShield.Api.Traffic;

internal sealed class LatencyHistogram
{
    private static readonly double[] BucketUpperBoundsMilliseconds =
    [
        0.1, 0.25, 0.5, 0.75, 1, 1.5, 2, 3, 5, 7.5, 10, 15,
        25, 50, 75, 100, 150, 250, 500, 1_000, 2_500, 5_000,
        10_000, double.PositiveInfinity,
    ];

    private readonly long[] _buckets = new long[BucketUpperBoundsMilliseconds.Length];
    private long _count;
    private long _totalMicroseconds;
    private long _maximumMicroseconds;

    public void Record(TimeSpan duration)
    {
        var milliseconds = Math.Max(0, duration.TotalMilliseconds);
        var microseconds = (long)Math.Round(milliseconds * 1_000);
        var bucket = Array.FindIndex(
            BucketUpperBoundsMilliseconds,
            upperBound => milliseconds <= upperBound);

        Interlocked.Increment(ref _buckets[bucket]);
        Interlocked.Add(ref _totalMicroseconds, microseconds);
        SetMaximum(microseconds);
        Interlocked.Increment(ref _count);
    }

    public LatencySummary GetSnapshot()
    {
        var count = Interlocked.Read(ref _count);
        if (count == 0)
        {
            return new LatencySummary(0, null, null, null, null);
        }

        var average = Interlocked.Read(ref _totalMicroseconds) / 1_000d / count;
        return new LatencySummary(
            count,
            Round(average),
            GetPercentile(count, 0.50),
            GetPercentile(count, 0.95),
            GetPercentile(count, 0.99));
    }

    public void Reset()
    {
        foreach (ref var bucket in _buckets.AsSpan())
        {
            Interlocked.Exchange(ref bucket, 0);
        }

        Interlocked.Exchange(ref _count, 0);
        Interlocked.Exchange(ref _totalMicroseconds, 0);
        Interlocked.Exchange(ref _maximumMicroseconds, 0);
    }

    private double GetPercentile(long count, double percentile)
    {
        var target = (long)Math.Ceiling(count * percentile);
        long cumulative = 0;

        for (var index = 0; index < _buckets.Length; index++)
        {
            cumulative += Interlocked.Read(ref _buckets[index]);
            if (cumulative < target)
            {
                continue;
            }

            var upperBound = BucketUpperBoundsMilliseconds[index];
            var maximum = Interlocked.Read(ref _maximumMicroseconds) / 1_000d;
            return Round(double.IsPositiveInfinity(upperBound)
                ? maximum
                : Math.Min(upperBound, maximum));
        }

        return Round(Interlocked.Read(ref _maximumMicroseconds) / 1_000d);
    }

    private void SetMaximum(long candidate)
    {
        var current = Interlocked.Read(ref _maximumMicroseconds);
        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(
                ref _maximumMicroseconds,
                candidate,
                current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }

    private static double Round(double value) => Math.Round(value, 3);
}
