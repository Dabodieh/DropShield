namespace DropShield.Tests.Support;

internal static class RedisTestEnvironment
{
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("DROPSHIELD_REDIS_TEST_CONNECTION") ?? string.Empty;
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class RedisFactAttribute : FactAttribute
{
    public RedisFactAttribute()
    {
        if (!RedisTestEnvironment.IsConfigured)
        {
            Skip = "Redis integration tests require DROPSHIELD_REDIS_TEST_CONNECTION.";
        }
    }
}
