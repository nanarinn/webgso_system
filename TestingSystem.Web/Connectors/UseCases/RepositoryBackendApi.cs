using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Net;
using TestingSystem.Web.Data;
using TestingSystem.Web.Services;

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
    DateTimeOffset UpdatedAt,
    string DataType = "",
    DateTime SubmissionDateTime = default);

public sealed record DashboardResponse(
    IReadOnlyList<DashboardRecentRecord> RecentRecords,
    IReadOnlyList<DashboardMetricItem> Plants,
    IReadOnlyList<DashboardMetricItem> SuccessfulDdq,
    IReadOnlyList<DashboardMetricItem> FailedDdq,
    IReadOnlyList<DashboardMetricItem> ActiveMmf,
    IReadOnlyList<DashboardDdqTypeSummary> DdqTypes);

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
            .Select(plant => new
            {
                plant.Id,
                plant.PlantId,
                plant.Name,
                plant.State,
                plant.CapacityMw,
                plant.MmfApiEndpoint,
                plant.CreatedAt,
                plant.IsActive
            })
            .ToListAsync(cancellationToken);
        var jobs = await database.CollectionJobs.AsNoTracking()
            .OrderByDescending(job => job.StartedAt)
            .ThenByDescending(job => job.Id)
            .Select(job => new
            {
                job.CollectionId,
                job.PlantId,
                job.DataType,
                job.Status,
                job.StartedAt,
                job.SubmissionDateTime
            })
            .Take(5000)
            .Join(database.LssPlants.AsNoTracking().Select(plant => new { plant.Id, plant.Name }),
                job => job.PlantId, plant => plant.Id,
                (job, plant) => new { Job = job, Plant = plant })
            .ToListAsync(cancellationToken);
        var ddqSummaryRows = await database.CollectionJobs.AsNoTracking()
            .Where(job => job.DataType != "MMF")
            .GroupBy(job => new { job.DataType, job.SubmissionDateTime, job.Status })
            .Select(group => new DashboardDdqSummarySource(group.Key.DataType, group.Key.Status,
                group.Key.SubmissionDateTime, group.Max(job => job.StartedAt),
                group.Select(job => job.PlantId).Distinct().Count()))
            .ToListAsync(cancellationToken);
        var ddqTypes = DashboardDdqSummaryPolicy.Build(ddqSummaryRows);
        var latestSubmissionTimes = ddqTypes
            .Where(summary => summary.SubmissionDateTime.HasValue)
            .Select(summary => summary.SubmissionDateTime!.Value)
            .Distinct()
            .ToArray();
        var latestDdqJobs = await database.CollectionJobs.AsNoTracking()
            .Where(job => job.DataType != "MMF" && latestSubmissionTimes.Contains(job.SubmissionDateTime))
            .Select(job => new
            {
                job.CollectionId,
                job.PlantId,
                job.DataType,
                job.Status,
                job.StartedAt,
                job.SubmissionDateTime
            })
            .Join(database.LssPlants.AsNoTracking().Select(plant => new { plant.Id, plant.Name }),
                job => job.PlantId, plant => plant.Id,
                (job, plant) => new { Job = job, Plant = plant })
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
        var successful = latestDdqJobs.Where(item => item.Job.Status == "Completed")
            .Select(item => CreateRequestItem(item.Job.CollectionId, item.Plant.Name, item.Job.DataType,
                item.Job.StartedAt, item.Job.SubmissionDateTime, "Successful"))
            .ToList();
        var failed = latestDdqJobs.Where(item => item.Job.Status == "Failed")
            .Select(item => CreateRequestItem(item.Job.CollectionId, item.Plant.Name, item.Job.DataType,
                item.Job.StartedAt, item.Job.SubmissionDateTime, "Failed"))
            .ToList();
        var activeMmf = plants.Where(plant => !string.IsNullOrWhiteSpace(plant.MmfApiEndpoint))
            .Select(plant => new DashboardMetricItem(
                plant.Name,
                "MMF API endpoint configured and active",
                plant.PlantId,
                "Active",
                plant.CreatedAt)).ToList();

        return new(recent, plantItems, successful, failed, activeMmf, ddqTypes);
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
        if (database.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) != true)
            return database.Database.ProviderName?.Split('.').LastOrDefault() ?? "Configured database";

        var connection = new NpgsqlConnectionStringBuilder(database.Database.GetDbConnection().ConnectionString);
        var host = (connection.Host ?? string.Empty).Trim();

        var isLocal = host is "." or "(local)" or "localhost"
            || host.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);

        return isLocal ? "PostgreSQL · Local" : "PostgreSQL · Network";
    }

    private static DashboardMetricItem CreateRequestItem(
        string collectionId, string plantName, string dataType, DateTimeOffset updatedAt,
        DateTime submissionDateTime, string status) => new(
            $"{plantName} {DdqDatasetPolicy.CanonicalName(dataType)} Submission",
            $"{DdqDatasetPolicy.CanonicalName(dataType)} collection data for {plantName}",
            collectionId,
            status,
            updatedAt,
        DdqDatasetPolicy.CanonicalName(dataType),
        submissionDateTime);

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
