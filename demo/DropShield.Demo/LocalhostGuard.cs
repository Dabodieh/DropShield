namespace DropShield.Demo;

/// <summary>
/// The demo runner sends real HTTP requests, so it must be structurally incapable of targeting
/// a retailer or any other remote host, with no override flag. Mirrors the same allowed-host
/// list used by load-tests/lib and DropShield.Api's own OriginBaseUrl validation.
/// </summary>
public static class LocalhostGuard
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost",
        "127.0.0.1",
        "::1",
        "host.docker.internal",
    };

    public static bool TryValidate(string baseUrl, out Uri validated, out string? error)
    {
        validated = null!;
        error = null;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            error = $"'{baseUrl}' is not an absolute URL.";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            error = $"'{baseUrl}' must use http or https.";
            return false;
        }

        var host = uri.HostNameType == UriHostNameType.IPv6 ? uri.Host.Trim('[', ']') : uri.Host;
        if (!AllowedHosts.Contains(host))
        {
            error = $"'{host}' is not an allowed demo target. Allowed: " +
                     string.Join(", ", AllowedHosts);
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            error = $"'{baseUrl}' must contain only a scheme, allowed host, and optional port.";
            return false;
        }

        validated = uri;
        return true;
    }
}
