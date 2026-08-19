using System.Text.Json;

namespace DropShield.Api.Traffic;

/// <summary>
/// Determines whether a raw POST /graphql request body invokes one of the narrowly supported
/// GraphQL cart-add mutations (<c>addSimpleProductsToCart</c>,
/// <c>addVirtualProductsToCart</c>, or <c>addProductsToCart</c>) and, if so, which SKUs it
/// targets. The first two reach Magento's legacy
/// <c>Magento\QuoteGraphQl\Model\Cart\AddProductsToCart::execute</c> service; the last
/// reaches Mage-OS' modern service.
///
/// Deliberately not a GraphQL parser: it inspects the request body's JSON envelope
/// (<c>{query, variables, operationName}</c>) and looks for the mutation name in the query
/// text plus SKU values in <c>variables.cart_items[].sku</c>/<c>parent_sku</c> — the same shape
/// Magento's own resolver reads (see AddSimpleProductToCart::extractSku). This is enough to
/// tell "a supported cart-add mutation is present" from "an ordinary catalogue/customer query,"
/// which is all routing needs; Magento itself remains the authority on whether the document is
/// otherwise valid GraphQL.
/// </summary>
public static class GraphQlCartMutationInspector
{
    private static readonly string[] SupportedMutationNames =
    [
        "addProductsToCart",
        "addSimpleProductsToCart",
        "addVirtualProductsToCart",
    ];

    public static GraphQlCartMutation Inspect(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
        {
            return GraphQlCartMutation.None;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body.ToArray());
        }
        catch (JsonException)
        {
            return GraphQlCartMutation.None;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("query", out var queryElement) ||
                queryElement.ValueKind != JsonValueKind.String)
            {
                return GraphQlCartMutation.None;
            }

            var query = queryElement.GetString() ?? string.Empty;
            if (!SupportedMutationNames.Any(name => query.Contains(name, StringComparison.Ordinal)))
            {
                return GraphQlCartMutation.None;
            }

            var skus = new List<string>();
            if (root.TryGetProperty("variables", out var variables) &&
                variables.ValueKind == JsonValueKind.Object)
            {
                CollectSkus(variables, skus);
            }

            CollectSkusFromInlineArguments(query, skus);

            return skus.Count == 0
                ? GraphQlCartMutation.None
                : new GraphQlCartMutation(true, skus);
        }
    }

    private static void CollectSkus(JsonElement variables, List<string> skus)
    {
        if (!variables.TryGetProperty("input", out var input) ||
            !input.TryGetProperty("cart_items", out var cartItems) ||
            cartItems.ValueKind != JsonValueKind.Array)
        {
            if (!variables.TryGetProperty("cartItems", out cartItems) ||
                cartItems.ValueKind != JsonValueKind.Array)
            {
                return;
            }
        }

        foreach (var item in cartItems.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (item.TryGetProperty("parent_sku", out var parentSku) &&
                parentSku.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(parentSku.GetString()))
            {
                skus.Add(parentSku.GetString()!);
                continue;
            }

            if (item.TryGetProperty("sku", out var directSku) &&
                directSku.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(directSku.GetString()))
            {
                skus.Add(directSku.GetString()!);
                continue;
            }

            if (item.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("sku", out var sku) &&
                sku.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(sku.GetString()))
            {
                skus.Add(sku.GetString()!);
            }
        }
    }

    /// <summary>
    /// Some GraphQL clients inline sku literals directly in the query text instead of using
    /// variables (e.g. <c>sku: "pokemon-etb"</c>). Variables are the common case and are
    /// checked first; this is a bounded fallback scan, not general GraphQL argument parsing.
    /// </summary>
    private static void CollectSkusFromInlineArguments(string query, List<string> skus)
    {
        const string marker = "sku:";
        var index = 0;
        while (true)
        {
            var found = query.IndexOf(marker, index, StringComparison.Ordinal);
            if (found < 0)
            {
                return;
            }

            var start = query.IndexOf('"', found + marker.Length);
            if (start < 0)
            {
                return;
            }

            var end = query.IndexOf('"', start + 1);
            if (end < 0)
            {
                return;
            }

            var value = query[(start + 1)..end];
            if (!string.IsNullOrEmpty(value))
            {
                skus.Add(value);
            }

            index = end + 1;
        }
    }
}

public sealed record GraphQlCartMutation(bool IsCartAddMutation, IReadOnlyList<string> RequestedSkus)
{
    public static GraphQlCartMutation None { get; } = new(false, []);
}
