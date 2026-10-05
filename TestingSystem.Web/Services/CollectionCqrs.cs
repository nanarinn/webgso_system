using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Models;

namespace TestingSystem.Web.Services;

public sealed record QueueCollectionCommand(
    int PlantId,
    string ApiType,
    string DataType,
    string Mode,
    DateTime SubmissionDateTime,
    string RequestedBy,
    int RetryCount = 0,
    bool ReplaceExisting = false,
    string? CollectionId = null,
    int? CollectionScheduleId = null);

public sealed record QueuedCollection(long JobId, string CollectionId);

public interface ICollectionCommandBus
{
    Task<QueuedCollection> EnqueueAsync(QueueCollectionCommand command, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QueuedCollection>> EnqueueManyAsync(
        IReadOnlyList<QueueCollectionCommand> commands,
        CancellationToken cancellationToken = default);
}

public sealed class SqlCollectionCommandBus(IDbContextFactory<ApplicationDbContext> contextFactory)
    : ICollectionCommandBus
{
    public async Task<QueuedCollection> EnqueueAsync(
        QueueCollectionCommand command,
        CancellationToken cancellationToken = default)
    {
        var queued = await EnqueueManyAsync([command], cancellationToken);
        return queued[0];
    }

    public async Task<IReadOnlyList<QueuedCollection>> EnqueueManyAsync(
        IReadOnlyList<QueueCollectionCommand> commands,
        CancellationToken cancellationToken = default)
    {
        if (commands.Count == 0) return [];

        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var batchStartedAt = DateTimeOffset.UtcNow;
        var jobs = commands.Select((command, index) =>
        {
            var queuedAt = batchStartedAt.AddMilliseconds(index);
            var apiType = command.ApiType.Equals("MMF", StringComparison.OrdinalIgnoreCase) ? "MMF" : "DDQ";
            return new CollectionJobEntity
            {
                CollectionId = CollectionCommandIdentity.Create(apiType, queuedAt, command.CollectionId),
                PlantId = command.PlantId,
                CollectionScheduleId = command.CollectionScheduleId,
                DataType = apiType == "MMF" ? "MMF" : command.DataType.Trim(),
                Mode = command.Mode.Trim(),
                SubmissionDate = DateOnly.FromDateTime(command.SubmissionDateTime),
                SubmissionDateTime = command.SubmissionDateTime,
                StartedAt = queuedAt,
                Status = CollectionJobStatus.Queued,
                ResponseMessage = $"{apiType} {command.DataType.Trim()} collection queued.",
                RequestedBy = Limit(command.RequestedBy, 160),
                RetryCount = Math.Clamp(command.RetryCount, 0, 5),
                ReplaceExisting = command.ReplaceExisting
            };
        }).ToList();

        database.CollectionJobs.AddRange(jobs);
        await database.SaveChangesAsync(cancellationToken);
        return jobs.Select(job => new QueuedCollection(job.Id, job.CollectionId)).ToList();
    }

    private static string Limit(string? value, int length) => string.IsNullOrWhiteSpace(value)
        ? "System" : value.Trim()[..Math.Min(value.Trim().Length, length)];
}

public static class CollectionJobStatus
{
    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}

public sealed class CollectionCommandProcessor(
    IDbContextFactory<ApplicationDbContext> contextFactory,
    ILssDataApiClient apiClient,
    ILogger<CollectionCommandProcessor> logger)
{
    private static readonly SemaphoreSlim DatabaseWriteGate = new(
        CollectionProcessingPolicy.MaximumConcurrentDatabaseWrites,
        CollectionProcessingPolicy.MaximumConcurrentDatabaseWrites);

    public async Task ProcessAsync(long jobId, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        LssApiCollectionResult? apiResult = null;
        try
        {
            await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
            var job = await database.CollectionJobs.SingleAsync(item => item.Id == jobId, cancellationToken);
            var plant = await database.LssPlants.AsNoTracking().Include(item => item.ApiConfigurations)
                .SingleOrDefaultAsync(item => item.Id == job.PlantId, cancellationToken)
                ?? throw new InvalidOperationException("The collection plant no longer exists.");
            if (!plant.IsActive) throw new InvalidOperationException("The collection plant is inactive.");

            var apiType = job.DataType.Equals("MMF", StringComparison.OrdinalIgnoreCase) ? "MMF" : "DDQ";
            var apiDataType = apiType == "MMF" ? "MMF" : job.DataType;
            var endpoint = plant.ApiConfigurations.FirstOrDefault(item =>
                item.ApiType.Equals(apiType, StringComparison.OrdinalIgnoreCase)
                && item.DataType.Equals(apiDataType, StringComparison.OrdinalIgnoreCase))?.ApiLink;
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new InvalidOperationException($"No {apiType} {apiDataType} API endpoint is configured for {plant.Name}.");

            for (var attempt = 0; attempt <= job.RetryCount; attempt++)
            {
                apiResult = await apiClient.CollectAsync(plant.Name, $"{apiType} {apiDataType}", endpoint, cancellationToken);
                if (apiResult.IsSuccessful) break;
                if (attempt < job.RetryCount)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(8, 1 << attempt)), cancellationToken);
            }
            if (apiResult is null || !apiResult.IsSuccessful)
                throw new InvalidOperationException(apiResult?.Message ?? "The API did not return a response.");

            var intervalMinutes = apiType == "MMF" ? 15 : DdqDatasetPolicy.Get(apiDataType).ReferenceTemplateIntervalMinutes;
            var translated = ScheduledApiPayloadTranslator.Translate(apiResult.Payload, apiType, apiDataType,
                plant.Id, job.Id, job.SubmissionDateTime, intervalMinutes, apiResult.ContentType);
            if (translated.Readings.Count == 0)
                throw new InvalidOperationException("The API response did not contain supported numeric readings.");

            await DatabaseWriteGate.WaitAsync(cancellationToken);
            try
            {
                var strategy = database.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                    var replacedReadings = 0;
                    if (job.ReplaceExisting)
                    {
                        var dayStart = job.SubmissionDate.ToDateTime(TimeOnly.MinValue);
                        var dayEnd = dayStart.AddDays(1);
                        var replacedJobIds = await database.CollectionJobs.AsNoTracking()
                            .Where(item => item.Id != job.Id && item.PlantId == job.PlantId
                                && item.DataType == job.DataType && item.SubmissionDate == job.SubmissionDate)
                            .Select(item => item.Id).ToListAsync(cancellationToken);
                        replacedReadings = await database.SolarApiReadings
                            .Where(reading => reading.CollectionJobId != job.Id && reading.PlantId == job.PlantId
                                && reading.DataType == job.DataType
                                && reading.SubmissionDateTime >= dayStart && reading.SubmissionDateTime < dayEnd)
                            .ExecuteDeleteAsync(cancellationToken);
                        if (replacedJobIds.Count > 0)
                            await database.DataAnomalies.Where(item => replacedJobIds.Contains(item.CollectionJobId))
                                .ExecuteDeleteAsync(cancellationToken);
                    }

                    database.RawApiResponses.Add(CreateRawResponse(job.Id, apiResult));
                    database.SolarApiReadings.AddRange(translated.Readings);
                    watch.Stop();
                    job.CompletedAt = DateTimeOffset.UtcNow;
                    job.Status = CollectionJobStatus.Completed;
                    job.DurationSeconds = watch.Elapsed.TotalSeconds;
                    job.ResponseMessage = $"HTTP {apiResult.HttpStatusCode}; {translated.Readings.Count:N0} reading(s) saved; "
                        + $"{replacedReadings:N0} replaced. Validation is applied during report generation.";
                    database.AuditEvents.Add(CreateAudit(job, "Success"));
                    await database.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                });
            }
            finally
            {
                DatabaseWriteGate.Release();
            }
            logger.LogInformation("Collection command {CollectionId} completed with {ReadingCount} readings.",
                job.CollectionId, translated.Readings.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            watch.Stop();
            await using var database = await contextFactory.CreateDbContextAsync(CancellationToken.None);
            var job = await database.CollectionJobs.SingleAsync(item => item.Id == jobId, CancellationToken.None);
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.Status = CollectionJobStatus.Failed;
            job.DurationSeconds = watch.Elapsed.TotalSeconds;
            job.ErrorMessage = Limit(exception.GetBaseException().Message, 1000);
            job.ResponseMessage = Limit($"Collection failed: {job.ErrorMessage}", 1000);
            if (apiResult is not null && !string.IsNullOrWhiteSpace(apiResult.Payload)
                && !await database.RawApiResponses.AnyAsync(item => item.CollectionJobId == jobId, CancellationToken.None))
                database.RawApiResponses.Add(CreateRawResponse(job.Id, apiResult));
            database.AuditEvents.Add(CreateAudit(job, "Failed"));
            await database.SaveChangesAsync(CancellationToken.None);
            logger.LogWarning(exception, "Collection command {CollectionId} failed.", job.CollectionId);
        }
    }

    private static RawApiResponse CreateRawResponse(long jobId, LssApiCollectionResult result) => new()
    {
        CollectionJobId = jobId,
        ReceivedAt = DateTimeOffset.UtcNow,
        HttpStatusCode = result.HttpStatusCode ?? 0,
        ContentType = result.ContentType,
        Payload = result.Payload,
        Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(result.Payload)))
    };

    private static AuditEvent CreateAudit(CollectionJobEntity job, string result) => new()
    {
        OccurredAt = DateTimeOffset.UtcNow,
        Actor = job.RequestedBy,
        Action = "Process collection command",
        EntityType = "CollectionJob",
        EntityReference = job.CollectionId,
        Result = result,
        Detail = job.ResponseMessage,
        CorrelationId = job.CollectionId
    };

    private static string Limit(string? value, int length) => string.IsNullOrWhiteSpace(value)
        ? string.Empty : value[..Math.Min(value.Length, length)];
}

