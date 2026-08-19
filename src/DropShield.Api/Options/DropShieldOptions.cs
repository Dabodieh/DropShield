namespace DropShield.Api.Options;

public sealed class DropShieldOptions
{
    public const string SectionName = "DropShield";

    public bool Enabled { get; set; } = true;

    public TrafficStateProvider StateProvider { get; set; } = TrafficStateProvider.InMemory;

    public string OriginBaseUrl { get; set; } = "http://localhost:5058";

    public int OriginTimeoutSeconds { get; set; } = 10;

    public OriginMode OriginMode { get; set; } = OriginMode.DemoStore;

    public AdobeCommerceOptions AdobeCommerce { get; set; } = new();

    public List<string> ProtectedProducts { get; set; } = [];

    public SyntheticClientIdentityOptions SyntheticClientIdentity { get; set; } = new();

    public InternalMetricsOptions InternalMetrics { get; set; } = new();

    public InternalHashingOptions InternalHashing { get; set; } = new();

    public RedisStateOptions Redis { get; set; } = new();

    public AdmissionOptions Admission { get; set; } = new();

    public AdmissionTokenOptions AdmissionTokens { get; set; } = new();

    public ActionProofOptions ActionProofs { get; set; } = new();

    public InventoryReservationOptions InventoryReservation { get; set; } = new();

    public BehaviourScoringOptions BehaviourScoring { get; set; } = new();

    public OriginAssertionOptions OriginAssertions { get; set; } = new();

    public TrafficPoliciesOptions Policies { get; set; } = new();

    public EdgeTrustOptions EdgeTrust { get; set; } = new();
}

public enum TrafficStateProvider
{
    InMemory,
    Redis,
}

public enum OriginMode
{
    DemoStore,
    AdobeCommerce,
}

public sealed class AdobeCommerceOptions
{
    // Bounded PoC limit for inspected/protected REST JSON, GraphQL JSON, and storefront form
    // requests. This is not presented as a universal Magento request-size limit.
    public int MaximumProtectedRequestBodyBytes { get; set; } = 262_144;

    public ProtectionManifestOptions ProtectionManifest { get; set; } = new();
}

/// <summary>Settings for the authenticated, Commerce-owned protection manifest.</summary>
public sealed class ProtectionManifestOptions
{
    public bool Enabled { get; set; } = true;

    public string EndpointPath { get; set; } = "/rest/V1/dropshield/protection-manifest";

    // Intentionally empty: supply through a secret or environment configuration only.
    public string AccessToken { get; set; } = string.Empty;

    public int RefreshIntervalSeconds { get; set; } = 30;

    public int StaleAfterSeconds { get; set; } = 300;

    public int MaximumResponseBytes { get; set; } = 262_144;

    public int MaximumProducts { get; set; } = 10_000;
}

public sealed class SyntheticClientIdentityOptions
{
    public bool Enabled { get; set; }

    public string HeaderName { get; set; } = "X-DropShield-Test-Client";
}

public sealed class InternalMetricsOptions
{
    public bool Enabled { get; set; }
}

/// <summary>
/// Shared key for internal, non-bearer HMAC partitioning (reservation ownership, behavioural
/// actor identity). Distinct from the admission token, action proof, and origin assertion
/// signing keys so rotating any one of them does not silently reshuffle unrelated Redis
/// partitions.
/// </summary>
public sealed class InternalHashingOptions
{
    public string SigningKey { get; set; } = string.Empty;
}

public sealed class RedisStateOptions
{
    public string ConnectionString { get; set; } = "127.0.0.1:6379";

    public int Database { get; set; }

    public string KeyPrefix { get; set; } = "dropshield:v1";

    public string IdentityHashKey { get; set; } = string.Empty;

    public int ConnectTimeoutMilliseconds { get; set; } = 1_000;

    public int OperationTimeoutMilliseconds { get; set; } = 1_000;
}

public sealed class AdmissionOptions
{
    public bool Enabled { get; set; }

    /// <summary>
    /// DemoStore's local synthetic drop identifier. In AdobeCommerce mode the active drop ID
    /// comes exclusively from the authenticated protection manifest.
    /// </summary>
    public string DropId { get; set; } = "pokemon-etb";

    public int MaximumActiveSessions { get; set; } = 200;

    public int AdmissionBatchSize { get; set; } = 20;

    public int MaximumWaitingSessions { get; set; } = 2_000;

    public int SessionTtlSeconds { get; set; } = 300;

    public int WaitingTtlSeconds { get; set; } = 600;

    public int RetryAfterSeconds { get; set; } = 5;
}

public sealed class AdmissionTokenOptions
{
    public bool Enabled { get; set; }

    public string CookieName { get; set; } = "DropShield.Admission";

    public int LifetimeSeconds { get; set; } = 60;

    public string KeyId { get; set; } = "primary";

    public string SigningKey { get; set; } = string.Empty;
}

public sealed class ActionProofOptions
{
    public bool Enabled { get; set; }

    public string HeaderName { get; set; } = "X-DropShield-Action";

    public int LifetimeSeconds { get; set; } = 30;

    public int ReplayTtlMarginSeconds { get; set; } = 30;

    public int MaximumInMemoryMarkers { get; set; } = 100_000;

    public string KeyId { get; set; } = "primary";

    public string SigningKey { get; set; } = string.Empty;
}

public sealed class InventoryReservationOptions
{
    public bool Enabled { get; set; }

    public int InitialStock { get; set; } = 500;

    public int ReservationTtlSeconds { get; set; } = 300;

    public int MaximumInMemoryReservations { get; set; } = 100_000;
}

public sealed class BehaviourScoringOptions
{
    public bool Enabled { get; set; }

    public int ObservationWindowSeconds { get; set; } = 60;

    public int StateTtlSeconds { get; set; } = 120;

    public int MaximumInMemoryActors { get; set; } = 100_000;

    public int MaximumEventsPerActor { get; set; } = 128;

    public int RestrictionRetryAfterSeconds { get; set; } = 5;
}

public sealed class OriginAssertionOptions
{
    public bool Enabled { get; set; }

    public string HeaderName { get; set; } = "X-DropShield-Origin-Assertion";

    public int LifetimeSeconds { get; set; } = 20;

    public string KeyId { get; set; } = "primary";

    public string SigningKey { get; set; } = string.Empty;
}

/// <summary>
/// Optional shared-secret trust check for a fronting edge (for example the Fastly reference
/// adapter in integrations/fastly). Dedicated to edge authentication only — never shared with
/// admission, action proof, or origin assertion signing keys. When disabled, DropShield accepts
/// requests regardless of edge origin, matching the current direct-access PoC deployment model.
/// </summary>
public sealed class EdgeTrustOptions
{
    public bool Enabled { get; set; }

    public string HeaderName { get; set; } = "X-DropShield-Edge-Key";

    public string SharedKey { get; set; } = string.Empty;
}

public sealed class TrafficPoliciesOptions
{
    public StockPolicyOptions Stock { get; set; } = new();

    public ClientPolicyOptions Cart { get; set; } = new();

    public ClientPolicyOptions Checkout { get; set; } = new();
}

public class ClientPolicyOptions
{
    public bool Enabled { get; set; } = true;

    public int ClientPermitLimit { get; set; }

    public int ClientWindowSeconds { get; set; }
}

public sealed class StockPolicyOptions : ClientPolicyOptions
{
    public int AggregatePermitLimit { get; set; }

    public int AggregateWindowSeconds { get; set; }
}
