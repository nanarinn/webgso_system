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
        var scheduleIds = schedules.Select(item => item.Id).ToArray();
        var lastRuns = await database.CollectionJobs.AsNoTracking()
            .Where(item => item.CollectionScheduleId.HasValue
                && scheduleIds.Contains(item.CollectionScheduleId.Value))
            .GroupBy(item => item.CollectionScheduleId!.Value)
            .Select(group => new { ScheduleId = group.Key, StartedAt = group.Max(item => item.StartedAt) })
            .ToDictionaryAsync(item => item.ScheduleId, item => item.StartedAt, cancellationToken);
        var dueCommands = new List<(string ScheduleCode, string CollectionId, QueueCollectionCommand Command)>();

        foreach (var schedule in schedules)
        {
            var prefix = $"COL-SCH-{schedule.Id}-";
            var lastRun = lastRuns.TryGetValue(schedule.Id, out var storedLastRun)
                ? (DateTimeOffset?)storedLastRun : null;
            var timeZone = ResolveTimeZone(schedule.TimeZoneId);
            var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DateTime;
            var lastRunLocal = lastRun.HasValue
                ? TimeZoneInfo.ConvertTime(lastRun.Value, timeZone).DateTime : (DateTime?)null;
            if (!CollectionSchedulePolicy.IsDue(schedule.DataType, schedule.Interval, schedule.RunTime,
                    schedule.ScheduleDay, schedule.ScheduleMonths, localNow, lastRunLocal))
                continue;

            var submissionDateTime = CollectionSchedulePolicy.SubmissionDateTime(
                schedule.DataType, schedule.RunTime, localNow);
            var collectionId = $"{prefix}{submissionDateTime:yyyyMMddHHmm}";
            var dataType = CollectionSchedulePolicy.ApiDataType(schedule.ApiType, schedule.DataType);
            dueCommands.Add((schedule.ScheduleCode, collectionId, new QueueCollectionCommand(
                schedule.PlantId, schedule.ApiType, dataType, $"Scheduled {schedule.ApiType}",
                submissionDateTime, "System Scheduler", schedule.RetryCount,
                ReplaceExisting: CollectionProcessingPolicy.ShouldReplaceExisting(schedule.ApiType, true),
                CollectionId: collectionId,
                CollectionScheduleId: schedule.Id)));
        }

        if (dueCommands.Count == 0) return;
        var candidateIds = dueCommands.Select(item => item.CollectionId).ToArray();
        var storedCollectionIds = await database.CollectionJobs.AsNoTracking()
            .Where(item => candidateIds.Contains(item.CollectionId))
            .Select(item => item.CollectionId)
            .ToListAsync(cancellationToken);
        var existingCollectionIds = storedCollectionIds.ToHashSet(StringComparer.Ordinal);
        dueCommands.RemoveAll(item => existingCollectionIds.Contains(item.CollectionId));
        if (dueCommands.Count == 0) return;
        try
        {
            await commandBus.EnqueueManyAsync(dueCommands.Select(item => item.Command).ToList(), cancellationToken);
            foreach (var due in dueCommands)
                logger.LogInformation("Schedule {ScheduleCode} queued collection {CollectionId}.",
                    due.ScheduleCode, due.CollectionId);
        }
        catch (DbUpdateException exception)
        {
            logger.LogDebug(exception, "One or more scheduled collection slots were already queued; the batch will be retried.");
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
