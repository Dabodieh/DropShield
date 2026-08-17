using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DropShield.Api.Options;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Admission;

public sealed partial class AdmissionSessionProvider(IOptions<DropShieldOptions> options)
{
    public const string CookieName = "DropShield.Session";
    private const string SessionItemKey = "DropShield.Admission.Session";

    private readonly AdmissionOptions _options = options.Value.Admission;

    public string GetOrCreate(HttpContext context)
    {
        if (context.Items.TryGetValue(SessionItemKey, out var existing) && existing is string session)
        {
            return session;
        }

        var sessionId = context.Request.Cookies.TryGetValue(CookieName, out var candidate) &&
                        candidate is not null &&
                        SessionIdPattern().IsMatch(candidate)
            ? candidate
            : Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        context.Response.Cookies.Append(
            CookieName,
            sessionId,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Path = "/",
                MaxAge = TimeSpan.FromSeconds(Math.Max(
                    _options.SessionTtlSeconds,
                    _options.WaitingTtlSeconds)),
            });

        context.Items[SessionItemKey] = sessionId;

        return sessionId;
    }

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex SessionIdPattern();
}
