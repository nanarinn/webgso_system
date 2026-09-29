using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Backend.UseCases;
using TestingSystem.Web.Backend.UseCases.UCLSS027Audit;
using TestingSystem.Web.Connectors.UseCases;
using TestingSystem.Web.Data;
using TestingSystem.Web.Models;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class StaffAccounts : ComponentBase
{
    [Inject] private IServiceScopeFactory ScopeFactory { get; set; } = default!;
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] private IAuditTrailService AuditTrail { get; set; } = default!;
    [Inject] private IUserAccessNotificationService Notifications { get; set; } = default!;

    private List<UserRow> Users { get; set; } = [];
    private string SearchTerm { get; set; } = string.Empty;
    private string Message { get; set; } = string.Empty;
    private bool IsError { get; set; }
    private bool IsSaving { get; set; }
    private bool IsSuperAdministrator { get; set; }
    private string CurrentActor { get; set; } = "Unknown";
    private string CurrentRole { get; set; } = SystemRoles.Administrator;
    private UserRow? DecisionUser { get; set; }
    private string DecisionStatus { get; set; } = string.Empty;
    private string DecisionRole { get; set; } = string.Empty;

    private string CurrentRoleLabel => RoleLabel(CurrentRole);
    private List<UserRow> FilteredUsers => Users.Where(x => string.IsNullOrWhiteSpace(SearchTerm)
        || new[] { x.DisplayName, x.UserName, x.Email, x.Role, x.Status }
            .Any(value => value.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))).ToList();

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        CurrentActor = state.User.Identity?.Name ?? "Unknown";
        IsSuperAdministrator = state.User.IsInRole(SystemRoles.SuperAdministrator);
        CurrentRole = IsSuperAdministrator ? SystemRoles.SuperAdministrator : SystemRoles.Administrator;
        await LoadUsersAsync();
    }

    private async Task LoadUsersAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        var accounts = await database.Users.AsNoTracking()
            .OrderByDescending(user => user.AccessRequestedAt.HasValue)
            .ThenByDescending(user => user.AccessRequestedAt)
            .ThenByDescending(user => user.Id)
            .ToListAsync();
        var rows = new List<UserRow>();
        var roleNames = await database.Roles.AsNoTracking().ToDictionaryAsync(role => role.Id, role => role.Name ?? "");
        var userRoles = await database.UserRoles.AsNoTracking().ToListAsync();
        foreach (var user in accounts)
        {
            var roles = userRoles.Where(item => item.UserId == user.Id && roleNames.ContainsKey(item.RoleId))
                .Select(item => roleNames[item.RoleId]).ToArray();
            var role = roles.FirstOrDefault(x => x is SystemRoles.SuperAdministrator or SystemRoles.Administrator or SystemRoles.Staff)
                ?? (roles.Contains(SystemRoles.LegacyReviewer) ? SystemRoles.Staff : "None");
            rows.Add(new UserRow(user.Id, user.DisplayName, user.UserName ?? "", user.Email ?? "", role,
                user.AccessStatus, user.IsEntraUser, user.EntraObjectId ?? "", user.AccessRequestedAt));
        }
        Users = rows;
    }

    private bool CanManage(UserRow user) => UserAccessPolicy.CanReview(CurrentRole, user.Role,
        user.Role == "None" ? SystemRoles.Staff : user.Role) && user.UserName != CurrentActor;

    private static string ReactivateRole(UserRow user) => user.Role == "None" ? SystemRoles.Staff : user.Role;

    private void AskDecision(UserRow user, string status, string role)
    {
        Message = string.Empty;
        if (!UserAccessPolicy.CanReview(CurrentRole, user.Role, string.IsNullOrWhiteSpace(role) ? SystemRoles.Staff : role))
        {
            Message = "You do not have permission to make this access change.";
            IsError = true;
            return;
        }
        DecisionUser = user;
        DecisionStatus = status;
        DecisionRole = role;
    }

    private void CancelDecision() => DecisionUser = null;

    private async Task ConfirmDecisionAsync()
    {
        if (DecisionUser is null) return;
        IsSaving = true;
        Message = string.Empty;
        try
        {
            await using var scope = ScopeFactory.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var user = await userManager.FindByIdAsync(DecisionUser.Id);
            if (user is null) throw new InvalidOperationException("The account no longer exists.");
            var existingRoles = await userManager.GetRolesAsync(user);
            var currentRole = existingRoles.FirstOrDefault(x => x is SystemRoles.SuperAdministrator or SystemRoles.Administrator or SystemRoles.Staff)
                ?? (existingRoles.Contains(SystemRoles.LegacyReviewer) ? SystemRoles.Staff : "None");
            var requestedRole = string.IsNullOrWhiteSpace(DecisionRole) ? SystemRoles.Staff : DecisionRole;
            if (!UserAccessPolicy.CanReview(CurrentRole, currentRole, requestedRole))
                throw new InvalidOperationException("This role change is outside your approval authority.");
            if (user.UserName == CurrentActor && DecisionStatus != UserAccessStatus.Approved)
                throw new InvalidOperationException("You cannot deactivate your own account.");

            if (DecisionStatus == UserAccessStatus.Approved)
            {
                if (!await roleManager.RoleExistsAsync(requestedRole))
                    Check(await roleManager.CreateAsync(new IdentityRole(requestedRole)));
                var managedRoles = existingRoles.Where(x => x is SystemRoles.Administrator or SystemRoles.Staff or SystemRoles.LegacyReviewer).ToArray();
                if (managedRoles.Length > 0) Check(await userManager.RemoveFromRolesAsync(user, managedRoles));
                if (!await userManager.IsInRoleAsync(user, requestedRole)) Check(await userManager.AddToRoleAsync(user, requestedRole));
            }

            user.AccessStatus = DecisionStatus;
            user.AccessReviewedAt = DateTimeOffset.UtcNow;
            user.AccessReviewedBy = CurrentActor;
            Check(await userManager.UpdateAsync(user));
            if (DecisionStatus is UserAccessStatus.Inactive or UserAccessStatus.Rejected)
                Check(await userManager.UpdateSecurityStampAsync(user));
            await AuditTrail.RecordAsync(CurrentActor, "ReviewUserAccess", "Success", "ApplicationUser", user.Id,
                $"Access status changed to {DecisionStatus}; role {requestedRole}.");
            if (DecisionStatus is UserAccessStatus.Approved or UserAccessStatus.Rejected)
                await Notifications.QueueDecisionAsync(user, DecisionStatus, DecisionStatus == UserAccessStatus.Approved ? requestedRole : null);

            Message = $"{user.DisplayName} is now {DecisionStatus.ToLowerInvariant()}." +
                (DecisionStatus is UserAccessStatus.Approved or UserAccessStatus.Rejected ? " Email notification prepared." : "");
            IsError = false;
            DecisionUser = null;
            await LoadUsersAsync();
        }
        catch (Exception exception)
        {
            Message = exception.Message;
            IsError = true;
        }
        finally { IsSaving = false; }
    }

    private string DecisionTitle => DecisionStatus switch
    {
        UserAccessStatus.Approved => DecisionUser?.Status == UserAccessStatus.Inactive ? "Reactivate this account?" : $"Approve as {RoleLabel(DecisionRole)}?",
        UserAccessStatus.Rejected => "Reject this access request?",
        _ => "Deactivate this account?"
    };
    private string DecisionDescription => DecisionStatus switch
    {
        UserAccessStatus.Approved => $"{DecisionUser?.DisplayName} will be able to sign in with the {RoleLabel(DecisionRole)} role.",
        UserAccessStatus.Rejected => $"{DecisionUser?.DisplayName} will not be allowed into the repository.",
        _ => $"{DecisionUser?.DisplayName} will be signed out when the current session expires and cannot sign in again."
    };
    private string DecisionIcon => DecisionStatus == UserAccessStatus.Approved ? "✓" : "!";
    private string DecisionIconCss => DecisionStatus == UserAccessStatus.Approved ? "approve" : "deny";
    private string DecisionButtonCss => DecisionStatus == UserAccessStatus.Approved ? "admin-primary" : "access-danger";

    private static string FormatDate(DateTimeOffset? value) => value?.ToLocalTime().ToString("dd MMM yyyy, HH:mm") ?? "Existing account";
    private static string RoleLabel(string role) => role switch { SystemRoles.SuperAdministrator => "Super Admin", SystemRoles.Administrator => "Administrator", SystemRoles.Staff or SystemRoles.LegacyReviewer => "Staff", _ => "Not assigned" };
    private static string RoleCss(string role) => role.ToLowerInvariant().Replace("superadministrator", "superadmin");
    private static string StatusCss(string status) => status switch { UserAccessStatus.Approved => "status-successful", UserAccessStatus.Pending => "status-warning", _ => "status-failed" };
    private static string GetInitials(string name) => string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => x[0])).ToUpperInvariant();
    private static void Check(IdentityResult result) { if (!result.Succeeded) throw new InvalidOperationException(string.Join(" ", result.Errors.Select(x => x.Description))); }
    private sealed record UserRow(string Id, string DisplayName, string UserName, string Email, string Role, string Status, bool IsEntraUser, string EntraObjectId, DateTimeOffset? RequestedAt);
}
