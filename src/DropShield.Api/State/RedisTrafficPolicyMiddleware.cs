using DropShield.Api.Models;
using DropShield.Api.Traffic;

namespace DropShield.Api.State;

public sealed class RedisTrafficPolicyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        RedisTrafficPolicyEvaluator evaluator,
        TrafficMetrics metrics,
        ILogger<RedisTrafficPolicyMiddleware> logger)
    {
        var route = TrafficRouteClassifier.Classify(context.Request);
        if (route == TrafficRoute.Unknown)
        {
            await next(context);
            return;
        }

        var observation = context.Features.Get<TrafficRequestObservation>();
        var isProtectedStock = observation?.IsProtectedStock ?? false;

        RedisTrafficPolicyDecision decision;
        try
        {
            decision = await evaluator.EvaluateAsync(
                context,
                context.RequestAborted);
        }
        catch (DistributedTrafficStateUnavailableException exception)
        {
            metrics.RecordStateFailure(route, isProtectedStock);
            logger.LogWarning(
                exception,
                "Shared traffic state unavailable for {Method} {TrafficRoute}; request denied",
                context.Request.Method,
                route);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(
                new GatewayErrorResponse(
                    "state_unavailable",
                    "Traffic policy state is temporarily unavailable."),
                context.RequestAborted);
            return;
        }

        if (decision.IsAllowed)
        {
            await next(context);
            return;
        }

        metrics.RecordRateLimited(route, isProtectedStock, decision.Reason);
        logger.LogDebug(
            "Rate limited {Method} {TrafficRoute} with {RateLimitReason}",
            context.Request.Method,
            route,
            decision.Reason);
        await RateLimitResponseWriter.WriteAsync(
            context,
            decision.RetryAfter,
            context.RequestAborted);
    }
}
