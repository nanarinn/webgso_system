using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using TestingSystem.Web.Backend.UseCases.UCLSS027Audit;
using TestingSystem.Web.Data;
using TestingSystem.Web.Database.Entities;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class AuditLogs : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
    [Inject] private IJSRuntime JavaScript { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] private IAuditTrailService AuditTrail { get; set; } = default!;
    private List<AuditEvent> Events { get; set; } = [];
    private string SearchTerm { get; set; } = string.Empty;
    private string ActorFilter { get; set; } = "All";
    private string ActionFilter { get; set; } = "All";
    private string ResultFilter { get; set; } = "All";
    private DateOnly? FromDate { get; set; }
    private DateOnly? ToDate { get; set; }
    private bool ExportFormOpen { get; set; }
    private bool IsExporting { get; set; }
    private string ExportActor { get; set; } = "All";
    private string ExportAction { get; set; } = string.Empty;
    private string ExportResult { get; set; } = "All";
    private DateOnly? ExportFromDate { get; set; }
    private DateOnly? ExportToDate { get; set; }
    private string? ExportError { get; set; }
    private IEnumerable<string> AvailableActors => Events.Select(item => item.Actor)
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item);
    private IEnumerable<string> AvailableActions => Events.Select(item => item.Action)
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item);
    private List<AuditEvent> FilteredEvents => Events.Where(item =>
            (string.IsNullOrWhiteSpace(SearchTerm) || item.Actor.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || item.Action.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || (item.EntityType?.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ?? false)
                || (item.EntityReference?.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ?? false)
                || item.Result.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || (item.Detail?.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ?? false))
            && (ActorFilter == "All" || item.Actor.Equals(ActorFilter, StringComparison.OrdinalIgnoreCase))
            && (ActionFilter == "All" || item.Action.Equals(ActionFilter, StringComparison.OrdinalIgnoreCase))
            && (ResultFilter == "All" || item.Result.Equals(ResultFilter, StringComparison.OrdinalIgnoreCase))
            && (!FromDate.HasValue || DateOnly.FromDateTime(item.OccurredAt.ToLocalTime().DateTime) >= FromDate.Value)
            && (!ToDate.HasValue || DateOnly.FromDateTime(item.OccurredAt.ToLocalTime().DateTime) <= ToDate.Value))
        .ToList();

    protected override Task OnInitializedAsync() => LoadEventsAsync();

    private async Task LoadEventsAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        Events = await database.AuditEvents.AsNoTracking().OrderByDescending(item => item.OccurredAt).Take(5000).ToListAsync();
    }

    private void ResetAuditPage() => AuditPage = 1;
    private void ClearFilters()
    {
        SearchTerm = string.Empty;
        ActorFilter = "All";
        ActionFilter = "All";
        ResultFilter = "All";
        FromDate = null;
        ToDate = null;
        AuditPage = 1;
    }

    private void OpenExportForm()
    {
        ExportActor = ActorFilter;
        ExportAction = ActionFilter == "All" ? string.Empty : ActionFilter;
        ExportResult = ResultFilter;
        ExportFromDate = FromDate;
        ExportToDate = ToDate;
        ExportError = null;
        ExportFormOpen = true;
    }

    private async Task ExportFilteredEventsAsync()
    {
        if (IsExporting) return;
        if (string.IsNullOrWhiteSpace(ExportAction))
        {
            ExportError = "Select the event or action to export.";
            return;
        }
        if (!ExportFromDate.HasValue || !ExportToDate.HasValue)
        {
            ExportError = "Select both the From date and To date.";
            return;
        }
        if (ExportToDate < ExportFromDate)
        {
            ExportError = "To date cannot be earlier than From date.";
            return;
        }

        IsExporting = true;
        ExportError = null;
        try
        {
            var from = ExportFromDate.Value.ToDateTime(TimeOnly.MinValue);
            var toExclusive = ExportToDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
            await using var database = await ContextFactory.CreateDbContextAsync();
            var query = database.AuditEvents.AsNoTracking().Where(item =>
                item.Action == ExportAction && item.OccurredAt >= from && item.OccurredAt < toExclusive);
            if (ExportActor != "All") query = query.Where(item => item.Actor == ExportActor);
            if (ExportResult != "All") query = query.Where(item => item.Result == ExportResult);
            var rows = await query.OrderByDescending(item => item.OccurredAt).ToListAsync();
            if (rows.Count == 0)
            {
                ExportError = "No audit events match the selected export filters.";
                return;
            }

            var csv = new StringBuilder();
            AddCsvRow(csv, "Timestamp", "Actor", "Event / Action", "Entity Type", "Reference", "Result", "Detail", "Correlation ID");
            foreach (var item in rows)
                AddCsvRow(csv, item.OccurredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    item.Actor, item.Action, item.EntityType, item.EntityReference, item.Result, item.Detail, item.CorrelationId);

            var fileName = $"Audit_Events_{SafeFilePart(ExportAction)}_{ExportFromDate:yyyyMMdd}-{ExportToDate:yyyyMMdd}.csv";
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
            await JavaScript.InvokeVoidAsync("sblssDownloadBase64", fileName,
                "text/csv;charset=utf-8", Convert.ToBase64String(bytes));
            var principal = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
            await AuditTrail.RecordAsync(principal, "Download audit events", "Success", "Download", fileName,
                $"Exported {rows.Count} '{ExportAction}' event(s) from {ExportFromDate:dd MMM yyyy} to {ExportToDate:dd MMM yyyy}; actor: {ExportActor}; result: {ExportResult}.");
            await LoadEventsAsync();
            ExportFormOpen = false;
        }
        finally
        {
            IsExporting = false;
        }
    }

    private static void AddCsvRow(StringBuilder output, params string?[] values) =>
        output.AppendLine(string.Join(',', values.Select(CsvValue)));

    private static string CsvValue(string? value)
    {
        var safe = value ?? string.Empty;
        if (safe.Length > 0 && safe[0] is '=' or '+' or '-' or '@') safe = "'" + safe;
        return $"\"{safe.Replace("\"", "\"\"")}\"";
    }

    private static string SafeFilePart(string value)
    {
        var safe = new string(value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "Events" : safe[..Math.Min(50, safe.Length)];
    }
}
