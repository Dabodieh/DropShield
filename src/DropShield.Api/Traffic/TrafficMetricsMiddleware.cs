namespace DropShield.Api.Traffic;

public sealed class TrafficMetricsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TrafficMetrics metrics)
    {
        var route = TrafficRouteClassifier.Classify(context.Request);
        if (route != TrafficRoute.Unknown)
        {
            metrics.RecordIncoming(route);
        }

        await next(context);
    }
}
