using DropShield.Api.Models;
using DropShield.Api.Options;
using DropShield.Api.Origin;
using DropShield.Api.Traffic;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<DropShieldOptions>()
    .BindConfiguration(DropShieldOptions.SectionName)
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<DropShieldOptions>, DropShieldOptionsValidator>();
builder.Services.AddSingleton<ClientIdentityProvider>();
builder.Services.AddSingleton<TrafficMetrics>();
builder.Services.AddTransient<DemoStoreForwarder>();
builder.Services.AddHttpClient<IDemoStoreClient, DemoStoreClient>((services, client) =>
{
    var options = services.GetRequiredService<IOptions<DropShieldOptions>>().Value;
    client.BaseAddress = new Uri(options.OriginBaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(options.OriginTimeoutSeconds);
});
builder.Services.AddRateLimiter(_ => { });
builder.Services.AddSingleton<
    IConfigureOptions<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>,
    TrafficPolicyOptionsSetup>();

var app = builder.Build();

app.UseRouting();
app.UseMiddleware<TrafficMetricsMiddleware>();
app.UseRateLimiter();

app.MapGet(
    "/health",
    () => TypedResults.Ok(new HealthResponse("healthy", "DropShield.Api")));

app.MapGet(
    "/api/products",
    (HttpContext context, DemoStoreForwarder forwarder, CancellationToken cancellationToken) =>
        forwarder.ForwardAsync(context, TrafficRoute.Products, cancellationToken));

app.MapGet(
    "/api/products/{productId}",
    (HttpContext context, DemoStoreForwarder forwarder, CancellationToken cancellationToken) =>
        forwarder.ForwardAsync(context, TrafficRoute.Product, cancellationToken));

app.MapGet(
    "/api/products/{productId}/stock",
    (HttpContext context, DemoStoreForwarder forwarder, CancellationToken cancellationToken) =>
        forwarder.ForwardAsync(context, TrafficRoute.Stock, cancellationToken));

app.MapPost(
    "/api/cart",
    (HttpContext context, DemoStoreForwarder forwarder, CancellationToken cancellationToken) =>
        forwarder.ForwardAsync(context, TrafficRoute.Cart, cancellationToken));

app.MapPost(
    "/api/checkout",
    (HttpContext context, DemoStoreForwarder forwarder, CancellationToken cancellationToken) =>
        forwarder.ForwardAsync(context, TrafficRoute.Checkout, cancellationToken));

app.MapGet(
    "/internal/metrics",
    (TrafficMetrics metrics, IOptions<DropShieldOptions> options, IHostEnvironment environment) =>
    {
        if (!InternalMetricsAreAvailable(options.Value, environment))
        {
            return Results.NotFound();
        }

        return Results.Ok(metrics.GetSnapshot());
    });

app.MapPost(
    "/internal/metrics/reset",
    (TrafficMetrics metrics, IOptions<DropShieldOptions> options, IHostEnvironment environment) =>
    {
        if (!InternalMetricsAreAvailable(options.Value, environment))
        {
            return Results.NotFound();
        }

        metrics.Reset();
        return Results.NoContent();
    });

app.Run();

static bool InternalMetricsAreAvailable(
    DropShieldOptions options,
    IHostEnvironment environment) =>
    options.InternalMetrics.Enabled &&
    (environment.IsDevelopment() || environment.IsEnvironment("Testing"));
