using System.Security.Claims;

namespace TestingSystem.Web.Connectors.UseCases;

public static class EntraAccess
{
    public static string SafeReturnUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.StartsWith('/')
        && !value.StartsWith("//") && !value.Contains('\\')
        && !value.Any(char.IsControl) ? value : "/";

    public static EntraIdentity Read(ClaimsPrincipal principal, string tenant)
    {
        if (!Guid.TryParse(tenant, out var expected)
            || !Guid.TryParse(principal.FindFirst("tid")?.Value, out var actual)
            || expected != actual
            || !Guid.TryParse(principal.FindFirst("oid")?.Value, out var objectId))
            throw new InvalidOperationException("Invalid tenant or object identifier.");

        var displayName = principal.FindFirst("name")?.Value?.Trim();
        var email = principal.FindFirst("email")?.Value
            ?? principal.FindFirst("preferred_username")?.Value
            ?? principal.FindFirst("upn")?.Value;
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("Microsoft account email is unavailable.");

        return new EntraIdentity(
            $"{actual:D}:{objectId:D}", objectId.ToString("D"),
            string.IsNullOrWhiteSpace(displayName) ? email : displayName,
            email.Trim());
    }

    public static string LoginError(Exception? failure)
    {
        var message = failure?.ToString() ?? string.Empty;
        if (message.Contains("SBLSS_ACCESS_PENDING", StringComparison.OrdinalIgnoreCase)) return "microsoft-pending";
        if (message.Contains("SBLSS_ACCESS_REJECTED", StringComparison.OrdinalIgnoreCase)) return "microsoft-rejected";
        if (message.Contains("SBLSS_ACCESS_INACTIVE", StringComparison.OrdinalIgnoreCase)) return "microsoft-inactive";
        if (message.Contains("AADSTS50105", StringComparison.OrdinalIgnoreCase)) return "microsoft-assignment";
        if (message.Contains("AADSTS50011", StringComparison.OrdinalIgnoreCase)) return "microsoft-redirect";
        if (message.Contains("AADSTS7000215", StringComparison.OrdinalIgnoreCase)
            || message.Contains("AADSTS7000222", StringComparison.OrdinalIgnoreCase)) return "microsoft-secret";
        if (message.Contains("access_denied", StringComparison.OrdinalIgnoreCase)) return "microsoft-consent";
        return "microsoft";
    }
}

public sealed record EntraIdentity(string Key, string ObjectId, string DisplayName, string Email);
