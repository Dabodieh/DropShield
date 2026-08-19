using DropShield.Api.Options;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Catalog;

/// <summary>Local synthetic mapping used only by the DemoStore origin mode.</summary>
public sealed class DemoStoreProtectedDropCatalog : IProtectedDropCatalog
{
    private readonly ProtectedDropSnapshot _snapshot;

    public DemoStoreProtectedDropCatalog(IOptions<DropShieldOptions> options, TimeProvider timeProvider)
    {
        var configured = options.Value;
        var products = configured.ProtectedProducts
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(sku => new ProtectedDropProduct(configured.Admission.DropId, null, sku))
            .ToArray();
        _snapshot = new ProtectedDropSnapshot(configured.Admission.DropId, products, timeProvider.GetUtcNow());
    }

    public ProtectedDropCatalogStatus Status => new(
        true, true, _snapshot.LoadedAt, 0, _snapshot.DropId, _snapshot.Products.Count);

    public bool TryResolveSku(string sku, out ProtectedDropProduct product)
    {
        product = _snapshot.Products.FirstOrDefault(item =>
            string.Equals(item.Sku, sku, StringComparison.OrdinalIgnoreCase))!;
        return product is not null;
    }

    public bool TryResolveProductId(long productId, out ProtectedDropProduct product)
    {
        product = default!;
        return false;
    }

    public ProtectedDropSnapshot GetActiveDrop() => _snapshot;
}
