using DropShield.Api.Catalog;

namespace DropShield.Tests.Support;

/// <summary>
/// A settable <see cref="IProtectedDropCatalog"/> for exercising states
/// <see cref="StaticProtectedDropCatalog"/> cannot represent: no active drop while still usable,
/// and unusable (never-loaded/stale-equivalent).
/// </summary>
internal sealed class ControllableProtectedDropCatalog : IProtectedDropCatalog
{
    private ProtectedDropSnapshot? _snapshot;
    private bool _usable = true;

    public void SetActiveDrop(string dropId, params (long ProductId, string Sku)[] products) =>
        _snapshot = new ProtectedDropSnapshot(
            dropId,
            products.Select(product => new ProtectedDropProduct(dropId, product.ProductId, product.Sku)).ToArray(),
            DateTimeOffset.UtcNow);

    public void SetNoActiveDrop() => _snapshot = null;

    public void SetUsable(bool usable) => _usable = usable;

    public ProtectedDropCatalogStatus Status =>
        new(true, _usable, DateTimeOffset.UtcNow, 0, _snapshot?.DropId, _snapshot?.Products.Count ?? 0);

    public ProtectedDropSnapshot? GetActiveDrop() => _snapshot;

    public bool TryResolveSku(string sku, out ProtectedDropProduct product)
    {
        product = _snapshot?.Products.FirstOrDefault(item =>
            string.Equals(item.Sku, sku, StringComparison.OrdinalIgnoreCase))!;
        return product is not null;
    }

    public bool TryResolveProductId(long productId, out ProtectedDropProduct product)
    {
        product = _snapshot?.Products.FirstOrDefault(item => item.ProductId == productId)!;
        return product is not null;
    }
}
