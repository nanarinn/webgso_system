namespace TestingSystem.Web.Services;

public sealed class PrototypeLssDataApiClient(IHttpClientFactory httpClientFactory) : ILssDataApiClient
{
    public Task<LssApiCollectionResult> CollectAsync(
        string plantName,
        string source,
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        // Replace this simulation with HttpClient when the plant API contract is available.
        var isConfigured = Uri.TryCreate(endpoint, UriKind.Absolute, out _);
        var result = isConfigured
            ? new LssApiCollectionResult(true, source == "DDQ" ? 2880 : 576,
                $"{source} data from {plantName} was received through its configured API endpoint.")
            : new LssApiCollectionResult(false, 0,
                $"{source} collection could not start because the plant API endpoint is invalid.");
        return Task.FromResult(result);
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
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            using var response = await httpClientFactory.CreateClient().SendAsync(request, timeout.Token);
            startedAt.Stop();
            var connected = (int)response.StatusCode < 500;
            var message = connected
                ? $"Endpoint reached successfully with HTTP {(int)response.StatusCode}."
                : $"Endpoint responded with HTTP {(int)response.StatusCode}.";
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
