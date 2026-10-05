using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Backend.UseCases.UCLSS027Audit;
using TestingSystem.Web.Data;
using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Models;
using TestingSystem.Web.Services;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class CollectionSchedules : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
    [Inject] private ILogger<CollectionSchedules> Logger { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] private IAuditTrailService AuditTrail { get; set; } = default!;
    private static readonly string[] ApiTypes = ["DDQ", "MMF"];
    private static readonly string[] DdqDataTypes = ["Rolling 24 Hour", "Daily", "Weekly", "4 Month"];
    private static readonly string[] RollingIntervals = ["15 minutes", "30 minutes", "1 hour"];
    private static readonly string[] WeekDays = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
    private static readonly (int Value, string Label)[] Months = [(1,"Jan"),(2,"Feb"),(3,"Mar"),(4,"Apr"),(5,"May"),(6,"Jun"),(7,"Jul"),(8,"Aug"),(9,"Sep"),(10,"Oct"),(11,"Nov"),(12,"Dec")];
    private List<LssPlant> Plants { get; set; } = [];
    private List<CollectionSchedule> Schedules { get; set; } = [];
    private string SearchTerm { get; set; } = string.Empty;
    private string SelectedApiType { get; set; } = "DDQ";
    private bool IsDialogOpen { get; set; }
    private bool IsDeleteConfirmationOpen { get; set; }
    private bool IsStatusConfirmationOpen { get; set; }
    private bool StatusChangeConfirmed { get; set; }
    private bool OriginalIsActive { get; set; } = true;
    private bool IsPlantDropdownOpen { get; set; }
    private int? EditingId { get; set; }
    private HashSet<int> EditingScheduleIds { get; set; } = [];
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

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await using var db = await ContextFactory.CreateDbContextAsync();
        Plants = await db.LssPlants.Include(x => x.ApiConfigurations).OrderBy(x => x.Name).ToListAsync();
        Schedules = await db.CollectionSchedules.OrderByDescending(x => x.Id).ToListAsync();
    }

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
        NewPlantId = 0; NewPlantIds = []; EditingScheduleIds = []; NewScheduleDay = string.Empty; NewScheduleMonths = []; IsPlantDropdownOpen = false; IsDialogOpen = true;
    }
    private void OpenEditDialog(CollectionSchedule item)
    {
        var group = GetScheduleEditGroup(item);
        EditingId = item.Id; EditingScheduleIds = group.Select(schedule => schedule.Id).ToHashSet();
        NewPlantId = item.PlantId; NewPlantIds = group.Select(schedule => schedule.PlantId).ToHashSet(); NewApiType = item.ApiType; NewDataType = item.DataType; PlantSearchTerm = string.Empty; IsPlantDropdownOpen = false;
        NewInterval = item.Interval; NewRunTime = item.RunTime; NewScheduleDay = item.ScheduleDay; NewScheduleMonths = ParseMonths(item.ScheduleMonths); NewRetryCount = item.RetryCount; NewIsActive = item.IsActive; OriginalIsActive = item.IsActive; FormError = string.Empty; IsDeleteConfirmationOpen = false; IsStatusConfirmationOpen = false; StatusChangeConfirmed = false; IsDialogOpen = true;
    }
    private List<CollectionSchedule> GetScheduleEditGroup(CollectionSchedule item)
    {
        var parts = item.ScheduleCode.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 4 && parts[^2].Length >= 14 && parts[^2].All(char.IsDigit))
        {
            var prefix = string.Join("-", parts[..^1]) + "-";
            return Schedules.Where(schedule => schedule.ScheduleCode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return [item];
    }
    private void KeepAvailablePlantSelections()
    {
        NewPlantIds.IntersectWith(AvailablePlantList.Select(plant => plant.Id));
        PlantSearchTerm = string.Empty;
    }
    private void ApiTypeChanged() { NewDataType = NewApiType == "DDQ" ? DdqDataTypes[0] : "MMF"; NewInterval = "15 minutes"; KeepAvailablePlantSelections(); }
    private void ScheduleTypeChanged()
    {
        NewScheduleDay = NewDataType switch { "Weekly" => "Monday", "4 Month" => "1", _ => string.Empty };
        NewScheduleMonths = [];
        NewInterval = "15 minutes";
        KeepAvailablePlantSelections();
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
        var targetPlantIds = NewPlantIds.OrderBy(id => id).ToArray();
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
                var groupItems = await db.CollectionSchedules.Where(x => EditingScheduleIds.Contains(x.Id)).ToListAsync();
                var conflictingPlantIds = await db.CollectionSchedules.AsNoTracking()
                    .Where(x => !EditingScheduleIds.Contains(x.Id) && targetPlantIds.Contains(x.PlantId)
                        && x.ApiType == NewApiType && x.DataType == NewDataType)
                    .Where(x => NewDataType == "Rolling 24 Hour" || NewDataType == "MMF"
                        ? x.Interval == NewInterval
                        : NewDataType == "Weekly"
                            ? x.ScheduleDay == NewScheduleDay && x.RunTime == NewRunTime
                            : NewDataType == "4 Month"
                                ? x.ScheduleDay == NewScheduleDay && x.ScheduleMonths == scheduleMonths && x.RunTime == NewRunTime
                                : x.RunTime == NewRunTime)
                    .Select(x => x.PlantId).Distinct().ToListAsync();
                if (conflictingPlantIds.Count > 0)
                {
                    FormError = $"These plants already have the same schedule: {string.Join(", ", conflictingPlantIds.Select(id => GetPlant(id).Name))}.";
                    return;
                }

                var itemsByPlant = groupItems.ToDictionary(schedule => schedule.PlantId);
                db.CollectionSchedules.RemoveRange(groupItems.Where(schedule => !targetPlantIds.Contains(schedule.PlantId)));
                var groupPrefix = $"SCH-{NewApiType}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
                foreach (var plantId in targetPlantIds)
                {
                    var plant = GetPlant(plantId);
                    if (!itemsByPlant.TryGetValue(plantId, out var schedule))
                    {
                        schedule = new CollectionSchedule { PlantId = plantId };
                        db.CollectionSchedules.Add(schedule);
                    }
                    schedule.ScheduleCode = $"{groupPrefix}-{plantId}";
                    schedule.Name = $"{plant.Name} - {NewDataType}"; schedule.PlantId = plantId; schedule.ApiType = NewApiType; schedule.DataType = NewDataType;
                    schedule.Interval = NewInterval; schedule.RunTime = NewRunTime; schedule.ScheduleDay = NewScheduleDay; schedule.ScheduleMonths = scheduleMonths;
                    schedule.CronExpression = cronExpression; schedule.RetryCount = NewRetryCount; schedule.IsActive = NewIsActive;
                }
                await db.SaveChangesAsync();
                var principal = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
                var action = NewIsActive != OriginalIsActive
                    ? "Change collection schedule status"
                    : "Update collection schedules";
                await AuditTrail.RecordAsync(principal, action, "Success", "CollectionSchedule",
                    string.Join(",", groupItems.Select(item => item.ScheduleCode)),
                    $"{targetPlantIds.Length} plant(s); {NewApiType} {NewDataType}; {NewInterval}; time {NewRunTime}; retry {NewRetryCount}; status {(NewIsActive ? "Active" : "Inactive")}.",
                    $"schedule:{NewApiType}:{NewDataType}:{NewRunTime}");
                SuccessTitle = targetPlantIds.Length == 1 ? "Schedule updated" : "Schedules updated";
                SuccessMessage = $"The collection schedule has been saved for {targetPlantIds.Length} plant(s).";
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
                var principal = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
                await AuditTrail.RecordAsync(principal, "Create collection schedules", "Success",
                    "CollectionSchedule", $"{NewApiType}-{NewDataType}",
                    $"Created for {plantIdsToCreate.Length} plant(s); interval {NewInterval}; time {NewRunTime}; retry {NewRetryCount}; status {(NewIsActive ? "Active" : "Inactive")}."
                    + (duplicatePlantIds.Count > 0 ? $" Skipped {duplicatePlantIds.Count} duplicate(s)." : string.Empty),
                    $"schedule:{NewApiType}:{NewDataType}:{NewRunTime}");
                SuccessTitle = plantIdsToCreate.Length == 1 ? "Schedule added" : "Schedules added";
                SuccessMessage = $"{plantIdsToCreate.Length} schedule(s) saved to MS SQL."
                    + (duplicatePlantIds.Count > 0 ? $" {duplicatePlantIds.Count} duplicate schedule(s) were skipped." : string.Empty);
            }
            IsDialogOpen = false; await LoadAsync(); SelectedApiType = NewApiType;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Unable to save collection schedule for plants {PlantIds} at {RunTime}.", string.Join(',', targetPlantIds), NewRunTime);
            var principal = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
            await AuditTrail.RecordAsync(principal,
                EditingId.HasValue ? "Update collection schedules" : "Create collection schedules",
                "Failed", "CollectionSchedule", $"{NewApiType}-{NewDataType}",
                exception.GetBaseException().Message, $"schedule:{NewApiType}:{NewDataType}:{NewRunTime}");
            FormError = "The schedule could not be saved. Please retry after the database schema has been synchronised.";
        }
    }
    private async Task DeleteScheduleAsync()
    {
        if (!EditingId.HasValue) return;
        try
        {
            await using var db = await ContextFactory.CreateDbContextAsync();
            var items = await db.CollectionSchedules.Where(x => EditingScheduleIds.Contains(x.Id)).ToListAsync();
            var references = string.Join(",", items.Select(item => item.ScheduleCode));
            db.CollectionSchedules.RemoveRange(items); await db.SaveChangesAsync();
            var principal = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
            await AuditTrail.RecordAsync(principal, "Delete collection schedules", "Success",
                "CollectionSchedule", references, $"Deleted {items.Count} schedule(s).", "schedule:delete");
            IsDialogOpen = false; await LoadAsync();
            SuccessTitle = items.Count == 1 ? "Schedule deleted" : "Schedules deleted";
            SuccessMessage = $"{items.Count} collection schedule(s) have been removed from MS SQL.";
        }
        catch (Exception exception)
        {
            var principal = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
            await AuditTrail.RecordAsync(principal, "Delete collection schedules", "Failed",
                "CollectionSchedule", EditingId.Value.ToString(), exception.GetBaseException().Message, "schedule:delete");
            FormError = $"The schedule could not be deleted: {exception.GetBaseException().Message}";
            IsDeleteConfirmationOpen = false;
        }
    }

}
