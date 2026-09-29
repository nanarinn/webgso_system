using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace TestingSystem.Web.Services;

public sealed record ReportEmailAttachment(string FileName, string ContentType, byte[] Content);
public sealed record ReportEmailSendResult(bool Succeeded, string Message);

public interface IReportEmailSender
{
    bool IsConfigured { get; }
    string ConfigurationMessage { get; }
    Task<ReportEmailSendResult> SendAsync(string subject, string htmlBody, IReadOnlyCollection<string> recipients,
        IReadOnlyCollection<ReportEmailAttachment> attachments, CancellationToken cancellationToken);
}

public sealed class GraphReportEmailSender(HttpClient httpClient, IConfiguration configuration) : IReportEmailSender
{
    private string TenantId => Value("ReportEmail:TenantId", "EntraId:TenantId");
    private string ClientId => Value("ReportEmail:ClientId", "EntraId:ClientId");
    private string ClientSecret => Value("ReportEmail:ClientSecret", "EntraId:ClientSecret");
    private string SenderMailbox => configuration["ReportEmail:SenderMailbox"]?.Trim() ?? string.Empty;
    private bool Enabled => configuration.GetValue("ReportEmail:Enabled", false);

    public bool IsConfigured => Enabled
        && !string.IsNullOrWhiteSpace(TenantId)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(SenderMailbox);

    public string ConfigurationMessage => IsConfigured
        ? "Microsoft Graph report email is configured."
        : "Report email needs ReportEmail:Enabled, SenderMailbox and Microsoft Entra application credentials.";

    public async Task<ReportEmailSendResult> SendAsync(string subject, string htmlBody,
        IReadOnlyCollection<string> recipients, IReadOnlyCollection<ReportEmailAttachment> attachments,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured) return new(false, ConfigurationMessage);
        if (recipients.Count == 0) return new(false, "No email recipients were configured.");

        try
        {
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post,
                $"https://login.microsoftonline.com/{Uri.EscapeDataString(TenantId)}/oauth2/v2.0/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId,
                    ["client_secret"] = ClientSecret,
                    ["scope"] = "https://graph.microsoft.com/.default",
                    ["grant_type"] = "client_credentials"
                })
            };
            using var tokenResponse = await httpClient.SendAsync(tokenRequest, cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
                return new(false, await GraphErrorAsync(tokenResponse, "Microsoft sign-in for report email failed.", cancellationToken));

            using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(cancellationToken));
            if (!tokenJson.RootElement.TryGetProperty("access_token", out var tokenElement)
                || string.IsNullOrWhiteSpace(tokenElement.GetString()))
                return new(false, "Microsoft Entra did not return an email access token.");

            var message = new
            {
                message = new
                {
                    subject,
                    body = new { contentType = "HTML", content = htmlBody },
                    toRecipients = recipients.Select(email => new { emailAddress = new { address = email } }),
                    attachments = attachments.Select(attachment => new Dictionary<string, object>
                    {
                        ["@odata.type"] = "#microsoft.graph.fileAttachment",
                        ["name"] = attachment.FileName,
                        ["contentType"] = attachment.ContentType,
                        ["contentBytes"] = Convert.ToBase64String(attachment.Content)
                    })
                },
                saveToSentItems = true
            };
            using var mailRequest = new HttpRequestMessage(HttpMethod.Post,
                $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(SenderMailbox)}/sendMail")
            {
                Content = JsonContent.Create(message)
            };
            mailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenElement.GetString());
            using var mailResponse = await httpClient.SendAsync(mailRequest, cancellationToken);
            return mailResponse.StatusCode == HttpStatusCode.Accepted
                ? new(true, $"Email accepted by Microsoft Graph for {recipients.Count} recipient(s).")
                : new(false, await GraphErrorAsync(mailResponse, "Microsoft Graph rejected the report email.", cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(false, $"Report email failed: {Limit(exception.GetBaseException().Message, 600)}");
        }
    }

    private string Value(string primary, string fallback) =>
        configuration[primary]?.Trim() is { Length: > 0 } value ? value : configuration[fallback]?.Trim() ?? string.Empty;

    private static async Task<string> GraphErrorAsync(HttpResponseMessage response, string prefix,
        CancellationToken cancellationToken)
    {
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var json = JsonDocument.Parse(detail);
            detail = json.RootElement.TryGetProperty("error", out var error)
                ? error.TryGetProperty("message", out var message) ? message.GetString() ?? detail
                    : error.TryGetProperty("description", out var description) ? description.GetString() ?? detail : detail
                : detail;
        }
        catch (JsonException) { }
        return $"{prefix} HTTP {(int)response.StatusCode}: {Limit(detail, 600)}";
    }

    private static string Limit(string value, int length) =>
        string.IsNullOrWhiteSpace(value) ? "No error detail was returned." : value[..Math.Min(value.Length, length)];
}
