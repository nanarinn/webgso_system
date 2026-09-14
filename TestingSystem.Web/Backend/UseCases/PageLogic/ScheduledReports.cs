using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Demo;
using TestingSystem.Web.Services;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class ScheduledReports : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
    [Inject] private IStoredDataService DataStore { get; set; } = default!;

    private static readonly string[] Templates = ["Daily DDQ Performance", "MMF Forecast Variance", "Weekly LSS Operations", "Monthly Portfolio Compliance"];
    private static readonly string[] ReportingPeriods = ["Previous day", "Previous week", "Previous month", "Rolling 24 hours"];
    private static readonly string[] Frequencies = ["Daily", "Weekly", "Monthly"];
    private List<string> PlantNames { get; set; } = [];
    private List<DemoDistributionList> EmailLists { get; set; } = [];
    private List<DemoReportSchedule> Schedules { get; set; } = [];
    private List<DemoReportDelivery> Deliveries { get; set; } = [];
    private string ActiveTab { get; set; } = "Schedules";
    private bool IsScheduleDialogOpen { get; set; }
    private bool IsEmailDialogOpen { get; set; }
    private string? EditingScheduleId { get; set; }
    private string? EditingEmailListId { get; set; }
    private DemoReportDelivery? SelectedDelivery { get; set; }
    private string FormError { get; set; } = string.Empty;

    private string ScheduleName { get; set; } = string.Empty;
    private string ScheduleTemplate { get; set; } = Templates[0];
    private string ReportingPeriod { get; set; } = ReportingPeriods[0];
    private string Frequency { get; set; } = Frequencies[0];
    private string ExecutionTime { get; set; } = "07:00";
    private string SelectedDistributionListId { get; set; } = string.Empty;
    private HashSet<string> SelectedPlants { get; set; } = [];
    private string EmailListName { get; set; } = string.Empty;
    private string EmailAddresses { get; set; } = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        PlantNames = await database.LssPlants.Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => x.Name).ToListAsync();

        var emailSeed = DemoData.DistributionLists();
        await DataStore.SeedAsync("ReportEmailLists", emailSeed.Select(item => (item.Id, item)));
        EmailLists = await DataStore.LoadAsync<DemoDistributionList>("ReportEmailLists");
        var scheduleSeed = DemoData.ReportSchedules(PlantNames);
        await DataStore.SeedAsync("ReportSchedules", scheduleSeed.Select(item => (item.Id, item)));
        Schedules = await DataStore.LoadAsync<DemoReportSchedule>("ReportSchedules");

        var now = DateTime.Now;
        var deliverySeed = DemoData.ReportDeliveries(Schedules, EmailLists, now);
        await DataStore.SeedAsync("ReportDeliveries", deliverySeed.Select(item => (item.Id, item)));
        Deliveries = await DataStore.LoadAsync<DemoReportDelivery>("ReportDeliveries");
    }

    private void ShowSchedules() => ActiveTab = "Schedules";
    private void ShowEmailLists() => ActiveTab = "EmailLists";
    private void ShowDelivery() => ActiveTab = "Delivery";

    private void OpenAddSchedule()
    {
        EditingScheduleId = null;
        ScheduleName = string.Empty;
        ScheduleTemplate = Templates[0];
        ReportingPeriod = ReportingPeriods[0];
        Frequency = Frequencies[0];
        ExecutionTime = "07:00";
        SelectedDistributionListId = EmailLists.First(x => x.IsActive).Id;
        SelectedPlants = [];
        FormError = string.Empty;
        IsScheduleDialogOpen = true;
    }

    private void OpenEditSchedule(DemoReportSchedule schedule)
    {
        EditingScheduleId = schedule.Id;
        ScheduleName = schedule.Name;
        ScheduleTemplate = schedule.Template;
        ReportingPeriod = schedule.ReportingPeriod;
        Frequency = schedule.Frequency;
        ExecutionTime = schedule.ExecutionTime;
        SelectedDistributionListId = schedule.DistributionListId;
        SelectedPlants = schedule.Plants.ToHashSet(StringComparer.OrdinalIgnoreCase);
        FormError = string.Empty;
        IsScheduleDialogOpen = true;
    }

    private void CloseScheduleDialog() => IsScheduleDialogOpen = false;
    private void TogglePlantSelection(string plant) { if (!SelectedPlants.Add(plant)) SelectedPlants.Remove(plant); }

    private async Task SaveSchedule()
    {
        if (SelectedPlants.Count == 0) { FormError = "Select at least one LSS plant."; return; }
        var saved = new DemoReportSchedule(EditingScheduleId ?? $"RSC-{Schedules.Count + 1:000}", ScheduleName.Trim(), ScheduleTemplate, SelectedPlants.OrderBy(x => x).ToList(), ReportingPeriod, Frequency, ExecutionTime, SelectedDistributionListId, true);
        if (EditingScheduleId is null) Schedules.Insert(0, saved);
        else { var index = Schedules.FindIndex(x => x.Id == EditingScheduleId); saved = saved with { IsActive = Schedules[index].IsActive }; Schedules[index] = saved; }
        await DataStore.SaveAsync("ReportSchedules", saved.Id, saved);
        IsScheduleDialogOpen = false;
    }

    private async Task ToggleSchedule(DemoReportSchedule schedule) { var index = Schedules.IndexOf(schedule); var saved = schedule with { IsActive = !schedule.IsActive }; await DataStore.SaveAsync("ReportSchedules", saved.Id, saved); Schedules[index] = saved; }

    private void OpenAddEmailList() { EditingEmailListId = null; EmailListName = EmailAddresses = FormError = string.Empty; IsEmailDialogOpen = true; }
    private void OpenEditEmailList(DemoDistributionList list) { EditingEmailListId = list.Id; EmailListName = list.Name; EmailAddresses = string.Join("; ", list.Emails); FormError = string.Empty; IsEmailDialogOpen = true; }
    private void CloseEmailDialog() => IsEmailDialogOpen = false;

    private async Task SaveEmailList()
    {
        var emails = EmailAddresses.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (emails.Count == 0 || emails.Any(email => !email.Contains('@'))) { FormError = "Enter at least one valid email address."; return; }
        var saved = new DemoDistributionList(EditingEmailListId ?? $"DL-{EmailLists.Count + 1:000}", EmailListName.Trim(), emails, true);
        if (EditingEmailListId is null) EmailLists.Insert(0, saved);
        else { var index = EmailLists.FindIndex(x => x.Id == EditingEmailListId); saved = saved with { IsActive = EmailLists[index].IsActive }; EmailLists[index] = saved; }
        await DataStore.SaveAsync("ReportEmailLists", saved.Id, saved);
        IsEmailDialogOpen = false;
    }

    private async Task ToggleEmailList(DemoDistributionList list) { var index = EmailLists.IndexOf(list); var saved = list with { IsActive = !list.IsActive }; await DataStore.SaveAsync("ReportEmailLists", saved.Id, saved); EmailLists[index] = saved; }
    private void ViewDelivery(DemoReportDelivery delivery) => SelectedDelivery = delivery;
    private void CloseDelivery() => SelectedDelivery = null;
    private string GetEmailListName(string id) => EmailLists.FirstOrDefault(x => x.Id == id)?.Name ?? "Not assigned";
    private static string GetPlantSummary(List<string> plants) => plants.Count <= 2 ? string.Join(", ", plants) : $"{plants.Count} configured plants";
    private static string GetCssValue(string value) => value.ToLowerInvariant().Replace(" ", string.Empty);

}
