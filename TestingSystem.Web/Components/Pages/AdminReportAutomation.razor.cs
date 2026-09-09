using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;

namespace TestingSystem.Web.Components.Pages;

public partial class AdminReportAutomation : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;

    private static readonly string[] Templates = ["Daily DDQ Performance", "MMF Forecast Variance", "Weekly LSS Operations", "Monthly Portfolio Compliance"];
    private static readonly string[] ReportingPeriods = ["Previous day", "Previous week", "Previous month", "Rolling 24 hours"];
    private static readonly string[] Frequencies = ["Daily", "Weekly", "Monthly"];
    private List<string> PlantNames { get; set; } = [];
    private List<DistributionList> EmailLists { get; set; } = [];
    private List<ReportSchedule> Schedules { get; set; } = [];
    private List<ReportDelivery> Deliveries { get; set; } = [];
    private string ActiveTab { get; set; } = "Schedules";
    private bool IsScheduleDialogOpen { get; set; }
    private bool IsEmailDialogOpen { get; set; }
    private string? EditingScheduleId { get; set; }
    private string? EditingEmailListId { get; set; }
    private ReportDelivery? SelectedDelivery { get; set; }
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

        EmailLists =
        [
            new("DL-OPS-001", "LSS Operations Team", ["operations@sblss.local", "shiftlead@sblss.local"], true),
            new("DL-DQ-002", "Data Quality Reviewers", ["reviewer@sblss.local", "dataquality@sblss.local"], true),
            new("DL-MGT-003", "Management Reporting", ["management@sblss.local"], true)
        ];
        Schedules =
        [
            new("RSC-001", "Daily DDQ Delivery", Templates[0], PlantNames.Take(4).ToList(), ReportingPeriods[0], "Daily", "07:15", "DL-DQ-002", true),
            new("RSC-002", "Weekly Operations Pack", Templates[2], PlantNames.Take(4).ToList(), ReportingPeriods[1], "Weekly", "08:00", "DL-OPS-001", true),
            new("RSC-003", "Monthly Management Report", Templates[3], PlantNames.Take(4).ToList(), ReportingPeriods[2], "Monthly", "09:00", "DL-MGT-003", false)
        ];

        var now = DateTime.Now;
        Deliveries =
        [
            new("RDL-20260906-031", Schedules[0].Name, "DDQ", now.AddMinutes(-35), 4, "Generated", "Sent", "Generated", "Sent", "Delivered", EmailLists[1].Name, EmailLists[1].Emails, "Main report and separate anomaly report were sent to all assigned recipients."),
            new("RDL-20260906-030", Schedules[1].Name, "BOTH", now.AddHours(-2), 0, "Generated", "Sent", "Not required", "Not required", "Delivered", EmailLists[0].Name, EmailLists[0].Emails, "Main report was delivered. No anomalies were detected for this reporting period."),
            new("RDL-20260905-029", Schedules[0].Name, "DDQ", now.AddDays(-1), 7, "Generated", "Sent", "Generated", "Failed", "Warning", EmailLists[1].Name, EmailLists[1].Emails, "The main report was sent, but anomaly-report email delivery failed and requires retry."),
            new("RDL-20260904-028", Schedules[1].Name, "BOTH", now.AddDays(-2), 2, "Failed", "Not sent", "Not generated", "Not sent", "Failed", EmailLists[0].Name, EmailLists[0].Emails, "Report generation failed before email delivery because source data was incomplete.")
        ];
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

    private void OpenEditSchedule(ReportSchedule schedule)
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

    private void SaveSchedule()
    {
        if (SelectedPlants.Count == 0) { FormError = "Select at least one LSS plant."; return; }
        var saved = new ReportSchedule(EditingScheduleId ?? $"RSC-{Schedules.Count + 1:000}", ScheduleName.Trim(), ScheduleTemplate, SelectedPlants.OrderBy(x => x).ToList(), ReportingPeriod, Frequency, ExecutionTime, SelectedDistributionListId, true);
        if (EditingScheduleId is null) Schedules.Insert(0, saved);
        else { var index = Schedules.FindIndex(x => x.Id == EditingScheduleId); saved = saved with { IsActive = Schedules[index].IsActive }; Schedules[index] = saved; }
        IsScheduleDialogOpen = false;
    }

    private void ToggleSchedule(ReportSchedule schedule) { var index = Schedules.IndexOf(schedule); Schedules[index] = schedule with { IsActive = !schedule.IsActive }; }

    private void OpenAddEmailList() { EditingEmailListId = null; EmailListName = EmailAddresses = FormError = string.Empty; IsEmailDialogOpen = true; }
    private void OpenEditEmailList(DistributionList list) { EditingEmailListId = list.Id; EmailListName = list.Name; EmailAddresses = string.Join("; ", list.Emails); FormError = string.Empty; IsEmailDialogOpen = true; }
    private void CloseEmailDialog() => IsEmailDialogOpen = false;

    private void SaveEmailList()
    {
        var emails = EmailAddresses.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (emails.Count == 0 || emails.Any(email => !email.Contains('@'))) { FormError = "Enter at least one valid email address."; return; }
        var saved = new DistributionList(EditingEmailListId ?? $"DL-{EmailLists.Count + 1:000}", EmailListName.Trim(), emails, true);
        if (EditingEmailListId is null) EmailLists.Insert(0, saved);
        else { var index = EmailLists.FindIndex(x => x.Id == EditingEmailListId); saved = saved with { IsActive = EmailLists[index].IsActive }; EmailLists[index] = saved; }
        IsEmailDialogOpen = false;
    }

    private void ToggleEmailList(DistributionList list) { var index = EmailLists.IndexOf(list); EmailLists[index] = list with { IsActive = !list.IsActive }; }
    private void ViewDelivery(ReportDelivery delivery) => SelectedDelivery = delivery;
    private void CloseDelivery() => SelectedDelivery = null;
    private string GetEmailListName(string id) => EmailLists.FirstOrDefault(x => x.Id == id)?.Name ?? "Not assigned";
    private static string GetPlantSummary(List<string> plants) => plants.Count <= 2 ? string.Join(", ", plants) : $"{plants.Count} configured plants";
    private static string GetCssValue(string value) => value.ToLowerInvariant().Replace(" ", string.Empty);

    private sealed record DistributionList(string Id, string Name, List<string> Emails, bool IsActive);
    private sealed record ReportSchedule(string Id, string Name, string Template, List<string> Plants, string ReportingPeriod, string Frequency, string ExecutionTime, string DistributionListId, bool IsActive);
    private sealed record ReportDelivery(string Id, string ScheduleName, string Source, DateTime GeneratedAt, int AnomalyCount, string MainReportStatus, string MainEmailStatus, string AnomalyReportStatus, string AnomalyEmailStatus, string OverallStatus, string DistributionListName, List<string> Recipients, string Message);
}
