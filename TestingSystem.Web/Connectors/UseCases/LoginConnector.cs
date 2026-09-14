using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using TestingSystem.Web.Backend.UseCases.UCLSS027Audit;
using TestingSystem.Web.Models;

namespace TestingSystem.Web.Connectors.UseCases;

public static class LoginConnector
{
    public static IEndpointRouteBuilder MapLoginEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/account/login", LoginAsync).AllowAnonymous();
        endpoints.MapPost("/account/logout", LogoutAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        HttpContext context,
        SignInManager<ApplicationUser> signInManager,
        IAuditTrailService auditTrail,
        IAntiforgery antiforgery)
    {
        await antiforgery.ValidateRequestAsync(context);
        var form = await context.Request.ReadFormAsync();
        var username = form["username"].ToString().Trim();
        var password = form["password"].ToString();
        var safeReturnUrl = GetSafeReturnUrl(form["returnUrl"].ToString());
        var result = await signInManager.PasswordSignInAsync(
            username, password, isPersistent: false, lockoutOnFailure: true);

        await auditTrail.RecordAsync(
            username, "Login", result.Succeeded ? "Success" : "Failed",
            "ApplicationUser", username,
            result.IsLockedOut ? "Account locked after failed authentication." : null,
            context.TraceIdentifier);

        return result.Succeeded
            ? Results.LocalRedirect(safeReturnUrl)
            : Results.LocalRedirect($"/login?error=invalid&returnUrl={Uri.EscapeDataString(safeReturnUrl)}");
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context,
        SignInManager<ApplicationUser> signInManager,
        IAuditTrailService auditTrail,
        IAntiforgery antiforgery)
    {
        await antiforgery.ValidateRequestAsync(context);
        var actor = context.User.Identity?.Name ?? "Unknown";
        await signInManager.SignOutAsync();
        await auditTrail.RecordAsync(actor, "Logout", "Success",
            correlationId: context.TraceIdentifier);
        return Results.LocalRedirect("/login");
    }

    private static string GetSafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl)
        && returnUrl.StartsWith('/')
        && !returnUrl.StartsWith("//")
            ? returnUrl
            : "/";
}
