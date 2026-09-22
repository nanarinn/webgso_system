using Microsoft.AspNetCore.Identity;

namespace TestingSystem.Web.Models;

public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public string AccessStatus { get; set; } = UserAccessStatus.Pending;
    public bool IsEntraUser { get; set; }
    public string? EntraObjectId { get; set; }
    public DateTimeOffset? AccessRequestedAt { get; set; }
    public DateTimeOffset? AccessReviewedAt { get; set; }
    public string? AccessReviewedBy { get; set; }
    public string? AccessReviewNote { get; set; }
}

public static class UserAccessStatus
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Inactive = "Inactive";
}

public static class SystemRoles
{
    public const string SuperAdministrator = "SuperAdministrator";
    public const string Administrator = "Administrator";
    public const string Staff = "Staff";
    public const string LegacyReviewer = "Reviewer";
    public const string Administrators = SuperAdministrator + "," + Administrator;
}
