using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Components;
using TestingSystem.Web.Configuration;
using TestingSystem.Web.Data;
using TestingSystem.Web.HealthChecks;
using TestingSystem.Web.Models;
using TestingSystem.Web.Services;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.Configure<DatabaseSettings>(builder.Configuration.GetSection(DatabaseSettings.SectionName));
builder.Services.AddHttpClient();
builder.Services.AddScoped<ILssDataApiClient, PrototypeLssDataApiClient>();

builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
        sqlOptions.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null)));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
        options.User.RequireUniqueEmail = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "SingleBuyer.LssRepository";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/login";
    options.SlidingExpiration = true;
});

builder.Services.AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>("sql-server");

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

await DatabaseInitializer.InitialiseAsync(app.Services, app.Configuration);

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/account/login", async (
    HttpContext context,
    IAntiforgery antiforgery,
    SignInManager<ApplicationUser> signInManager) =>
{
    await antiforgery.ValidateRequestAsync(context);
    var form = await context.Request.ReadFormAsync();
    var userName = form["username"].ToString().Trim();
    var password = form["password"].ToString();
    var returnUrl = GetSafeReturnUrl(form["returnUrl"].ToString());

    var result = await signInManager.PasswordSignInAsync(
        userName,
        password,
        isPersistent: false,
        lockoutOnFailure: true);

    return result.Succeeded
        ? Results.LocalRedirect(returnUrl)
        : Results.LocalRedirect($"/login?error=invalid&returnUrl={Uri.EscapeDataString(returnUrl)}");
});

app.MapPost("/account/logout", async (
    HttpContext context,
    IAntiforgery antiforgery,
    SignInManager<ApplicationUser> signInManager) =>
{
    await antiforgery.ValidateRequestAsync(context);
    await signInManager.SignOutAsync();
    return Results.LocalRedirect("/login");
});

app.MapHealthChecks("/health");
app.MapGet("/api/ping", () => Results.Ok(new
{
    name = "Single Buyer Large Scale Solar Repository",
    stack = ".NET 8 Blazor",
    date = DateTimeOffset.UtcNow
}));

app.MapGet("/api/status", async (IDbContextFactory<ApplicationDbContext> contextFactory) =>
{
    await using var database = await contextFactory.CreateDbContextAsync();
    return Results.Ok(new
    {
        database = "Microsoft SQL Server",
        connected = await database.Database.CanConnectAsync(),
        users = await database.Users.CountAsync(),
        documents = await database.SolarDocuments.CountAsync()
    });
}).RequireAuthorization();

app.Run();

static string GetSafeReturnUrl(string? returnUrl)
{
    return !string.IsNullOrWhiteSpace(returnUrl)
        && returnUrl.StartsWith('/')
        && !returnUrl.StartsWith("//")
            ? returnUrl
            : "/";
}
