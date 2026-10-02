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

            var submissionDateTime = latestSubmission.Value;
            var readings = await database.PublishedReadings()
                .Where(item => item.DataType == storedDataType
                    && item.SubmissionDateTime == submissionDateTime)
                .ToListAsync(cancellationToken);
            if (readings.Count == 0)
                throw new InvalidOperationException($"No {storedDataType} readings are available for {submissionDateTime:dd/MM/yyyy HH:mm}.");

            var previousSubmissionDateTime = submissionDateTime.AddDays(-1);
            var previousReadings = await database.PublishedReadings()
                .Where(item => item.DataType == storedDataType
                    && item.SubmissionDateTime == previousSubmissionDateTime)
                .ToListAsync(cancellationToken);
            var validationRules = await database.ValidationRules.AsNoTracking()
                .Where(rule => rule.DataType == schedule.ApiType && rule.IsActive)
                .ToListAsync(cancellationToken);
            IReadOnlyList<SolarApiReading> reportReadings = readings;
            var reportDetectedAnomalies = new List<ReportDetectedAnomaly>();
            if (schedule.ApiType.Equals("MMF", StringComparison.OrdinalIgnoreCase))
            {
                var rawSources = await (from response in database.RawApiResponses.AsNoTracking()
                    join job in database.CollectionJobs.AsNoTracking() on response.CollectionJobId equals job.Id
                    where job.SubmissionDateTime == submissionDateTime && job.DataType == storedDataType
                        && job.Status == CollectionJobStatus.Completed
                    orderby response.ReceivedAt descending
                    select new { response, job }).ToListAsync(cancellationToken);
                var translatedForReport = new List<SolarApiReading>();
                foreach (var source in rawSources.GroupBy(item => item.job.PlantId).Select(group => group.First()))
                {
                    var translated = ScheduledApiPayloadTranslator.Translate(source.response.Payload, "MMF", "MMF",
                        source.job.PlantId, source.job.Id, source.job.SubmissionDateTime,
                        schedule.IntervalMinutes, source.response.ContentType, validationRules, applyValidation: true);
                    translatedForReport.AddRange(translated.Readings);
                    reportDetectedAnomalies.AddRange(translated.Issues.Select(issue => new ReportDetectedAnomaly(
                        source.job.CollectionId, source.job.PlantId, issue.FieldName, issue.OriginalValue,
                        issue.Reason, "Applied during report generation", source.response.ReceivedAt,
                        issue.RuleCode, validationRules.FirstOrDefault(rule => rule.Code == issue.RuleCode)?.Name ?? string.Empty)));
                }
                if (translatedForReport.Count > 0) reportReadings = translatedForReport;
            }
            var prepared = schedule.ApiType.Equals("MMF", StringComparison.OrdinalIgnoreCase)
                ? ReportSubstitutionPolicy.PrepareMmf(submissionDateTime, schedule.IntervalMinutes,
                    plants, reportReadings, previousReadings, validationRules)
                : ReportSubstitutionPolicy.PrepareDdq(storedDataType, submissionDateTime,
                    plants, reportReadings, previousReadings, validationRules);
            var csv = schedule.ApiType.Equals("MMF", StringComparison.OrdinalIgnoreCase)
                ? OnDemandCsvExporter.BuildMmf(submissionDateTime, plants, prepared.Readings)
                : OnDemandCsvExporter.BuildDdq(storedDataType, submissionDateTime, plants, prepared.Readings);
            var anomalyCsv = ReportAnomalyCsvExporter.Build(schedule.ApiType, storedDataType, submissionDateTime,
                plants, readings, prepared, reportDetectedAnomalies);
            var fileName = $"{SafeFileName(schedule.Name)}-{submissionDateTime:yyyyMMdd-HHmm}.csv";
            var anomalyFileName = $"{SafeFileName(schedule.Name)}-{submissionDateTime:yyyyMMdd-HHmm}-Anomaly.csv";
            var subject = $"SBLSS {schedule.ApiType} Report - {schedule.Name} - {submissionDateTime:dd/MM/yyyy HH:mm}";
            var body = $"<p>The scheduled <strong>{WebUtility.HtmlEncode(schedule.Name)}</strong> report is attached.</p>"
                + $"<p>Source: {WebUtility.HtmlEncode(schedule.ApiType)}<br>Data: {WebUtility.HtmlEncode(schedule.DataType)}"
                + $"<br>Submission date &amp; time: {submissionDateTime:dd/MM/yyyy HH:mm}<br>Active plants: {plants.Count}</p>"
                + $"<p>Data substitution: {WebUtility.HtmlEncode(prepared.Summary.Description)}<br>"
                + $"<br>Validation exclusions: {reportDetectedAnomalies.Count:N0}</p>"
                + "<p>The anomaly attachment records report-time validation exclusions and every rule-triggered substitution.</p>"
                + "<p>This email was generated automatically by the SBLSS System.</p>";
            var result = await emailSender.SendAsync(subject, body,
                schedule.Recipients.Select(item => item.Email).ToArray(),
                [
                    new ReportEmailAttachment(fileName, "text/csv", Encoding.UTF8.GetBytes(csv)),
                    new ReportEmailAttachment(anomalyFileName, "text/csv", Encoding.UTF8.GetBytes(anomalyCsv))
                ], cancellationToken);

            attempt.ReportStatus = "Generated";
            attempt.EmailStatus = result.Succeeded ? "Sent" : "Failed";
            attempt.OverallStatus = result.Succeeded ? "Delivered" : "Failed";
            attempt.Message = Limit($"{result.Message} {prepared.Summary.Description} "
                + $"Anomaly report: {reportDetectedAnomalies.Count:N0} validation exclusion(s), "
                + $"{prepared.Summary.TotalSubstituted:N0} rule-triggered substitution(s).", 2000);
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
