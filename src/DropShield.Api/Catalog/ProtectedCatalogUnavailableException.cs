namespace DropShield.Api.Catalog;

public sealed class ProtectedCatalogUnavailableException : Exception
{
    public ProtectedCatalogUnavailableException()
        : base("The protected-product catalog is unavailable.")
    {
    }
}
