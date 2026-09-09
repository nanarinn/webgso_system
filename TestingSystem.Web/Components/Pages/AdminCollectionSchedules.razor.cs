using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;

namespace TestingSystem.Web.Components.Pages;

public partial class AdminCollectionSchedules : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;

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
        Schedules =
        [
            new("SCH-DDQ-001", "DDQ Quarter-Hour Collection", PlantNames[0], "DDQ", "15 minutes", "00:15", 3, true),
            new("SCH-MMF-002", "Morning MMF Forecast", PlantNames[1], "MMF", "Daily", "07:00", 2, true),
            new("SCH-DDQ-003", "Sepang DDQ Collection", PlantNames[2], "DDQ", "30 minutes", "00:30", 3, true),
            new("SCH-MMF-004", "Pengerang MMF Collection", PlantNames[3], "MMF", "1 hour", "01:00", 2, false)
        ];
    }

    private void OpenDialog() { NewName = ""; NewPlant = PlantNames[0]; NewSource = "DDQ"; NewInterval = "15 minutes"; NewRunTime = "00:15"; NewRetryCount = 3; IsDialogOpen = true; }
    private void CloseDialog() => IsDialogOpen = false;
    private void SaveSchedule() { Schedules.Insert(0, new($"SCH-{NewSource}-{Schedules.Count + 1:000}", NewName.Trim(), NewPlant, NewSource, NewInterval, NewRunTime, NewRetryCount, true)); IsDialogOpen = false; }
    private void ToggleSchedule(ScheduleConfig schedule) { var index = Schedules.IndexOf(schedule); Schedules[index] = schedule with { IsActive = !schedule.IsActive }; }
    private sealed record ScheduleConfig(string ScheduleId, string Name, string Plant, string Source, string Interval, string RunTime, int RetryCount, bool IsActive);
}
