using TestingSystem.Web.Models;

namespace TestingSystem.Web.Backend.UseCases;

public static class UserAccessPolicy
{
    public static bool CanReview(string reviewerRole, string targetRole, string requestedRole)
    {
        if (reviewerRole == SystemRoles.SuperAdministrator) return true;
        return reviewerRole == SystemRoles.Administrator
            && targetRole is not SystemRoles.SuperAdministrator and not SystemRoles.Administrator
            && requestedRole == SystemRoles.Staff;
    }
}
