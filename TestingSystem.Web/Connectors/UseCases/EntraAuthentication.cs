using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Models;

namespace TestingSystem.Web.Connectors.UseCases;

public static class EntraAuthentication
{
    public const string Scheme = "MicrosoftEntra";

    public static bool IsReady(IConfiguration configuration) =>
        configuration.GetValue<bool>("EntraId:Enabled")
        && Guid.TryParse(configuration["EntraId:TenantId"], out _)
        && Guid.TryParse(configuration["EntraId:ClientId"], out _)
        && !string.IsNullOrWhiteSpace(configuration["EntraId:ClientSecret"]);

    public static void AddEntraAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (!IsReady(configuration)) return;

        var tenant = configuration["EntraId:TenantId"]!;
        services.AddAuthentication().AddOpenIdConnect(Scheme, options =>
        {
            options.Authority = $"https://login.microsoftonline.com/{tenant}/v2.0";
            options.ClientId = configuration["EntraId:ClientId"];
            options.ClientSecret = configuration["EntraId:ClientSecret"];
            options.CallbackPath = "/signin-oidc";
            options.SignInScheme = IdentityConstants.ApplicationScheme;
            options.ResponseType = "code";
            options.UsePkce = true;
            options.Prompt = "select_account";
            options.MapInboundClaims = false;
            options.SaveTokens = true;
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");

            options.Events.OnTokenValidated = async context =>
            {
                try
                {
                    var remotePrincipal = context.Principal!;
                    var (key, roles) = EntraAccess.Read(remotePrincipal, tenant);
                    var services = context.HttpContext.RequestServices;
                    var database = services.GetRequiredService<ApplicationDbContext>();
                    var users = services.GetRequiredService<UserManager<ApplicationUser>>();
                    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
                    var signInManager = services.GetRequiredService<SignInManager<ApplicationUser>>();

                    await database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                    {
                        database.ChangeTracker.Clear();
                        await using var transaction = await database.Database.BeginTransactionAsync();
                        var user = await users.FindByLoginAsync(Scheme, key);
                        if (user is null)
                        {
                            var displayName = remotePrincipal.FindFirst("name")?.Value ?? "Microsoft user";
                            user = new ApplicationUser
                            {
                                UserName = "entra_" + key.Replace(":", "_"),
                                DisplayName = displayName[..Math.Min(displayName.Length, 160)],
                                Email = remotePrincipal.FindFirst("email")?.Value
                                    ?? remotePrincipal.FindFirst("preferred_username")?.Value
                                    ?? remotePrincipal.FindFirst("upn")?.Value,
                                EmailConfirmed = true
                            };
                            Check(await users.CreateAsync(user));
                            Check(await users.AddLoginAsync(
                                user, new UserLoginInfo(Scheme, key, "Microsoft")));
                        }

                        if (await users.IsLockedOutAsync(user) || !await signInManager.CanSignInAsync(user))
                            throw new InvalidOperationException("Account is unavailable.");

                        foreach (var role in roles)
                            if (!await roleManager.RoleExistsAsync(role))
                                Check(await roleManager.CreateAsync(new IdentityRole(role)));

                        var currentRoles = await users.GetRolesAsync(user);
                        Check(await users.RemoveFromRolesAsync(user, currentRoles.Except(roles)));
                        Check(await users.AddToRolesAsync(user, roles.Except(currentRoles)));
                        context.Principal = await signInManager.CreateUserPrincipalAsync(user);
                        await transaction.CommitAsync();
                    });
                }
                catch (Exception exception)
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("MicrosoftEntraSignIn");
                    logger.LogWarning(exception, "Microsoft Entra user provisioning failed.");
                    context.Fail("Microsoft sign-in could not be completed.");
                }
            };

            options.Events.OnRemoteFailure = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("MicrosoftEntraSignIn");
                logger.LogWarning(context.Failure, "Microsoft Entra authentication failed.");

                var error = context.Failure?.Message.Contains(
                    "assigned application role", StringComparison.OrdinalIgnoreCase) == true
                    ? "microsoft-role"
                    : context.Failure?.Message.Contains(
                        "access_denied", StringComparison.OrdinalIgnoreCase) == true
                        ? "microsoft-consent"
                        : "microsoft";
                context.HandleResponse();
                context.Response.Redirect($"/login?error={error}");
                return Task.CompletedTask;
            };

            options.Events.OnAccessDenied = context =>
            {
                context.HandleResponse();
                context.Response.Redirect("/login?error=microsoft-consent");
                return Task.CompletedTask;
            };
        });
    }

    public static void MapEntraLogin(this WebApplication app)
    {
        app.MapPost("/account/microsoft", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            if (!IsReady(app.Configuration) || !context.Request.IsHttps)
                return Results.LocalRedirect("/login?error=microsoft-setup");

            var form = await context.Request.ReadFormAsync();
            return Results.Challenge(new AuthenticationProperties
            {
                RedirectUri = EntraAccess.SafeReturnUrl(form["returnUrl"])
            }, [Scheme]);
        }).AllowAnonymous();
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException("Identity update failed.");
    }
}
