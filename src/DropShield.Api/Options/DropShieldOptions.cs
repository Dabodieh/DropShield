namespace DropShield.Api.Options;

public sealed class DropShieldOptions
{
    public const string SectionName = "DropShield";

    public bool Enabled { get; set; } = true;

    public TrafficStateProvider StateProvider { get; set; } = TrafficStateProvider.InMemory;

    public string OriginBaseUrl { get; set; } = "http://localhost:5058";

    public int OriginTimeoutSeconds { get; set; } = 10;

    public List<string> ProtectedProducts { get; set; } = [];

    public SyntheticClientIdentityOptions SyntheticClientIdentity { get; set; } = new();

    public InternalMetricsOptions InternalMetrics { get; set; } = new();

    public RedisStateOptions Redis { get; set; } = new();

    public AdmissionOptions Admission { get; set; } = new();

    public AdmissionTokenOptions AdmissionTokens { get; set; } = new();

    public ActionProofOptions ActionProofs { get; set; } = new();

    public TrafficPoliciesOptions Policies { get; set; } = new();
}

public enum TrafficStateProvider
{
    InMemory,
    Redis,
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

    public string ProtectedProduct { get; set; } = "pokemon-etb";

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
