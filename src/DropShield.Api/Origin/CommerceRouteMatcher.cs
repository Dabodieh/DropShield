using DropShield.Api.Actions;
using DropShield.Api.Traffic;

namespace DropShield.Api.Origin;

/// <summary>
/// The intentionally small Adobe Commerce mutation allow-list. It matches only the local
/// Mage-OS 3.0.0 guest-cart routes that the connector protects; it is not a REST proxy.
/// </summary>
public static class CommerceRouteMatcher
{
    public const string GuestCartItemsTemplate =
        "POST /rest[/default]/V1/guest-carts/{cartId}/items";
    public const string GuestCartPaymentInformationTemplate =
        "POST /rest[/default]/V1/guest-carts/{cartId}/payment-information";

    public static bool TryMatch(HttpRequest request, out CommerceRouteMatch match)
    {
        match = default!;
        if (!HttpMethods.IsPost(request.Method))
        {
            return false;
        }

        var segments = (request.Path.Value ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var offset = segments.Length > 1 && segments[0].Equals("rest", StringComparison.OrdinalIgnoreCase)
            ? 1
            : -1;
        if (offset < 0)
        {
            return false;
        }

        if (segments.Length > offset && segments[offset].Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            offset++;
        }

        if (segments.Length != offset + 4 ||
            !segments[offset].Equals("V1", StringComparison.OrdinalIgnoreCase) ||
            !segments[offset + 1].Equals("guest-carts", StringComparison.OrdinalIgnoreCase) ||
            !IsOpaqueCartId(segments[offset + 2]))
        {
            return false;
        }

        var operation = segments[offset + 3];
        match = operation.Equals("items", StringComparison.OrdinalIgnoreCase)
            ? new CommerceRouteMatch(TrafficRoute.CommerceRestCart, ActionKind.Cart, GuestCartItemsTemplate)
            : operation.Equals("payment-information", StringComparison.OrdinalIgnoreCase)
                ? new CommerceRouteMatch(
                    TrafficRoute.CommerceRestCheckout,
                    ActionKind.Checkout,
                    GuestCartPaymentInformationTemplate)
                : default!;
        return match is not null;
    }

    public static string GetAssertionRoute(HttpRequest request) =>
        $"{request.Method.ToUpperInvariant()} {request.PathBase.Add(request.Path)}";

    private static bool IsOpaqueCartId(string value) =>
        value.Length is > 0 and <= 128 &&
        value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');
}

public sealed record CommerceRouteMatch(
    TrafficRoute TrafficRoute,
    ActionKind Action,
    string Template);
