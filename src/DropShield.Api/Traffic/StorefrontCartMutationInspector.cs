using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace DropShield.Api.Traffic;

/// <summary>Reads only the standard storefront form's numeric product field.</summary>
public static class StorefrontCartMutationInspector
{
    public static long? InspectProductId(ReadOnlySpan<byte> body)
    {
        var values = QueryHelpers.ParseQuery(Encoding.UTF8.GetString(body));
        return values.TryGetValue("product", out var product) &&
               long.TryParse(product.ToString(), out var productId) && productId > 0
            ? productId
            : null;
    }
}
