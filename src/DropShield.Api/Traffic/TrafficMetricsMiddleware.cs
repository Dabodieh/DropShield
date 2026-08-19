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
            await ClassifyProtectedMutationAsync(
                context.Request,
                route,
                observation,
                options.Value,
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
        CancellationToken cancellationToken)
    {
        if (route == TrafficRoute.CommerceRestCheckout &&
            options.OriginMode == OriginMode.AdobeCommerce)
        {
            // A checkout request contains no reliable SKU. The first Commerce profile therefore
            // requires a checkout action proof for this allow-listed guest-cart operation; the
            // connector independently checks whether the quote actually contains the drop.
            observation.IsCommerceCheckoutMutation = true;
            return;
        }

        if (route is not (TrafficRoute.GraphQlCartAdd or TrafficRoute.CommerceRestCart))
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
        var isProtected = requestedSkus.Any(sku =>
            options.ProtectedProducts.Contains(sku, StringComparer.OrdinalIgnoreCase) &&
            string.Equals(sku, options.Admission.ProtectedProduct, StringComparison.OrdinalIgnoreCase));
        if (route == TrafficRoute.GraphQlCartAdd)
        {
            observation.IsProtectedGraphQlCartMutation = isProtected;
        }
        else if (options.OriginMode == OriginMode.AdobeCommerce)
        {
            observation.IsProtectedCommerceCartMutation = isProtected;
        }
    }
}
