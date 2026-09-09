using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Models;

namespace TestingSystem.Web.Components.Pages;

public partial class AdminUsers : ComponentBase
{
    [Inject] private UserManager<ApplicationUser> UserManager { get; set; } = default!;
    [Inject] private RoleManager<IdentityRole> RoleManager { get; set; } = default!;
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;

    private List<UserRow> Users { get; set; } = [];
    private string SearchTerm { get; set; } = string.Empty;
    private bool IsDialogOpen { get; set; }
    private bool IsSaving { get; set; }
    private string FormError { get; set; } = string.Empty;
    private string SuccessMessage { get; set; } = string.Empty;
    private string NewDisplayName { get; set; } = string.Empty;
    private string NewUserName { get; set; } = string.Empty;
    private string NewEmail { get; set; } = string.Empty;
    private string NewPhone { get; set; } = string.Empty;
    private string NewRole { get; set; } = "Reviewer";
    private string NewPassword { get; set; } = string.Empty;

    private List<UserRow> FilteredUsers => string.IsNullOrWhiteSpace(SearchTerm) ? Users : Users.Where(x => x.DisplayName.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) || x.UserName.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) || x.Email.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) || x.Role.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) || GetRoleLabel(x.Role).Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)).ToList();

    protected override async Task OnInitializedAsync() => await LoadUsersAsync();

    private async Task LoadUsersAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        Users = await (
            from user in database.Users
            join userRole in database.UserRoles on user.Id equals userRole.UserId into userRoles
            from userRole in userRoles.DefaultIfEmpty()
            join role in database.Roles on userRole.RoleId equals role.Id into roles
            from role in roles.DefaultIfEmpty()
            orderby user.UserName
            select new UserRow(
                user.Id,
                user.DisplayName,
                user.UserName ?? "",
                user.Email ?? "",
                user.PhoneNumber ?? "",
                role.Name ?? "Reviewer",
                user.LockoutEnd > DateTimeOffset.UtcNow))
            .ToListAsync();
    }

    private void OpenDialog()
    {
        NewDisplayName = NewUserName = NewEmail = NewPhone = NewPassword = FormError = string.Empty;
        NewRole = "Reviewer";
        SuccessMessage = string.Empty;
        IsDialogOpen = true;
    }

    private void CloseDialog() { IsDialogOpen = false; FormError = string.Empty; }

    private async Task SaveUserAsync()
    {
        IsSaving = true;
        FormError = string.Empty;
        try
        {
            var user = new ApplicationUser { UserName = NewUserName.Trim(), DisplayName = NewDisplayName.Trim(), Email = NewEmail.Trim(), EmailConfirmed = true, PhoneNumber = NewPhone.Trim() };
            var result = await UserManager.CreateAsync(user, NewPassword);
            if (!result.Succeeded) { FormError = string.Join(" ", result.Errors.Select(x => x.Description)); return; }
            if (!await RoleManager.RoleExistsAsync(NewRole)) await RoleManager.CreateAsync(new IdentityRole(NewRole));
            var roleResult = await UserManager.AddToRoleAsync(user, NewRole);
            if (!roleResult.Succeeded) { FormError = string.Join(" ", roleResult.Errors.Select(x => x.Description)); return; }
            await LoadUsersAsync();
            IsDialogOpen = false;
            SuccessMessage = $"User {user.UserName} was created in Microsoft SQL Server.";
        }
        finally { IsSaving = false; }
    }

    private static string GetRoleLabel(string role) => role == "Administrator" ? "Administrator" : "Staff";
    private static string GetInitials(string name) => string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => x[0])).ToUpperInvariant();
    private sealed record UserRow(string Id, string DisplayName, string UserName, string Email, string PhoneNumber, string Role, bool IsLocked);
}
