using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;

namespace TestingSystem.Web.Services;

/// <summary>Queues due schedules; the command worker performs API and database work.</summary>
public sealed class ScheduledCollectionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ScheduledCollectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try { await QueueDueSchedulesAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Scheduled collection producer cycle failed."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task QueueDueSchedulesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        var commandBus = scope.ServiceProvider.GetRequiredService<ICollectionCommandBus>();
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var schedules = await database.CollectionSchedules.AsNoTracking().Where(item => item.IsActive)
            .OrderBy(item => item.Id).ToListAsync(cancellationToken);
        var priorRuns = await database.CollectionJobs.AsNoTracking()
            .Where(item => item.CollectionId.StartsWith("COL-SCH-"))
            .Select(item => new { item.CollectionId, item.StartedAt }).ToListAsync(cancellationToken);

        foreach (var schedule in schedules)
        {
            var prefix = $"COL-SCH-{schedule.Id}-";
            var lastRun = priorRuns.Where(item => item.CollectionId.StartsWith(prefix))
                .OrderByDescending(item => item.StartedAt).Select(item => (DateTimeOffset?)item.StartedAt).FirstOrDefault();
            var timeZone = ResolveTimeZone(schedule.TimeZoneId);
            var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DateTime;
            var lastRunLocal = lastRun.HasValue
                ? TimeZoneInfo.ConvertTime(lastRun.Value, timeZone).DateTime : (DateTime?)null;
            if (!CollectionSchedulePolicy.IsDue(schedule.DataType, schedule.Interval, schedule.RunTime,
                    schedule.ScheduleDay, schedule.ScheduleMonths, localNow, lastRunLocal))
                continue;

            var collectionId = $"{prefix}{localNow:yyyyMMddHHmm}";
            if (await database.CollectionJobs.AnyAsync(item => item.CollectionId == collectionId, cancellationToken))
                continue;

            var dataType = CollectionSchedulePolicy.ApiDataType(schedule.ApiType, schedule.DataType);
            try
            {
                await commandBus.EnqueueAsync(new QueueCollectionCommand(
                    schedule.PlantId, schedule.ApiType, dataType, $"Scheduled {schedule.ApiType}",
                    DateOnly.FromDateTime(localNow), "System Scheduler", schedule.RetryCount,
                    ReplaceExisting: false, CollectionId: collectionId), cancellationToken);
                logger.LogInformation("Schedule {ScheduleCode} queued collection {CollectionId}.",
                    schedule.ScheduleCode, collectionId);
            }
            catch (DbUpdateException exception)
            {
                logger.LogDebug(exception, "Collection slot {CollectionId} was already queued.", collectionId);
            }
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        foreach (var id in new[] { timeZoneId, "Asia/Kuala_Lumpur", "Singapore Standard Time" }.Distinct())
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        return TimeZoneInfo.Local;
    }
}
