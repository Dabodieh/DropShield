using System.Collections.ObjectModel;

namespace DropShield.Api.Traffic;

public sealed class TrafficMetrics
{
    private readonly long[] _incoming = new long[Enum.GetValues<TrafficRoute>().Length];
    private readonly long[] _forwarded = new long[Enum.GetValues<TrafficRoute>().Length];
    private readonly long[] _rateLimited = new long[Enum.GetValues<TrafficRoute>().Length];

    public void RecordIncoming(TrafficRoute route) =>
        Interlocked.Increment(ref _incoming[(int)route]);

    public void RecordForwarded(TrafficRoute route) =>
        Interlocked.Increment(ref _forwarded[(int)route]);

    public void RecordRateLimited(TrafficRoute route) =>
        Interlocked.Increment(ref _rateLimited[(int)route]);

    public TrafficMetricsSnapshot GetSnapshot()
    {
        var routes = Enum.GetValues<TrafficRoute>()
            .Where(route => route != TrafficRoute.Unknown)
            .ToDictionary(
                TrafficRouteClassifier.GetMetricName,
                route => GetCounts(route));

        var total = new TrafficCounts(
            routes.Values.Sum(counts => counts.Incoming),
            routes.Values.Sum(counts => counts.Forwarded),
            routes.Values.Sum(counts => counts.RateLimited));

        return new TrafficMetricsSnapshot(
            total,
            new ReadOnlyDictionary<string, TrafficCounts>(routes));
    }

    public void Reset()
    {
        foreach (var route in Enum.GetValues<TrafficRoute>())
        {
            Interlocked.Exchange(ref _incoming[(int)route], 0);
            Interlocked.Exchange(ref _forwarded[(int)route], 0);
            Interlocked.Exchange(ref _rateLimited[(int)route], 0);
        }
    }

    private TrafficCounts GetCounts(TrafficRoute route) => new(
        Interlocked.Read(ref _incoming[(int)route]),
        Interlocked.Read(ref _forwarded[(int)route]),
        Interlocked.Read(ref _rateLimited[(int)route]));
}

public sealed record TrafficMetricsSnapshot(
    TrafficCounts Total,
    IReadOnlyDictionary<string, TrafficCounts> Routes);

public sealed record TrafficCounts(long Incoming, long Forwarded, long RateLimited);
