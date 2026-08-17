using DropShield.DemoStore.Models;

namespace DropShield.DemoStore.Services;

public sealed class ProductCatalog
{
    private static readonly Product PokemonEliteTrainerBox = new(
        "pokemon-etb",
        "Pokémon Elite Trainer Box",
        49.99m,
        "GBP");

    private static readonly IReadOnlyList<Product> Products = [PokemonEliteTrainerBox];

    public IReadOnlyList<Product> GetAll() => Products;

    public Product? Find(string productId) => Products.FirstOrDefault(
        product => string.Equals(product.Id, productId, StringComparison.OrdinalIgnoreCase));
}

