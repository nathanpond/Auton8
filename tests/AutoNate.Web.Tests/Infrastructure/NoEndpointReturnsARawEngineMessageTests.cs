using System.Text.RegularExpressions;
using Xunit;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// No endpoint hands a Flowable exception's message to a caller (#350).
/// </summary>
/// <remarks>
/// <para>
/// <c>FlowableRequestException.Message</c> is built by
/// <c>FlowableClient.EnsureSuccessAsync</c> as
/// <c>$"Flowable could not {operation}. HTTP {code} {reason}. {rawResponseBody}"</c>
/// — the engine's <b>entire HTTP body</b>. That body has been observed carrying a
/// JDBC URL with its password, internal hostnames and container ids, absolute
/// filesystem paths, Java stack frames, and an <c>Extra info</c> tail with
/// process-definition ids.
/// </para>
/// <para>
/// <b>Why a scan and not another unit test.</b> Three consecutive rounds fixed
/// this defect — #334, #339, #344 — and all three fixed it on the <em>publish
/// route</em>, because that is where the issue in front of them said it was. Four
/// routes in <c>ExecutionEndpoints</c> were handing back the whole body the entire
/// time, to anyone who can interact with a running process instance: a far wider
/// audience than "can edit a workflow". Two were not even gated on
/// <c>IsCallerError</c>, so a Flowable 5xx went straight through.
/// </para>
/// <para>
/// A unit test proves one describer is correct. It cannot notice a route that
/// never calls it.
/// </para>
/// <para>
/// Scoped to <c>FlowableRequestException</c> catch blocks deliberately. A first
/// version scanned for any <c>ex.Message</c> in a response and flagged eight
/// unrelated sites — AQL parse errors, code-transformer failures, projection
/// errors. For those the message IS the useful thing and is written by our own
/// code, not by a system that can see the database URL. Flagging them would have
/// made this guard noise someone edits away.
/// </para>
/// </remarks>
public sealed class NoEndpointReturnsARawEngineMessageTests
{
    private static readonly Regex FlowableCatch = new(
        @"catch\s*\(\s*FlowableRequestException\s+(?<name>\w+)", RegexOptions.Compiled);

    /// <summary>
    /// #626. A method that TAKES a FlowableRequestException is the same hazard as
    /// a catch block that holds one.
    /// </summary>
    /// <remarks>
    /// The scan used to look only at catch blocks, and #222 walked straight past
    /// it: the catch forwarded the exception to a helper, and the helper put
    /// <c>.Message</c> into a persisted row that a DIFFERENT endpoint then served.
    /// A rule about what catches do cannot see a rule about what helpers do, so
    /// the scope is now "anywhere the exception is in hand".
    /// </remarks>
    private static readonly Regex FlowableParameter = new(
        @"FlowableRequestException\s+(?<name>\w+)\s*[,)]", RegexOptions.Compiled);

