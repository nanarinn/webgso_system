using System.Net.Mail;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Demo;
using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Services;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class ScheduledReports : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
    [Inject] private IReportEmailSender ReportEmailSender { get; set; } = default!;

    private static readonly string[] ApiTypes = ["DDQ", "MMF"];
    private static readonly string[] DdqDataTypes = ["Rolling 24 Hour", "Day Ahead", "Week Ahead", "4 Month Ahead"];
    private static readonly (string Label, int Value)[] Intervals = [("15 minutes", 15), ("30 minutes", 30), ("1 hour", 60)];
    private static readonly string[] ReportScheduleTypes = ["Rolling 24 Hour", "Daily", "Weekly", "4 Month"];
    private static readonly string[] WeekDays = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
    private static readonly (int Value, string Label)[] Months = [(1,"Jan"),(2,"Feb"),(3,"Mar"),(4,"Apr"),(5,"May"),(6,"Jun"),(7,"Jul"),(8,"Aug"),(9,"Sep"),(10,"Oct"),(11,"Nov"),(12,"Dec")];

    private List<string> PlantNames { get; set; } = [];
    private List<RecipientContact> RecipientContacts { get; set; } = [];
    private List<DemoReportSchedule> Schedules { get; set; } = [];
    private List<DemoReportDelivery> Deliveries { get; set; } = [];
    private string ActiveTab { get; set; } = "Schedules";
    private string SearchTerm { get; set; } = string.Empty;
    private string DeliverySearchTerm { get; set; } = string.Empty;
    private string SelectedReportApi { get; set; } = "DDQ";
    private bool IsScheduleDialogOpen { get; set; }
    private bool IsDeleteConfirmationOpen { get; set; }
    private bool IsStatusConfirmationOpen { get; set; }
    private bool StatusChangeConfirmed { get; set; }
    private bool OriginalIsActive { get; set; } = true;
    private string? EditingScheduleId { get; set; }
    private DemoReportDelivery? SelectedDelivery { get; set; }
    private string FormError { get; set; } = string.Empty;
    private string SuccessMessage { get; set; } = string.Empty;

    private string ScheduleName { get; set; } = string.Empty;
    private string SelectedApiType { get; set; } = "DDQ";
    private string SelectedDataType { get; set; } = DdqDataTypes[0];
    private string SelectedScheduleType { get; set; } = ReportScheduleTypes[1];
    private string SelectedScheduleDay { get; set; } = string.Empty;
    private HashSet<int> SelectedScheduleMonths { get; set; } = [];
    private string ExecutionTime { get; set; } = "07:00";
    private int SelectedInterval { get; set; } = 15;
    private bool SelectedIsActive { get; set; } = true;
    private List<string> SelectedRecipientEmails { get; set; } = [];
    private string RecipientSearchTerm { get; set; } = string.Empty;
    private bool IsRecipientDropdownOpen { get; set; }

    private List<DemoReportSchedule> FilteredSchedules => Schedules
        .Where(x => EffectiveSource(x) == SelectedReportApi)
        .Where(x => string.IsNullOrWhiteSpace(SearchTerm)
            || x.Name.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
            || x.Plants.Any(plant => plant.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
            || EffectiveDataFields(x).Any(field => field.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
            || EffectiveRecipients(x).Any(email => email.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)))
        .ToList();
    private List<DemoReportDelivery> FilteredDeliveries => Deliveries
        .OrderByDescending(x => x.GeneratedAt)
        .Where(x => string.IsNullOrWhiteSpace(DeliverySearchTerm)
            || x.ScheduleName.Contains(DeliverySearchTerm, StringComparison.OrdinalIgnoreCase)
            || x.Id.Contains(DeliverySearchTerm, StringComparison.OrdinalIgnoreCase)
            || DeliverySource(x).Contains(DeliverySearchTerm, StringComparison.OrdinalIgnoreCase)
            || DeliveryDataSummary(x).Contains(DeliverySearchTerm, StringComparison.OrdinalIgnoreCase)
            || x.Recipients.Any(email => email.Contains(DeliverySearchTerm, StringComparison.OrdinalIgnoreCase)))
        .ToList();
    private IEnumerable<RecipientContact> RecipientSuggestions => RecipientContacts
        .Where(x => !SelectedRecipientEmails.Contains(x.Email, StringComparer.OrdinalIgnoreCase))
        .Where(x => string.IsNullOrWhiteSpace(RecipientSearchTerm)
            || x.Name.Contains(RecipientSearchTerm, StringComparison.OrdinalIgnoreCase)
            || x.Email.Contains(RecipientSearchTerm, StringComparison.OrdinalIgnoreCase))
        .Take(8);

    protected override async Task OnInitializedAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        PlantNames = await database.LssPlants.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.Name).Select(x => x.Name).ToListAsync();
        RecipientContacts = await database.Users.AsNoTracking().Where(x => x.Email != null && x.Email != "")
            .OrderBy(x => x.DisplayName).Select(x => new RecipientContact(x.DisplayName, x.Email!)).ToListAsync();
        var scheduleRows = await database.ReportSchedules.AsNoTracking().Include(x => x.Recipients).OrderByDescending(x => x.CreatedAt).ToListAsync();
        Schedules = scheduleRows.Select(MapSchedule).ToList();
        var deliveryRows = await database.ReportDeliveryAttempts.AsNoTracking().Include(x => x.Recipients).OrderByDescending(x => x.GeneratedAt).ToListAsync();
        Deliveries = deliveryRows.Select(MapDelivery).ToList();
    }

    private void ShowSchedules() => ActiveTab = "Schedules";
    private async Task ShowDelivery()
    {
        ActiveTab = "Delivery";
        await RefreshDeliveriesAsync();
    }

    private async Task RefreshDeliveriesAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        var deliveryRows = await database.ReportDeliveryAttempts.AsNoTracking().Include(x => x.Recipients)
            .OrderByDescending(x => x.GeneratedAt).ToListAsync();
        Deliveries = deliveryRows.Select(MapDelivery).ToList();
    }

    private void OpenAddSchedule()
    {
        EditingScheduleId = null;
        ScheduleName = string.Empty;
        SelectedApiType = "DDQ";
        SelectedDataType = DdqDataTypes[0];
        SelectedScheduleType = ReportScheduleTypes[1];
        SelectedScheduleDay = string.Empty;
        SelectedScheduleMonths = [];
        ExecutionTime = "07:00";
        SelectedInterval = 15;
        SelectedIsActive = true;
        OriginalIsActive = true;
        IsDeleteConfirmationOpen = false;
        IsStatusConfirmationOpen = false;
        StatusChangeConfirmed = false;
        SelectedRecipientEmails = [];
        RecipientSearchTerm = string.Empty;
        IsRecipientDropdownOpen = false;
        FormError = string.Empty;
        IsScheduleDialogOpen = true;
    }

    private void OpenEditSchedule(DemoReportSchedule schedule)
    {
        EditingScheduleId = schedule.Id;
        ScheduleName = schedule.Name;
        SelectedApiType = EffectiveSource(schedule);
        SelectedDataType = SelectedApiType == "MMF" ? "All MMF parameters" : EffectiveDataFields(schedule).FirstOrDefault() ?? DdqDataTypes[0];
        SelectedScheduleType = EffectiveScheduleType(schedule);
        SelectedScheduleDay = schedule.ScheduleDay ?? string.Empty;
        SelectedScheduleMonths = schedule.ScheduleMonths?.ToHashSet() ?? [];
        ExecutionTime = schedule.ExecutionTime;
        SelectedInterval = schedule.IntervalMinutes <= 0 ? 15 : schedule.IntervalMinutes;
        SelectedIsActive = schedule.IsActive;
        OriginalIsActive = schedule.IsActive;
        IsDeleteConfirmationOpen = false;
        IsStatusConfirmationOpen = false;
        StatusChangeConfirmed = false;
        SelectedRecipientEmails = EffectiveRecipients(schedule).ToList();
        RecipientSearchTerm = string.Empty;
        IsRecipientDropdownOpen = false;
        FormError = string.Empty;
        IsScheduleDialogOpen = true;
    }

    private void CloseScheduleDialog() => IsScheduleDialogOpen = false;
    private void OnApiTypeChanged(ChangeEventArgs args)
    {
        SelectedApiType = args.Value?.ToString() == "MMF" ? "MMF" : "DDQ";
        SelectedDataType = SelectedApiType == "MMF" ? "All MMF parameters" : DdqDataTypes[0];
        if (SelectedApiType == "MMF") SelectedInterval = 15;
    }

    private void OnDataTypeChanged(ChangeEventArgs args)
    {
        SelectedDataType = args.Value?.ToString() ?? DdqDataTypes[0];
        SelectedInterval = 15;
    }

    private void OnScheduleTypeChanged(ChangeEventArgs args)
    {
        SelectedScheduleType = args.Value?.ToString() ?? "Daily";
        SelectedScheduleDay = SelectedScheduleType switch { "Weekly" => "Monday", "4 Month" => "1", _ => string.Empty };
        SelectedScheduleMonths = [];
    }

    private void ToggleScheduleMonth(int month) { if (!SelectedScheduleMonths.Add(month)) SelectedScheduleMonths.Remove(month); }

    private void OnExecutionTimeChanged(ChangeEventArgs args)
    {
        var value = args.Value?.ToString();
        ExecutionTime = ReportScheduleTimePolicy.TryNormalize(value, out var normalized)
            ? normalized
            : value ?? "07:00";
    }

    private void SetScheduleStatus(bool isActive)
    {
        SelectedIsActive = isActive;
        IsStatusConfirmationOpen = false;
        StatusChangeConfirmed = false;
    }

    private async Task ConfirmStatusChangeAsync()
    {
        StatusChangeConfirmed = true;
        IsStatusConfirmationOpen = false;
        await SaveSchedule();
    }

    private async Task DeleteScheduleAsync()
    {
        if (EditingScheduleId is null) return;
        var schedule = Schedules.FirstOrDefault(x => x.Id == EditingScheduleId);
        if (schedule is null) return;
        await using var database = await ContextFactory.CreateDbContextAsync();
        var entity = await database.ReportSchedules.SingleOrDefaultAsync(x => x.ScheduleCode == schedule.Id);
        if (entity is not null) { database.ReportSchedules.Remove(entity); await database.SaveChangesAsync(); }
        Schedules.Remove(schedule);
        IsDeleteConfirmationOpen = false;
        IsScheduleDialogOpen = false;
        SuccessMessage = $"{schedule.Name} was deleted from MS SQL.";
    }

    private void AddRecipient(RecipientContact contact) => AddRecipientEmail(contact.Email);
    private void AddRecipientEmail(string email)
    {
        var value = email.Trim().TrimEnd(',', ';');
        if (!IsValidEmail(value)) { FormError = "Enter a valid email address or choose a suggested contact."; return; }
        if (!SelectedRecipientEmails.Contains(value, StringComparer.OrdinalIgnoreCase)) SelectedRecipientEmails.Add(value);
        RecipientSearchTerm = string.Empty;
        IsRecipientDropdownOpen = false;
        FormError = string.Empty;
    }
    private void RemoveRecipient(string email) => SelectedRecipientEmails.RemoveAll(x => x.Equals(email, StringComparison.OrdinalIgnoreCase));
    private void AddTypedRecipient() { if (!string.IsNullOrWhiteSpace(RecipientSearchTerm)) AddRecipientEmail(RecipientSearchTerm); }
    private void OnRecipientKeyDown(KeyboardEventArgs args)
    {
        if (args.Key is "Enter" or "," or ";") AddTypedRecipient();
        else if (args.Key == "Backspace" && string.IsNullOrEmpty(RecipientSearchTerm) && SelectedRecipientEmails.Count > 0)
            SelectedRecipientEmails.RemoveAt(SelectedRecipientEmails.Count - 1);
    }

    private async Task SaveSchedule()
    {
        FormError = string.Empty;
        if (EditingScheduleId is not null && SelectedIsActive != OriginalIsActive && !StatusChangeConfirmed)
        {
            IsStatusConfirmationOpen = true;
            return;
        }
        if (SelectedScheduleType == "4 Month" && SelectedScheduleMonths.Count == 0) { FormError = "Select at least one month for the 4 Month schedule."; return; }
        if (!ReportScheduleTimePolicy.TryNormalize(ExecutionTime, out var normalizedExecutionTime))
        {
            FormError = "Enter a valid schedule time.";
            return;
        }
        ExecutionTime = normalizedExecutionTime;
        if (!string.IsNullOrWhiteSpace(RecipientSearchTerm) && IsValidEmail(RecipientSearchTerm.Trim())) AddRecipientEmail(RecipientSearchTerm);
        var recipients = SelectedRecipientEmails.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (recipients.Count == 0 || recipients.Any(email => !IsValidEmail(email))) { FormError = "Enter at least one valid recipient email address."; return; }

        var template = SelectedApiType == "DDQ" ? "DDQ Analytics Report" : "MMF Analytics Report";
        var saved = new DemoReportSchedule(
            EditingScheduleId ?? $"RSC-{DateTime.Now:yyyyMMddHHmmss}", ScheduleName.Trim(), template,
            PlantNames.ToList(), SelectedScheduleType, SelectedScheduleType, ExecutionTime,
            string.Empty, SelectedIsActive, SelectedApiType,
            [SelectedDataType], SelectedInterval, recipients, SelectedScheduleType, SelectedScheduleDay,
            SelectedScheduleMonths.OrderBy(x => x).ToList());
        await using var database = await ContextFactory.CreateDbContextAsync();
        var entity = EditingScheduleId is null ? null : await database.ReportSchedules.Include(x => x.Recipients)
            .SingleOrDefaultAsync(x => x.ScheduleCode == EditingScheduleId);
        if (entity is null)
        {
            entity = new ReportScheduleEntity { ScheduleCode = saved.Id, CreatedAt = DateTimeOffset.UtcNow };
            database.ReportSchedules.Add(entity);
        }
        entity.Name = saved.Name; entity.ApiType = SelectedApiType; entity.DataType = SelectedApiType == "MMF" ? "All MMF parameters" : SelectedDataType;
        entity.IntervalMinutes = SelectedInterval; entity.ScheduleType = SelectedScheduleType; entity.ScheduleDay = SelectedScheduleDay;
        entity.ScheduleMonths = string.Join(',', SelectedScheduleMonths.OrderBy(x => x)); entity.ExecutionTime = ExecutionTime; entity.IsActive = SelectedIsActive;
        entity.Recipients.Clear();
        foreach (var email in recipients) entity.Recipients.Add(new ReportScheduleRecipient { Email = email });
        try
        {
            await database.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            FormError = "Schedule could not be saved. Check the form and try again.";
            return;
        }
        if (EditingScheduleId is null) Schedules.Insert(0, saved);
        else
        {
            var index = Schedules.FindIndex(x => x.Id == EditingScheduleId);
            if (index >= 0) Schedules[index] = saved;
        }
        IsScheduleDialogOpen = false;
        SuccessMessage = $"{saved.Name} was saved and will email {RecipientCount(saved)} recipient(s) according to its schedule.";
    }

    private static bool IsValidEmail(string value) { try { return new MailAddress(value).Address == value; } catch { return false; } }
    private void ViewDelivery(DemoReportDelivery delivery) => SelectedDelivery = delivery;
    private void CloseDelivery() => SelectedDelivery = null;
    private static List<string> EffectiveRecipients(DemoReportSchedule schedule) => schedule.Recipients ?? [];
    private int RecipientCount(DemoReportSchedule schedule) => EffectiveRecipients(schedule).Count;
    private string RecipientSummary(DemoReportSchedule schedule)
    {
        var recipients = EffectiveRecipients(schedule);
        return recipients.Count == 0 ? "No recipients" : recipients.Count == 1 ? recipients[0] : $"{recipients[0]} +{recipients.Count - 1}";
    }
    private static string EffectiveSource(DemoReportSchedule schedule) => !string.IsNullOrWhiteSpace(schedule.Source) ? schedule.Source : schedule.Template.Contains("MMF", StringComparison.OrdinalIgnoreCase) ? "MMF" : "DDQ";
    private static List<string> EffectiveDataFields(DemoReportSchedule schedule) => EffectiveSource(schedule) == "MMF"
        ? ["All MMF parameters"]
        : schedule.DataSelections is { Count: > 0 } ? schedule.DataSelections : ["Rolling 24 Hour"];
    private static int EffectiveInterval(DemoReportSchedule schedule) => schedule.IntervalMinutes > 0 ? schedule.IntervalMinutes : 15;
    private static string EffectiveScheduleType(DemoReportSchedule schedule)
    {
        if (schedule.ScheduleType == "Daily" && schedule.Frequency != "Daily" && string.IsNullOrWhiteSpace(schedule.ScheduleDay))
            return schedule.Frequency == "Monthly" ? "4 Month" : schedule.Frequency;
        return !string.IsNullOrWhiteSpace(schedule.ScheduleType) ? schedule.ScheduleType : schedule.Frequency;
    }
    private static string ScheduleDescription(DemoReportSchedule schedule) => EffectiveScheduleType(schedule) switch
    {
        "Rolling 24 Hour" => $"Every {EffectiveInterval(schedule)} minutes",
        "Weekly" => $"{schedule.ScheduleDay ?? "Monday"} at {schedule.ExecutionTime}",
        "4 Month" => $"Day {schedule.ScheduleDay ?? "1"} · {MonthLabels(schedule.ScheduleMonths)} · {schedule.ExecutionTime}",
        _ => $"Daily at {schedule.ExecutionTime}"
    };
    private static string MonthLabels(IEnumerable<int>? months) { var values = (months ?? []).Where(x => x is >= 1 and <= 12).OrderBy(x => x).Select(x => Months.First(m => m.Value == x).Label).ToList(); return values.Count == 0 ? "No months" : string.Join(", ", values); }
    private static string GetDataSummary(DemoReportSchedule schedule) { var values = EffectiveDataFields(schedule); return values.Count <= 2 ? string.Join(", ", values) : $"{values.Count} selections"; }
    private string GetPlantSummary(List<string> _) => "All active plants";
    private static string GetCssValue(string value) => value.ToLowerInvariant().Replace(" ", string.Empty);
    private DemoReportSchedule? DeliverySchedule(DemoReportDelivery delivery) =>
        Schedules.FirstOrDefault(x => x.Name.Equals(delivery.ScheduleName, StringComparison.OrdinalIgnoreCase));
    private string DeliverySource(DemoReportDelivery delivery)
    {
        var schedule = DeliverySchedule(delivery);
        return schedule is null ? delivery.Source : EffectiveSource(schedule);
    }
    private string DeliveryDataSummary(DemoReportDelivery delivery)
    {
        var schedule = DeliverySchedule(delivery);
        return schedule is null
            ? (delivery.Source.Equals("MMF", StringComparison.OrdinalIgnoreCase) ? "All MMF parameters" : "Rolling 24 Hour")
            : GetDataSummary(schedule);
    }
    private string DeliveryIntervalSummary(DemoReportDelivery delivery)
    {
        var schedule = DeliverySchedule(delivery);
        return $"{(schedule is null ? 15 : EffectiveInterval(schedule))} minute data interval";
    }
    private string DeliveryScheduleType(DemoReportDelivery delivery)
    {
        var schedule = DeliverySchedule(delivery);
        return schedule is null ? "Scheduled" : EffectiveScheduleType(schedule);
    }
    private string DeliveryScheduleDescription(DemoReportDelivery delivery)
    {
        var schedule = DeliverySchedule(delivery);
        return schedule is null ? "Stored delivery attempt" : ScheduleDescription(schedule);
    }
    private static string DeliveryRecipientSummary(DemoReportDelivery delivery) => delivery.Recipients.Count switch
    {
        0 => "No recipients",
        1 => delivery.Recipients[0],
        _ => $"{delivery.Recipients[0]} +{delivery.Recipients.Count - 1}"
    };
    private DemoReportSchedule MapSchedule(ReportScheduleEntity row) => new(row.ScheduleCode, row.Name,
        $"{row.ApiType} Analytics Report", PlantNames.ToList(), row.ScheduleType, row.ScheduleType, row.ExecutionTime,
        string.Empty, row.IsActive, row.ApiType, [row.DataType], row.IntervalMinutes,
        row.Recipients.Select(x => x.Email).ToList(), row.ScheduleType, row.ScheduleDay,
        row.ScheduleMonths.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out var month) ? month : 0).Where(x => x > 0).ToList());
    private static DemoReportDelivery MapDelivery(ReportDeliveryAttempt row) => new(row.DeliveryCode, row.ScheduleName,
        row.ApiType, row.GeneratedAt.LocalDateTime, 0, row.ReportStatus, row.EmailStatus, "Not required", "Not required",
        row.OverallStatus, string.Empty, row.Recipients.Select(x => x.Email).ToList(), row.Message);
    private sealed record RecipientContact(string Name, string Email);
}
