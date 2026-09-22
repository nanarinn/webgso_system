using System.Security.Claims;
using TestingSystem.Web.Connectors.UseCases;
using TestingSystem.Web.Backend.UseCases;
using TestingSystem.Web.Models;

const string tenant = "27971c0d-2474-4fa6-bc9c-d30415822fc9";

ClaimsPrincipal Principal(string tenantId, bool includeEmail = true) => new(new ClaimsIdentity(
    new[] { new Claim("tid", tenantId), new Claim("oid", "dd729b64-465a-4da1-a59e-65fa1da73361"), new Claim("name", "Test User") }
        .Concat(includeEmail ? [new Claim("preferred_username", "test@sae-malaysia.com")] : []), "test"));

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

var identity = EntraAccess.Read(Principal(tenant), tenant);
Check(identity.Email == "test@sae-malaysia.com");
Check(identity.DisplayName == "Test User");
Check(identity.ObjectId == "dd729b64-465a-4da1-a59e-65fa1da73361");
Reject(Principal(Guid.NewGuid().ToString()));
Reject(Principal(tenant, includeEmail: false));
Reject(new ClaimsPrincipal(new ClaimsIdentity()));
foreach (var value in new[] { "//evil.example", "/\\evil.example", "https://evil.example", "/foo\r\nbar", "" })
    Check(EntraAccess.SafeReturnUrl(value) == "/");
Check(EntraAccess.SafeReturnUrl("/mmf-analytics?plant=1") == "/mmf-analytics?plant=1");
Check(EntraAccess.LoginError(new Exception("SBLSS_ACCESS_PENDING")) == "microsoft-pending");
Check(EntraAccess.LoginError(new Exception("AADSTS50105 user assignment required")) == "microsoft-assignment");
Check(EntraAccess.LoginError(new Exception("AADSTS50011 reply URL mismatch")) == "microsoft-redirect");
Check(EntraAccess.LoginError(new Exception("AADSTS7000215 invalid client secret")) == "microsoft-secret");
Check(UserAccessPolicy.CanReview(SystemRoles.SuperAdministrator, SystemRoles.Administrator, SystemRoles.Staff));
Check(UserAccessPolicy.CanReview(SystemRoles.SuperAdministrator, SystemRoles.Staff, SystemRoles.Administrator));
Check(UserAccessPolicy.CanReview(SystemRoles.Administrator, "None", SystemRoles.Staff));
Check(!UserAccessPolicy.CanReview(SystemRoles.Administrator, SystemRoles.Staff, SystemRoles.Administrator));
Check(!UserAccessPolicy.CanReview(SystemRoles.Administrator, SystemRoles.Administrator, SystemRoles.Staff));
Check(!UserAccessPolicy.CanReview(SystemRoles.Staff, SystemRoles.Staff, SystemRoles.Staff));
Console.WriteLine("Entra identity, safe redirect and access approval policy checks passed.");
