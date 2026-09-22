using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Models;
using TestingSystem.Web.Backend.UseCases.UCLSS027Audit;

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
                    var identity = EntraAccess.Read(remotePrincipal, tenant);
                    var services = context.HttpContext.RequestServices;
                    var users = services.GetRequiredService<UserManager<ApplicationUser>>();
                    var signInManager = services.GetRequiredService<SignInManager<ApplicationUser>>();
                    var audit = services.GetRequiredService<IAuditTrailService>();
                    var user = await users.FindByLoginAsync(Scheme, identity.Key);
                    if (user is null)
                    {
                        user = new ApplicationUser
                        {
                            UserName = "entra_" + identity.ObjectId,
                            DisplayName = identity.DisplayName[..Math.Min(identity.DisplayName.Length, 160)],
                            Email = identity.Email,
                            EmailConfirmed = true,
                            IsEntraUser = true,
                            EntraObjectId = identity.ObjectId,
                            AccessStatus = UserAccessStatus.Pending,
                            AccessRequestedAt = DateTimeOffset.UtcNow
                        };
                        Check(await users.CreateAsync(user));
                        Check(await users.AddLoginAsync(user, new UserLoginInfo(Scheme, identity.Key, "Microsoft")));
                        await audit.RecordAsync(identity.Email, "RequestAccess", "Pending", "ApplicationUser", user.Id,
                            "Microsoft Entra identity verified; administrator approval is required.", context.HttpContext.TraceIdentifier);
                    }

                    if (await users.IsLockedOutAsync(user) || !await signInManager.CanSignInAsync(user))
                        throw new InvalidOperationException("SBLSS_ACCESS_INACTIVE");

                    if (user.AccessStatus != UserAccessStatus.Approved)
                        throw new InvalidOperationException(user.AccessStatus switch
                        {
                            UserAccessStatus.Rejected => "SBLSS_ACCESS_REJECTED",
                            UserAccessStatus.Inactive => "SBLSS_ACCESS_INACTIVE",
                            _ => "SBLSS_ACCESS_PENDING"
                        });

                    context.Principal = await signInManager.CreateUserPrincipalAsync(user);
                }
                catch (Exception exception)
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("MicrosoftEntraSignIn");
                    var accessResult = exception.GetBaseException().Message;
                    if (accessResult.StartsWith("SBLSS_ACCESS_", StringComparison.Ordinal))
                    {
                        logger.LogInformation("Microsoft Entra access stopped with application status {AccessStatus}.", accessResult);
                        context.Fail(accessResult);
                    }
                    else
                    {
                        logger.LogWarning(exception, "Microsoft Entra user provisioning failed.");
                        context.Fail("Microsoft sign-in could not be completed.");
                    }
                }
            };

            options.Events.OnRemoteFailure = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("MicrosoftEntraSignIn");
                logger.LogWarning(context.Failure, "Microsoft Entra authentication failed.");

                var error = EntraAccess.LoginError(context.Failure);
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
