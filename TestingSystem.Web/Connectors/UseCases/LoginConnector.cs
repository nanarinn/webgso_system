using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using TestingSystem.Web.Backend.UseCases.UCLSS027Audit;
using TestingSystem.Web.Models;

namespace TestingSystem.Web.Connectors.UseCases;

public static class LoginConnector
{
    public static IEndpointRouteBuilder MapLoginEndpoints(this IEndpointRouteBuilder endpoints)
    {
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

}
