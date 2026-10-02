using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Demo;
using TestingSystem.Web.Services;
using TestingSystem.Web.Database.Entities;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class CollectionManagement : ComponentBase, IAsyncDisposable
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
    [Inject] private ILssDataApiClient ApiClient { get; set; } = default!;
    [Inject] private ICollectionCommandBus CollectionCommands { get; set; } = default!;

    private List<PlantConnection> Plants { get; set; } = [];

    private List<DemoCollectionJob> Jobs { get; set; } = [];
    private string SearchTerm { get; set; } = string.Empty;
    private int SelectedPlantId { get; set; }
    private static readonly IReadOnlyList<string> ManualDdqDataTypes = ManualDdqCollectionPolicy.DataTypes;
    private string SelectedManualDataType { get; set; } = ManualDdqDataTypes[0];
    private bool IsRunDialogOpen { get; set; }
    private bool IsReplaceConfirmationOpen { get; set; }
    private bool IsRunningManualCollection { get; set; }
    private string ManualCollectionError { get; set; } = string.Empty;
    private string ReplaceConfirmationMessage { get; set; } = string.Empty;
    private bool IsSummaryDialogOpen { get; set; }
    private bool IsJobDetailOpen { get; set; }
    private DemoCollectionJob? SelectedJob { get; set; }
    private string SummaryFilter { get; set; } = "All";
    private DemoCollectionJob? RunResult { get; set; }
    private string LoadErrorMessage { get; set; } = string.Empty;
    private bool IsLoadingJobs { get; set; }
    private List<ManualBatchItem> ManualBatchItems { get; set; } = [];
    private bool ManualBatchHasRun { get; set; }
    private int ManualBatchCompletedCount => ManualBatchItems.Count(item => item.Status is "Successful" or "Failed");
    private static readonly IReadOnlyList<ManualBatchOption> ManualBatchOptions =
    [
        new("DDQ|Rolling 24 Hour", "DDQ", "Rolling 24 Hour", "DDQ · Rolling 24 Hour"),
        new("DDQ|Day Ahead", "DDQ", "Day Ahead", "DDQ · Day Ahead"),
        new("DDQ|Week Ahead", "DDQ", "Week Ahead", "DDQ · Week Ahead"),
        new("DDQ|4 Month Ahead", "DDQ", "4 Month Ahead", "DDQ · 4 Month Ahead"),
        new("MMF|MMF", "MMF", "MMF", "MMF · All parameters")
    ];
    private string SelectedManualBatchKey { get; set; } = ManualBatchOptions[0].Key;
    private int Sequence { get; set; } = 6;
    private readonly CancellationTokenSource RefreshCancellation = new();
    private Task? RefreshTask { get; set; }

    private List<DemoCollectionJob> SummaryJobs => SummaryFilter == "All"
        ? Jobs
        : Jobs.Where(job => job.Status == SummaryFilter).ToList();

    private string SummaryDialogTitle => SummaryFilter == "All"
        ? "All collection jobs"
        : $"{SummaryFilter} collection jobs";

    private PlantConnection? SelectedPlant => Plants.FirstOrDefault(plant => plant.DatabaseId == SelectedPlantId);
    private string SelectedManualEndpoint => SelectedPlant?.ApiEndpoints.GetValueOrDefault(SelectedManualDataType) ?? string.Empty;

    private List<DemoCollectionJob> FilteredJobs => string.IsNullOrWhiteSpace(SearchTerm)
        ? Jobs
        : Jobs.Where(job => job.PlantName.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || job.State.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || job.CollectionId.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || job.ScheduleName.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || job.Mode.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || job.Status.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || job.Description.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
            .ToList();

    protected override async Task OnInitializedAsync()
    {
        await LoadPageAsync();
        RefreshTask = RefreshJobsAsync(RefreshCancellation.Token);
    }

    private async Task LoadPageAsync()
    {
        IsLoadingJobs = true;
        LoadErrorMessage = string.Empty;
        try
        {
            await using var database = await ContextFactory.CreateDbContextAsync();
            var plantEntities = await database.LssPlants.AsNoTracking().Include(plant => plant.ApiConfigurations)
                .Where(plant => plant.IsActive)
                .OrderBy(plant => plant.Name)
                .ToListAsync();
            Plants = plantEntities.Select(plant => new PlantConnection(
                    plant.Id,
                    plant.PlantId,
                    plant.Name,
                    plant.State,
                    plant.CapacityMw,
                    plant.ApiConfigurations
                        .Where(api => api.ApiType == "DDQ" && ManualDdqDataTypes.Contains(api.DataType))
                        .GroupBy(api => api.DataType)
                        .ToDictionary(group => group.Key, group => group.First().ApiLink, StringComparer.OrdinalIgnoreCase)))
                .Where(plant => plant.ApiEndpoints.Count > 0)
                .ToList();
            SelectedPlantId = Plants.FirstOrDefault()?.DatabaseId ?? 0;
            await LoadJobsAsync();
        }
        catch (Exception exception)
        {
            Jobs = [];
            LoadErrorMessage = $"Collection history could not be loaded: {exception.GetBaseException().Message}";
        }
        finally
        {
            IsLoadingJobs = false;
        }
    }

    private async Task RetryLoadAsync()
    {
        await LoadPageAsync();
    }

    private async Task LoadJobsAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        Jobs = await (from job in database.CollectionJobs.AsNoTracking()
            join plant in database.LssPlants.AsNoTracking() on job.PlantId equals plant.Id
            join schedule in database.CollectionSchedules.AsNoTracking()
                on job.CollectionScheduleId equals (int?)schedule.Id into scheduleGroup
            from schedule in scheduleGroup.DefaultIfEmpty()
            orderby job.StartedAt descending
            select new DemoCollectionJob(
                job.CollectionId,
                plant.Name,
                plant.State,
                job.Mode,
                job.SubmissionDateTime,
                job.CompletedAt.HasValue ? job.CompletedAt.Value.LocalDateTime : job.StartedAt.LocalDateTime,
                job.Status == "Completed" ? "Successful" : job.Status,
                job.Status == "Completed" ? "API data collected and stored in MS SQL." : (job.ErrorMessage ?? "Collection is still being processed."),
                job.ResponseMessage,
                job.DurationSeconds,
                job.Mode.Contains("MMF") || job.DataType == "MMF" ? "MMF" : "DDQ",
                job.DataType,
                schedule == null ? "" : schedule.Name,
                0,
                0,
                null,
                "",
                job.Id,
                job.ErrorMessage ?? string.Empty))
            .Take(500)
            .ToListAsync();
        Sequence = Math.Max(Sequence, Jobs.Count);
    }

    private void OpenJobDetail(DemoCollectionJob job)
    {
        SelectedJob = job;
        IsJobDetailOpen = true;
    }

    private void CloseJobDetail()
    {
        IsJobDetailOpen = false;
        SelectedJob = null;
    }

    private async Task RefreshJobsAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(async () =>
                {
                    try
                    {
                        await LoadJobsAsync();
                        LoadErrorMessage = string.Empty;
                    }
                    catch (Exception exception)
                    {
                        LoadErrorMessage = $"Automatic refresh failed: {exception.GetBaseException().Message}";
                    }
                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        RefreshCancellation.Cancel();
        if (RefreshTask is not null)
            try { await RefreshTask; } catch (OperationCanceledException) { }
        RefreshCancellation.Dispose();
    }

    private void OpenRunDialog()
    {
        SelectedPlantId = Plants.FirstOrDefault()?.DatabaseId ?? 0;
        SelectedManualDataType = ManualDdqDataTypes[0];
        ManualCollectionError = string.Empty;
        IsReplaceConfirmationOpen = false;
        RunResult = null;
        IsRunDialogOpen = true;
    }

    private async Task OpenRunDialogAsync()
    {
        ManualCollectionError = string.Empty;
        ManualBatchHasRun = false;
        IsReplaceConfirmationOpen = false;
        RunResult = null;
        SelectedManualBatchKey = ManualBatchOptions[0].Key;
        await LoadManualBatchItemsAsync();
        IsRunDialogOpen = true;
    }

    private async Task ManualBatchDataTypeChangedAsync()
    {
        if (IsRunningManualCollection) return;
        ManualBatchHasRun = false;
        ManualCollectionError = string.Empty;
        await LoadManualBatchItemsAsync();
    }

    private async Task LoadManualBatchItemsAsync()
    {
        var option = ManualBatchOptions.First(item => item.Key == SelectedManualBatchKey);
        await using var database = await ContextFactory.CreateDbContextAsync();
        var plants = await database.LssPlants.AsNoTracking().Include(plant => plant.ApiConfigurations)
            .Where(plant => plant.IsActive).OrderBy(plant => plant.Name).ToListAsync();
        ManualBatchItems = plants.Select(plant =>
        {
            var endpoint = plant.ApiConfigurations.FirstOrDefault(api =>
                api.ApiType == option.ApiType && api.DataType == option.DataType)?.ApiLink ?? string.Empty;
            return new ManualBatchItem
            {
                PlantDatabaseId = plant.Id,
                PlantId = plant.PlantId,
                PlantName = plant.Name,
                State = plant.State,
                CapacityMw = plant.CapacityMw,
                ApiType = option.ApiType,
                DataType = ManualBatchCollectionPolicy.StoredDataType(option.ApiType, option.DataType),
                Endpoint = endpoint,
                Status = "Start",
                Message = string.IsNullOrWhiteSpace(endpoint) ? "Endpoint not configured" : "Ready to run"
            };
        }).ToList();
    }

    private void CloseRunDialog()
    {
        if (IsRunningManualCollection) return;
        IsRunDialogOpen = false;
        IsReplaceConfirmationOpen = false;
        ManualCollectionError = string.Empty;
        RunResult = null;
        ManualBatchItems = [];
        ManualBatchHasRun = false;
    }

    private async Task RunAllManualCollectionsAsync()
    {
        if (IsRunningManualCollection || ManualBatchItems.Count == 0) return;
        IsRunningManualCollection = true;
        ManualCollectionError = string.Empty;
        try
        {
            foreach (var item in ManualBatchItems)
            {
                if (string.IsNullOrWhiteSpace(item.Endpoint))
                {
                    item.Status = "Failed";
                    item.Message = $"No {item.ApiType} {item.DataType} endpoint configured";
                    continue;
                }

                try
                {
                    var queued = await CollectionCommands.EnqueueAsync(new QueueCollectionCommand(
                        item.PlantDatabaseId, item.ApiType, item.DataType,
                        ManualBatchCollectionPolicy.Mode(item.ApiType), SubmissionMinute(DateTime.Now),
                        "Current user", ReplaceExisting: true));
                    item.JobId = queued.JobId;
                    item.Status = "Start";
                    item.Message = "Queued and waiting for a collection worker";
                }
                catch (Exception exception)
                {
                    item.Status = "Failed";
                    item.Message = Limit(exception.GetBaseException().Message, 500);
                }
            }
            await InvokeAsync(StateHasChanged);
            await MonitorManualBatchAsync();
            ManualBatchHasRun = true;
            await LoadJobsAsync();
            CollectionPage = 1;
        }
        finally
        {
            IsRunningManualCollection = false;
        }
    }

    private async Task MonitorManualBatchAsync()
    {
        var pending = ManualBatchItems.Where(item => item.JobId.HasValue).ToList();
        while (pending.Count > 0 && !RefreshCancellation.IsCancellationRequested)
        {
            var ids = pending.Select(item => item.JobId!.Value).ToArray();
            await using var database = await ContextFactory.CreateDbContextAsync(RefreshCancellation.Token);
            var states = await database.CollectionJobs.AsNoTracking().Where(job => ids.Contains(job.Id))
                .Select(job => new { job.Id, job.Status, job.ResponseMessage, job.ErrorMessage })
                .ToDictionaryAsync(job => job.Id, RefreshCancellation.Token);
            foreach (var item in pending)
            {
                if (!states.TryGetValue(item.JobId!.Value, out var state)) continue;
                item.Status = state.Status switch
                {
                    CollectionJobStatus.Queued => "Start",
                    CollectionJobStatus.Processing => "Processing",
                    CollectionJobStatus.Completed => "Successful",
                    CollectionJobStatus.Failed => "Failed",
                    _ => state.Status
                };
                item.Message = state.Status == CollectionJobStatus.Failed
                    ? state.ErrorMessage ?? state.ResponseMessage : state.ResponseMessage;
            }
            pending = pending.Where(item => item.Status is not ("Successful" or "Failed")).ToList();
            await InvokeAsync(StateHasChanged);
            if (pending.Count > 0) await Task.Delay(500, RefreshCancellation.Token);
        }
    }

    private async Task RunBatchEndpointAsync(ManualBatchItem item)
    {
        Sequence++;
        var startedAt = DateTime.Now;
        var submissionDate = DateOnly.FromDateTime(startedAt);
        var collectionId = $"MAN-{item.ApiType}-{startedAt:yyyyMMddHHmmssfff}-{Sequence:000}";
        await using var database = await ContextFactory.CreateDbContextAsync();
        var entity = new CollectionJobEntity
        {
            CollectionId = collectionId,
            PlantId = item.PlantDatabaseId,
            DataType = item.DataType,
            Mode = ManualBatchCollectionPolicy.Mode(item.ApiType),
            SubmissionDate = submissionDate,
            SubmissionDateTime = SubmissionMinute(startedAt),
            StartedAt = DateTimeOffset.Now,
            Status = "Running",
            ResponseMessage = $"Manual {item.ApiType} {item.DataType} collection started."
        };
        database.CollectionJobs.Add(entity);
        await database.SaveChangesAsync();

        var watch = Stopwatch.StartNew();
        LssApiCollectionResult? apiResult = null;
        try
        {
            apiResult = await ApiClient.CollectAsync(item.PlantName, $"{item.ApiType} {item.DataType}", item.Endpoint);
            if (!apiResult.IsSuccessful) throw new InvalidOperationException(apiResult.Message);

            var intervalMinutes = item.ApiType == "MMF"
                ? 15
                : DdqDatasetPolicy.Get(item.DataType).ReferenceTemplateIntervalMinutes;
            var translated = ScheduledApiPayloadTranslator.Translate(apiResult.Payload, item.ApiType, item.DataType,
                item.PlantDatabaseId, entity.Id, entity.SubmissionDateTime, intervalMinutes, apiResult.ContentType);
            if (translated.Readings.Count == 0)
                throw new InvalidOperationException("API responded, but no supported numeric readings were found.");

            var strategy = database.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await database.Database.BeginTransactionAsync();
                var dayStart = submissionDate.ToDateTime(TimeOnly.MinValue);
                var dayEnd = dayStart.AddDays(1);
                var replacedJobIds = await database.CollectionJobs.AsNoTracking()
                    .Where(job => job.Id != entity.Id && job.PlantId == item.PlantDatabaseId
                        && job.DataType == item.DataType && job.SubmissionDate == submissionDate)
                    .Select(job => job.Id).ToListAsync();
                var replacedReadings = await database.SolarApiReadings
                    .Where(reading => reading.PlantId == item.PlantDatabaseId && reading.DataType == item.DataType
                        && reading.SubmissionDateTime >= dayStart && reading.SubmissionDateTime < dayEnd)
                    .ExecuteDeleteAsync();
                if (replacedJobIds.Count > 0)
                    await database.DataAnomalies.Where(anomaly => replacedJobIds.Contains(anomaly.CollectionJobId)).ExecuteDeleteAsync();

                database.RawApiResponses.Add(CreateRawResponse(entity.Id, apiResult));
                database.SolarApiReadings.AddRange(translated.Readings);
                watch.Stop();
                entity.CompletedAt = DateTimeOffset.Now;
                entity.Status = "Completed";
                entity.DurationSeconds = watch.Elapsed.TotalSeconds;
                entity.ResponseMessage = $"HTTP {apiResult.HttpStatusCode}; {translated.Readings.Count:N0} reading(s) saved; {replacedReadings:N0} replaced. Validation is applied during report generation.";
                database.AuditEvents.Add(new AuditEvent
                {
                    OccurredAt = DateTimeOffset.UtcNow, Actor = "Current user",
                    Action = "Run all API manual collection", EntityType = "CollectionJob",
                    EntityReference = collectionId, Result = "Success", Detail = entity.ResponseMessage,
                    CorrelationId = $"{item.PlantId}:{item.ApiType}:{item.DataType}"
                });
                await database.SaveChangesAsync();
                await transaction.CommitAsync();
            });
            item.Status = "Successful";
            item.Message = $"{translated.Readings.Count:N0} readings saved";
        }
        catch (Exception exception)
        {
            watch.Stop();
            var message = Limit(exception.GetBaseException().Message, 500);
            database.ChangeTracker.Clear();
            var failedEntity = await database.CollectionJobs.SingleAsync(job => job.Id == entity.Id);
            failedEntity.CompletedAt = DateTimeOffset.Now;
            failedEntity.Status = "Failed";
            failedEntity.ErrorMessage = message;
            failedEntity.ResponseMessage = $"Manual {item.ApiType} {item.DataType} collection failed: {message}";
            failedEntity.DurationSeconds = watch.Elapsed.TotalSeconds;
            if (apiResult is not null && !string.IsNullOrWhiteSpace(apiResult.Payload))
                database.RawApiResponses.Add(CreateRawResponse(entity.Id, apiResult));
            database.AuditEvents.Add(new AuditEvent
            {
                OccurredAt = DateTimeOffset.UtcNow, Actor = "Current user",
                Action = "Run all API manual collection", EntityType = "CollectionJob",
                EntityReference = collectionId, Result = "Failed", Detail = failedEntity.ResponseMessage,
                CorrelationId = $"{item.PlantId}:{item.ApiType}:{item.DataType}"
            });
            await database.SaveChangesAsync();
            item.Status = "Failed";
            item.Message = message;
        }
    }

    private void OpenSummaryDialog(string filter)
    {
        SummaryFilter = filter;
        IsSummaryDialogOpen = true;
    }

    private void OpenAllSummary() => OpenSummaryDialog("All");
    private void OpenSuccessfulSummary() => OpenSummaryDialog("Successful");
    private void OpenFailedSummary() => OpenSummaryDialog("Failed");

    private void CloseSummaryDialog() => IsSummaryDialogOpen = false;

    private async Task RequestManualCollectionAsync()
    {
        ManualCollectionError = string.Empty;
        if (SelectedPlant is null)
        {
            ManualCollectionError = "Select a plant before starting the manual collection.";
            return;
        }
        if (!ManualDdqCollectionPolicy.IsSupported(SelectedManualDataType))
        {
            ManualCollectionError = "Manual collection supports Day Ahead, Week Ahead and 4 Month Ahead only.";
            return;
        }
        if (string.IsNullOrWhiteSpace(SelectedManualEndpoint))
        {
            ManualCollectionError = $"No DDQ {SelectedManualDataType} API endpoint is configured for {SelectedPlant.Name}.";
            return;
        }

        var submissionDate = DateOnly.FromDateTime(DateTime.Today);
        await using var database = await ContextFactory.CreateDbContextAsync();
        var scheduledJobs = await database.CollectionJobs.AsNoTracking()
            .Where(job => job.PlantId == SelectedPlant.DatabaseId
                && job.DataType == SelectedManualDataType
                && job.SubmissionDate == submissionDate
                && job.CollectionScheduleId != null)
            .Select(job => job.Status)
            .ToListAsync();
        var dayStart = submissionDate.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        var existingReadingCount = await database.SolarApiReadings.AsNoTracking().CountAsync(reading =>
            reading.PlantId == SelectedPlant.DatabaseId
            && reading.DataType == SelectedManualDataType
            && reading.SubmissionDateTime >= dayStart
            && reading.SubmissionDateTime < dayEnd);

        var existingState = ManualDdqCollectionPolicy.Evaluate(existingReadingCount, scheduledJobs);
        if (existingState == ManualCollectionExistingState.ScheduledRunning)
        {
            ManualCollectionError = "A scheduled collection for this plant and data type is still running. Wait for it to finish before using Run Manual.";
            return;
        }

        if (existingState is ManualCollectionExistingState.ScheduledFailed or ManualCollectionExistingState.ExistingData)
        {
            ReplaceConfirmationMessage = existingState == ManualCollectionExistingState.ScheduledFailed
                ? $"The scheduled {SelectedManualDataType} collection for {SelectedPlant.Name} failed. Run Manual will retry the API and replace the failed dataset if the new collection succeeds."
                : $"{existingReadingCount:N0} {SelectedManualDataType} reading(s) already exist for {SelectedPlant.Name} on {dayStart:dd MMM yyyy}. Replace them with a new manual collection?";
            IsReplaceConfirmationOpen = true;
            return;
        }

        await RunCollectionAsync();
    }

    private async Task ConfirmReplacementAsync()
    {
        IsReplaceConfirmationOpen = false;
        await RunCollectionAsync();
    }

    private void CancelReplacement() => IsReplaceConfirmationOpen = false;

    private async Task RunCollectionAsync()
    {
        if (IsRunningManualCollection || SelectedPlant is null) return;
        IsRunningManualCollection = true;
        ManualCollectionError = string.Empty;
        var plant = SelectedPlant;
        var dataType = SelectedManualDataType;
        try
        {
            var queued = await CollectionCommands.EnqueueAsync(new QueueCollectionCommand(
                plant.DatabaseId, "DDQ", dataType, "Manual DDQ", SubmissionMinute(DateTime.Now),
                "Current user", ReplaceExisting: true));
            var finalStatus = await WaitForCollectionAsync(queued.JobId);
            if (finalStatus.Status == CollectionJobStatus.Failed)
                ManualCollectionError = finalStatus.ErrorMessage ?? finalStatus.ResponseMessage;
            await LoadJobsAsync();
            CollectionPage = 1;
            RunResult = Jobs.FirstOrDefault(job => job.CollectionId == queued.CollectionId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ManualCollectionError = $"The collection command could not be completed: {exception.GetBaseException().Message}";
        }
        finally
        {
            IsRunningManualCollection = false;
        }
    }

    private async Task<CollectionCompletion> WaitForCollectionAsync(long jobId)
    {
        while (true)
        {
            await using var database = await ContextFactory.CreateDbContextAsync(RefreshCancellation.Token);
            var job = await database.CollectionJobs.AsNoTracking().Where(item => item.Id == jobId)
                .Select(item => new CollectionCompletion(item.Status, item.ResponseMessage, item.ErrorMessage))
                .SingleAsync(RefreshCancellation.Token);
            if (job.Status is CollectionJobStatus.Completed or CollectionJobStatus.Failed) return job;
            await Task.Delay(500, RefreshCancellation.Token);
        }
    }

    private static RawApiResponse CreateRawResponse(long collectionJobId, LssApiCollectionResult apiResult) => new()
    {
        CollectionJobId = collectionJobId,
        ReceivedAt = DateTimeOffset.UtcNow,
        HttpStatusCode = apiResult.HttpStatusCode ?? 0,
        ContentType = apiResult.ContentType,
        Payload = apiResult.Payload,
        Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiResult.Payload)))
    };

    private static string Limit(string? value, int length) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value[..Math.Min(value.Length, length)];

    private static DateTime SubmissionMinute(DateTime value) => new(value.Year, value.Month, value.Day,
        value.Hour, value.Minute, 0, DateTimeKind.Unspecified);

    private static string GetPlantCode(string plantName) => string.Concat(
        plantName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => word[0]));

    private sealed record PlantConnection(int DatabaseId, string PlantId, string Name, string State,
        decimal CapacityMw, IReadOnlyDictionary<string, string> ApiEndpoints);

    private sealed class ManualBatchItem
    {
        public long? JobId { get; set; }
        public int PlantDatabaseId { get; init; }
        public string PlantId { get; init; } = string.Empty;
        public string PlantName { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public decimal CapacityMw { get; init; }
        public string ApiType { get; init; } = string.Empty;
        public string DataType { get; init; } = string.Empty;
        public string Endpoint { get; init; } = string.Empty;
        public string Status { get; set; } = "Ready";
        public string Message { get; set; } = string.Empty;
    }

    private sealed record CollectionCompletion(string Status, string ResponseMessage, string? ErrorMessage);

    private sealed record ManualBatchOption(string Key, string ApiType, string DataType, string Label);
}
