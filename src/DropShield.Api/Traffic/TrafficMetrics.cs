using System.Collections.ObjectModel;

namespace DropShield.Api.Traffic;

public sealed class TrafficMetrics
{
    private const int RollingWindowSeconds = 10;

    private readonly TimeProvider _timeProvider;
    private readonly DateTimeOffset _startedAt;
    private readonly TrafficCounterSet _total = new();
    private readonly TrafficCounterSet _protectedStock = new();
    private readonly TrafficCounterSet[] _routes = CreateRouteCounters();
    private readonly StatusCodeCounterSet _totalStatusCodes = new();
    private readonly StatusCodeCounterSet[] _routeStatusCodes = CreateStatusCounters();
    private readonly LatencyHistogram _endToEndLatency = new();
    private readonly LatencyHistogram _dropShieldProcessingLatency = new();
    private readonly LatencyHistogram _originLatency = new();
    private readonly RollingTrafficWindow _rollingWindow;
    private long _collectionStartedAtUtcTicks;
    private long _perClientRejections;
    private long _aggregateRejections;
    private long _protectedStockChainedRejections;
    private long _unattributedRejections;

    public TrafficMetrics(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _startedAt = timeProvider.GetUtcNow();
        _collectionStartedAtUtcTicks = _startedAt.UtcTicks;
        _rollingWindow = new RollingTrafficWindow(timeProvider, RollingWindowSeconds);
    }

    public void RecordIncoming(TrafficRoute route, bool isProtectedStock)
    {
        _total.RecordIncoming();
        GetRouteCounters(route).RecordIncoming();
        if (isProtectedStock)
        {
            _protectedStock.RecordIncoming();
        }

        _rollingWindow.RecordIncoming();
    }

    public void RecordForwarded(TrafficRoute route, bool isProtectedStock)
    {
        _total.RecordForwarded();
        GetRouteCounters(route).RecordForwarded();
        if (isProtectedStock)
        {
            _protectedStock.RecordForwarded();
        }

        _rollingWindow.RecordForwarded();
    }

    public void RecordRateLimited(
        TrafficRoute route,
        bool isProtectedStock,
        RateLimitReason reason)
    {
        _total.RecordRateLimited();
        GetRouteCounters(route).RecordRateLimited();
        if (isProtectedStock)
        {
            _protectedStock.RecordRateLimited();
        }

        switch (reason)
        {
            case RateLimitReason.PerClient:
                Interlocked.Increment(ref _perClientRejections);
                break;
            case RateLimitReason.Aggregate:
                Interlocked.Increment(ref _aggregateRejections);
                break;
            case RateLimitReason.ProtectedStockChained:
                Interlocked.Increment(ref _protectedStockChainedRejections);
                break;
            default:
                Interlocked.Increment(ref _unattributedRejections);
                break;
        }

        _rollingWindow.RecordRateLimited();
    }

    public void RecordUpstreamFailure(TrafficRoute route, bool isProtectedStock)
    {
        _total.RecordUpstreamFailure();
        GetRouteCounters(route).RecordUpstreamFailure();
        if (isProtectedStock)
        {
            _protectedStock.RecordUpstreamFailure();
        }
    }

    public void RecordInternalFailure(TrafficRoute route, bool isProtectedStock)
    {
        _total.RecordInternalFailure();
        GetRouteCounters(route).RecordInternalFailure();
        if (isProtectedStock)
        {
            _protectedStock.RecordInternalFailure();
        }
    }

    public void RecordStateFailure(TrafficRoute route, bool isProtectedStock)
    {
        _total.RecordStateFailure();
        GetRouteCounters(route).RecordStateFailure();
        if (isProtectedStock)
        {
            _protectedStock.RecordStateFailure();
        }
    }

    public void RecordOriginLatency(TimeSpan duration) =>
        _originLatency.Record(duration);

    public void RecordCompleted(
        TrafficRoute route,
        int statusCode,
        TimeSpan endToEndDuration,
        TimeSpan dropShieldProcessingDuration)
    {
        _totalStatusCodes.Record(statusCode);
        GetRouteStatusCounters(route).Record(statusCode);
        _endToEndLatency.Record(endToEndDuration);
        _dropShieldProcessingLatency.Record(dropShieldProcessingDuration);
    }

