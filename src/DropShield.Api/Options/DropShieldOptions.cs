namespace DropShield.Api.Options;

public sealed class DropShieldOptions
{
    public const string SectionName = "DropShield";

    public bool Enabled { get; set; } = true;

    public string OriginBaseUrl { get; set; } = "http://localhost:5058";

    public int OriginTimeoutSeconds { get; set; } = 10;

    public List<string> ProtectedProducts { get; set; } = [];

    public SyntheticClientIdentityOptions SyntheticClientIdentity { get; set; } = new();

    public InternalMetricsOptions InternalMetrics { get; set; } = new();

    public TrafficPoliciesOptions Policies { get; set; } = new();
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
