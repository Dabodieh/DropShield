using DropShield.Api;
using DropShield.Api.Admission;
using DropShield.Api.Actions;
using DropShield.Api.Origin;
using DropShield.Api.State;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DropShield.Tests.Support;

internal sealed class DropShieldApiFactory(
    IReadOnlyDictionary<string, string?>? overrides = null,
    string environment = "Testing",
    IDistributedTrafficState? distributedState = null,
    IAdmissionState? admissionState = null,
    TimeProvider? timeProvider = null,
    IReplayState? replayState = null)
    : WebApplicationFactory<ApiAssemblyMarker>
{
    private readonly IReadOnlyDictionary<string, string?> _overrides = overrides ??
        new Dictionary<string, string?>();

    public RecordingDemoStoreClient Origin { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(DefaultSettings());
            configuration.AddInMemoryCollection(_overrides);
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDemoStoreClient>();
            services.AddSingleton<IDemoStoreClient>(Origin);
            if (distributedState is not null)
            {
                services.RemoveAll<IDistributedTrafficState>();
                services.AddSingleton(distributedState);
            }

            if (admissionState is not null)
            {
                services.RemoveAll<IAdmissionState>();
                services.AddSingleton(admissionState);
            }

            if (timeProvider is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(timeProvider);
            }

            if (replayState is not null)
            {
                services.RemoveAll<IReplayState>();
                services.AddSingleton(replayState);
            }
        });
    }

    private static Dictionary<string, string?> DefaultSettings() => new()
    {
        ["DropShield:Enabled"] = "true",
        ["DropShield:StateProvider"] = "InMemory",
        ["DropShield:OriginBaseUrl"] = "http://localhost:5058",
        ["DropShield:OriginTimeoutSeconds"] = "10",
        ["DropShield:ProtectedProducts:0"] = "pokemon-etb",
        ["DropShield:SyntheticClientIdentity:Enabled"] = "true",
        ["DropShield:SyntheticClientIdentity:HeaderName"] = "X-DropShield-Test-Client",
        ["DropShield:InternalMetrics:Enabled"] = "true",
        ["DropShield:Admission:Enabled"] = "false",
        ["DropShield:Admission:ProtectedProduct"] = "pokemon-etb",
        ["DropShield:Admission:MaximumActiveSessions"] = "200",
        ["DropShield:Admission:AdmissionBatchSize"] = "20",
        ["DropShield:Admission:MaximumWaitingSessions"] = "2000",
        ["DropShield:Admission:SessionTtlSeconds"] = "300",
        ["DropShield:Admission:WaitingTtlSeconds"] = "600",
        ["DropShield:Admission:RetryAfterSeconds"] = "5",
        ["DropShield:AdmissionTokens:Enabled"] = "false",
        ["DropShield:AdmissionTokens:CookieName"] = "DropShield.Admission",
        ["DropShield:AdmissionTokens:LifetimeSeconds"] = "60",
        ["DropShield:AdmissionTokens:KeyId"] = "primary",
        ["DropShield:AdmissionTokens:SigningKey"] = "",
        ["DropShield:ActionProofs:Enabled"] = "false",
        ["DropShield:ActionProofs:HeaderName"] = "X-DropShield-Action",
        ["DropShield:ActionProofs:LifetimeSeconds"] = "30",
        ["DropShield:ActionProofs:ReplayTtlMarginSeconds"] = "30",
        ["DropShield:ActionProofs:MaximumInMemoryMarkers"] = "100000",
        ["DropShield:Policies:Stock:Enabled"] = "true",
        ["DropShield:Policies:Stock:ClientPermitLimit"] = "2",
        ["DropShield:Policies:Stock:ClientWindowSeconds"] = "60",
        ["DropShield:Policies:Stock:AggregatePermitLimit"] = "100",
        ["DropShield:Policies:Stock:AggregateWindowSeconds"] = "60",
        ["DropShield:Policies:Cart:Enabled"] = "true",
        ["DropShield:Policies:Cart:ClientPermitLimit"] = "2",
        ["DropShield:Policies:Cart:ClientWindowSeconds"] = "60",
        ["DropShield:Policies:Checkout:Enabled"] = "true",
        ["DropShield:Policies:Checkout:ClientPermitLimit"] = "1",
        ["DropShield:Policies:Checkout:ClientWindowSeconds"] = "60",
    };
}
