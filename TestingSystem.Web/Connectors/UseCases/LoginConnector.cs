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
        await auditTrail.RecordAsync(actor, timedOut ? "Session timeout" : "Logout", "Success",
            detail: timedOut ? "Signed out automatically after 15 minutes of inactivity." : null,
            correlationId: context.TraceIdentifier);
        return Results.LocalRedirect("/login");
    }

}
