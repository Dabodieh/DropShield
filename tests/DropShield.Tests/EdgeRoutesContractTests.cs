using System.Text.Json;
using DropShield.Api.Traffic;

namespace DropShield.Tests;

/// <summary>
/// Guards against C# route / Fastly route drift: integrations/fastly/edge-routes.json lists the
/// route templates the Fastly reference adapter routes to DropShield.Api. Checked here against
/// TrafficRouteClassifier so the two cannot silently diverge; VCL itself never parses this file
/// at runtime (see integrations/fastly/README.md).
/// </summary>
public sealed class EdgeRoutesContractTests
{
    [Fact]
    public void EdgeRoutes_CoverEveryKnownTrafficRoute()
    {
        var edgeRoutes = LoadEdgeRoutes();

        foreach (var route in Enum.GetValues<TrafficRoute>())
        {
            if (route == TrafficRoute.Unknown)
            {
                continue;
            }

            var template = TrafficRouteClassifier.GetRouteTemplate(route);
            Assert.Contains(template, edgeRoutes);
        }
    }

    [Fact]
    public void RecvRouteSnippet_MatchesTheDocumentedPrefixesAndDenials()
    {
        var vcl = File.ReadAllText(FindRepoFile("integrations", "fastly", "vcl", "recv-route.vcl"));

        Assert.Contains("req.url ~ \"^/api/\"", vcl);
        Assert.Contains("req.url ~ \"^/graphql$\"", vcl);
        Assert.Contains("req.url ~ \"^/checkout/cart/add$\"", vcl);
        Assert.Contains("req.url ~ \"^/health$\"", vcl);
        Assert.Contains("req.url ~ \"^/internal/\"", vcl);
    }

    [Fact]
    public void EdgeRoutes_MatchTheOriginAssertionContractForProtectedMutations()
    {
        var edgeRoutes = LoadEdgeRoutes();
        var assertionRoutes = LoadOriginAssertionRoutes();

        Assert.Contains(assertionRoutes["cart"], edgeRoutes);
        Assert.Contains(assertionRoutes["checkout"], edgeRoutes);
        Assert.Contains(assertionRoutes["graphqlCartAdd"], edgeRoutes);
        Assert.Contains(assertionRoutes["storefrontCartAdd"], edgeRoutes);
    }

    private static HashSet<string> LoadEdgeRoutes()
    {
        var path = FindRepoFile("integrations", "fastly", "edge-routes.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var routes = document.RootElement.GetProperty("dropshieldRoutes");
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in routes.EnumerateArray())
        {
            result.Add(route.GetString()!);
        }

        return result;
    }

    private static Dictionary<string, string> LoadOriginAssertionRoutes()
    {
        var path = FindRepoFile("contracts", "origin-assertion-v1.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var routes = document.RootElement.GetProperty("routes");
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cart"] = routes.GetProperty("cart").GetString()!,
            ["checkout"] = routes.GetProperty("checkout").GetString()!,
            ["graphqlCartAdd"] = routes.GetProperty("graphqlCartAdd").GetString()!,
            ["storefrontCartAdd"] = routes.GetProperty("storefrontCartAdd").GetString()!,
        };
    }

    private static string FindRepoFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeSegments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate {Path.Combine(relativeSegments)} from the test output directory.");
    }
}
