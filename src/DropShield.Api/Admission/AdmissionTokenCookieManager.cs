using DropShield.Api.Options;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Admission;

public sealed class AdmissionTokenCookieManager(
    IOptions<DropShieldOptions> options,
    IHostEnvironment environment)
{
    private readonly DropShieldOptions _options = options.Value;

    public void Issue(HttpContext context, string token)
    {
        context.Response.Cookies.Append(
            _options.AdmissionTokens.CookieName,
            token,
            CreateOptions(context));
    }

    public void Delete(HttpContext context)
    {
        context.Response.Cookies.Delete(
            _options.AdmissionTokens.CookieName,
            new CookieOptions { Path = GetPath() });
    }

    private CookieOptions CreateOptions(HttpContext context) => new()
    {
        HttpOnly = true,
        Secure = CookieSecurityPolicy.ShouldUseSecureCookie(context, environment),
        SameSite = SameSiteMode.Lax,
        IsEssential = true,
        Path = GetPath(),
        MaxAge = TimeSpan.FromSeconds(_options.AdmissionTokens.LifetimeSeconds),
    };

    // Protected token consumers span /api, /graphql, and /checkout; root is their narrowest
    // common browser cookie path. The token remains HttpOnly and session-bound.
    private static string GetPath() => "/";
}
