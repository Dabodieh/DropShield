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
