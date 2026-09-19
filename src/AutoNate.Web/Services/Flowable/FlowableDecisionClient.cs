using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;

namespace AutoNate.Web.Services.Flowable;

/// <summary>
/// <see cref="IFlowableDecisionClient"/> over Flowable's DMN REST service (#106).
/// </summary>
/// <remarks>
/// <para>
/// Shares <see cref="FlowableClient.ConfigureHttpClient"/>, so the base address,
/// timeout and Basic credentials are configured in exactly one place. The two
/// clients differ only in the path they append — <c>service/</c> for BPMN,
/// <c>dmn-api/</c> for DMN — which is the whole of the relationship between them
/// and the reason the auth was not invented twice.
/// </para>
/// </remarks>
public sealed class FlowableDecisionClient(HttpClient httpClient, IMemoryCache cache)
    : IFlowableDecisionClient
{
    /// <summary>
    /// Case-insensitive on the way IN, camelCase on the way OUT.
    /// </summary>
    /// <remarks>
    /// The naming policy is not cosmetic. Without it the execute body goes out as
    /// <c>{"DecisionKey":…,"InputVariables":[…]}</c>, and the engine's binder,
    /// which is case-sensitive, reads neither -- so the call arrives with no key
    /// and no inputs. It would have been caught by nothing that only exercised
    /// the engine directly, because such a test writes its own JSON.
    /// </remarks>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly XNamespace Dmn = "https://www.omg.org/spec/DMN/20191111/MODEL/";

    private const string InputTypeCacheKeyPrefix = "flowable:dmn:input-types:";

    // A decision is immutable per (key, version) -- redeploying produces a new
    // id -- so once its declared input types are read they never change. Same
    // reasoning, and the same TTL, as the BPMN client's definition-name cache.
    private static readonly TimeSpan InputTypeCacheTtl = TimeSpan.FromHours(24);

    private readonly HttpClient _httpClient = httpClient;
    private readonly IMemoryCache _cache = cache;

    public async Task<DecisionDeploymentInfo> DeployDecisionAsync(
        string decisionKey, string dmnXml, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();

        // The engine dispatches on the EXTENSION, not on the content type: a
        // resource that does not end in .dmn or .dmn.xml is accepted into the
        // deployment and then quietly not parsed as a decision, so the deploy
        // returns 200 and no decision exists afterwards.
        var fileName = $"{decisionKey}.dmn";
        content.Add(new StringContent(dmnXml, Encoding.UTF8, "application/xml"), "file", fileName);

        using var response = await _httpClient.PostAsync(
            "dmn-api/dmn-repository/deployments", content, cancellationToken);
        await EnsureSuccessAsync(response, "deploy the decision");

        var deployment = await DeserializeAsync<DmnDeploymentResponse>(response, cancellationToken);

        var decision = await GetLatestDecisionAsync(decisionKey, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Flowable accepted the DMN deployment, but no decision with key '{decisionKey}' was found. "
                + "The most likely cause is that the definition's decision id does not match the key it was "
                + "deployed under, which the engine does not treat as an error.");

        return new DecisionDeploymentInfo
        {
            DeploymentId = deployment.Id ?? string.Empty,
            DecisionId = decision.Id,
            DecisionKey = decision.Key,
            DecisionVersion = decision.Version,
            DeployedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public async Task<DecisionDefinitionSummary?> GetLatestDecisionAsync(
        string decisionKey, CancellationToken cancellationToken = default)
    {
        var url = $"dmn-api/dmn-repository/decisions?key={Uri.EscapeDataString(decisionKey)}&latest=true";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, "query the latest deployed decision");

        var payload = await DeserializeAsync<DmnListResponse<DmnDecisionResponse>>(response, cancellationToken);
        var decision = payload.Data.FirstOrDefault();

        return decision is null
            ? null
            : new DecisionDefinitionSummary(
                decision.Id ?? string.Empty,
                decision.Key ?? string.Empty,
                decision.Name,
                decision.Version,
                decision.DeploymentId);
    }

    public async Task<DecisionEvaluationResult> EvaluateAsync(
        string decisionKey,
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken cancellationToken = default)
    {
        // Resolve the key first. The engine answers an unknown key with a 400,
        // which is already the right CLASS of error -- but the resolution is
        // needed anyway for the type check below, so it costs no extra round
        // trip and buys a 404 that names the key it looked for.
        var decision = await GetLatestDecisionAsync(decisionKey, cancellationToken)
            ?? throw new FlowableRequestException(
                HttpStatusCode.NotFound,
                "evaluate the decision",
                $"No decision is deployed with key '{decisionKey}'.");

        // UNWRAP FIRST. An input that arrived over HTTP is a JsonElement, not an
        // int or a string, and every type decision below would otherwise see
        // "JsonElement" and answer wrongly -- the check would refuse a perfectly
        // good call, and TypeNameFor would tell the engine "string" for a number.
        //
        // Found end to end, not in a unit test: the unit tests hand this method a
        // real Dictionary<string, object?> with CLR values, which is exactly what
        // no caller behind an endpoint ever does.
        var unwrapped = inputs.ToDictionary(
            pair => pair.Key,
            pair => Unwrap(pair.Value),
            StringComparer.Ordinal);

        await EnsureInputsSatisfyDeclaredTypesAsync(decision, unwrapped, cancellationToken);

        var request = new DmnExecuteRequest
        {
            DecisionKey = decision.Key,
            InputVariables = unwrapped
                .Select(pair => new DmnVariable
                {
                    Name = pair.Key,
                    Type = TypeNameFor(pair.Value),
                    Value = pair.Value
                })
                .ToList()
        };

        using var response = await _httpClient.PostAsJsonAsync(
            "dmn-api/dmn-rule/execute", request, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, "evaluate the decision");

        var payload = await DeserializeAsync<DmnExecuteResponse>(response, cancellationToken);

        var outputs = (payload.ResultVariables ?? [])
            .Select(row => (IReadOnlyDictionary<string, object?>)row
                .ToDictionary(v => v.Name ?? string.Empty, v => Unwrap(v.Value), StringComparer.Ordinal))
            .ToList();

        return new DecisionEvaluationResult(outputs);
    }

    /// <summary>
    /// The engine's name for a CLR type.
    /// </summary>
    /// <remarks>
    /// A wrong name here is the third failure mode: the engine reports a
    /// conversion failure naming the variable, rather than evaluating against a
    /// value it silently coerced. That is the behaviour worth keeping — a table
    /// whose input expects a number and receives the string "12" should say so.
    /// </remarks>
    /// <summary>
    /// Refuses inputs the decision's own table could not use (#106).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This exists because the engine does not report it.</b> Measured against
    /// Flowable 8.0.0: passing a string where an <c>inputExpression</c> declares
    /// <c>typeRef="number"</c> returns <b>201 Created with an empty result</b> --
    /// byte-identical to a legitimate no-match. So a caller who gets a type wrong
    /// is told "no rule applied", and a routing decision silently becomes "do
    /// nothing". That is the worst shape a failure can take: plausible, silent,
    /// and indistinguishable from a correct answer.
    /// </para>
    /// <para>
    /// The engine will serve a decision's own DMN XML
    /// (<c>decisions/{id}/resourcedata</c>), so the declared types are knowable
    /// rather than guessable and the check is made against what the author
    /// actually wrote. <c>DecisionEngineTests</c> pins the engine behaviour this
    /// compensates for, so if a later release starts reporting the mismatch
    /// itself, that test fails and this becomes removable rather than folklore.
    /// </para>
    /// </remarks>
    private async Task EnsureInputsSatisfyDeclaredTypesAsync(
        DecisionDefinitionSummary decision,
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken cancellationToken)
    {
        var declared = await GetDeclaredInputTypesAsync(decision, cancellationToken);
        if (declared.Count == 0) return;

        var offences = new List<string>();
        foreach (var (name, value) in inputs)
        {
            if (value is null) continue;
            if (!declared.TryGetValue(name, out var typeRef)) continue;
            if (Satisfies(value, typeRef)) continue;

            offences.Add(
                $"'{name}' is declared {typeRef} in the decision table but was given "
                + $"{value.GetType().Name} ({value})");
        }

        if (offences.Count == 0) return;

        throw new FlowableRequestException(
            HttpStatusCode.BadRequest,
            "evaluate the decision",
            $"Decision '{decision.Key}' cannot evaluate these inputs: {string.Join("; ", offences)}. "
            + "The engine would accept this call and return no matched rule, which is "
            + "indistinguishable from a legitimate no-match -- so it is refused here instead.");
    }

    /// <summary>Input variable name -> the <c>typeRef</c> the author declared.</summary>
    private async Task<IReadOnlyDictionary<string, string>> GetDeclaredInputTypesAsync(
        DecisionDefinitionSummary decision, CancellationToken cancellationToken)
    {
        var cacheKey = InputTypeCacheKeyPrefix + decision.Id;
        if (_cache.TryGetValue(cacheKey, out IReadOnlyDictionary<string, string>? cached)
            && cached is not null)
        {
            return cached;
        }

        using var response = await _httpClient.GetAsync(
            $"dmn-api/dmn-repository/decisions/{Uri.EscapeDataString(decision.Id)}/resourcedata",
            cancellationToken);
        await EnsureSuccessAsync(response, "read the decision's definition");

        var xml = await response.Content.ReadAsStringAsync(cancellationToken);

        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var document = XDocument.Parse(xml);
            foreach (var input in document.Descendants(Dmn + "input"))
            {
                var expression = input.Element(Dmn + "inputExpression");
                var typeRef = expression?.Attribute("typeRef")?.Value;
                // The expression's text is the variable name the engine binds.
                var name = expression?.Element(Dmn + "text")?.Value.Trim();
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(typeRef))
                {
                    types[name!] = typeRef!;
                }
            }
        }
        catch (System.Xml.XmlException)
        {
            // An unreadable definition is not worth failing an evaluation over:
            // the engine parsed it well enough to deploy it. Returning nothing
            // means "no declared types known", which skips the check rather than
            // inventing a verdict. Deliberately not cached -- a transient read
            // should not pin "we know nothing" for a day.
            return types;
        }

        _cache.Set(cacheKey, (IReadOnlyDictionary<string, string>)types, InputTypeCacheTtl);
        return types;
    }

    /// <summary>Whether a CLR value can stand in for a DMN <c>typeRef</c>.</summary>
    private static bool Satisfies(object value, string typeRef) => typeRef.ToLowerInvariant() switch
    {
        "number" or "double" or "integer" or "long" =>
            value is int or long or short or byte or double or float or decimal,
        "boolean" => value is bool,
        "date" or "datetime" => value is DateTime or DateTimeOffset,
        "string" => value is string,
        // An unrecognised typeRef is not a verdict. DMN allows custom types, and
        // refusing what we merely do not recognise would be worse than the
        // silence this method exists to end.
        _ => true
    };

    private static string TypeNameFor(object? value) => value switch
    {
        null => "string",
        bool => "boolean",
        int or long or short or byte => "integer",
        double or float or decimal => "double",
        DateTime or DateTimeOffset => "date",
        _ => "string"
    };

    /// <summary>
    /// A <see cref="JsonElement"/> as the CLR value it stands for.
    /// </summary>
    /// <remarks>
    /// Used on BOTH directions. Outputs come back as <c>JsonElement</c> from the
    /// engine's response; inputs arrive as <c>JsonElement</c> from an endpoint's
    /// model binder. The second is the one that bit: every type decision in this
    /// client asks what a value <em>is</em>, and a boxed <c>JsonElement</c>
    /// answers "JsonElement" to all of them.
    /// </remarks>
    private static object? Unwrap(object? value) => value is JsonElement element
        ? element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            // (object) on BOTH branches, deliberately. Without the casts C# unifies
            // long and double to double, boxes every integer as a double, and
            // TypeNameFor then tells the engine "double" for a whole number. It is
            // survivable -- a DMN number column accepts either -- which is why it
            // took a unit test asserting the emitted type name to see it at all.
            JsonValueKind.Number => element.TryGetInt64(out var whole)
                ? (object)whole
                : (object)element.GetDouble(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.ToString()
        }
        : value;

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync();
        var details = string.IsNullOrWhiteSpace(body) ? "No response body was returned." : body;

        throw new FlowableRequestException(
            response.StatusCode,
            operation,
            $"Flowable could not {operation}. HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {details}");
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken);
        return payload ?? throw new InvalidOperationException("Flowable returned an empty DMN response payload.");
    }

    private sealed class DmnListResponse<T>
    {
        public List<T> Data { get; init; } = [];

        public int Total { get; init; }
    }

    private sealed class DmnDeploymentResponse
    {
        public string? Id { get; init; }

        public string? Name { get; init; }
    }

    private sealed class DmnDecisionResponse
    {
        public string? Id { get; init; }

        public string? Key { get; init; }

        public string? Name { get; init; }

        public int Version { get; init; }

        public string? DeploymentId { get; init; }
    }

    private sealed class DmnExecuteRequest
    {
        public string DecisionKey { get; init; } = string.Empty;

        public List<DmnVariable> InputVariables { get; init; } = [];
    }

    private sealed class DmnVariable
    {
        public string? Name { get; init; }

        public string? Type { get; init; }

        public object? Value { get; init; }
    }

    private sealed class DmnExecuteResponse
    {
        public List<List<DmnVariable>>? ResultVariables { get; init; }
    }
}
