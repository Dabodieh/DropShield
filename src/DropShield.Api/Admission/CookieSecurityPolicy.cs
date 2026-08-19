namespace DropShield.Api.Admission;

internal static class CookieSecurityPolicy
{
    public static bool ShouldUseSecureCookie(HttpContext context, IHostEnvironment environment) =>
        context.Request.IsHttps ||
        (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"));
}
