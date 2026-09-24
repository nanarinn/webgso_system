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
        await signInManager.SignOutAsync();
        await auditTrail.RecordAsync(actor, "Logout", "Success",
            correlationId: context.TraceIdentifier);
        return Results.LocalRedirect("/login");
    }

}
