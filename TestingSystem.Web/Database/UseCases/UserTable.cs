using Microsoft.AspNetCore.Identity;

namespace TestingSystem.Web.Models;

public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
}
