using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Net;
using TestingSystem.Web.Data;

namespace TestingSystem.Web.Connectors.UseCases;

public sealed record DashboardRecentRecord(
    string Reference,
    string DisplayReference,
    string PlantName,
    string DataType,
    string Status,
    DateTimeOffset UpdatedAt);

public sealed record DashboardMetricItem(
    string Name,
    string Description,
    string Reference,
    string Status,
    DateTimeOffset UpdatedAt);

public sealed record DashboardResponse(
    IReadOnlyList<DashboardRecentRecord> RecentRecords,
    IReadOnlyList<DashboardMetricItem> Plants,
    IReadOnlyList<DashboardMetricItem> SuccessfulDdq,
    IReadOnlyList<DashboardMetricItem> FailedDdq,
    IReadOnlyList<DashboardMetricItem> ActiveMmf);

public sealed record RepositoryStatusResponse(
    string Database,
    bool Connected,
    int Users,
    int Plants,
    int CollectionJobs,
    int Readings,
    int CollectionSchedules,
    int ReportSchedules,
    int ReportExecutions,
    int OpenAnomalies);

public interface IRepositoryBackendApi
{
    Task<DashboardResponse> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<RepositoryStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default);
}

public sealed class SqlRepositoryBackendApi(IDbContextFactory<ApplicationDbContext> contextFactory)
    : IRepositoryBackendApi
{
    public async Task<DashboardResponse> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var plants = await database.LssPlants.AsNoTracking()
            .Where(plant => plant.IsActive)
            .OrderByDescending(plant => plant.CreatedAt)
            .ThenByDescending(plant => plant.Id)
            .ToListAsync(cancellationToken);
        var jobs = await database.CollectionJobs.AsNoTracking()
            .OrderByDescending(job => job.StartedAt)
            .Join(database.LssPlants.AsNoTracking(), job => job.PlantId, plant => plant.Id,
                (job, plant) => new { Job = job, Plant = plant })
            .Take(100)
            .ToListAsync(cancellationToken);

        var recent = jobs.Take(4).Select(item => new DashboardRecentRecord(
            item.Job.CollectionId,
            DisplayCollectionReference(item.Job.CollectionId),
            item.Plant.Name,
            item.Job.DataType,
            item.Job.Status == "Completed" ? "Successful" : item.Job.Status,
            item.Job.StartedAt)).ToList();
        var plantItems = plants.Select(plant => new DashboardMetricItem(
            plant.Name,
            $"{plant.State} · {plant.CapacityMw:N0} MW · API configuration registered",
            plant.PlantId,
            plant.IsActive ? "Active" : "Inactive",
            plant.CreatedAt)).ToList();
        var successful = jobs.Where(item => item.Job.DataType != "MMF" && item.Job.Status == "Completed")
            .Select(item => CreateRequestItem(item.Job.CollectionId, item.Plant.Name, item.Job.StartedAt, "Successful"))
            .ToList();
        var failed = jobs.Where(item => item.Job.DataType != "MMF" && item.Job.Status == "Failed")
            .Select(item => CreateRequestItem(item.Job.CollectionId, item.Plant.Name, item.Job.StartedAt, "Failed"))
            .ToList();
        var activeMmf = plants.Where(plant => !string.IsNullOrWhiteSpace(plant.MmfApiEndpoint))
            .Select(plant => new DashboardMetricItem(
                plant.Name,
                "MMF API endpoint configured and active",
                plant.PlantId,
                "Active",
                plant.CreatedAt)).ToList();

        return new(recent, plantItems, successful, failed, activeMmf);
    }

    public async Task<RepositoryStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var databaseLabel = GetDatabaseLabel(database);
        bool connected;
        try
        {
            connected = await database.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            connected = false;
        }

        // Keep the status endpoint useful when the configured SQL server is offline.
        // Counts are only queried after the same connection has passed the connectivity check.
        if (!connected)
            return new(databaseLabel, false, 0, 0, 0, 0, 0, 0, 0, 0);

        return new(
            databaseLabel,
            true,
            await database.Users.CountAsync(cancellationToken),
            await database.LssPlants.CountAsync(cancellationToken),
            await database.CollectionJobs.CountAsync(cancellationToken),
            await database.SolarApiReadings.CountAsync(cancellationToken),
            await database.CollectionSchedules.CountAsync(cancellationToken),
            await database.ReportSchedules.CountAsync(cancellationToken),
            await database.ReportExecutions.CountAsync(cancellationToken),
            await database.DataAnomalies.CountAsync(item => item.Status == "Open", cancellationToken));
    }

    private static string GetDatabaseLabel(DbContext database)
    {
        if (database.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) != true)
            return database.Database.ProviderName?.Split('.').LastOrDefault() ?? "Configured database";

        var connection = new SqlConnectionStringBuilder(database.Database.GetDbConnection().ConnectionString);
        var server = connection.DataSource.Trim();
        var host = server
            .Replace("tcp:", "", StringComparison.OrdinalIgnoreCase)
            .Replace("np:", "", StringComparison.OrdinalIgnoreCase)
            .Replace("lpc:", "", StringComparison.OrdinalIgnoreCase)
            .Split(',', '\\')[0]
            .Trim('[', ']', ' ');

        var isLocal = host is "." or "(local)" or "localhost"
            || host.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);

        return isLocal ? "Microsoft SQL Server · Local" : "Microsoft SQL Server · Network";
    }

    private static DashboardMetricItem CreateRequestItem(
        string collectionId, string plantName, DateTimeOffset updatedAt, string status) => new(
            $"{plantName} DDQ Submission",
            $"DDQ collection data for {plantName}",
            collectionId,
            status,
            updatedAt);

    private static string DisplayCollectionReference(string reference)
    {
        if (reference.StartsWith("CMD-", StringComparison.OrdinalIgnoreCase))
        {
            var parts = reference.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3) return $"{parts[0]}-{parts[1]}-{parts[2]}";
        }
        if (reference.StartsWith("COL-SCH-", StringComparison.OrdinalIgnoreCase))
        {
            var parts = reference.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3) return $"COL-SCH-{parts[2]}";
        }
        return reference.Length <= 20 ? reference : $"{reference[..19]}…";
    }
}

public static class RepositoryBackendApiEndpoints
{
    public static IEndpointRouteBuilder MapRepositoryBackendApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api").RequireAuthorization();
        api.MapGet("/dashboard", async (IRepositoryBackendApi backend, CancellationToken cancellationToken) =>
            Results.Ok(await backend.GetDashboardAsync(cancellationToken)));
        api.MapGet("/status", async (IRepositoryBackendApi backend, CancellationToken cancellationToken) =>
            Results.Ok(await backend.GetStatusAsync(cancellationToken)));
        return endpoints;
    }
}
