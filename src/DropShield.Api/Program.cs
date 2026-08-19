using DropShield.Api.Admission;
using DropShield.Api.Actions;
using DropShield.Api.Behaviour;
using DropShield.Api.Inventory;
using DropShield.Api.Models;
using DropShield.Api.Options;
using DropShield.Api.Origin;
using DropShield.Api.Security;
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
builder.Services.AddSingleton<InternalHashingKeyProvider>();
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
builder.Services.AddSingleton<ActionProofSigningKeyProvider>();
builder.Services.AddSingleton<IActionTokenService, ActionTokenService>();
builder.Services.AddSingleton<InMemoryReplayState>();
builder.Services.AddSingleton<RedisReplayState>();
builder.Services.AddSingleton<IReplayState>(services =>
    services.GetRequiredService<IOptions<DropShieldOptions>>().Value.StateProvider ==
    TrafficStateProvider.Redis
        ? services.GetRequiredService<RedisReplayState>()
        : services.GetRequiredService<InMemoryReplayState>());
builder.Services.AddSingleton<AdmissionProofAuthorizer>();
builder.Services.AddSingleton<ReservationSessionHasher>();
builder.Services.AddSingleton<InMemoryInventoryReservationState>();
builder.Services.AddSingleton<RedisInventoryReservationState>();
builder.Services.AddSingleton<IInventoryReservationState>(services =>
    services.GetRequiredService<IOptions<DropShieldOptions>>().Value.StateProvider == TrafficStateProvider.Redis
        ? services.GetRequiredService<RedisInventoryReservationState>()
        : services.GetRequiredService<InMemoryInventoryReservationState>());
builder.Services.AddSingleton<BehaviourIdentityProvider>();
builder.Services.AddSingleton<InMemoryBehaviourState>();
builder.Services.AddSingleton<RedisBehaviourState>();
builder.Services.AddSingleton<IBehaviourState>(services =>
    services.GetRequiredService<IOptions<DropShieldOptions>>().Value.StateProvider == TrafficStateProvider.Redis
        ? services.GetRequiredService<RedisBehaviourState>()
        : services.GetRequiredService<InMemoryBehaviourState>());
builder.Services.AddSingleton<BehaviourActivityRecorder>();
builder.Services.AddSingleton<InMemoryAdmissionState>();
builder.Services.AddSingleton<RedisAdmissionKeyBuilder>();
builder.Services.AddSingleton<RedisAdmissionState>();
builder.Services.AddSingleton<IAdmissionState>(services =>
    services.GetRequiredService<IOptions<DropShieldOptions>>().Value.StateProvider ==
    TrafficStateProvider.Redis
        ? services.GetRequiredService<RedisAdmissionState>()
        : services.GetRequiredService<InMemoryAdmissionState>());
