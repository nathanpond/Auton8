using System.Text;
using System.Text.RegularExpressions;

namespace AutoNate.Web.Tests.Infrastructure;

internal sealed record PortBinding(
    string File,
    string Service,
    string Entry,
    int Line,
    string? HostIp,
    string? ExceptionReason)
{
    public bool IsLoopback =>
        HostIp is "127.0.0.1" or "::1" or "localhost";

    public bool HasValidException =>
        !string.IsNullOrWhiteSpace(ExceptionReason);
}

/// <summary>
/// Finds published ports in the compose files this repository ships, together
/// with any <c># loopback-exception:</c> marker that applies to them.
/// </summary>
/// <remarks>
/// Hand-rolled rather than built on a YAML package, deliberately. The rule this
/// supports turns on a <b>comment</b> — an exception is only valid when a
/// written reason sits next to the port it excuses — and YAML parsers discard
/// comments during load. A parser would handle the easy half of the problem and
/// lose the half that makes the exception mechanism auditable.
///
/// The scanner therefore understands only the slice of compose syntax that
/// matters here: a top-level <c>services:</c> mapping, service keys beneath it,
/// and each service's <c>ports:</c> sequence in either the short string form or
/// the long object form.
/// </remarks>
internal static class ComposeFileScanner
{
    private const string ExceptionMarker = "# loopback-exception:";

    // Environment interpolation may contain colons that are not port
    // separators — `${AUTONATE_POSTGRES_PORT:-5432}` is in the real compose
    // file and splitting naively on ':' mis-parses it as a host IP. Mask these
    // spans before splitting, then count what is left.
    private static readonly Regex Interpolation = new(@"\$\{[^}]*\}", RegexOptions.Compiled);

    /// <summary>
    /// Every compose file the repository ships, excluding build output and the
    /// scanner's own fixtures.
    /// </summary>
    public static IReadOnlyList<string> DiscoverComposeFiles()
    {
        var root = RepoRoot.Path;
        var candidates = Directory
            .EnumerateFiles(root, "*.yml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories))
            .Where(p => !IsExcludedPath(root, p));

        return candidates.Where(LooksLikeComposeFile).OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    private static bool IsExcludedPath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path)
            .Replace(Path.DirectorySeparatorChar, '/');

        return relative.Contains("/node_modules/", StringComparison.Ordinal)
            || relative.StartsWith("node_modules/", StringComparison.Ordinal)
            || relative.Contains("/bin/", StringComparison.Ordinal)
            || relative.Contains("/obj/", StringComparison.Ordinal)
            || relative.Contains("/target/", StringComparison.Ordinal)
            || relative.Contains("/.git/", StringComparison.Ordinal)
            // The scanner's own fixtures are deliberately non-compliant.
            || relative.Contains("ComposeFixtures/", StringComparison.Ordinal);
    }

