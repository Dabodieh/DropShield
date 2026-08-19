using DropShield.Demo;

namespace DropShield.Tests;

/// <summary>
/// The demo runner (demo/DropShield.Demo) sends real HTTP requests and has no remote-override
/// flag, so this guard is the only thing standing between a misconfigured base URL and traffic
/// leaving localhost. Covers the same allowed-host set as load-tests and DropShield.Api's own
/// OriginBaseUrl validation.
/// </summary>
public sealed class DemoLocalhostGuardTests
{
    [Theory]
    [InlineData("http://localhost:5257")]
    [InlineData("https://127.0.0.1:5257")]
    [InlineData("http://[::1]:5257")]
    [InlineData("http://host.docker.internal:5257")]
    [InlineData("http://localhost")]
    public void TryValidate_AcceptsApprovedLocalTargets(string url)
    {
        var result = LocalhostGuard.TryValidate(url, out var validated, out var error);

        Assert.True(result);
        Assert.Null(error);
        Assert.Equal(new Uri(url).Host, validated.Host);
    }

    [Theory]
    [InlineData("http://example.org")]
    [InlineData("https://example.com")]
    [InlineData("http://8.8.8.8")]
    [InlineData("http://evil.localhost.attacker.com")]
    public void TryValidate_RejectsRemoteHosts(string url)
    {
        var result = LocalhostGuard.TryValidate(url, out _, out var error);

        Assert.False(result);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("ftp://localhost:5257")]
    [InlineData("not-a-url")]
    [InlineData("http://localhost:5257/api/products?x=1")]
    [InlineData("http://user:pass@localhost:5257")]
    public void TryValidate_RejectsMalformedOrDecoratedUrls(string url)
    {
        var result = LocalhostGuard.TryValidate(url, out _, out var error);

        Assert.False(result);
        Assert.NotNull(error);
    }
}
