using DropShield.Api.Options;
using DropShield.Api.Catalog;
using DropShield.Api.Traffic;

namespace DropShield.Api.Admission;

public static class AdmissionPolicy
{
    public static bool AppliesTo(HttpRequest request, DropShieldOptions options, IProtectedDropCatalog catalog) =>
        options.Enabled &&
        options.Admission.Enabled &&
        TrafficRouteClassifier.Classify(request) == TrafficRoute.Stock &&
        TrafficRouteClassifier.GetProductId(request) is { } productId &&
        catalog.TryResolveSku(productId, out _);
}
