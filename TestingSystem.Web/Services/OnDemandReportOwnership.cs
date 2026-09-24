using System.Security.Claims;

namespace TestingSystem.Web.Services;

public static class OnDemandReportOwnership
{
    public static string GetRequesterKey(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrWhiteSpace(userId)) return $"user:{userId.Trim()}";

        var email = principal.FindFirst(ClaimTypes.Email)?.Value
            ?? principal.FindFirst("preferred_username")?.Value
            ?? principal.Identity?.Name;
        return string.IsNullOrWhiteSpace(email) ? string.Empty : $"email:{email.Trim().ToLowerInvariant()}";
    }

    public static bool IsOwner(string? storedRequester, string requesterKey) =>
        !string.IsNullOrWhiteSpace(requesterKey)
        && string.Equals(storedRequester, requesterKey, StringComparison.OrdinalIgnoreCase);
}
