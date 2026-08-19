namespace DropShield.Api.Catalog;

/// <summary>
/// Resolves the active protected drop without making shopper-path network requests. Adobe
/// Commerce implementations serve an immutable, refreshed manifest snapshot.
/// </summary>
public interface IProtectedDropCatalog
{
    ProtectedDropCatalogStatus Status { get; }

    bool TryResolveSku(string sku, out ProtectedDropProduct product);

    bool TryResolveProductId(long productId, out ProtectedDropProduct product);

    ProtectedDropSnapshot? GetActiveDrop();
}

public sealed record ProtectedDropProduct(string DropId, long? ProductId, string Sku);

public sealed record ProtectedDropSnapshot(
    string DropId,
    IReadOnlyList<ProtectedDropProduct> Products,
    DateTimeOffset LoadedAt);

public sealed record ProtectedDropCatalogStatus(
    bool HasLoaded,
    bool IsUsable,
    DateTimeOffset? LastSuccessfulRefresh,
    int RefreshFailures,
    string? ActiveDropId,
    int ProtectedProductCount);
