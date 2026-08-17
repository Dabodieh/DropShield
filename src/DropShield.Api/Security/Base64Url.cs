namespace DropShield.Api.Security;

internal static class Base64Url
{
    public static string Encode(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    public static bool TryDecode(string value, out byte[] decoded)
    {
        decoded = [];
        if (string.IsNullOrEmpty(value) || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            return false;
        }

        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 = (base64.Length % 4) switch
            {
                0 => base64,
                2 => base64 + "==",
                3 => base64 + "=",
                _ => string.Empty,
            };
            if (base64.Length == 0)
            {
                return false;
            }

            decoded = Convert.FromBase64String(base64);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
