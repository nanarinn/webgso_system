using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Services;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class CollectionSchedules : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
    [Inject] private IStoredDataService DataStore { get; set; } = default!;
    private const string DataCategory = "CollectionSchedules";

    private List<string> PlantNames { get; set; } = [];
    private List<ScheduleConfig> Schedules { get; set; } = [];
    private string SearchTerm { get; set; } = string.Empty;
    private bool IsDialogOpen { get; set; }
    private string NewName { get; set; } = string.Empty;
    private string NewPlant { get; set; } = string.Empty;
    private string NewSource { get; set; } = "DDQ";
    private string NewInterval { get; set; } = "15 minutes";
    private string NewRunTime { get; set; } = "00:15";
    private int NewRetryCount { get; set; } = 3;

    private List<ScheduleConfig> FilteredSchedules => string.IsNullOrWhiteSpace(SearchTerm) ? Schedules : Schedules.Where(x => x.Name.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) || x.Plant.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) || x.Source.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)).ToList();

    protected override async Task OnInitializedAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        PlantNames = await database.LssPlants
            .Where(plant => plant.IsActive)
            .OrderBy(plant => plant.Name)
            .Select(plant => plant.Name)
            .ToListAsync();
        NewPlant = PlantNames[0];
        ScheduleConfig[] seed =
        {
            new("SCH-DDQ-001", "DDQ Quarter-Hour Collection", PlantNames[0], "DDQ", "15 minutes", "00:15", 3, true),
            new("SCH-MMF-002", "Morning MMF Forecast", PlantNames[1], "MMF", "Daily", "07:00", 2, true),
            new("SCH-DDQ-003", "Sepang DDQ Collection", PlantNames[2], "DDQ", "30 minutes", "00:30", 3, true),
            new("SCH-MMF-004", "Pengerang MMF Collection", PlantNames[3], "MMF", "1 hour", "01:00", 2, false)
        };
        await DataStore.SeedAsync(DataCategory, seed.Select(item => (item.ScheduleId, item)));
        Schedules = await DataStore.LoadAsync<ScheduleConfig>(DataCategory);
    }

    private void OpenDialog() { NewName = ""; NewPlant = PlantNames[0]; NewSource = "DDQ"; NewInterval = "15 minutes"; NewRunTime = "00:15"; NewRetryCount = 3; IsDialogOpen = true; }
    private void CloseDialog() => IsDialogOpen = false;
    private async Task SaveSchedule()
    {
        var schedule = new ScheduleConfig($"SCH-{NewSource}-{Schedules.Count + 1:000}", NewName.Trim(), NewPlant, NewSource, NewInterval, NewRunTime, NewRetryCount, true);
        await DataStore.SaveAsync(DataCategory, schedule.ScheduleId, schedule);
        Schedules.Insert(0, schedule);
        IsDialogOpen = false;
    }
    private async Task ToggleSchedule(ScheduleConfig schedule)
    {
        var index = Schedules.IndexOf(schedule);
        var updated = schedule with { IsActive = !schedule.IsActive };
        await DataStore.SaveAsync(DataCategory, updated.ScheduleId, updated);
        Schedules[index] = updated;
    }
    private sealed record ScheduleConfig(string ScheduleId, string Name, string Plant, string Source, string Interval, string RunTime, int RetryCount, bool IsActive);
}
