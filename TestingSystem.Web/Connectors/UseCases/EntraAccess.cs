using System.Security.Claims;

namespace TestingSystem.Web.Connectors.UseCases;

public static class EntraAccess
{
    public static string SafeReturnUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.StartsWith('/')
        && !value.StartsWith("//") && !value.Contains('\\')
        && !value.Any(char.IsControl) ? value : "/";

    public static (string Key, string[] Roles) Read(ClaimsPrincipal principal, string tenant)
    {
        if (!Guid.TryParse(tenant, out var expected)
            || !Guid.TryParse(principal.FindFirst("tid")?.Value, out var actual)
            || expected != actual
            || !Guid.TryParse(principal.FindFirst("oid")?.Value, out var objectId))
            throw new InvalidOperationException("Invalid tenant or object identifier.");

        var roles = principal.FindAll("roles").Select(claim => claim.Value)
            .Where(role => role is "Administrator" or "Staff")
            .Distinct()
            .ToArray();
        if (roles.Length == 0)
            throw new InvalidOperationException("An assigned application role is required.");

        return ($"{actual:D}:{objectId:D}", roles);
    }
}
