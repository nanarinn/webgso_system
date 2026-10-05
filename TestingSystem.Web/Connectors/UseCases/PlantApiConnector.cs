using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;

namespace TestingSystem.Web.Services;

public sealed class PrototypeLssDataApiClient(
    IHttpClientFactory httpClientFactory,
    IWebHostEnvironment environment,
    IConfiguration configuration) : ILssDataApiClient
{
    private const int MaximumPayloadBytes = 10 * 1024 * 1024;
    private const int MaximumErrorMessageLength = 500;

    public async Task<LssApiCollectionResult> CollectAsync(
        string plantName,
        string source,
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || !IsHttpUri(uri) || !await IsEndpointAllowedAsync(uri, cancellationToken))
            return new(false, 0, $"{source} collection could not start because the plant API endpoint is invalid.");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("application/json, application/xml, text/xml, text/csv, text/plain; q=0.8, */*; q=0.5");
            using var response = await httpClientFactory.CreateClient("PlantDataApi").SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var payload = await ReadPayloadAsync(response.Content, timeout.Token);
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
            return new(false, 0, $"Connection failed: {Limit(exception.Message)}");
        }
        catch (InvalidDataException exception)
        {
            return new(false, 0, exception.Message);
        }
    }

    public async Task<LssApiTestResult> TestConnectionAsync(
        string endpoint,
        string apiType,
        string dataType,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || !IsHttpUri(uri) || !await IsEndpointAllowedAsync(uri, cancellationToken))
            return new(false, "The API endpoint is invalid or not allowed by the server network policy.", null, 0);

        var startedAt = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("application/json, application/xml, text/xml, text/csv, text/plain; q=0.8, */*; q=0.5");
            using var response = await httpClientFactory.CreateClient("PlantDataApi").SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var payload = await ReadPayloadAsync(response.Content, timeout.Token);
            startedAt.Stop();
            if (!response.IsSuccessStatusCode)
                return new(false,
                    $"API GET returned HTTP {(int)response.StatusCode}; the endpoint was not updated.",
                    (int)response.StatusCode, startedAt.ElapsedMilliseconds);

            var intervalMinutes = apiType.Equals("MMF", StringComparison.OrdinalIgnoreCase)
                ? 15
                : DdqDatasetPolicy.Get(dataType).ReferenceTemplateIntervalMinutes;
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            var verification = LssApiPayloadVerifier.Verify(payload, contentType, apiType, dataType, intervalMinutes);
            var message = verification.IsValid
                ? $"HTTP {(int)response.StatusCode}. {verification.Message} This endpoint is ready for scheduled collection."
                : $"HTTP {(int)response.StatusCode}, but this is not a usable {apiType} data API. {verification.Message}";
            return new(verification.IsValid, message, (int)response.StatusCode, startedAt.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            startedAt.Stop();
            return new(false, "Connection timed out after 8 seconds.", null, startedAt.ElapsedMilliseconds);
        }
        catch (HttpRequestException exception)
        {
            startedAt.Stop();
            return new(false, $"Connection failed: {Limit(exception.Message)}", null, startedAt.ElapsedMilliseconds);
        }
        catch (InvalidDataException exception)
        {
            startedAt.Stop();
            return new(false, exception.Message, null, startedAt.ElapsedMilliseconds);
        }
    }

    private static bool IsHttpUri(Uri uri) =>
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && string.IsNullOrEmpty(uri.UserInfo)
        && !string.IsNullOrWhiteSpace(uri.Host);

    private async Task<bool> IsEndpointAllowedAsync(Uri uri, CancellationToken cancellationToken)
    {
        var host = uri.DnsSafeHost.TrimEnd('.');
        var allowedPrivateHosts = configuration.GetSection("LssApi:AllowedPrivateHosts").Get<string[]>() ?? [];
        var allowedInsecureHosts = configuration.GetSection("LssApi:AllowInsecureHosts").Get<string[]>() ?? [];
        var explicitlyAllowedPrivate = allowedPrivateHosts.Contains(host, StringComparer.OrdinalIgnoreCase);
        var explicitlyAllowedInsecure = allowedInsecureHosts.Contains(host, StringComparer.OrdinalIgnoreCase);

        if (!environment.IsDevelopment() && uri.Scheme != Uri.UriSchemeHttps && !explicitlyAllowedInsecure)
            return false;

        if (IPAddress.TryParse(host, out var address))
        {
            if (environment.IsDevelopment()) return true;
            return !IsPrivateOrReserved(address) || explicitlyAllowedPrivate;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            return environment.IsDevelopment();
        if (host.Equals("metadata", StringComparison.OrdinalIgnoreCase)
            || host.Equals("instance-data", StringComparison.OrdinalIgnoreCase)
            || host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            using var dnsTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            dnsTimeout.CancelAfter(TimeSpan.FromSeconds(3));
            var addresses = await Dns.GetHostAddressesAsync(host, dnsTimeout.Token);
            return addresses.Length > 0
                && (environment.IsDevelopment() || explicitlyAllowedPrivate
                    || addresses.All(address => !IsPrivateOrReserved(address)));
        }
        catch (System.Net.Sockets.SocketException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static bool IsPrivateOrReserved(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            return true;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return (bytes[0] & 0xFE) == 0xFC;
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return true;

        return bytes[0] == 0
            || bytes[0] == 10
            || bytes[0] == 127
            || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
            || (bytes[0] == 169 && bytes[1] == 254)
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || bytes[0] >= 224;
    }

    private static async Task<string> ReadPayloadAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumPayloadBytes)
            throw new InvalidDataException("The plant API response exceeds the 10 MB size limit.");

        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var count = await source.ReadAsync(chunk, cancellationToken);
            if (count == 0) break;
            if (buffer.Length + count > MaximumPayloadBytes)
                throw new InvalidDataException("The plant API response exceeds the 10 MB size limit.");
            await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
        }

        buffer.Position = 0;
        Encoding encoding = Encoding.UTF8;
        var charset = content.Headers.ContentType?.CharSet?.Trim('"', '\'');
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try { encoding = Encoding.GetEncoding(charset); }
            catch (ArgumentException)
            {
                throw new InvalidDataException("The plant API response uses an unsupported character encoding.");
            }
        }

        using var reader = new StreamReader(buffer, encoding, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static string Limit(string message) => message[..Math.Min(message.Length, MaximumErrorMessageLength)];
}
