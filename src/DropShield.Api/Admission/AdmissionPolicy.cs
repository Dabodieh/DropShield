using DropShield.Api.Options;
using DropShield.Api.Traffic;

namespace DropShield.Api.Admission;

public static class AdmissionPolicy
{
    public static bool AppliesTo(HttpRequest request, DropShieldOptions options) =>
        options.Enabled &&
        options.Admission.Enabled &&
        TrafficRouteClassifier.IsProtectedStockRequest(
            request,
            options.ProtectedProducts) &&
        string.Equals(
            TrafficRouteClassifier.GetProductId(request),
            options.Admission.ProtectedProduct,
            StringComparison.OrdinalIgnoreCase);
}
