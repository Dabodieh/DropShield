using System.Text;
using DropShield.Api.Catalog;

namespace DropShield.Tests;

public sealed class ProtectionManifestTests
{
    [Fact]
    public void Parse_ValidManifest_ResolvesSkuAndProductId()
    {
        var catalog = new TestCatalog(AdobeCommerceProtectedDropCatalog.Parse(Bytes("""
            {"version":1,"active_drop":{"id":"pokemon-aug-2026","products":[{"product_id":123,"sku":"PKM-ETB-001"}]}}
            """), DateTimeOffset.UtcNow, 10));

        Assert.True(catalog.TryResolveSku("pkm-etb-001", out var bySku));
        Assert.Equal("pokemon-aug-2026", bySku.DropId);
        Assert.True(catalog.TryResolveProductId(123, out var byId));
        Assert.Equal("PKM-ETB-001", byId.Sku);
    }

    [Theory]
    [InlineData("{\"version\":2,\"active_drop\":null}")]
    [InlineData("{\"version\":1,\"active_drop\":{\"id\":\"bad id\",\"products\":[]}}")]
    [InlineData("{\"version\":1,\"active_drop\":{\"id\":\"drop\",\"products\":[{\"product_id\":1,\"sku\":\"A\"},{\"product_id\":1,\"sku\":\"B\"}]}}")]
    public void Parse_InvalidManifest_RejectsNewSnapshot(string json)
    {
        Assert.Throws<InvalidDataException>(() =>
            AdobeCommerceProtectedDropCatalog.Parse(Bytes(json), DateTimeOffset.UtcNow, 10));
    }

    [Fact]
    public void Parse_NoActiveDrop_ReturnsNull()
    {
        Assert.Null(AdobeCommerceProtectedDropCatalog.Parse(Bytes("{\"version\":1,\"active_drop\":null}"), DateTimeOffset.UtcNow, 10));
    }

    [Fact]
    public void Parse_MissingActiveDropKey_ReturnsNull()
    {
        // Magento's webapi serializer omits a null-valued property entirely rather than
        // emitting it as JSON null (confirmed by runtime testing against Mage-OS when no drop
        // is enabled), so this must be treated the same as an explicit null.
        Assert.Null(AdobeCommerceProtectedDropCatalog.Parse(Bytes("{\"version\":1}"), DateTimeOffset.UtcNow, 10));
    }

    private static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);

    private sealed class TestCatalog(ProtectedDropSnapshot? snapshot) : IProtectedDropCatalog
    {
        public ProtectedDropCatalogStatus Status => new(true, true, DateTimeOffset.UtcNow, 0, snapshot?.DropId, snapshot?.Products.Count ?? 0);
        public ProtectedDropSnapshot? GetActiveDrop() => snapshot;
        public bool TryResolveSku(string sku, out ProtectedDropProduct product)
        {
            product = snapshot?.Products.FirstOrDefault(item => string.Equals(item.Sku, sku, StringComparison.OrdinalIgnoreCase))!;
            return product is not null;
        }
        public bool TryResolveProductId(long productId, out ProtectedDropProduct product)
        {
            product = snapshot?.Products.FirstOrDefault(item => item.ProductId == productId)!;
            return product is not null;
        }
    }
}