    public TrafficMetricsSnapshot GetSnapshot()
    {
        var now = _timeProvider.GetUtcNow();
        var collectionStartedAt = new DateTimeOffset(
            Interlocked.Read(ref _collectionStartedAtUtcTicks),
            TimeSpan.Zero);
        var routes = Enum.GetValues<TrafficRoute>()
            .Where(route => route != TrafficRoute.Unknown)
            .ToDictionary(
                TrafficRouteClassifier.GetMetricName,
                route => new RouteMetricsSnapshot(
                    TrafficRouteClassifier.GetRouteTemplate(route),
                    GetRouteCounters(route).GetSnapshot(),
                    GetRouteStatusCounters(route).GetSnapshot()));

        return new TrafficMetricsSnapshot(
            _startedAt,
            collectionStartedAt,
            Math.Max(0, (long)(now - _startedAt).TotalSeconds),
            _total.GetSnapshot(),
            new RateLimitReasonSnapshot(
                Interlocked.Read(ref _perClientRejections),
                Interlocked.Read(ref _aggregateRejections),
                Interlocked.Read(ref _protectedStockChainedRejections),
                Interlocked.Read(ref _unattributedRejections)),
            _totalStatusCodes.GetSnapshot(),
            new LatencyMetricsSnapshot(
                _endToEndLatency.GetSnapshot(),
                _dropShieldProcessingLatency.GetSnapshot(),
                _originLatency.GetSnapshot()),
            _rollingWindow.GetSnapshot(collectionStartedAt),
            _protectedStock.GetSnapshot(),
            new ReadOnlyDictionary<string, RouteMetricsSnapshot>(routes));
    }

    public void Reset()
    {
        _total.Reset();
        _protectedStock.Reset();
        _totalStatusCodes.Reset();

        foreach (var route in Enum.GetValues<TrafficRoute>())
        {
            GetRouteCounters(route).Reset();
            GetRouteStatusCounters(route).Reset();
        }

        _endToEndLatency.Reset();
        _dropShieldProcessingLatency.Reset();
        _originLatency.Reset();
        _rollingWindow.Reset();
        Interlocked.Exchange(ref _perClientRejections, 0);
        Interlocked.Exchange(ref _aggregateRejections, 0);
        Interlocked.Exchange(ref _protectedStockChainedRejections, 0);
        Interlocked.Exchange(ref _unattributedRejections, 0);
        Interlocked.Exchange(
            ref _collectionStartedAtUtcTicks,
            _timeProvider.GetUtcNow().UtcTicks);
    }

    private TrafficCounterSet GetRouteCounters(TrafficRoute route) =>
        _routes[(int)route];

    private StatusCodeCounterSet GetRouteStatusCounters(TrafficRoute route) =>
        _routeStatusCodes[(int)route];

    private static TrafficCounterSet[] CreateRouteCounters() =>
        Enumerable.Range(0, Enum.GetValues<TrafficRoute>().Length)
            .Select(_ => new TrafficCounterSet())
            .ToArray();

    private static StatusCodeCounterSet[] CreateStatusCounters() =>
        Enumerable.Range(0, Enum.GetValues<TrafficRoute>().Length)
            .Select(_ => new StatusCodeCounterSet())
            .ToArray();

    private sealed class TrafficCounterSet
    {
        private long _incoming;
        private long _forwarded;
        private long _rateLimited;
        private long _upstreamFailures;
        private long _internalFailures;
        private long _stateFailures;

        public void RecordIncoming() => Interlocked.Increment(ref _incoming);

        public void RecordForwarded() => Interlocked.Increment(ref _forwarded);

        public void RecordRateLimited() => Interlocked.Increment(ref _rateLimited);

        public void RecordUpstreamFailure() => Interlocked.Increment(ref _upstreamFailures);

        public void RecordInternalFailure() => Interlocked.Increment(ref _internalFailures);

        public void RecordStateFailure() => Interlocked.Increment(ref _stateFailures);

        public TrafficCountersSnapshot GetSnapshot()
        {
            var incoming = Interlocked.Read(ref _incoming);
            var forwarded = Interlocked.Read(ref _forwarded);
            var rateLimited = Interlocked.Read(ref _rateLimited);

            return new TrafficCountersSnapshot(
                incoming,
                forwarded,
                rateLimited,
                Interlocked.Read(ref _upstreamFailures),
                Interlocked.Read(ref _internalFailures),
                Interlocked.Read(ref _stateFailures),
                Percentage(forwarded, incoming),
                Percentage(rateLimited, incoming),
                Percentage(rateLimited, incoming));
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _incoming, 0);
            Interlocked.Exchange(ref _forwarded, 0);
            Interlocked.Exchange(ref _rateLimited, 0);
            Interlocked.Exchange(ref _upstreamFailures, 0);
            Interlocked.Exchange(ref _internalFailures, 0);
            Interlocked.Exchange(ref _stateFailures, 0);
        }

