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
    private string FormError { get; set; } = string.Empty;
    private string SuccessMessage { get; set; } = string.Empty;
    private string SuccessTitle { get; set; } = string.Empty;
    private string PlantSearchTerm { get; set; } = string.Empty;
    private int NewPlantId { get; set; }
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
        Schedules = await db.CollectionSchedules.OrderBy(x => x.Name).ToListAsync();
    }
    private LssPlant GetPlant(int id) => Plants.FirstOrDefault(x => x.Id == id) ?? new LssPlant { Name = "Unknown plant", PlantId = "-" };
    private string RequiredApiDataType => CollectionSchedulePolicy.ApiDataType(NewApiType, NewDataType);
    private IEnumerable<LssPlant> AvailablePlants => Plants.Where(x => x.IsActive && x.ApiConfigurations.Any(a =>
        a.ApiType == NewApiType && a.DataType == RequiredApiDataType && !string.IsNullOrWhiteSpace(a.ApiLink)));
    private IEnumerable<LssPlant> PlantSuggestions => AvailablePlants.Where(x => NewPlantId == 0 && (string.IsNullOrWhiteSpace(PlantSearchTerm) || x.Name.Contains(PlantSearchTerm, StringComparison.OrdinalIgnoreCase) || x.PlantId.Contains(PlantSearchTerm, StringComparison.OrdinalIgnoreCase))).Take(8);
    private void SelectPlant(LssPlant plant) { NewPlantId = plant.Id; PlantSearchTerm = $"{plant.Name} ({plant.PlantId})"; IsPlantDropdownOpen = false; }
    private void ClearPlantSelection() { NewPlantId = 0; IsPlantDropdownOpen = true; }
    private void TogglePlantDropdown() { if (!IsPlantDropdownOpen) { NewPlantId = 0; PlantSearchTerm = string.Empty; } IsPlantDropdownOpen = !IsPlantDropdownOpen; }
    private void OpenDialog()
    {
        EditingId = null; NewApiType = SelectedApiType; NewDataType = NewApiType == "DDQ" ? DdqDataTypes[0] : "MMF"; PlantSearchTerm = string.Empty;
        NewInterval = "15 minutes"; NewRunTime = "00:15"; NewRetryCount = 3; NewIsActive = true; OriginalIsActive = true; FormError = string.Empty; IsDeleteConfirmationOpen = false; IsStatusConfirmationOpen = false; StatusChangeConfirmed = false;
        NewPlantId = 0; NewScheduleDay = "Monday"; NewScheduleMonths = []; IsPlantDropdownOpen = false; IsDialogOpen = true;
    }
    private void OpenEditDialog(CollectionSchedule item)
    {
        EditingId = item.Id; NewPlantId = item.PlantId; NewApiType = item.ApiType; NewDataType = item.DataType; PlantSearchTerm = $"{GetPlant(item.PlantId).Name} ({GetPlant(item.PlantId).PlantId})"; IsPlantDropdownOpen = false;
        NewInterval = item.Interval; NewRunTime = item.RunTime; NewScheduleDay = item.ScheduleDay; NewScheduleMonths = ParseMonths(item.ScheduleMonths); NewRetryCount = item.RetryCount; NewIsActive = item.IsActive; OriginalIsActive = item.IsActive; FormError = string.Empty; IsDeleteConfirmationOpen = false; IsStatusConfirmationOpen = false; StatusChangeConfirmed = false; IsDialogOpen = true;
    }
    private void ApiTypeChanged() { NewDataType = NewApiType == "DDQ" ? DdqDataTypes[0] : "MMF"; NewInterval = "15 minutes"; NewPlantId = 0; PlantSearchTerm = string.Empty; }
    private void ScheduleTypeChanged()
    {
        NewScheduleDay = NewDataType switch { "Weekly" => "Monday", "4 Month" => "1", _ => string.Empty };
        NewScheduleMonths = [];
        if (NewDataType is "Rolling 24 Hour" or "MMF") NewInterval = "15 minutes";
        NewPlantId = 0;
        PlantSearchTerm = string.Empty;
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
        if (NewPlantId == 0) { FormError = $"Select an active plant with a configured {NewApiType} {RequiredApiDataType} API."; return; }
        if (NewDataType == "4 Month" && NewScheduleMonths.Count == 0) { FormError = "Select at least one month for the 4 Month schedule."; return; }
        if (EditingId.HasValue && NewIsActive != OriginalIsActive && !StatusChangeConfirmed) { IsStatusConfirmationOpen = true; return; }
        await using var db = await ContextFactory.CreateDbContextAsync();
        var hasEndpoint = await db.PlantApiConfigurations.AnyAsync(x => x.PlantId == NewPlantId
            && x.ApiType == NewApiType && x.DataType == RequiredApiDataType && x.ApiLink != string.Empty);
        if (!hasEndpoint) { FormError = $"The selected plant does not have a {NewApiType} {RequiredApiDataType} API endpoint."; return; }
        var item = EditingId.HasValue ? await db.CollectionSchedules.SingleAsync(x => x.Id == EditingId) : new CollectionSchedule { ScheduleCode = $"SCH-{NewApiType}-{DateTime.UtcNow:yyyyMMddHHmmss}" };
        if (NewApiType == "MMF") { NewDataType = "MMF"; NewInterval = "15 minutes"; NewScheduleDay = string.Empty; }
        var plant = GetPlant(NewPlantId); item.Name = $"{plant.Name} - {NewDataType}"; item.PlantId = NewPlantId; item.ApiType = NewApiType; item.DataType = NewDataType;
        item.Interval = NewInterval; item.RunTime = NewRunTime; item.ScheduleDay = NewScheduleDay; item.ScheduleMonths = NewDataType == "4 Month" ? string.Join(',', NewScheduleMonths.OrderBy(x => x)) : string.Empty; item.CronExpression = $"{NewDataType}|{NewScheduleDay}|{item.ScheduleMonths}|{NewInterval}|{NewRunTime}"; item.RetryCount = NewRetryCount; item.IsActive = NewIsActive;
        if (!EditingId.HasValue) db.CollectionSchedules.Add(item); await db.SaveChangesAsync(); IsDialogOpen = false; await LoadAsync(); SelectedApiType = NewApiType;
        SuccessTitle = EditingId.HasValue ? "Schedule updated" : "Schedule added"; SuccessMessage = $"{item.Name} has been successfully saved to MS SQL.";
    }
    private async Task DeleteScheduleAsync()
    {
        if (!EditingId.HasValue) return; await using var db = await ContextFactory.CreateDbContextAsync(); var item = await db.CollectionSchedules.SingleAsync(x => x.Id == EditingId);
        var name = item.Name; db.CollectionSchedules.Remove(item); await db.SaveChangesAsync(); IsDialogOpen = false; await LoadAsync(); SuccessTitle = "Schedule deleted"; SuccessMessage = $"{name} has been removed from MS SQL.";
    }
}
