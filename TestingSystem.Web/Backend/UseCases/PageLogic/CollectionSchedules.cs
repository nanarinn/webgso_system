using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Models;
using TestingSystem.Web.Services;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class CollectionSchedules : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
    [Inject] private ILogger<CollectionSchedules> Logger { get; set; } = default!;
    private static readonly string[] ApiTypes = ["DDQ", "MMF"];
    private static readonly string[] DdqDataTypes = ["Rolling 24 Hour", "Daily", "Weekly", "4 Month"];
    private static readonly string[] RollingIntervals = ["15 minutes", "30 minutes", "1 hour"];
    private static readonly string[] WeekDays = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
    private static readonly (int Value, string Label)[] Months = [(1,"Jan"),(2,"Feb"),(3,"Mar"),(4,"Apr"),(5,"May"),(6,"Jun"),(7,"Jul"),(8,"Aug"),(9,"Sep"),(10,"Oct"),(11,"Nov"),(12,"Dec")];
    private List<LssPlant> Plants { get; set; } = [];
    private List<CollectionSchedule> Schedules { get; set; } = [];
    private Dictionary<int, ScheduleRunStatus> LatestRunStatuses { get; set; } = [];
    private readonly CancellationTokenSource StatusRefreshCancellation = new();
    private Task? StatusRefreshTask;
    private string SearchTerm { get; set; } = string.Empty;
    private string SelectedApiType { get; set; } = "DDQ";
    private bool IsDialogOpen { get; set; }
    private bool IsDeleteConfirmationOpen { get; set; }
    private bool IsStatusConfirmationOpen { get; set; }
    private bool StatusChangeConfirmed { get; set; }
    private bool OriginalIsActive { get; set; } = true;
    private bool IsPlantDropdownOpen { get; set; }
    private int? EditingId { get; set; }
    private string FormError { get; set; } = string.Empty;
    private string SuccessMessage { get; set; } = string.Empty;
    private string SuccessTitle { get; set; } = string.Empty;
    private string PlantSearchTerm { get; set; } = string.Empty;
    private int NewPlantId { get; set; }
    private HashSet<int> NewPlantIds { get; set; } = [];
    private string NewApiType { get; set; } = "DDQ";
    private string NewDataType { get; set; } = "Rolling 24 Hour";
    private string NewInterval { get; set; } = "15 minutes";
    private string NewRunTime { get; set; } = "00:15";
    private string NewScheduleDay { get; set; } = "Monday";
    private HashSet<int> NewScheduleMonths { get; set; } = [];
    private int NewRetryCount { get; set; } = 3;
    private bool NewIsActive { get; set; } = true;

    private List<CollectionSchedule> FilteredSchedules => Schedules.Where(x => x.ApiType == SelectedApiType &&
        (string.IsNullOrWhiteSpace(SearchTerm) || x.Name.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ||
         GetPlant(x.PlantId).Name.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) || x.DataType.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))).ToList();

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
        StatusRefreshTask = RefreshRunStatusesAsync(StatusRefreshCancellation.Token);
    }

    private async Task LoadAsync()
    {
        await using var db = await ContextFactory.CreateDbContextAsync();
        Plants = await db.LssPlants.Include(x => x.ApiConfigurations).OrderBy(x => x.Name).ToListAsync();
        Schedules = await db.CollectionSchedules.OrderByDescending(x => x.Id).ToListAsync();
        await LoadRunStatusesAsync(db);
    }

    private async Task LoadRunStatusesAsync(ApplicationDbContext db, CancellationToken cancellationToken = default)
    {
        var jobs = await db.CollectionJobs.AsNoTracking()
            .Where(x => x.CollectionScheduleId != null)
            .OrderByDescending(x => x.StartedAt)
            .Select(x => new { x.CollectionScheduleId, x.Status, x.StartedAt, x.ErrorMessage })
            .ToListAsync(cancellationToken);

        LatestRunStatuses = jobs
            .Select(x => new { ScheduleId = x.CollectionScheduleId, Job = x })
            .Where(x => x.ScheduleId.HasValue)
            .GroupBy(x => x.ScheduleId!.Value)
            .ToDictionary(
                group => group.Key,
                group => MapRunStatus(group.First().Job.Status, group.First().Job.StartedAt,
                    group.First().Job.ErrorMessage));
    }

    private async Task RefreshRunStatusesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await using var db = await ContextFactory.CreateDbContextAsync(cancellationToken);
                await LoadRunStatusesAsync(db, cancellationToken);
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private ScheduleRunStatus GetRunStatus(int scheduleId) => LatestRunStatuses.GetValueOrDefault(scheduleId)
        ?? new("Waiting", "status-warning", "This schedule has not run yet.");

    private static ScheduleRunStatus MapRunStatus(string status, DateTimeOffset startedAt, string? errorMessage) => status switch
    {
        "Completed" => new("Successful", "status-successful", $"Last collection completed. Started {startedAt.ToLocalTime():dd MMM yyyy HH:mm}.") ,
        "Failed" => new("Failed", "status-failed", string.IsNullOrWhiteSpace(errorMessage) ? "The last collection failed." : errorMessage),
        "Running" or "Pending" => new("Processing", "status-processing", $"Collection started {startedAt.ToLocalTime():dd MMM yyyy HH:mm}."),
        _ => new(status, "status-warning", $"Last collection status: {status}.")
    };

    private LssPlant GetPlant(int id) => Plants.FirstOrDefault(x => x.Id == id) ?? new LssPlant { Name = "Unknown plant", PlantId = "-" };
    private string RequiredApiDataType => CollectionSchedulePolicy.ApiDataType(NewApiType, NewDataType);
    private IEnumerable<LssPlant> AvailablePlants => Plants.Where(x => x.IsActive && x.ApiConfigurations.Any(a =>
        a.ApiType == NewApiType && a.DataType == RequiredApiDataType && !string.IsNullOrWhiteSpace(a.ApiLink)));
    private List<LssPlant> AvailablePlantList => AvailablePlants.OrderBy(x => x.Name).ThenBy(x => x.PlantId).ToList();
    private IEnumerable<LssPlant> PlantSuggestions => AvailablePlantList.Where(x => string.IsNullOrWhiteSpace(PlantSearchTerm)
        || x.Name.Contains(PlantSearchTerm, StringComparison.OrdinalIgnoreCase)
        || x.PlantId.Contains(PlantSearchTerm, StringComparison.OrdinalIgnoreCase)
        || x.State.Contains(PlantSearchTerm, StringComparison.OrdinalIgnoreCase));
    private IEnumerable<LssPlant> SelectedPlants => Plants.Where(x => NewPlantIds.Contains(x.Id)).OrderBy(x => x.Name);
    private bool AllAvailablePlantsSelected => AvailablePlantList.Count > 0
        && AvailablePlantList.All(x => NewPlantIds.Contains(x.Id));
    private void TogglePlantSelection(LssPlant plant)
    {
        if (!NewPlantIds.Add(plant.Id)) NewPlantIds.Remove(plant.Id);
        FormError = string.Empty;
    }
    private void SelectAllAvailablePlants()
    {
        var availableIds = AvailablePlantList.Select(x => x.Id).ToHashSet();
        if (availableIds.Count == 0) return;
        if (availableIds.All(NewPlantIds.Contains))
        {
            NewPlantIds.ExceptWith(availableIds);
        }
        else
        {
            NewPlantIds.UnionWith(availableIds);
            IsPlantDropdownOpen = false;
            PlantSearchTerm = string.Empty;
        }
        FormError = string.Empty;
    }
    private void RemoveSelectedPlant(int plantId) => NewPlantIds.Remove(plantId);
    private void ClearPlantSelections() { NewPlantIds.Clear(); PlantSearchTerm = string.Empty; FormError = string.Empty; }
    private void TogglePlantDropdown() => IsPlantDropdownOpen = !IsPlantDropdownOpen;
    private void OpenDialog()
    {
        EditingId = null; NewApiType = SelectedApiType; NewDataType = NewApiType == "DDQ" ? DdqDataTypes[0] : "MMF"; PlantSearchTerm = string.Empty;
        NewInterval = "15 minutes"; NewRunTime = "00:15"; NewRetryCount = 3; NewIsActive = true; OriginalIsActive = true; FormError = string.Empty; IsDeleteConfirmationOpen = false; IsStatusConfirmationOpen = false; StatusChangeConfirmed = false;
        NewPlantId = 0; NewPlantIds = []; NewScheduleDay = string.Empty; NewScheduleMonths = []; IsPlantDropdownOpen = false; IsDialogOpen = true;
    }
    private void OpenEditDialog(CollectionSchedule item)
    {
        EditingId = item.Id; NewPlantId = item.PlantId; NewPlantIds = [item.PlantId]; NewApiType = item.ApiType; NewDataType = item.DataType; PlantSearchTerm = $"{GetPlant(item.PlantId).Name} ({GetPlant(item.PlantId).PlantId})"; IsPlantDropdownOpen = false;
        NewInterval = item.Interval; NewRunTime = item.RunTime; NewScheduleDay = item.ScheduleDay; NewScheduleMonths = ParseMonths(item.ScheduleMonths); NewRetryCount = item.RetryCount; NewIsActive = item.IsActive; OriginalIsActive = item.IsActive; FormError = string.Empty; IsDeleteConfirmationOpen = false; IsStatusConfirmationOpen = false; StatusChangeConfirmed = false; IsDialogOpen = true;
    }
    private void ApiTypeChanged() { NewDataType = NewApiType == "DDQ" ? DdqDataTypes[0] : "MMF"; NewInterval = "15 minutes"; if (!EditingId.HasValue) { NewPlantIds.Clear(); PlantSearchTerm = string.Empty; } }
    private void ScheduleTypeChanged()
    {
        NewScheduleDay = NewDataType switch { "Weekly" => "Monday", "4 Month" => "1", _ => string.Empty };
        NewScheduleMonths = [];
        NewInterval = "15 minutes";
        if (!EditingId.HasValue) { NewPlantIds.Clear(); PlantSearchTerm = string.Empty; }
    }
    private string ScheduleTime(CollectionSchedule item) => item.DataType switch { "Rolling 24 Hour" or "MMF" => $"Every {item.Interval}", "Weekly" => $"{item.ScheduleDay} at {item.RunTime}", "4 Month" => $"Day {item.ScheduleDay} · {MonthLabels(ParseMonths(item.ScheduleMonths))} · {item.RunTime}", _ => $"Daily at {item.RunTime}" };
    private void ToggleScheduleMonth(int month) { if (!NewScheduleMonths.Add(month)) NewScheduleMonths.Remove(month); }
    private static HashSet<int> ParseMonths(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out var month) ? month : 0).Where(x => x is >= 1 and <= 12).ToHashSet();
    private static string MonthLabels(IEnumerable<int> months) { var values = months.OrderBy(x => x).Select(x => Months.First(m => m.Value == x).Label).ToList(); return values.Count == 0 ? "No months" : string.Join(", ", values); }
    private void CloseDialog() => IsDialogOpen = false;
    private void SetScheduleStatus(bool isActive) { NewIsActive = isActive; StatusChangeConfirmed = false; IsStatusConfirmationOpen = false; }
    private async Task ConfirmStatusChangeAsync() { StatusChangeConfirmed = true; IsStatusConfirmationOpen = false; await SaveScheduleAsync(); }
    private async Task SaveScheduleAsync()
    {
        var targetPlantIds = EditingId.HasValue
            ? (NewPlantId > 0 ? new[] { NewPlantId } : Array.Empty<int>())
            : NewPlantIds.OrderBy(id => id).ToArray();
        if (targetPlantIds.Length == 0) { FormError = $"Select one or more active plants with a configured {NewApiType} {RequiredApiDataType} API."; return; }
        if (NewDataType == "4 Month" && NewScheduleMonths.Count == 0) { FormError = "Select at least one month for the 4 Month schedule."; return; }
        if (!ReportScheduleTimePolicy.TryNormalize(NewRunTime, out var normalizedRunTime)) { FormError = "Enter a valid schedule time in 24-hour format (HH:mm)."; return; }
        NewRunTime = normalizedRunTime;
        if (EditingId.HasValue && NewIsActive != OriginalIsActive && !StatusChangeConfirmed) { IsStatusConfirmationOpen = true; return; }
        try
        {
            await using var db = await ContextFactory.CreateDbContextAsync();
            if (NewApiType == "MMF") { NewDataType = "MMF"; NewInterval = "15 minutes"; NewScheduleDay = string.Empty; }
            var endpointPlantIds = await db.PlantApiConfigurations.AsNoTracking()
                .Where(x => targetPlantIds.Contains(x.PlantId) && x.ApiType == NewApiType
                    && x.DataType == RequiredApiDataType && x.ApiLink != string.Empty)
                .Select(x => x.PlantId).Distinct().ToListAsync();
            var missingEndpointIds = targetPlantIds.Except(endpointPlantIds).ToArray();
            if (missingEndpointIds.Length > 0)
            {
                var missingNames = string.Join(", ", missingEndpointIds.Select(id => GetPlant(id).Name));
                FormError = $"These plants do not have a configured {NewApiType} {RequiredApiDataType} endpoint: {missingNames}.";
                return;
            }

            var scheduleMonths = NewDataType == "4 Month"
                ? string.Join(',', NewScheduleMonths.OrderBy(x => x))
                : string.Empty;
            var cronExpression = $"{NewDataType}|{NewScheduleDay}|{scheduleMonths}|{NewInterval}|{NewRunTime}";
            if (EditingId.HasValue)
            {
                var item = await db.CollectionSchedules.SingleAsync(x => x.Id == EditingId);
                var plant = GetPlant(NewPlantId);
                item.Name = $"{plant.Name} - {NewDataType}"; item.PlantId = NewPlantId; item.ApiType = NewApiType; item.DataType = NewDataType;
                item.Interval = NewInterval; item.RunTime = NewRunTime; item.ScheduleDay = NewScheduleDay; item.ScheduleMonths = scheduleMonths;
                item.CronExpression = cronExpression; item.RetryCount = NewRetryCount; item.IsActive = NewIsActive;
                await db.SaveChangesAsync();
                SuccessTitle = "Schedule updated";
                SuccessMessage = $"{item.Name} has been successfully saved to MS SQL.";
            }
            else
            {
                var existingSchedules = await db.CollectionSchedules.AsNoTracking()
                    .Where(x => targetPlantIds.Contains(x.PlantId) && x.ApiType == NewApiType
                        && x.DataType == NewDataType)
                    .Select(x => new { x.PlantId, x.Interval, x.RunTime, x.ScheduleDay, x.ScheduleMonths })
                    .ToListAsync();
                var duplicatePlantIds = existingSchedules.Where(x => NewDataType switch
                    {
                        "Rolling 24 Hour" or "MMF" => x.Interval == NewInterval,
                        "Weekly" => x.ScheduleDay == NewScheduleDay && x.RunTime == NewRunTime,
                        "4 Month" => x.ScheduleDay == NewScheduleDay && x.ScheduleMonths == scheduleMonths && x.RunTime == NewRunTime,
                        _ => x.RunTime == NewRunTime
                    })
                    .Select(x => x.PlantId).Distinct().ToList();
                var plantIdsToCreate = targetPlantIds.Except(duplicatePlantIds).ToArray();
                var createdAt = DateTime.UtcNow;
                foreach (var plantId in plantIdsToCreate)
                {
                    var plant = GetPlant(plantId);
                    db.CollectionSchedules.Add(new CollectionSchedule
                    {
                        ScheduleCode = $"SCH-{NewApiType}-{createdAt:yyyyMMddHHmmssfff}-{plantId}",
                        Name = $"{plant.Name} - {NewDataType}", PlantId = plantId, ApiType = NewApiType,
                        DataType = NewDataType, Interval = NewInterval, RunTime = NewRunTime,
                        ScheduleDay = NewScheduleDay, ScheduleMonths = scheduleMonths, CronExpression = cronExpression,
                        RetryCount = NewRetryCount, IsActive = NewIsActive
                    });
                }
                if (plantIdsToCreate.Length == 0)
                {
                    FormError = "Every selected plant already has this collection schedule. No duplicate schedule was created.";
                    return;
                }
                await db.SaveChangesAsync();
                SuccessTitle = plantIdsToCreate.Length == 1 ? "Schedule added" : "Schedules added";
                SuccessMessage = $"{plantIdsToCreate.Length} schedule(s) saved to MS SQL."
                    + (duplicatePlantIds.Count > 0 ? $" {duplicatePlantIds.Count} duplicate schedule(s) were skipped." : string.Empty);
            }
            IsDialogOpen = false; await LoadAsync(); SelectedApiType = NewApiType;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Unable to save collection schedule for plants {PlantIds} at {RunTime}.", string.Join(',', targetPlantIds), NewRunTime);
            FormError = "The schedule could not be saved. Please retry after the database schema has been synchronised.";
        }
    }
    private async Task DeleteScheduleAsync()
    {
        if (!EditingId.HasValue) return; await using var db = await ContextFactory.CreateDbContextAsync(); var item = await db.CollectionSchedules.SingleAsync(x => x.Id == EditingId);
        var name = item.Name; db.CollectionSchedules.Remove(item); await db.SaveChangesAsync(); IsDialogOpen = false; await LoadAsync(); SuccessTitle = "Schedule deleted"; SuccessMessage = $"{name} has been removed from MS SQL.";
    }

    public async ValueTask DisposeAsync()
    {
        await StatusRefreshCancellation.CancelAsync();
        if (StatusRefreshTask is not null)
        {
            try { await StatusRefreshTask; }
            catch (OperationCanceledException) { }
        }
        StatusRefreshCancellation.Dispose();
    }

    private sealed record ScheduleRunStatus(string Label, string CssClass, string Detail);

}
