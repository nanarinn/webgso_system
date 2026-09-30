using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Database.Entities;

namespace TestingSystem.Web.Services;

public sealed class ScheduledReportWorker(
    IServiceScopeFactory scopeFactory,
    IReportEmailSender emailSender,
    ILogger<ScheduledReportWorker> logger) : BackgroundService
{
    private static readonly TimeZoneInfo MalaysiaTimeZone = ResolveMalaysiaTimeZone();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);
        if (!emailSender.IsConfigured)
            logger.LogWarning("Scheduled report email is inactive. {Message}", emailSender.ConfigurationMessage);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                if (emailSender.IsConfigured) await ProcessSchedulesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Scheduled report cycle failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessSchedulesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var schedules = await database.ReportSchedules.AsNoTracking().Include(item => item.Recipients)
            .Where(item => item.IsActive).OrderBy(item => item.Id).ToListAsync(cancellationToken);
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, MalaysiaTimeZone).DateTime;

        foreach (var schedule in schedules)
        {
            var slot = ReportScheduleDuePolicy.GetDueSlot(schedule.ScheduleType, schedule.IntervalMinutes,
                schedule.ExecutionTime, schedule.ScheduleDay, schedule.ScheduleMonths, localNow);
            if (slot is null) continue;

            var deliveryCode = $"RDL-{schedule.Id}-{slot}";
            if (await database.ReportDeliveryAttempts.AnyAsync(item => item.DeliveryCode == deliveryCode, cancellationToken))
                continue;
            await ExecuteScheduleAsync(contextFactory, schedule, deliveryCode, cancellationToken);
        }
    }

    private async Task ExecuteScheduleAsync(IDbContextFactory<ApplicationDbContext> contextFactory,
        ReportScheduleEntity schedule, string deliveryCode, CancellationToken cancellationToken)
    {
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var attempt = new ReportDeliveryAttempt
        {
            DeliveryCode = deliveryCode,
            ReportScheduleId = schedule.Id,
            ScheduleName = schedule.Name,
            ApiType = schedule.ApiType,
            GeneratedAt = DateTimeOffset.UtcNow,
            ReportStatus = "Processing",
            EmailStatus = "Pending",
            OverallStatus = "Processing",
            Message = "Preparing report data.",
            Recipients = schedule.Recipients.Select(item => new ReportDeliveryRecipient { Email = item.Email }).ToList()
        };
        database.ReportDeliveryAttempts.Add(attempt);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return;
        }

        try
        {
            var plants = await database.LssPlants.AsNoTracking().Where(item => item.IsActive)
                .OrderBy(item => item.PlantId).ToListAsync(cancellationToken);
            if (plants.Count == 0) throw new InvalidOperationException("No active LSS plant is available for the report.");

            var storedDataType = schedule.ApiType.Equals("MMF", StringComparison.OrdinalIgnoreCase)
                ? "MMF" : schedule.DataType;
            var latestSubmission = await database.PublishedReadings()
                .Where(item => item.DataType == storedDataType)
                .MaxAsync(item => (DateTime?)item.SubmissionDateTime, cancellationToken);
            if (!latestSubmission.HasValue)
                throw new InvalidOperationException($"No {storedDataType} readings are available yet.");

            var start = latestSubmission.Value.Date;
            var end = start.AddDays(1);
            var readings = await database.PublishedReadings()
                .Where(item => item.DataType == storedDataType
                    && item.SubmissionDateTime >= start && item.SubmissionDateTime < end)
                .ToListAsync(cancellationToken);
            if (readings.Count == 0)
                throw new InvalidOperationException($"No {storedDataType} readings are available for {start:dd/MM/yyyy}.");

            var csv = schedule.ApiType.Equals("MMF", StringComparison.OrdinalIgnoreCase)
                ? OnDemandCsvExporter.BuildMmf(start, plants, readings)
                : OnDemandCsvExporter.BuildDdq(storedDataType, start, plants, readings);
            var fileName = $"{SafeFileName(schedule.Name)}-{start:yyyyMMdd}.csv";
            var subject = $"SBLSS {schedule.ApiType} Report - {schedule.Name} - {start:dd/MM/yyyy}";
            var body = $"<p>The scheduled <strong>{WebUtility.HtmlEncode(schedule.Name)}</strong> report is attached.</p>"
                + $"<p>Source: {WebUtility.HtmlEncode(schedule.ApiType)}<br>Data: {WebUtility.HtmlEncode(schedule.DataType)}"
                + $"<br>Submission date: {start:dd/MM/yyyy}<br>Active plants: {plants.Count}</p>"
                + "<p>This email was generated automatically by the SBLSS System.</p>";
            var result = await emailSender.SendAsync(subject, body,
                schedule.Recipients.Select(item => item.Email).ToArray(),
                [new ReportEmailAttachment(fileName, "text/csv", Encoding.UTF8.GetBytes(csv))], cancellationToken);

            attempt.ReportStatus = "Generated";
            attempt.EmailStatus = result.Succeeded ? "Sent" : "Failed";
            attempt.OverallStatus = result.Succeeded ? "Delivered" : "Failed";
            attempt.Message = Limit(result.Message, 2000);
            database.AuditEvents.Add(new AuditEvent
            {
                OccurredAt = DateTimeOffset.UtcNow,
                Actor = "System Scheduler",
                Action = "Generate and email scheduled report",
                EntityType = "ReportDelivery",
                EntityReference = deliveryCode,
                Result = result.Succeeded ? "Success" : "Failed",
                Detail = attempt.Message,
                CorrelationId = schedule.ScheduleCode
            });
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            attempt.ReportStatus = "Failed";
            attempt.EmailStatus = "Not sent";
            attempt.OverallStatus = "Failed";
            attempt.Message = Limit(exception.GetBaseException().Message, 2000);
            database.AuditEvents.Add(new AuditEvent
            {
                OccurredAt = DateTimeOffset.UtcNow,
                Actor = "System Scheduler",
                Action = "Generate and email scheduled report",
                EntityType = "ReportDelivery",
                EntityReference = deliveryCode,
                Result = "Failed",
                Detail = attempt.Message,
                CorrelationId = schedule.ScheduleCode
            });
            await database.SaveChangesAsync(cancellationToken);
            logger.LogWarning(exception, "Report schedule {ScheduleCode} failed for delivery {DeliveryCode}.",
                schedule.ScheduleCode, deliveryCode);
        }
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "SBLSS-report" : safe;
    }

    private static TimeZoneInfo ResolveMalaysiaTimeZone()
    {
        foreach (var id in new[] { "Asia/Kuala_Lumpur", "Singapore Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        return TimeZoneInfo.CreateCustomTimeZone("Malaysia Standard Time", TimeSpan.FromHours(8),
            "Malaysia Standard Time", "Malaysia Standard Time");
    }

    private static string Limit(string value, int length) =>
        string.IsNullOrWhiteSpace(value) ? "Report delivery failed." : value[..Math.Min(value.Length, length)];
}