builder.Services.AddSingleton<AdmissionEvaluator>();
builder.Services.AddSingleton<OriginAssertionSigningKeyProvider>();
builder.Services.AddSingleton<IOriginAssertionService, OriginAssertionService>();
builder.Services.AddTransient<DemoStoreForwarder>();
builder.Services.AddHttpClient<IDemoStoreClient, DemoStoreClient>((services, client) =>
{
    var options = services.GetRequiredService<IOptions<DropShieldOptions>>().Value;
    client.BaseAddress = new Uri(options.OriginBaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(options.OriginTimeoutSeconds);
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AllowAutoRedirect = false,
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
app.UseMiddleware<EdgeTrustMiddleware>();
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
app.UseMiddleware<BehaviourPolicyMiddleware>();
app.UseMiddleware<ActionProofMiddleware>();
app.UseMiddleware<InventoryReservationMiddleware>();

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
    async (
        string productId,
        HttpContext context,
        DemoStoreForwarder forwarder,
        IOptions<DropShieldOptions> options,
        CancellationToken cancellationToken) =>
    {
        // Commerce has no equivalent to the demonstration stock endpoint. Keep this
        // DropShield-owned admission entry point local rather than proxying a made-up Magento
        // route; its reservation policy remains the capacity authority for this narrow mode.
        if (options.Value.OriginMode == OriginMode.AdobeCommerce &&
            string.Equals(
                productId,
                options.Value.Admission.ProtectedProduct,
                StringComparison.OrdinalIgnoreCase))
        {
            await context.Response.WriteAsJsonAsync(
                new { productId, available = options.Value.InventoryReservation.InitialStock },
                cancellationToken);
            return;
        }

        await forwarder.ForwardAsync(context, TrafficRoute.Stock, cancellationToken);
    });

app.MapPost(
    "/api/cart",
    (HttpContext context, DemoStoreForwarder forwarder, CancellationToken cancellationToken) =>
        forwarder.ForwardAsync(context, TrafficRoute.Cart, cancellationToken));

app.MapPost(
    "/api/checkout",
    (HttpContext context, DemoStoreForwarder forwarder, CancellationToken cancellationToken) =>
        forwarder.ForwardAsync(context, TrafficRoute.Checkout, cancellationToken));

app.MapPost(
    "/graphql",
    (HttpContext context, DemoStoreForwarder forwarder, CancellationToken cancellationToken) =>
        forwarder.ForwardAsync(context, TrafficRoute.GraphQlCartAdd, cancellationToken));

app.MapPost(
    "/checkout/cart/add",
    (HttpContext context, DemoStoreForwarder forwarder, CancellationToken cancellationToken) =>
        forwarder.ForwardAsync(context, TrafficRoute.StorefrontCartAdd, cancellationToken));

app.MapPost(
    "/rest/V1/guest-carts/{cartId}/items",
    (HttpContext context, DemoStoreForwarder forwarder, IOptions<DropShieldOptions> options,
        CancellationToken cancellationToken) =>
        ForwardCommerceAsync(context, forwarder, options.Value, cancellationToken));

app.MapPost(
    "/rest/default/V1/guest-carts/{cartId}/items",
    (HttpContext context, DemoStoreForwarder forwarder, IOptions<DropShieldOptions> options,
        CancellationToken cancellationToken) =>
        ForwardCommerceAsync(context, forwarder, options.Value, cancellationToken));

app.MapPost(
    "/rest/V1/guest-carts/{cartId}/payment-information",
    (HttpContext context, DemoStoreForwarder forwarder, IOptions<DropShieldOptions> options,
        CancellationToken cancellationToken) =>
        ForwardCommerceAsync(context, forwarder, options.Value, cancellationToken));

app.MapPost(
    "/rest/default/V1/guest-carts/{cartId}/payment-information",
    (HttpContext context, DemoStoreForwarder forwarder, IOptions<DropShieldOptions> options,
        CancellationToken cancellationToken) =>
        ForwardCommerceAsync(context, forwarder, options.Value, cancellationToken));

app.MapGet("/internal/inventory", async (IInventoryReservationState state, IOptions<DropShieldOptions> options, IHostEnvironment environment, CancellationToken cancellationToken) =>
    !InternalDiagnosticsAreAvailable(options.Value, environment)
        ? Results.NotFound()
        : Results.Ok(await state.GetSnapshotAsync(options.Value.Admission.ProtectedProduct, cancellationToken)));

app.MapPost(
    "/api/action-proofs/cart",
    (HttpContext context, AdmissionProofAuthorizer authorizer, IActionTokenService tokenService,
        TrafficMetrics metrics, IOptions<DropShieldOptions> options, CancellationToken cancellationToken) =>
        IssueActionProofAsync(context, ActionKind.Cart, authorizer, tokenService, metrics, options.Value, cancellationToken));

app.MapPost(
    "/api/action-proofs/checkout",
    (HttpContext context, AdmissionProofAuthorizer authorizer, IActionTokenService tokenService,
        TrafficMetrics metrics, IOptions<DropShieldOptions> options, CancellationToken cancellationToken) =>
        IssueActionProofAsync(context, ActionKind.Checkout, authorizer, tokenService, metrics, options.Value, cancellationToken));

app.MapGet(
    "/internal/metrics",
    (TrafficMetrics metrics, IOptions<DropShieldOptions> options, IHostEnvironment environment) =>
    {
        if (!InternalDiagnosticsAreAvailable(options.Value, environment))
        {
            return Results.NotFound();
        }

        return Results.Ok(metrics.GetSnapshot());
    });

app.MapPost(
    "/internal/metrics/reset",
    (TrafficMetrics metrics, IOptions<DropShieldOptions> options, IHostEnvironment environment) =>
    {
        if (!InternalDiagnosticsAreAvailable(options.Value, environment))
        {
            return Results.NotFound();
        }

        metrics.Reset();
        return Results.NoContent();
    });

app.Run();

// Gates every /internal/* diagnostic route (metrics snapshot/reset, inventory snapshot).
// One flag by design: these are all local-development diagnostics with the same trust level,
// not independently sensitive surfaces that warrant separate configuration.
static bool InternalDiagnosticsAreAvailable(
    DropShieldOptions options,
    IHostEnvironment environment) =>
    options.InternalMetrics.Enabled &&
    (environment.IsDevelopment() || environment.IsEnvironment("Testing"));

static async Task ForwardCommerceAsync(
    HttpContext context,
    DemoStoreForwarder forwarder,
    DropShieldOptions options,
    CancellationToken cancellationToken)
{
    if (options.OriginMode != OriginMode.AdobeCommerce ||
        !CommerceRouteMatcher.TryMatch(context.Request, out var match))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await forwarder.ForwardAsync(context, match.TrafficRoute, cancellationToken);
}

static async Task<IResult> IssueActionProofAsync(
    HttpContext context,
    ActionKind action,
    AdmissionProofAuthorizer authorizer,
    IActionTokenService tokenService,
    TrafficMetrics metrics,
    DropShieldOptions options,
    CancellationToken cancellationToken)
{
    if (!options.ActionProofs.Enabled)
    {
        return Results.NotFound();
    }

    var admission = await authorizer.AuthorizeAsync(context, cancellationToken);
    if (admission.IsStateUnavailable)
    {
        return Results.Json(
            new GatewayErrorResponse(
                "state_unavailable",
                "Admission state is temporarily unavailable."),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

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
