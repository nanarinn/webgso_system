using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Components;
using TestingSystem.Web.Configuration;
using TestingSystem.Web.Data;
using TestingSystem.Web.HealthChecks;
using TestingSystem.Web.Models;
using TestingSystem.Web.Services;
using TestingSystem.Web.Backend.UseCases.UCLSS027Audit;
using TestingSystem.Web.Connectors.UseCases;

var builder = WebApplication.CreateBuilder(args);
var externalSecretsFile = builder.Configuration["SBLSS_SECRETS_FILE"];
if (!string.IsNullOrWhiteSpace(externalSecretsFile))
{
    if (!Path.IsPathFullyQualified(externalSecretsFile))
        throw new InvalidOperationException("SBLSS_SECRETS_FILE must be an absolute path.");

    builder.Configuration.AddJsonFile(externalSecretsFile, optional: false, reloadOnChange: false);
    builder.Configuration.AddEnvironmentVariables();
}

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.Configure<DatabaseSettings>(builder.Configuration.GetSection(DatabaseSettings.SectionName));
builder.Services.AddHttpClient("PlantDataApi")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<IReportEmailSender, GraphReportEmailSender>();
builder.Services.AddScoped<ILssDataApiClient, PrototypeLssDataApiClient>();
builder.Services.AddScoped<ICollectionCommandBus, SqlCollectionCommandBus>();
builder.Services.AddScoped<CollectionCommandProcessor>();
builder.Services.AddHostedService<CollectionCommandWorker>();
builder.Services.AddHostedService<ScheduledCollectionWorker>();
builder.Services.AddHostedService<ScheduledReportWorker>();
builder.Services.AddScoped<IAuditTrailService, AuditTrailService>();
builder.Services.AddScoped<IRepositoryBackendApi, SqlRepositoryBackendApi>();
builder.Services.AddScoped<IUserAccessNotificationService, UserAccessNotificationService>();
builder.Services.AddAntiforgery();

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
builder.Services.AddEntraAuthentication(builder.Configuration);

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "SingleBuyer.LssRepository";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = SessionSecurity.IdleTimeout;
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/login";
    options.SlidingExpiration = true;
});

builder.Services.AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>("sql-server");

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (!app.Environment.IsDevelopment()) return;

        context.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
        context.Context.Response.Headers.Pragma = "no-cache";
        context.Context.Response.Headers.Expires = "0";
    }
});
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

await DatabaseInitializer.InitialiseAsync(app.Services, app.Configuration);

