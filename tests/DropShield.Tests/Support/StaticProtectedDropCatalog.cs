using DropShield.Api.Catalog;

namespace DropShield.Tests.Support;

internal sealed class StaticProtectedDropCatalog(string dropId, params (long ProductId, string Sku)[] products) : IProtectedDropCatalog
{
    private readonly ProtectedDropSnapshot _snapshot = new(
        dropId,
        products.Select(product => new ProtectedDropProduct(dropId, product.ProductId, product.Sku)).ToArray(),
        DateTimeOffset.UtcNow);

    public ProtectedDropCatalogStatus Status => new(true, true, _snapshot.LoadedAt, 0, _snapshot.DropId, _snapshot.Products.Count);
    public ProtectedDropSnapshot GetActiveDrop() => _snapshot;
    public bool TryResolveSku(string sku, out ProtectedDropProduct product)
    {
        product = _snapshot.Products.FirstOrDefault(item => string.Equals(item.Sku, sku, StringComparison.OrdinalIgnoreCase))!;
        return product is not null;
    }
    public bool TryResolveProductId(long productId, out ProtectedDropProduct product)
    {
        product = _snapshot.Products.FirstOrDefault(item => item.ProductId == productId)!;
        return product is not null;
    }
}
