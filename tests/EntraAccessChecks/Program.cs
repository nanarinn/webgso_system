using System.Security.Claims;
using TestingSystem.Web.Connectors.UseCases;

const string tenant = "27971c0d-2474-4fa6-bc9c-d30415822fc9";

ClaimsPrincipal Principal(string tenantId, params string[] roles) => new(new ClaimsIdentity(
    new[] { new Claim("tid", tenantId), new Claim("oid", "dd729b64-465a-4da1-a59e-65fa1da73361") }
        .Concat(roles.Select(role => new Claim("roles", role))), "test"));

void Check(bool result)
{
    if (!result) throw new Exception("Check failed");
}

void Reject(ClaimsPrincipal principal)
{
    try { EntraAccess.Read(principal, tenant); }
    catch (InvalidOperationException) { return; }
    throw new Exception("Unexpected access granted");
}

Check(EntraAccess.Read(Principal(tenant, "Staff"), tenant).Roles.SequenceEqual(["Staff"]));
Check(EntraAccess.Read(Principal(tenant, "Administrator", "Unknown"), tenant).Roles.Length == 1);
Reject(Principal(Guid.NewGuid().ToString(), "Administrator"));
Reject(Principal(tenant));
Reject(Principal(tenant, "administrator"));
Reject(new ClaimsPrincipal(new ClaimsIdentity()));
foreach (var value in new[] { "//evil.example", "/\\evil.example", "https://evil.example", "/foo\r\nbar", "" })
    Check(EntraAccess.SafeReturnUrl(value) == "/");
Check(EntraAccess.SafeReturnUrl("/mmf-analytics?plant=1") == "/mmf-analytics?plant=1");
Console.WriteLine("Entra tenant, identity, role and redirect checks passed.");
