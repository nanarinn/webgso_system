namespace TestingSystem.Web.Services;

public sealed class PrototypeLssDataApiClient(IHttpClientFactory httpClientFactory) : ILssDataApiClient
{
    public async Task<LssApiCollectionResult> CollectAsync(
        string plantName,
        string source,
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return new(false, 0, $"{source} collection could not start because the plant API endpoint is invalid.");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("application/json, application/xml, text/xml, text/csv, text/plain; q=0.8, */*; q=0.5");
            using var response = await httpClientFactory.CreateClient().SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var payload = await response.Content.ReadAsStringAsync(timeout.Token);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            return response.IsSuccessStatusCode
                ? new(true, 0, $"{source} data from {plantName} was received with HTTP {(int)response.StatusCode}.",
                    (int)response.StatusCode, contentType, payload)
                : new(false, 0, $"API returned HTTP {(int)response.StatusCode} ({response.StatusCode}).",
                    (int)response.StatusCode, contentType, payload);
        }
        catch (OperationCanceledException)
        {
            return new(false, 0, "Collection timed out after 30 seconds.");
        }
        catch (HttpRequestException exception)
        {
            return new(false, 0, $"Connection failed: {exception.Message}");
        }
    }

    public async Task<LssApiTestResult> TestConnectionAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return new(false, "Enter a valid HTTP or HTTPS API endpoint.", null, 0);
        }

        var startedAt = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("application/json, application/xml, text/xml, text/csv, text/plain; q=0.8, */*; q=0.5");
            using var response = await httpClientFactory.CreateClient().SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            startedAt.Stop();
            var connected = response.IsSuccessStatusCode;
            var message = connected
                ? $"API GET succeeded with HTTP {(int)response.StatusCode}; scheduled collection can reach this endpoint."
                : $"API GET returned HTTP {(int)response.StatusCode}; scheduled collection will record this response as failed.";
            return new(connected, message, (int)response.StatusCode, startedAt.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            startedAt.Stop();
            return new(false, "Connection timed out after 8 seconds.", null, startedAt.ElapsedMilliseconds);
        }
        catch (HttpRequestException exception)
        {
            startedAt.Stop();
            return new(false, $"Connection failed: {exception.Message}", null, startedAt.ElapsedMilliseconds);
        }
    }
}