if (args.Contains("--update-database", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("Microsoft SQL Server schema is aligned with the current SBLSS system.");
    return;
}

if (args.Contains("--clear-collection-data", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    database.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
    var jobs = await database.CollectionJobs.CountAsync();
    var responses = await database.RawApiResponses.CountAsync();
    var readings = await database.SolarApiReadings.CountAsync();
    var anomalies = await database.DataAnomalies.CountAsync();

    var strategy = database.Database.CreateExecutionStrategy();
    await strategy.ExecuteAsync(async () =>
    {
        await using var transaction = await database.Database.BeginTransactionAsync();
        await database.DataAnomalies.ExecuteDeleteAsync();
        await database.SolarApiReadings.ExecuteDeleteAsync();
        await database.RawApiResponses.ExecuteDeleteAsync();
        await database.CollectionJobs.ExecuteDeleteAsync();
        await transaction.CommitAsync();
    });

    Console.WriteLine("Collection Management test data cleared.");
    Console.WriteLine($"CollectionJobs deleted:   {jobs:N0}");
    Console.WriteLine($"RawApiResponses deleted:  {responses:N0}");
    Console.WriteLine($"SolarApiReadings deleted: {readings:N0}");
    Console.WriteLine($"DataAnomalies deleted:    {anomalies:N0}");
    Console.WriteLine("Plants, API configurations, collection schedules and user accounts were retained.");
    return;
}

if (args.Contains("--reset-workflow-data", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    database.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
    var strategy = database.Database.CreateExecutionStrategy();
    await strategy.ExecuteAsync(async () =>
    {
        await using var transaction = await database.Database.BeginTransactionAsync();
        await database.ReportDeliveryRecipients.ExecuteDeleteAsync();
        await database.ReportDeliveryAttempts.ExecuteDeleteAsync();
        await database.ReportScheduleRecipients.ExecuteDeleteAsync();
        await database.ReportSchedules.ExecuteDeleteAsync();
        await database.ReportExecutions.ExecuteDeleteAsync();
        await database.DataAnomalies.ExecuteDeleteAsync();
        await database.SolarApiReadings.ExecuteDeleteAsync();
        await database.RawApiResponses.ExecuteDeleteAsync();
        await database.CollectionJobs.ExecuteDeleteAsync();
        await database.CollectionSchedules.ExecuteDeleteAsync();
        await database.PlantApiConfigurations.ExecuteDeleteAsync();
        await database.LssPlants.ExecuteDeleteAsync();
        await database.AuditEvents.ExecuteDeleteAsync();
        await transaction.CommitAsync();
    });

    Console.WriteLine("SBLSS operational workflow data was reset.");
    Console.WriteLine("Removed: plants, API configurations, collection schedules/history, readings, anomalies, reports and audit events.");
    Console.WriteLine("Retained: Microsoft Entra accounts, roles, access approvals and system validation rules.");
    return;
}

var superAdminIndex = Array.FindIndex(args, value => value.Equals("--set-superadmins", StringComparison.OrdinalIgnoreCase));
if (superAdminIndex >= 0)
{
    var emails = args.Skip(superAdminIndex + 1)
        .TakeWhile(value => !value.StartsWith("--", StringComparison.Ordinal))
        .Select(value => value.Trim())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (emails.Length == 0)
        throw new ArgumentException("Provide at least one account email after --set-superadmins.");

    await using var scope = app.Services.CreateAsyncScope();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    if (!await roles.RoleExistsAsync(SystemRoles.SuperAdministrator))
        EnsureIdentitySucceeded(await roles.CreateAsync(new IdentityRole(SystemRoles.SuperAdministrator)));

    foreach (var email in emails)
    {
        var user = await users.FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"No SBLSS account was found for '{email}'. The user must sign in once before promotion.");
        var existingRoles = await users.GetRolesAsync(user);
        var obsoleteRoles = existingRoles.Where(role =>
            role is SystemRoles.Administrator or SystemRoles.Staff or SystemRoles.LegacyReviewer).ToArray();
        if (obsoleteRoles.Length > 0)
            EnsureIdentitySucceeded(await users.RemoveFromRolesAsync(user, obsoleteRoles));
        if (!await users.IsInRoleAsync(user, SystemRoles.SuperAdministrator))
            EnsureIdentitySucceeded(await users.AddToRoleAsync(user, SystemRoles.SuperAdministrator));

        user.AccessStatus = UserAccessStatus.Approved;
        user.AccessReviewedAt = DateTimeOffset.UtcNow;
        user.AccessReviewedBy = "System administration command";
        EnsureIdentitySucceeded(await users.UpdateAsync(user));
        EnsureIdentitySucceeded(await users.UpdateSecurityStampAsync(user));
        Console.WriteLine($"{user.DisplayName} <{email}> is now an approved Super Admin.");
    }
    return;
}

if (args.Contains("--seed-dummy-api", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await DummyApiDataSeeder.SeedAsync(database);
    var jobs = await database.CollectionJobs.CountAsync(job => job.Mode == "Simulated API");
    var rawResponses = await database.RawApiResponses.CountAsync();
    var readings = await database.SolarApiReadings.CountAsync();
    var apiConfigurations = await database.PlantApiConfigurations.CountAsync();
    var collectionSchedules = await database.CollectionSchedules.CountAsync();
    var reportSchedules = await database.ReportSchedules.CountAsync();
    var reportDeliveries = await database.ReportDeliveryAttempts.CountAsync();
    var reportExecutions = await database.ReportExecutions.CountAsync();
    var anomalies = await database.DataAnomalies.CountAsync();
    var auditEvents = await database.AuditEvents.CountAsync();
    Console.WriteLine();
    Console.WriteLine("Simulated API seed completed.");
    Console.WriteLine($"CollectionJobs:   {jobs:N0}");
    Console.WriteLine($"RawApiResponses:  {rawResponses:N0}");
    Console.WriteLine($"SolarApiReadings: {readings:N0}");
    Console.WriteLine($"API configurations: {apiConfigurations:N0}");
    Console.WriteLine($"Collection schedules: {collectionSchedules:N0}");
    Console.WriteLine($"Report schedules: {reportSchedules:N0}");
    Console.WriteLine($"Report deliveries: {reportDeliveries:N0}");
    Console.WriteLine($"Report executions: {reportExecutions:N0}");
    Console.WriteLine($"Data anomalies: {anomalies:N0}");
    Console.WriteLine($"Audit events: {auditEvents:N0}");
    if (jobs == 0 || rawResponses == 0 || readings == 0 || apiConfigurations == 0
        || collectionSchedules == 0 || reportSchedules == 0 || reportDeliveries == 0 || reportExecutions == 0
        || anomalies == 0 || auditEvents == 0)
        throw new InvalidOperationException("Simulated API seed produced zero database rows.");
    return;
}

var sandImportIndex = Array.FindIndex(args, value => value.Equals("--import-sand-data", StringComparison.OrdinalIgnoreCase));
if (sandImportIndex >= 0)
{
    if (sandImportIndex + 1 >= args.Length) throw new ArgumentException("Provide the SAND workbook path after --import-sand-data.");
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await SandForecastDataImporter.ImportAsync(database, args[sandImportIndex + 1]);
    Console.WriteLine("Bidor SAND Town import completed.");
    return;
}

var mmfImportIndex = Array.FindIndex(args, value => value.Equals("--import-mmf-data", StringComparison.OrdinalIgnoreCase));
if (mmfImportIndex >= 0)
{
    if (mmfImportIndex + 1 >= args.Length) throw new ArgumentException("Provide the MMF workbook path after --import-mmf-data.");
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        await SampleApiDataImporter.ImportMmfAsync(database, args[mmfImportIndex + 1]);
        Console.WriteLine("MMF raw API import and averaged translation completed and verified.");
    }
    catch (Exception exception)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine();
        Console.WriteLine($"MMF IMPORT ROOT ERROR: {exception.GetBaseException().Message}");
        Console.ResetColor();
        throw;
    }
    return;
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapLoginEndpoints();
app.MapEntraLogin();
app.MapRepositoryBackendApi();

app.MapHealthChecks("/health");
app.MapGet("/api/ping", () => Results.Ok(new { status = "Healthy" }))
    .RequireAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapGet("/api/test-data/mmf/{plantCode}", (string plantCode, DateOnly? date, int? interval) =>
    {
        var selectedInterval = interval ?? 15;
        if (selectedInterval is not (15 or 30 or 60))
            return Results.BadRequest(new { message = "MMF test interval must be 15, 30 or 60 minutes." });
        var selectedDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        return Results.Ok(MmfTestApiPayload.Build(plantCode, selectedDate, selectedInterval));
    });
}

app.Run();

static void EnsureIdentitySucceeded(IdentityResult result)
{
    if (!result.Succeeded)
        throw new InvalidOperationException(string.Join(" ", result.Errors.Select(error => error.Description)));
}
