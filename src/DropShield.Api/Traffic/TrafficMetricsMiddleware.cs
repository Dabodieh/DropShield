using System.Diagnostics;
using DropShield.Api.Actions;
using DropShield.Api.Behaviour;
using DropShield.Api.Catalog;
using DropShield.Api.Options;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Traffic;

public sealed class TrafficMetricsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        TrafficMetrics metrics,
        BehaviourActivityRecorder behaviourRecorder,
        IProtectedDropCatalog catalog,
        IOptions<DropShieldOptions> options,
        ILogger<TrafficMetricsMiddleware> logger)
    {
        // First middleware in the pipeline: strips any client-supplied assertion before anything
        // downstream can see it. A real assertion is only added later, by DemoStoreForwarder.
        context.Request.Headers.Remove(options.Value.OriginAssertions.HeaderName);

        var route = TrafficRouteClassifier.Classify(context.Request);
        if (route == TrafficRoute.Unknown)
        {
            await next(context);
            return;
        }

        var isProtectedStock = IsProtectedStockRequest(context.Request, catalog);
        var observation = new TrafficRequestObservation(isProtectedStock);
        context.Features.Set(observation);
        metrics.RecordIncoming(route, isProtectedStock);
        var startedTimestamp = Stopwatch.GetTimestamp();
        var internalFailure = false;

        try
        {
            await ClassifyProtectedMutationAsync(
                context.Request,
                route,
                observation,
                options.Value,
                catalog,
                context.RequestAborted);
            await next(context);
        }
        catch (RequestBodyTooLargeException)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "request_too_large",
                message = "The protected request body exceeds the configured limit.",
            }, context.RequestAborted);
        }
        catch (ProtectedCatalogUnavailableException)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "protection_catalog_unavailable",
                message = "Protected-product configuration is temporarily unavailable.",
            }, context.RequestAborted);
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

            var isTransaction = route is TrafficRoute.Cart or TrafficRoute.Checkout or TrafficRoute.StorefrontCartAdd ||
                                observation.ProtectedAction is not null ||
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

    /// <summary>
    /// POST /graphql is a shared endpoint: catalogue queries, customer/account operations, and
    /// cart-add mutations for both protected and ordinary SKUs all arrive here. Only a request
    /// whose GraphQL document invokes one of the narrow supported cart-add mutations for the
    /// configured protected drop should enter the protected-mutation pipeline
    /// (admission/action-proof/reservation);
    /// everything else on this route is ordinary traffic. Reads the body once; the request
    /// stream is left rewound for downstream consumers (action proof, forwarding).
    /// </summary>
    private static async Task ClassifyProtectedMutationAsync(
        HttpRequest request,
        TrafficRoute route,
        TrafficRequestObservation observation,
        DropShieldOptions options,
        IProtectedDropCatalog catalog,
        CancellationToken cancellationToken)
    {
        if (options.OriginMode == OriginMode.AdobeCommerce &&
            route is TrafficRoute.CommerceRestCart or TrafficRoute.CommerceRestCheckout or
                TrafficRoute.GraphQlCartAdd or TrafficRoute.StorefrontCartAdd &&
            !catalog.Status.IsUsable)
        {
            // A missing/stale manifest could omit a newly protected product. Do not infer it is
            // ordinary; the Commerce connector independently remains authoritative.
            throw new ProtectedCatalogUnavailableException();
        }

        if (route == TrafficRoute.CommerceRestCheckout &&
            options.OriginMode == OriginMode.AdobeCommerce)
        {
            // A checkout request contains no reliable SKU. With no active drop it stays an
            // ordinary Commerce request. With an active drop, the connector independently
            // verifies whether the quote contains that drop before accepting the assertion.
            var activeDrop = catalog.GetActiveDrop();
            observation.IsCommerceCheckoutMutation = activeDrop is not null;
            observation.ProtectedDropId = activeDrop?.DropId;
            return;
        }

        if (route is not (TrafficRoute.GraphQlCartAdd or TrafficRoute.CommerceRestCart or TrafficRoute.StorefrontCartAdd))
        {
            return;
        }

        observation.BufferedBody = await RequestBodyReader.ReadAsync(
            request,
            options.AdobeCommerce.MaximumProtectedRequestBodyBytes,
            cancellationToken);
        var requestedSkus = route == TrafficRoute.GraphQlCartAdd
            ? GraphQlCartMutationInspector.Inspect(observation.BufferedBody).RequestedSkus
            : CommerceRestCartMutationInspector.Inspect(observation.BufferedBody).RequestedSkus;
        var productId = route == TrafficRoute.StorefrontCartAdd
            ? StorefrontCartMutationInspector.InspectProductId(observation.BufferedBody)
            : null;
        var protectedProduct = requestedSkus
            .Select(sku => catalog.TryResolveSku(sku, out var found) ? found : null)
            .FirstOrDefault(found => found is not null);
        if (protectedProduct is null && productId is not null && catalog.TryResolveProductId(productId.Value, out var byId))
        {
            protectedProduct = byId;
        }

        var isProtected = protectedProduct is not null;
        observation.ProtectedDropId = protectedProduct?.DropId;
        if (route == TrafficRoute.GraphQlCartAdd)
        {
            observation.IsProtectedGraphQlCartMutation = isProtected;
        }
        else if (options.OriginMode == OriginMode.AdobeCommerce)
        {
            observation.IsProtectedCommerceCartMutation = isProtected;
        }
        else if (route == TrafficRoute.StorefrontCartAdd)
        {
            observation.IsProtectedCommerceCartMutation = isProtected;
        }
    }

    private static bool IsProtectedStockRequest(HttpRequest request, IProtectedDropCatalog catalog) =>
        TrafficRouteClassifier.Classify(request) == TrafficRoute.Stock &&
        TrafficRouteClassifier.GetProductId(request) is { } productId &&
        catalog.TryResolveSku(productId, out _);
}
