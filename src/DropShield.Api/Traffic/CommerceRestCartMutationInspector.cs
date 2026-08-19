using System.Text.Json;

namespace DropShield.Api.Traffic;

/// <summary>Reads only the documented guest-cart item-add JSON shape to identify its SKU.</summary>
public static class CommerceRestCartMutationInspector
{
    public static CommerceRestCartMutation Inspect(ReadOnlySpan<byte> body)
    {
        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("cartItem", out var cartItem) ||
                cartItem.ValueKind != JsonValueKind.Object ||
                !cartItem.TryGetProperty("sku", out var sku) ||
                sku.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(sku.GetString()))
            {
                return CommerceRestCartMutation.None;
            }

            return new CommerceRestCartMutation([sku.GetString()!]);
        }
        catch (JsonException)
        {
            return CommerceRestCartMutation.None;
        }
    }
}

public sealed record CommerceRestCartMutation(IReadOnlyList<string> RequestedSkus)
{
    public static CommerceRestCartMutation None { get; } = new([]);
}
