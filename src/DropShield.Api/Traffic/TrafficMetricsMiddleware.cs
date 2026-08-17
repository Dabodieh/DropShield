using System.Diagnostics;
using DropShield.Api.Actions;
using DropShield.Api.Behaviour;
using DropShield.Api.Options;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Traffic;

public sealed class TrafficMetricsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        TrafficMetrics metrics,
        BehaviourActivityRecorder behaviourRecorder,
        IOptions<DropShieldOptions> options,
        ILogger<TrafficMetricsMiddleware> logger)
    {
        context.Request.Headers.Remove(options.Value.OriginAssertions.HeaderName);

        var route = TrafficRouteClassifier.Classify(context.Request);
        if (route == TrafficRoute.Unknown)
        {
            await next(context);
            return;
        }

        var isProtectedStock = TrafficRouteClassifier.IsProtectedStockRequest(
            context.Request,
            options.Value.ProtectedProducts);
        var observation = new TrafficRequestObservation(isProtectedStock);
        context.Features.Set(observation);
        metrics.RecordIncoming(route, isProtectedStock);
        var startedTimestamp = Stopwatch.GetTimestamp();
        var internalFailure = false;

        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            internalFailure = true;
            metrics.RecordInternalFailure(route, isProtectedStock);
            logger.LogError(
                exception,
                "Unexpected DropShield failure for {Method} {TrafficRoute}",
                context.Request.Method,
                route);
            throw;
        }
        finally
        {
            var endToEnd = Stopwatch.GetElapsedTime(startedTimestamp);
            var processing = endToEnd - observation.OriginDuration;
            if (processing < TimeSpan.Zero)
            {
                processing = TimeSpan.Zero;
            }

            metrics.RecordCompleted(
                route,
                internalFailure
                    ? StatusCodes.Status500InternalServerError
                    : context.Response.StatusCode,
                endToEnd,
                processing);
            await behaviourRecorder.RecordAsync(
                context,
                BehaviourEventType.Request,
                CancellationToken.None);
            if (isProtectedStock)
            {
                await behaviourRecorder.RecordAsync(
                    context,
                    BehaviourEventType.StockRequest,
                    CancellationToken.None);
            }

            var isTransaction = route is TrafficRoute.Cart or TrafficRoute.Checkout ||
                                (route == TrafficRoute.ActionProof &&
                                 ActionProofPolicy.TryGetAction(context.Request, out _));
            if (isTransaction)
            {
                await behaviourRecorder.RecordAsync(
                    context,
                    BehaviourEventType.Transaction,
                    CancellationToken.None);
            }
        }
    }
}
