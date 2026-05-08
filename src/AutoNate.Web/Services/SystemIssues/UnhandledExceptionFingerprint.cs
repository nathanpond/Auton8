namespace AutoNate.Web.Services.SystemIssues;

// Stable fingerprint for an unhandled exception, used as the dedup key in
// system_issues. Same exception type from the same call site collapses to a
// single row whose occurrence_count climbs — without this, every request that
// blows up would create a fresh row and bury detector-quality issues.
//
// Line numbers and source paths are deliberately stripped: a recompile that
// shifts a line by one would otherwise mint a new fingerprint and break the
// dedup. Method identity is what we care about.
internal static class UnhandledExceptionFingerprint
{
    public static string Compute(string scope, Exception exception)
    {
        var typeName = exception.GetType().FullName ?? "System.Exception";
        var topFrame = ExtractTopFrame(exception.StackTrace);
        return string.IsNullOrEmpty(topFrame)
            ? $"unhandled:{scope}:{typeName}"
            : $"unhandled:{scope}:{typeName}:{topFrame}";
    }

    public static string? ExtractTopFrame(string? stackTrace)
    {
        if (string.IsNullOrWhiteSpace(stackTrace)) return null;
        using var reader = new StringReader(stackTrace);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("at ", StringComparison.Ordinal)) continue;
            // "at Namespace.Type.Method(args) in /path/to/file.cs:line N"
            // → "Namespace.Type.Method(args)"
            var inIdx = trimmed.IndexOf(" in ", StringComparison.Ordinal);
            return (inIdx < 0 ? trimmed[3..] : trimmed[3..inIdx]).Trim();
        }
        return null;
    }
}
