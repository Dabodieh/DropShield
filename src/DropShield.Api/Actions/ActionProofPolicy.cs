using DropShield.Api.Traffic;

namespace DropShield.Api.Actions;

public static class ActionProofPolicy
{
    /// <summary>
    /// A mutation is a fixed REST/storefront cart-add or checkout route (always protected, no
    /// SKU inspection needed — matches the existing REST precedent), or a POST /graphql request
    /// that <see cref="Traffic.TrafficMetricsMiddleware"/> already determined targets a
    /// protected-drop addProductsToCart mutation. Ordinary GraphQL traffic (catalogue queries,
    /// customer data, cart-add for non-protected SKUs) on the same shared /graphql endpoint is
    /// never treated as a mutation.
    /// </summary>
    public static bool AppliesToMutation(HttpContext context)
    {
        var request = context.Request;
        if (!HttpMethods.IsPost(request.Method))
        {
            return false;
        }

        if (request.Path.Equals("/api/cart", StringComparison.OrdinalIgnoreCase) ||
            request.Path.Equals("/api/checkout", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (request.Path.Equals("/graphql", StringComparison.OrdinalIgnoreCase))
        {
            return context.Features.Get<TrafficRequestObservation>()?.IsProtectedGraphQlCartMutation
                ?? false;
        }

        return context.Features.Get<TrafficRequestObservation>()?.ProtectedAction is not null;
    }

    public static ActionKind GetMutationAction(HttpContext context)
    {
        var request = context.Request;
        return context.Features.Get<TrafficRequestObservation>()?.ProtectedAction ??
               (request.Path.Equals("/api/checkout", StringComparison.OrdinalIgnoreCase)
                   ? ActionKind.Checkout
                   : ActionKind.Cart);
    }

    public static bool TryGetAction(HttpRequest request, out ActionKind action)
    {
        action = default;
        if (!HttpMethods.IsPost(request.Method))
        {
            return false;
        }

        if (request.Path.Equals("/api/action-proofs/cart", StringComparison.OrdinalIgnoreCase))
        {
            action = ActionKind.Cart;
            return true;
        }

        if (request.Path.Equals("/api/action-proofs/checkout", StringComparison.OrdinalIgnoreCase))
        {
            action = ActionKind.Checkout;
            return true;
        }

        return false;
    }
}
