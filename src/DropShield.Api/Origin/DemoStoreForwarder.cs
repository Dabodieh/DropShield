using System.Diagnostics;
using System.Security.Cryptography;
using DropShield.Api.Actions;
using DropShield.Api.Models;
using DropShield.Api.Options;
using DropShield.Api.Traffic;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Origin;

public sealed class DemoStoreForwarder(
    IDemoStoreClient client,
    IOriginAssertionService assertionService,
    IOptions<DropShieldOptions> options,
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

        var configured = options.Value;
        var isProtectedGraphQlMutation = route == TrafficRoute.GraphQlCartAdd &&
            (observation?.IsProtectedGraphQlCartMutation ?? false);
        (string HeaderName, string Value)? assertionHeader = null;
        if (configured.OriginAssertions.Enabled &&
            (route is TrafficRoute.Cart or TrafficRoute.Checkout or TrafficRoute.StorefrontCartAdd ||
             isProtectedGraphQlMutation))
        {
            metrics.RecordCommerceProtectedRequest();
            string assertion;
            try
            {
                assertion = await IssueAssertionAsync(context, route, configured, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                metrics.RecordOriginAssertionFailure();
                logger.LogWarning(
                    exception,
                    "Origin assertion issuance failed for {Method} {TrafficRoute}; forwarding blocked",
                    context.Request.Method,
                    route);
                await WriteAssertionUnavailableAsync(context, cancellationToken);
                return;
            }

            metrics.RecordOriginAssertionIssued();
            assertionHeader = (configured.OriginAssertions.HeaderName, assertion);
        }

        try
        {
            using var originResponse = await client.SendAsync(
                new HttpMethod(context.Request.Method),
                GetRelativeTarget(context.Request),
                context.Request,
                cancellationToken,
                assertionHeader);

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

    private static string GetRelativeTarget(HttpRequest request) =>
        request.PathBase.Add(request.Path).Add(request.QueryString) ?? "/";

    private async Task<string> IssueAssertionAsync(
        HttpContext context,
        TrafficRoute route,
        DropShieldOptions configured,
        CancellationToken cancellationToken)
    {
        var action = route == TrafficRoute.Checkout ? ActionKind.Checkout : ActionKind.Cart;
        var body = await RequestBodyReader.ReadAsync(context.Request, cancellationToken);
        return assertionService.Issue(
            configured.Admission.ProtectedProduct,
            action.ToString().ToLowerInvariant(),
            context.Request.Method,
            TrafficRouteClassifier.GetRouteTemplate(route),
            body);
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

    private static async Task WriteAssertionUnavailableAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(
            new GatewayErrorResponse(
                "state_unavailable",
                "Origin assertion issuance is temporarily unavailable."),
            cancellationToken);
    }
}
