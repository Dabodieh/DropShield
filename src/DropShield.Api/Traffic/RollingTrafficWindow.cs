namespace DropShield.Api.Traffic;

internal sealed class RollingTrafficWindow(TimeProvider timeProvider, int windowSeconds)
{
    private readonly object _sync = new();
    private readonly Bucket[] _buckets = Enumerable.Range(0, windowSeconds)
        .Select(_ => new Bucket())
        .ToArray();

    public void RecordIncoming() => Record(RollingEvent.Incoming);

    public void RecordForwarded() => Record(RollingEvent.Forwarded);

    public void RecordRateLimited() => Record(RollingEvent.RateLimited);

    public RollingRateSnapshot GetSnapshot(DateTimeOffset collectionStartedAt)
    {
        var now = timeProvider.GetUtcNow();
        var currentSecond = now.ToUnixTimeSeconds();
        var oldestSecond = currentSecond - windowSeconds + 1;
        long incoming = 0;
        long forwarded = 0;
        long rateLimited = 0;

        lock (_sync)
        {
            foreach (var bucket in _buckets)
            {
                if (bucket.Second < oldestSecond || bucket.Second > currentSecond)
                {
                    continue;
                }

                incoming += bucket.Incoming;
                forwarded += bucket.Forwarded;
                rateLimited += bucket.RateLimited;
            }
        }

        var elapsedSeconds = Math.Max(
            1,
            currentSecond - collectionStartedAt.ToUnixTimeSeconds() + 1);
        var sampleSeconds = (int)Math.Min(windowSeconds, elapsedSeconds);

        return new RollingRateSnapshot(
            windowSeconds,
            sampleSeconds,
            Round(incoming / (double)sampleSeconds),
            Round(forwarded / (double)sampleSeconds),
            Round(rateLimited / (double)sampleSeconds));
    }

    public void Reset()
    {
        lock (_sync)
        {
            foreach (var bucket in _buckets)
            {
                bucket.Second = long.MinValue;
                bucket.Incoming = 0;
                bucket.Forwarded = 0;
                bucket.RateLimited = 0;
            }
        }
    }

    private void Record(RollingEvent rollingEvent)
    {
        var second = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var index = (int)(Math.Abs(second % windowSeconds));

        lock (_sync)
        {
            var bucket = _buckets[index];
            if (bucket.Second != second)
            {
                bucket.Second = second;
                bucket.Incoming = 0;
                bucket.Forwarded = 0;
                bucket.RateLimited = 0;
            }

            switch (rollingEvent)
            {
                case RollingEvent.Incoming:
                    bucket.Incoming++;
                    break;
                case RollingEvent.Forwarded:
                    bucket.Forwarded++;
                    break;
                case RollingEvent.RateLimited:
                    bucket.RateLimited++;
                    break;
            }
        }
    }

    private static double Round(double value) => Math.Round(value, 3);

    private sealed class Bucket
    {
        public long Second { get; set; } = long.MinValue;

        public long Incoming { get; set; }

        public long Forwarded { get; set; }

        public long RateLimited { get; set; }
    }

    private enum RollingEvent
    {
        Incoming,
        Forwarded,
        RateLimited,
    }
}