    /// <summary>Every scope holding a FlowableRequestException, with its body.</summary>
    private static IEnumerable<(string Variable, string Body)> FlowableScopes(string text)
    {
        foreach (var scope in FlowableCatchBlocks(text)) yield return scope;

        foreach (Match m in FlowableParameter.Matches(text))
        {
            // Skip the catch form, already yielded above.
            var before = text[Math.Max(0, m.Index - 40)..m.Index];
            if (before.Contains("catch", StringComparison.Ordinal)) continue;

            var open = text.IndexOf('{', m.Index + m.Length);
            if (open < 0) continue;

            var depth = 0;
            var i = open;
            for (; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0) break;
            }

            yield return (m.Groups["name"].Value, text[open..Math.Min(i + 1, text.Length)]);
        }
    }

    /// <summary>Every FlowableRequestException catch block, with its body.</summary>
    private static IEnumerable<(string Variable, string Body)> FlowableCatchBlocks(string text)
    {
        foreach (Match m in FlowableCatch.Matches(text))
        {
            var open = text.IndexOf('{', m.Index + m.Length);
            if (open < 0) continue;

            var depth = 0;
            var i = open;
            for (; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0) break;
            }

            yield return (m.Groups["name"].Value, text[open..Math.Min(i + 1, text.Length)]);
        }
    }

    /// <summary>
    /// The message reaching a RESPONSE. Logging it is required, not forbidden;
    /// holding it in a local is fine.
    /// </summary>
    private static IReadOnlyList<string> Offences(string source)
    {
        var found = new List<string>();

        foreach (var (variable, body) in FlowableScopes(source))
        {
            var lines = body.Split('\n');
            for (var n = 0; n < lines.Length; n++)
            {
                var line = lines[n];

                // Comments are prose, not code. Without this the guard flags the
                // comment EXPLAINING the fix -- measured, on #626's own fix, which
                // names `exception.Message` in order to say not to use it. The
                // sibling guard MultiInstanceReaderAgreementTests skips comments
                // for the same reason.
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;

                if (!line.Contains($"{variable}.Message", StringComparison.Ordinal)) continue;
                if (line.Contains("Log", StringComparison.Ordinal)) continue;
                if (line.Contains("EngineRefusal", StringComparison.Ordinal)) continue;

                // SIX lines back, not two. Measured: the first version of this
                // widening looked two lines back and did NOT catch #626, because
                // the message sat third in an argument list and the
                // `RecordSynchronousFailureAsync(` that names the sink was four
                // lines above it. A widening that does not fire is worse than
                // none -- it reads as coverage.
                var window = string.Join(" ", lines[Math.Max(0, n - 6)..(n + 1)]);
                if (window.Contains("Results.", StringComparison.Ordinal)
                    || window.Contains("message =", StringComparison.Ordinal)
                    || window.Contains("errors =", StringComparison.Ordinal)
                    || window.Contains("new[]", StringComparison.Ordinal)
                    // #626. PERSISTING it is the same leak one hop later: the row
                    // is served by GET /api/executions/{id}/history to anyone with
                    // WorkflowExecution:View. A response is not the only way out.
                    || window.Contains("Record", StringComparison.Ordinal)
                    || window.Contains("ErrorMessage", StringComparison.Ordinal))
                {
                    found.Add(line.Trim());
                }
            }
        }

        return found;
    }

    [Fact]
    public void No_endpoint_puts_a_flowable_exception_message_into_a_response()
    {
        var endpoints = Path.Combine(RepoRoot.Path, "src", "AutoNate.Web", "Endpoints");

        var offenders = Directory
            .EnumerateFiles(endpoints, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => Offences(File.ReadAllText(path))
                .Select(o => $"{Path.GetRelativePath(RepoRoot.Path, path)}: {o}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These endpoints put a FlowableRequestException's message into a response "
            + "body. That message is the engine's entire HTTP body — JDBC URLs with "
            + "passwords, internal hostnames, absolute paths, Java stack frames.\n  "
            + string.Join("\n  ", offenders)
            + "\n\nRoute it through EngineRefusal.Describe and log the raw text at "
            + "Warning, as the publish and execution routes do (#350).");
    }

    /// <summary>
    /// Every refusal that reaches a caller also reaches the log (#349, #350).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole design discards what the engine said. The log is the <b>only</b>
    /// place that text survives, so it is the compensating control for the entire
    /// approach — and deleting the <c>LogWarning</c> call from the publish route
    /// left 56/56 green.
    /// </para>
    /// <para>
    /// Scanned rather than unit-tested for the same reason as the rule above: a
    /// test proves one site logs; it cannot notice a sixth site that does not.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_flowable_catch_that_answers_a_caller_also_logs()
    {
        var endpoints = Path.Combine(RepoRoot.Path, "src", "AutoNate.Web", "Endpoints");

        var silent = new List<string>();

        foreach (var path in Directory.EnumerateFiles(endpoints, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(RepoRoot.Path, path);
            foreach (var (variable, body) in FlowableCatchBlocks(File.ReadAllText(path)))
            {
                // Only blocks that actually answer the caller.
                if (!body.Contains("Results.", StringComparison.Ordinal)) continue;

                var logs = body.Contains("Log", StringComparison.Ordinal)
                           && body.Contains(variable, StringComparison.Ordinal);

                if (!logs)
                {
                    silent.Add($"{relative}: catch ({variable}) answers a caller without logging");
                }
            }
        }

        Assert.True(
            silent.Count == 0,
            "These catch blocks tell a caller the engine refused something and do not "
            + "record what it actually said. The sanitised sentence deliberately "
            + "discards the engine's text, so the log is the only place it survives — "
            + "without it, a real engine fault is undiagnosable.\n  "
            + string.Join("\n  ", silent));
    }

    /// <summary>The scan finds the catch blocks at all (#350).</summary>
    /// <remarks>
    /// A block-finder that matched nothing would report a clean tree forever — the
    /// failure this milestone has found more often than any other.
    /// </remarks>
    [Fact]
    public void The_scan_actually_finds_the_flowable_catch_blocks()
    {
        var endpoints = Path.Combine(RepoRoot.Path, "src", "AutoNate.Web", "Endpoints");
        var blocks = Directory
            .EnumerateFiles(endpoints, "*.cs", SearchOption.AllDirectories)
            .SelectMany(p => FlowableCatchBlocks(File.ReadAllText(p)))
            .ToList();

        Assert.True(blocks.Count >= 5, $"only found {blocks.Count} FlowableRequestException catch blocks");
        Assert.All(blocks, b => Assert.True(b.Body.Length > 40, "a catch body came back empty"));
    }

    /// <summary>The scan can still see what it forbids (#350).</summary>
    [Fact]
    public void The_scan_flags_the_shape_that_shipped_and_not_the_one_that_replaced_it()
    {
        const string Shipped = """
            catch (FlowableRequestException exception) when (exception.IsCallerError)
            {
                return Results.Json(
                    new { message = exception.Message },
                    statusCode: (int)exception.StatusCode);
            }
            """;

        const string Fixed = """
            catch (FlowableRequestException exception) when (exception.IsCallerError)
            {
                logger.LogWarning(exception, "refused: {D}", described);
                return Results.Json(
                    new { message = EngineRefusal.Describe(exception, "this request") },
                    statusCode: (int)exception.StatusCode);
            }
            """;

        Assert.NotEmpty(Offences(Shipped));
        Assert.Empty(Offences(Fixed));
    }

    /// <summary>
    /// #665. The branch #626 widened the scan to reach -- the exception handed
    /// to a HELPER rather than caught inline -- with a synthetic offence, so the
    /// widening is proven rather than attested. Until #626 the scan read catch
    /// blocks only, and the raw body reached `/history` through exactly this
    /// shape.
    /// </summary>
    [Fact]
    public void The_scan_reaches_an_exception_handed_to_a_helper()
    {
        const string Offending = """
            private static IResult Describe(string taskId, FlowableRequestException exception, ILogger logger)
            {
                logger.LogWarning(exception, "failed");
                return Results.Json(new { message = exception.Message }, statusCode: 502);
            }
            """;

        const string Fixed = """
            private static IResult Describe(string taskId, FlowableRequestException exception, ILogger logger)
            {
                logger.LogWarning(exception, "failed");
                return Results.Json(
                    new { message = EngineRefusal.Describe(exception, "this step") },
                    statusCode: 502);
            }
            """;

        Assert.NotEmpty(Offences(Offending));
        Assert.Empty(Offences(Fixed));
    }
}