        private static double Percentage(long numerator, long denominator) =>
            denominator == 0
                ? 0
                : Math.Round(numerator * 100d / denominator, 3);
    }

    private sealed class StatusCodeCounterSet
    {
        private long _success2xx;
        private long _clientError4xx;
        private long _rateLimited429;
        private long _serverError5xx;
        private long _badGateway502;
        private long _other;

        public void Record(int statusCode)
        {
            switch (statusCode)
            {
                case >= 200 and < 300:
                    Interlocked.Increment(ref _success2xx);
                    break;
                case >= 400 and < 500:
                    Interlocked.Increment(ref _clientError4xx);
                    if (statusCode == StatusCodes.Status429TooManyRequests)
                    {
                        Interlocked.Increment(ref _rateLimited429);
                    }

                    break;
                case >= 500 and < 600:
                    Interlocked.Increment(ref _serverError5xx);
                    if (statusCode == StatusCodes.Status502BadGateway)
                    {
                        Interlocked.Increment(ref _badGateway502);
                    }

                    break;
                default:
                    Interlocked.Increment(ref _other);
                    break;
            }
        }

        public StatusCodeSnapshot GetSnapshot() => new(
            Interlocked.Read(ref _success2xx),
            Interlocked.Read(ref _clientError4xx),
            Interlocked.Read(ref _rateLimited429),
            Interlocked.Read(ref _serverError5xx),
            Interlocked.Read(ref _badGateway502),
            Interlocked.Read(ref _other));

        public void Reset()
        {
            Interlocked.Exchange(ref _success2xx, 0);
            Interlocked.Exchange(ref _clientError4xx, 0);
            Interlocked.Exchange(ref _rateLimited429, 0);
            Interlocked.Exchange(ref _serverError5xx, 0);
            Interlocked.Exchange(ref _badGateway502, 0);
            Interlocked.Exchange(ref _other, 0);
        }
    }
}

public enum RateLimitReason
{
    Unattributed,
    PerClient,
    Aggregate,
    ProtectedStockChained,
}

public sealed record TrafficMetricsSnapshot(
    DateTimeOffset StartedAt,
    DateTimeOffset CollectionStartedAt,
    long UptimeSeconds,
    TrafficCountersSnapshot Traffic,
    RateLimitReasonSnapshot RateLimitReasons,
    StatusCodeSnapshot StatusCodes,
    LatencyMetricsSnapshot LatencyMilliseconds,
    RollingRateSnapshot RecentRates,
    TrafficCountersSnapshot ProtectedStock,
    IReadOnlyDictionary<string, RouteMetricsSnapshot> Routes);

public sealed record TrafficCountersSnapshot(
    long Incoming,
    long Forwarded,
    long RateLimited,
    long UpstreamFailures,
    long InternalFailures,
    long StateFailures,
    double ForwardingPercentage,
    double RejectionPercentage,
    double OriginTrafficReductionPercentage);

public sealed record RateLimitReasonSnapshot(
    long PerClient,
    long Aggregate,
    long ProtectedStockChained,
    long Unattributed);

public sealed record StatusCodeSnapshot(
    long Success2xx,
    long ClientError4xx,
    long RateLimited429,
    long ServerError5xx,
    long BadGateway502,
    long Other);

public sealed record LatencyMetricsSnapshot(
    LatencySummary EndToEnd,
    LatencySummary DropShieldProcessing,
    LatencySummary Origin);

public sealed record LatencySummary(
    long Count,
    double? Average,
    double? P50,
    double? P95,
    double? P99);

public sealed record RollingRateSnapshot(
    int WindowSeconds,
    int SampleSeconds,
    double IncomingPerSecond,
    double ForwardedPerSecond,
    double RateLimitedPerSecond);

public sealed record RouteMetricsSnapshot(
    string RouteTemplate,
    TrafficCountersSnapshot Traffic,
    StatusCodeSnapshot StatusCodes);
