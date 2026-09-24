using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Models;

namespace TestingSystem.Web.Services;

public sealed class ScheduledCollectionWorker(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<ScheduledCollectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                await ProcessSchedulesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Scheduled collection cycle failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessSchedulesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var schedules = await database.CollectionSchedules.AsNoTracking().Where(item => item.IsActive)
            .OrderBy(item => item.Id).ToListAsync(cancellationToken);
        var lastScheduledRuns = await database.CollectionJobs.AsNoTracking()
            .Where(item => item.CollectionId.StartsWith("COL-SCH-"))
            .Select(item => new { item.CollectionId, item.StartedAt })
            .ToListAsync(cancellationToken);

        foreach (var schedule in schedules)
        {
            var prefix = $"COL-SCH-{schedule.Id}-";
            var lastRun = lastScheduledRuns
                .Where(item => item.CollectionId.StartsWith(prefix))
                .OrderByDescending(item => item.StartedAt)
                .Select(item => (DateTimeOffset?)item.StartedAt)
                .FirstOrDefault();
            var timeZone = ResolveTimeZone(schedule.TimeZoneId);
            var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DateTime;
            var lastRunLocal = lastRun.HasValue ? TimeZoneInfo.ConvertTime(lastRun.Value, timeZone).DateTime : (DateTime?)null;
            if (!CollectionSchedulePolicy.IsDue(schedule.DataType, schedule.Interval, schedule.RunTime,
                    schedule.ScheduleDay, schedule.ScheduleMonths, localNow, lastRunLocal))
                continue;

            var slot = localNow.ToString("yyyyMMddHHmm");
            var collectionId = $"{prefix}{slot}";
            if (await database.CollectionJobs.AnyAsync(item => item.CollectionId == collectionId, cancellationToken))
                continue;

            await ExecuteScheduleAsync(contextFactory, schedule, collectionId, localNow, cancellationToken);
        }
    }

    private async Task ExecuteScheduleAsync(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        CollectionSchedule schedule,
        string collectionId,
        DateTime localNow,
        CancellationToken cancellationToken)
    {
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var plant = await database.LssPlants.Include(item => item.ApiConfigurations)
            .SingleOrDefaultAsync(item => item.Id == schedule.PlantId, cancellationToken);
        var apiDataType = CollectionSchedulePolicy.ApiDataType(schedule.ApiType, schedule.DataType);
        var endpoint = plant?.ApiConfigurations.FirstOrDefault(item =>
            item.ApiType.Equals(schedule.ApiType, StringComparison.OrdinalIgnoreCase)
            && item.DataType.Equals(apiDataType, StringComparison.OrdinalIgnoreCase))?.ApiLink;

        var job = new CollectionJobEntity
        {
            CollectionId = collectionId,
            PlantId = schedule.PlantId,
            DataType = schedule.ApiType == "MMF" ? "MMF" : apiDataType,
            Mode = $"Scheduled {schedule.ApiType}",
            SubmissionDate = DateOnly.FromDateTime(localNow),
            StartedAt = DateTimeOffset.UtcNow,
            Status = "Running",
            ResponseMessage = $"Schedule {schedule.ScheduleCode} started."
        };
        database.CollectionJobs.Add(job);
        await database.SaveChangesAsync(cancellationToken);

        var watch = Stopwatch.StartNew();
        try
        {
            if (plant is null || !plant.IsActive)
                throw new InvalidOperationException("The scheduled plant is missing or inactive.");
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new InvalidOperationException($"No {schedule.ApiType} API endpoint is configured for {apiDataType}.");
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
                || endpointUri.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("The configured API endpoint is not a valid HTTP or HTTPS URL.");

            ApiDownload? download = null;
            Exception? lastError = null;
            for (var attempt = 0; attempt <= Math.Clamp(schedule.RetryCount, 0, 5); attempt++)
            {
                try
                {
                    download = await DownloadAsync(endpointUri, cancellationToken);
                    if (download.StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices) break;
                    lastError = new HttpRequestException($"API returned HTTP {(int)download.StatusCode} ({download.StatusCode}).");
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    lastError = exception;
                }

                if (attempt < schedule.RetryCount)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(8, 1 << attempt)), cancellationToken);
            }

            if (download is null)
                throw new InvalidOperationException(lastError?.Message ?? "The API did not return a response.");

            database.RawApiResponses.Add(new RawApiResponse
            {
                CollectionJobId = job.Id,
                ReceivedAt = DateTimeOffset.UtcNow,
                HttpStatusCode = (int)download.StatusCode,
                ContentType = download.ContentType,
                Payload = download.Payload,
                Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(download.Payload)))
            });
            if (download.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
                throw new InvalidOperationException(lastError?.Message ?? $"API returned HTTP {(int)download.StatusCode}.");

            var intervalMinutes = CollectionSchedulePolicy.ApiIntervalMinutes(schedule.ApiType, apiDataType, schedule.Interval);
            var translated = ScheduledApiPayloadTranslator.Translate(download.Payload, schedule.ApiType,
                apiDataType, plant.Id, job.Id, localNow, intervalMinutes, download.ContentType);
            if (translated.Readings.Count == 0)
                throw new InvalidOperationException(
                    $"The API returned a {translated.PayloadFormat} response, but it did not contain any numeric {schedule.ApiType} readings that can be filtered.");
            database.SolarApiReadings.AddRange(translated.Readings);
            await AddAnomaliesAsync(database, job, plant, schedule.ApiType, translated, cancellationToken);

            watch.Stop();
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.Status = "Completed";
            job.DurationSeconds = watch.Elapsed.TotalSeconds;
            job.ResponseMessage = $"HTTP {(int)download.StatusCode}; {translated.PayloadFormat} response saved; {translated.Readings.Count:N0} reading(s) translated; {translated.Issues.Count:N0} validation issue(s).";
            database.AuditEvents.Add(new AuditEvent
            {
                OccurredAt = DateTimeOffset.UtcNow,
                Actor = "System Scheduler",
                Action = "Collect scheduled plant API data",
                EntityType = "CollectionJob",
                EntityReference = collectionId,
                Result = translated.Readings.Count == 0 ? "Warning" : "Success",
                Detail = job.ResponseMessage,
                CorrelationId = schedule.ScheduleCode
            });
            await database.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Schedule {ScheduleCode} created collection {CollectionId} with {ReadingCount} readings.",
                schedule.ScheduleCode, collectionId, translated.Readings.Count);
        }
        catch (Exception exception)
        {
            watch.Stop();
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.Status = "Failed";
            job.DurationSeconds = watch.Elapsed.TotalSeconds;
            job.ErrorMessage = Limit(exception.GetBaseException().Message, 1000);
            job.ResponseMessage = Limit($"Scheduled collection failed: {job.ErrorMessage}", 1000);
            database.AuditEvents.Add(new AuditEvent
            {
                OccurredAt = DateTimeOffset.UtcNow,
                Actor = "System Scheduler",
                Action = "Collect scheduled plant API data",
                EntityType = "CollectionJob",
                EntityReference = collectionId,
                Result = "Failed",
                Detail = job.ResponseMessage,
                CorrelationId = schedule.ScheduleCode
            });
            await database.SaveChangesAsync(cancellationToken);
            logger.LogWarning(exception, "Schedule {ScheduleCode} failed for collection {CollectionId}.",
                schedule.ScheduleCode, collectionId);
        }
    }

    private async Task<ApiDownload> DownloadAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.ParseAdd("application/json, application/xml, text/xml, text/csv, text/plain; q=0.8, */*; q=0.5");
        using var response = await httpClientFactory.CreateClient().SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        var payload = await response.Content.ReadAsStringAsync(timeout.Token);
        return new(response.StatusCode, response.Content.Headers.ContentType?.ToString() ?? "application/json", payload);
    }

    private static async Task AddAnomaliesAsync(
        ApplicationDbContext database,
        CollectionJobEntity job,
        LssPlant plant,
        string apiType,
        TranslationResult result,
        CancellationToken cancellationToken)
    {
        var issues = result.Issues.ToList();
        if (result.Readings.Count == 0 && issues.Count == 0)
            issues.Add(new(apiType == "MMF" ? "MMF-001" : "DDQ-001", "API payload", "0",
                "The API response was stored, but no supported numeric readings could be translated."));

        foreach (var reading in result.Readings)
        {
            if (reading.Value < 0)
                issues.Add(new(apiType == "MMF" ? "MMF-015" : "DDQ-004", reading.Parameter,
                    reading.Value.ToString(CultureInfo.InvariantCulture), "A negative measurement was received."));
            if (apiType == "DDQ" && reading.Value > plant.CapacityMw)
                issues.Add(new("DDQ-005", reading.Parameter, reading.Value.ToString(CultureInfo.InvariantCulture),
                    $"The forecast exceeds the configured plant capacity of {plant.CapacityMw:N2} MW."));
            if (apiType == "MMF") AddMmfRangeIssue(reading, issues);
        }

        var ruleCodes = issues.Select(item => item.RuleCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rules = await database.ValidationRules.AsNoTracking()
            .Where(item => ruleCodes.Contains(item.Code)).ToDictionaryAsync(item => item.Code, cancellationToken);

        database.DataAnomalies.AddRange(issues.Select(issue => new DataAnomaly
        {
            CollectionJobId = job.Id,
            ValidationRuleId = rules.GetValueOrDefault(issue.RuleCode)?.Id,
            DataType = apiType,
            FieldName = Limit(issue.FieldName, 100),
            OriginalValue = Limit(issue.OriginalValue, 500),
            Reason = Limit(issue.Reason, 1000),
            Status = "Open",
            DetectedAt = DateTimeOffset.UtcNow
        }));
    }

    private static void AddMmfRangeIssue(SolarApiReading reading, List<TranslationIssue> issues)
    {
        var name = reading.Parameter.ToLowerInvariant();
        (string Code, bool Invalid, string Reason) check = name switch
        {
            var value when value.Contains("ghi") || value.Contains("poa") || value.Contains("irradiance")
                => ("MMF-005", reading.Value is < 0 or > 1400, "Irradiance must be between 0 and 1,400 W/m²."),
            var value when value.Contains("humidity")
                => ("MMF-007", reading.Value is < 0 or > 100, "Relative humidity must be between 0% and 100%."),
            var value when value.Contains("windspeed") || value.Contains("wind speed")
                => ("MMF-008", reading.Value is < 0 or > 75, "Wind speed must be between 0 and 75 m/s."),
            var value when value.Contains("winddirection") || value.Contains("wind direction")
                => ("MMF-009", reading.Value is < 0 or > 360, "Wind direction must be between 0 and 360 degrees."),
            var value when value.Contains("temp")
                => ("MMF-006", reading.Value is < -20 or > 100, "Temperature is outside the supported operating range."),
            _ => (string.Empty, false, string.Empty)
        };
        if (check.Invalid)
            issues.Add(new(check.Code, reading.Parameter, reading.Value.ToString(CultureInfo.InvariantCulture), check.Reason));
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        foreach (var id in new[] { timeZoneId, "Asia/Kuala_Lumpur", "Singapore Standard Time" }.Distinct())
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        return TimeZoneInfo.Local;
    }

    private static string Limit(string? value, int length) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value[..Math.Min(value.Length, length)];

    private sealed record ApiDownload(HttpStatusCode StatusCode, string ContentType, string Payload);
}
