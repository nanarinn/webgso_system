using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Demo;
using TestingSystem.Web.Services;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class CollectionSchedules : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
    [Inject] private IStoredDataService DataStore { get; set; } = default!;
    private const string DataCategory = "CollectionSchedules";

    private List<string> PlantNames { get; set; } = [];
    private List<DemoScheduleConfig> Schedules { get; set; } = [];
    private string SearchTerm { get; set; } = string.Empty;
    private bool IsDialogOpen { get; set; }
    private string NewName { get; set; } = string.Empty;
    private string NewPlant { get; set; } = string.Empty;
    private string NewSource { get; set; } = "DDQ";
    private string NewInterval { get; set; } = "15 minutes";
    private string NewRunTime { get; set; } = "00:15";
    private int NewRetryCount { get; set; } = 3;

    private List<DemoScheduleConfig> FilteredSchedules => string.IsNullOrWhiteSpace(SearchTerm) ? Schedules : Schedules.Where(x => x.Name.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) || x.Plant.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) || x.Source.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)).ToList();

    protected override async Task OnInitializedAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        PlantNames = await database.LssPlants
            .Where(plant => plant.IsActive)
            .OrderBy(plant => plant.Name)
            .Select(plant => plant.Name)
            .ToListAsync();
        NewPlant = PlantNames[0];
        var seed = DemoData.CollectionSchedules(PlantNames);
        await DataStore.SeedAsync(DataCategory, seed.Select(item => (item.ScheduleId, item)));
        Schedules = await DataStore.LoadAsync<DemoScheduleConfig>(DataCategory);
    }

    private void OpenDialog() { NewName = ""; NewPlant = PlantNames[0]; NewSource = "DDQ"; NewInterval = "15 minutes"; NewRunTime = "00:15"; NewRetryCount = 3; IsDialogOpen = true; }
    private void CloseDialog() => IsDialogOpen = false;
    private async Task SaveSchedule()
    {
        var schedule = new DemoScheduleConfig($"SCH-{NewSource}-{Schedules.Count + 1:000}", NewName.Trim(), NewPlant, NewSource, NewInterval, NewRunTime, NewRetryCount, true);
        await DataStore.SaveAsync(DataCategory, schedule.ScheduleId, schedule);
        Schedules.Insert(0, schedule);
        IsDialogOpen = false;
    }
    private async Task ToggleSchedule(DemoScheduleConfig schedule)
    {
        var index = Schedules.IndexOf(schedule);
        var updated = schedule with { IsActive = !schedule.IsActive };
        await DataStore.SaveAsync(DataCategory, updated.ScheduleId, updated);
        Schedules[index] = updated;
    }
}
