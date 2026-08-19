namespace DropShield.Api.Traffic;

using DropShield.Api.Origin;

public static class TrafficRouteClassifier
{
    public static TrafficRoute Classify(HttpRequest request)
    {
        var path = request.Path.Value?.TrimEnd('/') ?? string.Empty;

        if (HttpMethods.IsGet(request.Method) &&
            path.Equals("/api/products", StringComparison.OrdinalIgnoreCase))
        {
            return TrafficRoute.Products;
        }

        if (HttpMethods.IsGet(request.Method) && TryGetProductSegments(path, out var segments))
        {
            if (segments.Length == 3)
            {
                return TrafficRoute.Product;
            }

            if (segments.Length == 4 &&
                segments[3].Equals("stock", StringComparison.OrdinalIgnoreCase))
            {
                return TrafficRoute.Stock;
            }
        }

        if (HttpMethods.IsPost(request.Method) &&
            path.Equals("/api/cart", StringComparison.OrdinalIgnoreCase))
        {
            return TrafficRoute.Cart;
        }

        if (HttpMethods.IsPost(request.Method) &&
            path.Equals("/api/checkout", StringComparison.OrdinalIgnoreCase))
        {
            return TrafficRoute.Checkout;
        }

        if (HttpMethods.IsPost(request.Method) &&
            path.Equals("/graphql", StringComparison.OrdinalIgnoreCase))
        {
            return TrafficRoute.GraphQlCartAdd;
        }

        if (HttpMethods.IsPost(request.Method) &&
            path.Equals("/checkout/cart/add", StringComparison.OrdinalIgnoreCase))
        {
            return TrafficRoute.StorefrontCartAdd;
        }

        if (CommerceRouteMatcher.TryMatch(request, out var commerceRoute))
        {
            return commerceRoute.TrafficRoute;
        }

        if (HttpMethods.IsPost(request.Method) &&
            Actions.ActionProofPolicy.TryGetAction(request, out _))
        {
            return TrafficRoute.ActionProof;
        }

        return TrafficRoute.Unknown;
    }

    public static string? GetProductId(HttpRequest request)
    {
        var path = request.Path.Value?.TrimEnd('/') ?? string.Empty;
        return TryGetProductSegments(path, out var segments) &&
               (segments.Length == 3 ||
                segments[3].Equals("stock", StringComparison.OrdinalIgnoreCase))
            ? segments[2]
            : null;
    }

    public static string GetMetricName(TrafficRoute route) => route switch
    {
        TrafficRoute.Products => "products",
        TrafficRoute.Product => "product",
        TrafficRoute.Stock => "stock",
        TrafficRoute.Cart => "cart",
        TrafficRoute.Checkout => "checkout",
        TrafficRoute.ActionProof => "actionProof",
        TrafficRoute.GraphQlCartAdd => "graphqlCartAdd",
        TrafficRoute.StorefrontCartAdd => "storefrontCartAdd",
        TrafficRoute.CommerceRestCart => "commerceRestCart",
        TrafficRoute.CommerceRestCheckout => "commerceRestCheckout",
        _ => "unknown",
    };

    public static string GetRouteTemplate(TrafficRoute route) => route switch
    {
        TrafficRoute.Products => "GET /api/products",
        TrafficRoute.Product => "GET /api/products/{productId}",
        TrafficRoute.Stock => "GET /api/products/{productId}/stock",
        TrafficRoute.Cart => "POST /api/cart",
        TrafficRoute.Checkout => "POST /api/checkout",
        TrafficRoute.ActionProof => "POST /api/action-proofs/{action}",
        TrafficRoute.GraphQlCartAdd => "POST /graphql",
        TrafficRoute.StorefrontCartAdd => "POST /checkout/cart/add",
        TrafficRoute.CommerceRestCart => CommerceRouteMatcher.GuestCartItemsTemplate,
        TrafficRoute.CommerceRestCheckout => CommerceRouteMatcher.GuestCartPaymentInformationTemplate,
        _ => "unknown",
    };

    public static bool IsProtectedStockRequest(
        HttpRequest request,
        IReadOnlyCollection<string> protectedProducts)
    {
        if (Classify(request) != TrafficRoute.Stock)
        {
            return false;
        }

        var productId = GetProductId(request);
        return productId is not null &&
               protectedProducts.Contains(productId, StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryGetProductSegments(string path, out string[] segments)
    {
        segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length is 3 or 4 &&
               segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) &&
               segments[1].Equals("products", StringComparison.OrdinalIgnoreCase);
    }
}
