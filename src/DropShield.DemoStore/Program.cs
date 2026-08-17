using DropShield.DemoStore.Models;
using DropShield.DemoStore.Options;
using DropShield.DemoStore.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<DemoStoreOptions>()
    .BindConfiguration(DemoStoreOptions.SectionName)
    .Validate(
        options => options.StockLookupDelayMilliseconds >= 0,
        "Stock lookup delay must be zero or greater.")
    .Validate(
        options => options.InitialAvailableStock >= 0,
        "Initial available stock must be zero or greater.")
    .ValidateOnStart();

builder.Services.AddSingleton<ProductCatalog>();
builder.Services.AddSingleton<StockService>();

var app = builder.Build();

app.MapGet(
    "/health",
    () => TypedResults.Ok(new HealthResponse("healthy", "DropShield.DemoStore")));

var products = app.MapGroup("/api/products");

products.MapGet(
    "/",
    (ProductCatalog catalog) => TypedResults.Ok(catalog.GetAll()));

products.MapGet(
    "/{productId}",
    (string productId, ProductCatalog catalog) =>
    {
        var product = catalog.Find(productId);
        return product is null ? Results.NotFound() : Results.Ok(product);
    });

products.MapGet(
    "/{productId}/stock",
    async (string productId, ProductCatalog catalog, StockService stockService, CancellationToken cancellationToken) =>
    {
        var product = catalog.Find(productId);
        if (product is null)
        {
            return Results.NotFound();
        }

        var available = await stockService.GetAvailableAsync(product.Id, cancellationToken);
        return Results.Ok(new StockResponse(product.Id, available));
    });

app.MapPost(
    "/api/cart",
    () =>
    {
        app.Logger.LogInformation("Accepted placeholder cart request");
        return Results.Accepted(value: new OperationResponse("accepted"));
    });

app.MapPost(
    "/api/checkout",
    () =>
    {
        app.Logger.LogInformation("Accepted placeholder checkout request");
        return Results.Accepted(value: new OperationResponse("accepted"));
    });

app.Run();