    /// <summary>
    /// A compose file has a <b>top-level</b> <c>services:</c> key. The nesting
    /// matters: `.github/workflows/ci.yml` declares `services:` under a job and
    /// is not a compose file.
    /// </summary>
    private static bool LooksLikeComposeFile(string path)
    {
        foreach (var raw in File.ReadLines(path))
        {
            if (raw.Length == 0 || char.IsWhiteSpace(raw[0]))
            {
                continue;
            }

            if (raw.StartsWith("services:", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<PortBinding> ScanFile(string path)
    {
        var lines = File.ReadAllLines(path);
        var relative = Path.GetRelativePath(RepoRoot.Path, path)
            .Replace(Path.DirectorySeparatorChar, '/');

        var results = new List<PortBinding>();

        var inServices = false;
        var servicesIndent = -1;
        string? currentService = null;
        var serviceIndent = -1;

        var inPorts = false;
        var portsIndent = -1;
        string? portsBlockException = null;

        // Long-object-form accumulation.
        var objectEntryLines = new List<string>();
        var objectEntryStart = -1;
        string? objectEntryException = null;

        string? pendingException = null;

        void FlushObjectEntry()
        {
            if (objectEntryLines.Count == 0)
            {
                return;
            }

            var hostIp = objectEntryLines
                .Select(l => Regex.Match(l, @"^\s*-?\s*host_ip\s*:\s*(?<v>\S+)\s*$"))
                .Where(m => m.Success)
                .Select(m => m.Groups["v"].Value.Trim('"', '\''))
                .FirstOrDefault();

            results.Add(new PortBinding(
                relative,
                currentService ?? "<unknown>",
                string.Join(" ", objectEntryLines.Select(l => l.Trim())),
                objectEntryStart + 1,
                hostIp,
                objectEntryException ?? portsBlockException));

            objectEntryLines.Clear();
            objectEntryStart = -1;
            objectEntryException = null;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var trimmed = raw.Trim();
            var indent = raw.Length - raw.TrimStart().Length;

            if (trimmed.StartsWith(ExceptionMarker, StringComparison.Ordinal))
            {
                var reason = trimmed[ExceptionMarker.Length..].Trim();
                // Consecutive comment lines continue the reason so a long
                // justification can be wrapped the way the rest of this
                // repository wraps its comments.
                pendingException = string.IsNullOrEmpty(pendingException)
                    ? reason
                    : pendingException + " " + reason;
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                if (pendingException is not null)
                {
                    pendingException += " " + trimmed.TrimStart('#').Trim();
                }

                continue;
            }

            if (trimmed.Length == 0)
            {
                // A blank line detaches a pending marker from whatever follows.
                pendingException = null;
                continue;
            }

            if (!inServices)
            {
                if (indent == 0 && trimmed.StartsWith("services:", StringComparison.Ordinal))
                {
                    inServices = true;
                    servicesIndent = indent;
                }

                pendingException = null;
                continue;
            }

            if (indent <= servicesIndent)
            {
                // Left the services mapping entirely.
                FlushObjectEntry();
                inServices = false;
                currentService = null;
                inPorts = false;
                pendingException = null;
                continue;
            }

            // A new service key: any mapping key one level under `services:`.
            if (currentService is null || indent <= serviceIndent)
            {
                if (Regex.IsMatch(trimmed, @"^[A-Za-z0-9_.-]+:\s*$"))
                {
                    FlushObjectEntry();
                    currentService = trimmed[..^1];
                    serviceIndent = indent;
                    inPorts = false;
                    portsBlockException = null;
                    pendingException = null;
                    continue;
                }
            }

            if (inPorts && indent <= portsIndent)
            {
                FlushObjectEntry();
                inPorts = false;
                portsBlockException = null;
            }

            if (trimmed.StartsWith("ports:", StringComparison.Ordinal))
            {
                FlushObjectEntry();
                inPorts = true;
                portsIndent = indent;
                portsBlockException = pendingException;
                pendingException = null;
                continue;
            }

            if (!inPorts)
            {
                pendingException = null;
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed == "-")
            {
                FlushObjectEntry();

                var value = trimmed.Length > 1 ? trimmed[1..].Trim() : string.Empty;

                // Long object form starts with a key: `- target: 80`.
                if (Regex.IsMatch(value, @"^[a-z_]+\s*:"))
                {
                    objectEntryLines.Add(value);
                    objectEntryStart = i;
                    objectEntryException = pendingException;
                    pendingException = null;
                    continue;
                }

                results.Add(ParseShortForm(relative, currentService, value, i + 1,
                    pendingException ?? portsBlockException));
                pendingException = null;
                continue;
            }

            if (objectEntryStart >= 0)
            {
                objectEntryLines.Add(trimmed);
            }

            pendingException = null;
        }

        FlushObjectEntry();
        return results;
    }

    private static PortBinding ParseShortForm(
        string file, string? service, string value, int line, string? exception)
    {
        var entry = value.Trim().Trim('"', '\'');

        // Mask interpolation and bracketed IPv6 so the remaining colons are
        // only the port separators compose actually defines.
        var masked = Interpolation.Replace(entry, m => new string('_', m.Length));
        var ipv6 = Regex.Match(masked, @"^\[(?<ip>[^\]]*)\]:(?<rest>.*)$");

        string? hostIp = null;
        string remainder;

        if (ipv6.Success)
        {
            hostIp = ipv6.Groups["ip"].Value;
            remainder = ipv6.Groups["rest"].Value;
        }
        else
        {
            remainder = masked;
        }

        var parts = remainder.Split(':');

        if (hostIp is null && parts.Length >= 3)
        {
            // host_ip:published:target
            var maskedLength = parts[0].Length;
            hostIp = entry[..maskedLength];
        }

        return new PortBinding(file, service ?? "<unknown>", entry, line, hostIp, exception);
    }
}
