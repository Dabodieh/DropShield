using DropShield.Api.Traffic;
using Microsoft.AspNetCore.Http;

namespace DropShield.Tests;

public sealed class TrafficMetricsTests
{
    [Fact]
    public void ConcurrentUpdates_AreCountedWithoutUnboundedLabels()
    {
        const int updateCount = 10_000;
        var metrics = new TrafficMetrics(TimeProvider.System);

        Parallel.For(0, updateCount, index =>
        {
            metrics.RecordIncoming(TrafficRoute.Stock, isProtectedStock: true);
            if (index % 2 == 0)
            {
                metrics.RecordForwarded(TrafficRoute.Stock, isProtectedStock: true);
                metrics.RecordOriginLatency(TimeSpan.FromMilliseconds(50));
                metrics.RecordCompleted(
                    TrafficRoute.Stock,
                    StatusCodes.Status200OK,
                    TimeSpan.FromMilliseconds(51),
                    TimeSpan.FromMilliseconds(1));
            }
            else
            {
                metrics.RecordRateLimited(
                    TrafficRoute.Stock,
                    isProtectedStock: true,
                    RateLimitReason.ProtectedStockChained);
                metrics.RecordCompleted(
                    TrafficRoute.Stock,
                    StatusCodes.Status429TooManyRequests,
                    TimeSpan.FromMilliseconds(1),
                    TimeSpan.FromMilliseconds(1));
            }
        });

        var snapshot = metrics.GetSnapshot();

        Assert.Equal(updateCount, snapshot.Traffic.Incoming);
        Assert.Equal(updateCount / 2, snapshot.Traffic.Forwarded);
        Assert.Equal(updateCount / 2, snapshot.Traffic.RateLimited);
        Assert.Equal(updateCount, snapshot.ProtectedStock.Incoming);
        Assert.Equal(updateCount, snapshot.Routes["stock"].Traffic.Incoming);
        Assert.Equal(updateCount / 2, snapshot.StatusCodes.Success2xx);
        Assert.Equal(updateCount / 2, snapshot.StatusCodes.RateLimited429);
        Assert.Equal(updateCount, snapshot.LatencyMilliseconds.EndToEnd.Count);
        Assert.Equal(updateCount / 2, snapshot.LatencyMilliseconds.Origin.Count);
        Assert.Equal(updateCount / 2, snapshot.RateLimitReasons.ProtectedStockChained);
        Assert.Equal(5, snapshot.Routes.Count);
    }

    [Fact]
    public void InternalFailures_AreCountedGloballyAndByRoute()
    {
        var metrics = new TrafficMetrics(TimeProvider.System);

        metrics.RecordIncoming(TrafficRoute.Cart, isProtectedStock: false);
        metrics.RecordInternalFailure(TrafficRoute.Cart, isProtectedStock: false);
        metrics.RecordCompleted(
            TrafficRoute.Cart,
            StatusCodes.Status500InternalServerError,
            TimeSpan.FromMilliseconds(2),
            TimeSpan.FromMilliseconds(2));

        var snapshot = metrics.GetSnapshot();

        Assert.Equal(1, snapshot.Traffic.InternalFailures);
        Assert.Equal(1, snapshot.Routes["cart"].Traffic.InternalFailures);
        Assert.Equal(1, snapshot.StatusCodes.ServerError5xx);
    }
}
