using System.Text.RegularExpressions;

namespace AutoNate.Web.Services.Records;

public static class RecordTypeShortCode
{
    // 2-8 uppercase letters (+ optional trailing digits). Admins pick it;
    // we prefer letters-only for nice keys like ACC-142.
    private static readonly Regex Valid = new(
        @"^[A-Z][A-Z0-9]{1,7}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Normalize(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        return raw.Trim().ToUpperInvariant();
    }

    public static bool IsValid(string candidate) => Valid.IsMatch(candidate);
}

public static class RecordFieldKey
{
    // Machine-name: lowercase snake_case, start with a letter, 1-64 chars.
    private static readonly Regex Valid = new(
        @"^[a-z][a-z0-9_]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsValid(string candidate) => Valid.IsMatch(candidate);

    public static string Normalize(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        return raw.Trim().ToLowerInvariant();
    }
}
