namespace DropShield.Api.Actions;

public static class ActionProofPolicy
{
    public static bool AppliesToMutation(HttpRequest request) =>
        HttpMethods.IsPost(request.Method) &&
        (request.Path.Equals("/api/cart", StringComparison.OrdinalIgnoreCase) ||
         request.Path.Equals("/api/checkout", StringComparison.OrdinalIgnoreCase));

    public static ActionKind GetMutationAction(HttpRequest request) =>
        request.Path.Equals("/api/cart", StringComparison.OrdinalIgnoreCase)
            ? ActionKind.Cart
            : ActionKind.Checkout;

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
