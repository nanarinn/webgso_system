using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Backend.UseCases.UCLSS027Audit;
using TestingSystem.Web.Models;

namespace TestingSystem.Web.Connectors.UseCases;

public static class LoginConnector
{
    public static IEndpointRouteBuilder MapLoginEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/account/login", LoginAsync).AllowAnonymous();
        endpoints.MapPost("/account/register", RegisterAsync).AllowAnonymous();
        endpoints.MapPost("/account/logout", LogoutAsync).RequireAuthorization();
        // This endpoint must remain anonymous. When the application cookie has already
        // expired, requiring authorization first redirects to Microsoft sign-in. The
        // timeout script submits this POST with the antiforgery token from the layout.
        endpoints.MapPost("/account/session/timeout", TimeoutLogoutAsync).AllowAnonymous();
        endpoints.MapGet("/account/session/keep-alive", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store, no-cache";
            return Results.NoContent();
        }).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IAuditTrailService auditTrail,
        IAntiforgery antiforgery)
    {
        await antiforgery.ValidateRequestAsync(context);
        var form = await context.Request.ReadFormAsync();
        var identifier = form["identifier"].ToString().Trim();
        var password = form["password"].ToString();
        var returnUrl = SafeReturnUrl(form["returnUrl"]);

        var user = await userManager.FindByNameAsync(identifier);
        if (user is null && identifier.Contains('@'))
        {
            var normalizedEmail = userManager.NormalizeEmail(identifier);
            user = await userManager.Users.FirstOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail);
        }

        if (user is null || user.IsEntraUser)
        {
            await auditTrail.RecordAsync(identifier, "Login", "Failed", "Authentication",
                detail: "Invalid local account credentials.", correlationId: context.TraceIdentifier);
            return Results.LocalRedirect(LoginError("invalid", returnUrl));
        }

        if (user.AccessStatus != UserAccessStatus.Approved)
        {
            var error = user.AccessStatus switch
            {
                UserAccessStatus.Pending => "access-pending",
                UserAccessStatus.Rejected => "access-rejected",
                _ => "access-inactive"
            };
            await auditTrail.RecordAsync(identifier, "Login", "Failed", "Authentication", user.Id,
                $"Local account access status is {user.AccessStatus}.", context.TraceIdentifier);
            return Results.LocalRedirect(LoginError(error, returnUrl));
        }

        var result = await signInManager.PasswordSignInAsync(user, password, false, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            var error = result.IsLockedOut ? "locked" : "invalid";
            await auditTrail.RecordAsync(identifier, "Login", "Failed", "Authentication", user.Id,
                result.IsLockedOut ? "Local account is temporarily locked." : "Invalid local account credentials.",
                context.TraceIdentifier);
            return Results.LocalRedirect(LoginError(error, returnUrl));
        }

        await auditTrail.RecordAsync(AuditTrailService.FormatActor(user.DisplayName, user.Email),
            "Login", "Success", "Authentication", user.Id,
            "Signed in with a local account.", context.TraceIdentifier);
        return Results.LocalRedirect(returnUrl);
    }

    private static async Task<IResult> RegisterAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        IUserAccessNotificationService notifications,
        IAuditTrailService auditTrail,
        IAntiforgery antiforgery)
    {
        await antiforgery.ValidateRequestAsync(context);
        var form = await context.Request.ReadFormAsync();
        var displayName = form["displayName"].ToString().Trim();
        var userName = form["userName"].ToString().Trim();
        var email = form["email"].ToString().Trim();
        var password = form["password"].ToString();
        var confirmPassword = form["confirmPassword"].ToString();

        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(userName)
            || string.IsNullOrWhiteSpace(email) || password != confirmPassword)
            return Results.LocalRedirect("/register?error=invalid");

        if (await userManager.FindByNameAsync(userName) is not null
            || await userManager.Users.AnyAsync(item => item.NormalizedEmail == userManager.NormalizeEmail(email)))
            return Results.LocalRedirect("/register?error=exists");

        var user = new ApplicationUser
        {
            UserName = userName,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName,
            AccessStatus = UserAccessStatus.Pending,
            AccessRequestedAt = DateTimeOffset.UtcNow,
            IsEntraUser = false
        };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            return Results.LocalRedirect("/register?error=password");

        await auditTrail.RecordAsync(AuditTrailService.FormatActor(user.DisplayName, user.Email),
            "Request user access", "Success", "ApplicationUser", user.Id,
            "A local account was created and is pending administrator approval.", context.TraceIdentifier);
        await notifications.QueueRequestAsync(user);
        return Results.LocalRedirect("/login?registered=pending");
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context,
        SignInManager<ApplicationUser> signInManager,
        IAuditTrailService auditTrail,
        IAntiforgery antiforgery)
    {
        await antiforgery.ValidateRequestAsync(context);
        var actor = context.User;
        var timedOut = string.Equals(context.Request.Query["reason"], "timeout", StringComparison.OrdinalIgnoreCase);
        await signInManager.SignOutAsync();
        await auditTrail.RecordAsync(actor, "Logout", "Success", "Authentication",
            detail: timedOut
                ? "Session timeout — user logged out automatically after 15 minutes of inactivity."
                : "User logged out manually.",
            correlationId: context.TraceIdentifier);
        return Results.LocalRedirect("/login");
    }

    private static async Task<IResult> TimeoutLogoutAsync(
        HttpContext context,
        SignInManager<ApplicationUser> signInManager,
        IAuditTrailService auditTrail,
        IAntiforgery antiforgery)
    {
        await antiforgery.ValidateRequestAsync(context);
        var actor = context.User;
        var wasAuthenticated = actor.Identity?.IsAuthenticated == true;
        await signInManager.SignOutAsync();
        if (wasAuthenticated)
        {
            await auditTrail.RecordAsync(actor, "Logout", "Success", "Authentication",
                detail: "Session timeout — user logged out automatically after 15 minutes of inactivity.",
                correlationId: context.TraceIdentifier);
        }
        return Results.NoContent();
    }

    private static string LoginError(string error, string returnUrl) =>
        $"/login?error={Uri.EscapeDataString(error)}&returnUrl={Uri.EscapeDataString(returnUrl)}";

    private static string SafeReturnUrl(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal)
        && !value.Contains('\r') && !value.Contains('\n')
            ? value
            : "/";

}
