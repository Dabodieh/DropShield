using System.Diagnostics;
using DropShield.Api.Models;
using DropShield.Api.Traffic;

namespace DropShield.Api.Origin;

public sealed class DemoStoreForwarder(
    IDemoStoreClient client,
    TrafficMetrics metrics,
    ILogger<DemoStoreForwarder> logger)
{
    public async Task ForwardAsync(
        HttpContext context,
        TrafficRoute route,
        CancellationToken cancellationToken)
    {
        var observation = context.Features.Get<TrafficRequestObservation>();
        var isProtectedStock = observation?.IsProtectedStock ?? false;
        metrics.RecordForwarded(route, isProtectedStock);
        var originStartedTimestamp = Stopwatch.GetTimestamp();
        var originDurationRecorded = false;

        void RecordOriginDuration()
        {
            if (originDurationRecorded)
            {
                return;
            }

            var originDuration = Stopwatch.GetElapsedTime(originStartedTimestamp);
            metrics.RecordOriginLatency(originDuration);
            observation?.SetOriginDuration(originDuration);
            originDurationRecorded = true;
        }

        try
        {
            using var originResponse = await client.SendAsync(
                new HttpMethod(context.Request.Method),
                context.Request.Path,
                context.Request,
                cancellationToken);

            context.Response.StatusCode = (int)originResponse.StatusCode;
            if (originResponse.Content.Headers.ContentType is not null)
            {
                context.Response.ContentType = originResponse.Content.Headers.ContentType.ToString();
            }

            await originResponse.Content.CopyToAsync(context.Response.Body, cancellationToken);
            RecordOriginDuration();

            if ((int)originResponse.StatusCode >= StatusCodes.Status500InternalServerError)
            {
                metrics.RecordUpstreamFailure(route, isProtectedStock);
            }

            logger.LogDebug(
                "Forwarded {Method} {TrafficRoute} with origin status {StatusCode}",
                context.Request.Method,
                route,
                (int)originResponse.StatusCode);
        }
        catch (HttpRequestException exception)
        {
            RecordOriginDuration();
            metrics.RecordUpstreamFailure(route, isProtectedStock);
            logger.LogWarning(
                exception,
                "DemoStore origin request failed for {Method} {TrafficRoute}",
                context.Request.Method,
                route);
            await WriteBadGatewayAsync(context, cancellationToken);
        }
        catch (OperationCanceledException exception)
            when (!context.RequestAborted.IsCancellationRequested)
        {
            RecordOriginDuration();
            metrics.RecordUpstreamFailure(route, isProtectedStock);
            logger.LogWarning(
                exception,
                "DemoStore origin request timed out for {Method} {TrafficRoute}",
                context.Request.Method,
                route);
            await WriteBadGatewayAsync(context, cancellationToken);
        }
        finally
        {
            RecordOriginDuration();
        }
    }

    private static async Task WriteBadGatewayAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status502BadGateway;
        await context.Response.WriteAsJsonAsync(
            new GatewayErrorResponse(
                "upstream_unavailable",
                "The ecommerce origin is temporarily unavailable."),
            cancellationToken);
    }
}
