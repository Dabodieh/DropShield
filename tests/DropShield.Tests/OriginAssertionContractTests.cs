using System.Text.Json;
using DropShield.Api.Traffic;

namespace DropShield.Tests;

/// <summary>
/// Guards against H3-style drift: the origin-assertion route templates DropShield.Api signs
/// into the "route" claim (<see cref="TrafficRouteClassifier.GetRouteTemplate"/>) and the
/// hardcoded ROUTE constants the PHP connector's plugins validate against
/// (integrations/adobe-commerce/DropShield_Connector/Plugin/*.php) must stay byte-identical.
/// Nothing else enforces this across languages, so both sides are checked here against the
/// single source of truth in contracts/origin-assertion-v1.json.
/// </summary>
public sealed class OriginAssertionContractTests
{
    [Fact]
    public void CartAndCheckoutRouteTemplates_MatchTheSharedContract()
    {
        var routes = LoadContractRoutes();

        Assert.Equal(routes["cart"], TrafficRouteClassifier.GetRouteTemplate(TrafficRoute.Cart));
        Assert.Equal(routes["checkout"], TrafficRouteClassifier.GetRouteTemplate(TrafficRoute.Checkout));
    }

    [Fact]
    public void GraphQlAndStorefrontRouteTemplates_MatchTheSharedContract()
    {
        var routes = LoadContractRoutes();

        Assert.Equal(
            routes["graphqlCartAdd"],
            TrafficRouteClassifier.GetRouteTemplate(TrafficRoute.GraphQlCartAdd));
        Assert.Equal(
            routes["storefrontCartAdd"],
            TrafficRouteClassifier.GetRouteTemplate(TrafficRoute.StorefrontCartAdd));
    }

    private static Dictionary<string, string> LoadContractRoutes()
    {
        var path = FindContractPath();
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

    private static string FindContractPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "contracts", "origin-assertion-v1.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate contracts/origin-assertion-v1.json from the test output directory.");
    }
}
