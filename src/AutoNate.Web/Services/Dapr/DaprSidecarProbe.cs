using AutoNate.Web.Configuration;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Dapr;

public sealed class DaprSidecarProbe(IHttpClientFactory httpClientFactory, IOptions<DaprOptions> options)
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly DaprOptions _options = options.Value;

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(_options.HttpEndpoint, UriKind.Absolute, out var endpoint))
        {
            return false;
        }

        var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(2);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "/v1.0/metadata"));

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }
}