public sealed class CollectionCommandWorker(
    IServiceScopeFactory scopeFactory,
    IDbContextFactory<ApplicationDbContext> contextFactory,
    IConfiguration configuration,
    ILogger<CollectionCommandWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedJobsAsync(stoppingToken);
        var concurrency = CollectionProcessingPolicy.NormalizeConcurrency(configuration.GetValue(
            "CollectionProcessing:MaxConcurrency", CollectionProcessingPolicy.DefaultMaxConcurrency));
        var activeJobs = new Dictionary<long, Task>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var freeSlots = concurrency - activeJobs.Count;
                    if (freeSlots > 0)
                    {
                        var jobIds = await ClaimAsync(freeSlots, stoppingToken);
                        foreach (var jobId in jobIds)
                            activeJobs[jobId] = ProcessOneAsync(jobId, stoppingToken);
                    }

                    if (activeJobs.Count == 0)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
                        continue;
                    }

                    await Task.WhenAny(activeJobs.Values);
                    foreach (var completed in activeJobs.Where(item => item.Value.IsCompleted).ToList())
                    {
                        activeJobs.Remove(completed.Key);
                        await completed.Value;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Collection command worker cycle failed.");
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
        }
        finally
        {
            if (activeJobs.Count > 0)
            {
                try
                {
                    await Task.WhenAll(activeJobs.Values).WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
                }
                catch (TimeoutException)
                {
                    logger.LogWarning("Collection worker shutdown timed out with {Count} collection(s) still active.",
                        activeJobs.Count);
                }
                catch (OperationCanceledException) { }
            }
        }
    }

    private async Task<List<long>> ClaimAsync(int count, CancellationToken cancellationToken)
    {
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var jobs = await database.CollectionJobs.Where(item => item.Status == CollectionJobStatus.Queued)
            .OrderBy(item => item.StartedAt).Take(count).ToListAsync(cancellationToken);
        foreach (var job in jobs)
        {
            job.Status = CollectionJobStatus.Processing;
            job.ResponseMessage = "Collection command is being processed by the background worker.";
        }
        await database.SaveChangesAsync(cancellationToken);
        return jobs.Select(item => item.Id).ToList();
    }

    private async Task ProcessOneAsync(long jobId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<CollectionCommandProcessor>();
        await processor.ProcessAsync(jobId, cancellationToken);
    }

    private async Task RecoverInterruptedJobsAsync(CancellationToken cancellationToken)
    {
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        await database.CollectionJobs.Where(item => item.Status == CollectionJobStatus.Processing)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, CollectionJobStatus.Queued)
                .SetProperty(item => item.ResponseMessage, "Recovered after an interrupted worker process."), cancellationToken);
    }
}

public static class PublishedReadingQueries
{
    public static IQueryable<SolarApiReading> PublishedReadings(this ApplicationDbContext database) =>
        database.SolarApiReadings.AsNoTracking().Where(reading => database.CollectionJobs
            .Any(job => job.Id == reading.CollectionJobId && job.Status == CollectionJobStatus.Completed));
}
