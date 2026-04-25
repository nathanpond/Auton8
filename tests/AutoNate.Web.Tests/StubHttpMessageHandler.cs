using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AutoNate.Web.Tests;

/// <summary>
/// Test double for HttpMessageHandler. Tests register route handlers keyed by
/// HTTP method + a path fragment; the first matching handler wins. All
/// received requests are captured for assertion.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly List<Route> _routes = new();

    public List<RecordedRequest> Requests { get; } = new();

    public StubHttpMessageHandler When(
        HttpMethod method,
        string pathFragment,
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes.Add(new Route(method, pathFragment, respond));
        return this;
    }

    public StubHttpMessageHandler WhenJson(
        HttpMethod method,
        string pathFragment,
        object payload,
        HttpStatusCode status = HttpStatusCode.OK) =>
        When(method, pathFragment, _ => JsonResponse(payload, status));

    public StubHttpMessageHandler WhenStatus(
        HttpMethod method,
        string pathFragment,
        HttpStatusCode status,
        string? body = null) =>
        When(method, pathFragment, _ => new HttpResponseMessage(status)
        {
            Content = body is null ? new StringContent(string.Empty) : new StringContent(body)
        });

    public static HttpResponseMessage JsonResponse(object payload, HttpStatusCode status = HttpStatusCode.OK)
    {
        var json = JsonSerializer.Serialize(payload);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    public static HttpResponseMessage TextResponse(string body, HttpStatusCode status = HttpStatusCode.OK, string mediaType = "text/plain")
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType)
        };
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? string.Empty;
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, url, body));

        // Longest fragment wins so a generic "/pd-1" route doesn't shadow a
        // more specific "/pd-1/resourcedata" one regardless of registration order.
        var match = _routes
            .Where(r => r.Method == request.Method && url.Contains(r.PathFragment, StringComparison.Ordinal))
            .OrderByDescending(r => r.PathFragment.Length)
            .FirstOrDefault();
        if (match is not null)
        {
            return match.Respond(request);
        }

        throw new InvalidOperationException(
            $"StubHttpMessageHandler: no route matched {request.Method} {url}. " +
            $"Registered routes: {string.Join(", ", _routes.Select(r => $"{r.Method} *{r.PathFragment}*"))}");
    }

    private sealed record Route(
        HttpMethod Method,
        string PathFragment,
        Func<HttpRequestMessage, HttpResponseMessage> Respond);

    public sealed record RecordedRequest(HttpMethod Method, string Url, string? Body);
}
