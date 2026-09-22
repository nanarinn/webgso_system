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
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.Configure<DatabaseSettings>(builder.Configuration.GetSection(DatabaseSettings.SectionName));
builder.Services.AddHttpClient();
builder.Services.AddScoped<ILssDataApiClient, PrototypeLssDataApiClient>();
builder.Services.AddScoped<IAuditTrailService, AuditTrailService>();
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
app.MapSystemStatusEndpoint();

app.MapHealthChecks("/health");
app.MapGet("/api/ping", () => Results.Ok(new
{
    name = "Single Buyer Large Scale Solar Repository",
    stack = ".NET 8 Blazor",
    date = DateTimeOffset.UtcNow
}));

app.Run();
