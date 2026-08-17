using DropShield.Api.Admission;
using DropShield.Api.Actions;
using DropShield.Api.Models;
using DropShield.Api.Options;
using DropShield.Api.Origin;
using DropShield.Api.State;
using DropShield.Api.Traffic;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<DropShieldOptions>()
    .BindConfiguration(DropShieldOptions.SectionName)
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<DropShieldOptions>, DropShieldOptionsValidator>();
builder.Services.AddSingleton<ClientIdentityProvider>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TrafficMetrics>();
builder.Services.AddSingleton<RedisConnectionProvider>();
builder.Services.AddSingleton<RedisTrafficKeyBuilder>();
builder.Services.AddSingleton<IDistributedTrafficState, RedisTrafficState>();
builder.Services.AddSingleton<RedisTrafficPolicyEvaluator>();
builder.Services.AddSingleton<AdmissionSessionProvider>();
builder.Services.AddSingleton<AdmissionSigningKeyProvider>();
builder.Services.AddSingleton<IAdmissionTokenService, AdmissionTokenService>();
builder.Services.AddSingleton<AdmissionTokenCookieManager>();
builder.Services.AddSingleton<IActionTokenService, ActionTokenService>();
builder.Services.AddSingleton<InMemoryReplayState>();
builder.Services.AddSingleton<RedisReplayState>();
builder.Services.AddSingleton<IReplayState>(services =>
    services.GetRequiredService<IOptions<DropShieldOptions>>().Value.StateProvider ==
    TrafficStateProvider.Redis
        ? services.GetRequiredService<RedisReplayState>()
        : services.GetRequiredService<InMemoryReplayState>());
builder.Services.AddSingleton<AdmissionProofAuthorizer>();
builder.Services.AddSingleton<InMemoryAdmissionState>();
builder.Services.AddSingleton<RedisAdmissionKeyBuilder>();
builder.Services.AddSingleton<RedisAdmissionState>();
builder.Services.AddSingleton<IAdmissionState>(services =>
    services.GetRequiredService<IOptions<DropShieldOptions>>().Value.StateProvider ==
    TrafficStateProvider.Redis
        ? services.GetRequiredService<RedisAdmissionState>()
        : services.GetRequiredService<InMemoryAdmissionState>());
builder.Services.AddSingleton<AdmissionEvaluator>();
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
_ = app.Services.GetRequiredService<TrafficMetrics>();
var configuredOptions = app.Services
    .GetRequiredService<IOptions<DropShieldOptions>>()
    .Value;

app.UseRouting();
app.UseMiddleware<TrafficMetricsMiddleware>();
if (configuredOptions.StateProvider == TrafficStateProvider.Redis)
{
    app.UseMiddleware<RedisTrafficPolicyMiddleware>();
}
else
{
    app.UseRateLimiter();
}
app.UseMiddleware<AdmissionTokenMiddleware>();
app.UseMiddleware<AdmissionControlMiddleware>();
app.UseMiddleware<ActionProofMiddleware>();

app.MapGet(
    "/health",
    async (
        IServiceProvider services,
        CancellationToken cancellationToken) =>
    {
        var providerName = configuredOptions.StateProvider.ToString();
        if (configuredOptions.StateProvider == TrafficStateProvider.InMemory)
        {
            return Results.Ok(new HealthResponse(
                "healthy",
                "DropShield.Api",
                providerName,
                "available"));
        }

        var state = services.GetRequiredService<IDistributedTrafficState>();
        var stateHealth = await state.GetHealthAsync(cancellationToken);
        var response = new HealthResponse(
            stateHealth.IsAvailable ? "healthy" : "unhealthy",
            "DropShield.Api",
            providerName,
            stateHealth.Status);

        return stateHealth.IsAvailable
            ? Results.Ok(response)
            : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
    });

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

app.MapPost(
    "/api/action-proofs/cart",
    (HttpContext context, AdmissionProofAuthorizer authorizer, IActionTokenService tokenService,
        TrafficMetrics metrics, IOptions<DropShieldOptions> options) =>
        IssueActionProof(context, ActionKind.Cart, authorizer, tokenService, metrics, options.Value));

app.MapPost(
    "/api/action-proofs/checkout",
    (HttpContext context, AdmissionProofAuthorizer authorizer, IActionTokenService tokenService,
        TrafficMetrics metrics, IOptions<DropShieldOptions> options) =>
        IssueActionProof(context, ActionKind.Checkout, authorizer, tokenService, metrics, options.Value));

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

static IResult IssueActionProof(
    HttpContext context,
    ActionKind action,
    AdmissionProofAuthorizer authorizer,
    IActionTokenService tokenService,
    TrafficMetrics metrics,
    DropShieldOptions options)
{
    if (!options.ActionProofs.Enabled)
    {
        return Results.NotFound();
    }

    var admission = authorizer.Authorize(context);
    if (!admission.IsAuthorized)
    {
        return Results.Json(
            new GatewayErrorResponse(
                "admission_required",
                "Admission is required for this protected drop."),
            statusCode: StatusCodes.Status403Forbidden);
    }

    var token = tokenService.Issue(
        options.Admission.ProtectedProduct,
        admission.SessionId!,
        action);
    metrics.RecordActionTokenIssued(action);
    return Results.Ok(new ActionProofResponse(
        action.ToString().ToLowerInvariant(),
        token,
        options.ActionProofs.LifetimeSeconds));
}
