using TestingSystem.Web.Models;

namespace TestingSystem.Web.Connectors.UseCases;

public sealed record UserAccessMessage(string Type, string Subject, string Body);

public static class UserAccessNotificationContent
{
    public static UserAccessMessage Request(ApplicationUser requester) => new(
        "AccessRequested",
        "New SBLSS access request pending approval",
        $"{DisplayName(requester)} ({requester.Email}) requested access to the SBLSS repository. Review the request in User Management.");

    public static UserAccessMessage Decision(ApplicationUser user, string status, string? role)
    {
        var approved = status == UserAccessStatus.Approved;
        return new(
            approved ? "AccessApproved" : "AccessRejected",
            approved ? "SBLSS repository access approved" : "SBLSS repository access request update",
            approved
                ? $"Your SBLSS repository access has been approved with the {role} role. You may now sign in with your local account."
                : "Your SBLSS repository access request was not approved. Contact the system administrator if you need assistance.");
    }

    private static string DisplayName(ApplicationUser user) =>
        string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email ?? "A local user" : user.DisplayName;
}
