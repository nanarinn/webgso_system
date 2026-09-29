namespace TestingSystem.Web.Services;

public interface ILssDataApiClient
{
    Task<LssApiCollectionResult> CollectAsync(
        string plantName,
        string source,
        string endpoint,
        CancellationToken cancellationToken = default);

    Task<LssApiTestResult> TestConnectionAsync(
        string endpoint,
        string apiType,
        string dataType,
        CancellationToken cancellationToken = default);
}

public sealed record LssApiCollectionResult(
    bool IsSuccessful,
    int RecordsReceived,
    string Message,
    int? HttpStatusCode = null,
    string ContentType = "",
    string Payload = "");

public sealed record LssApiTestResult(
    bool IsConnected,
    string Message,
    int? HttpStatusCode,
    long ElapsedMilliseconds);
