using System.Net;
using System.Net.Http.Json;
using DropShield.DemoStore;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DropShield.Tests;

public sealed class DemoStoreEndpointTests : IClassFixture<WebApplicationFactory<DemoStoreAssemblyMarker>>
{
    private readonly HttpClient _client;

    public DemoStoreEndpointTests(WebApplicationFactory<DemoStoreAssemblyMarker> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_ReturnsHealthyResponse()
    {
        var response = await _client.GetAsync("/health");
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("healthy", body.Status);
        Assert.Equal("DropShield.DemoStore", body.Service);
    }

    [Fact]
    public async Task Products_ReturnsPokemonProduct()
    {
        var response = await _client.GetAsync("/api/products");
        var products = await response.Content.ReadFromJsonAsync<ProductResponse[]>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var product = Assert.Single(Assert.IsType<ProductResponse[]>(products));
        AssertPokemonProduct(product);
    }

    [Fact]
    public async Task ProductDetail_ReturnsExpectedProduct()
    {
        var response = await _client.GetAsync("/api/products/pokemon-etb");
        var product = await response.Content.ReadFromJsonAsync<ProductResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(product);
        AssertPokemonProduct(product);
    }

    [Fact]
    public async Task ProductStock_ReturnsAvailableQuantity()
    {
        var response = await _client.GetAsync("/api/products/pokemon-etb/stock");
        var stock = await response.Content.ReadFromJsonAsync<StockResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(stock);
        Assert.Equal("pokemon-etb", stock.ProductId);
        Assert.Equal(500, stock.Available);
    }

    [Theory]
    [InlineData("/api/products/unknown-product")]
    [InlineData("/api/products/unknown-product/stock")]
    public async Task UnknownProduct_ReturnsNotFound(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/cart")]
    [InlineData("/api/checkout")]
    public async Task PlaceholderPost_ReturnsAccepted(string path)
    {
        var response = await _client.PostAsync(path, content: null);
        var body = await response.Content.ReadFromJsonAsync<OperationResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("accepted", body.Status);
    }

    private static void AssertPokemonProduct(ProductResponse product)
    {
        Assert.Equal("pokemon-etb", product.Id);
        Assert.Equal("Pokémon Elite Trainer Box", product.Name);
        Assert.Equal(49.99m, product.Price);
        Assert.Equal("GBP", product.Currency);
    }

    private sealed record HealthResponse(string Status, string Service);

    private sealed record ProductResponse(string Id, string Name, decimal Price, string Currency);

    private sealed record StockResponse(string ProductId, int Available);

    private sealed record OperationResponse(string Status);
}

