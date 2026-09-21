using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Demo;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class ReportHistory : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
    private List<DemoReportRecord> ReportsList { get; set; } = [];
    private DemoReportRecord? SelectedReport { get; set; }
    private string SearchTerm { get; set; } = string.Empty;
    private string StatusFilter { get; set; } = "All";
    private string TypeFilter { get; set; } = "All";

    private List<DemoReportRecord> FilteredReports => ReportsList.Where(report =>
            (string.IsNullOrWhiteSpace(SearchTerm)
                || report.Name.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || report.ReportId.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || report.Source.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || report.RequestedBy.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
            && (StatusFilter == "All" || report.Status == StatusFilter)
            && (TypeFilter == "All" || report.Type == TypeFilter))
        .ToList();

    protected override async Task OnInitializedAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        ReportsList = await database.ReportExecutions.AsNoTracking().OrderByDescending(x => x.RequestedAt)
            .Select(x => new DemoReportRecord(x.ReportId, x.Name, x.DataType, x.ReportType,
                x.SubmissionDate, x.RequestedAt.LocalDateTime,
                x.GenerationStatus == "Completed" ? "Completed" : x.GenerationStatus,
                x.RequestedBy, x.AnomalyCount, x.Description)).ToListAsync();
    }

    private void ViewReport(DemoReportRecord report) => SelectedReport = report;
    private void CloseReport() => SelectedReport = null;
    private static string GetCssValue(string value) => value.ToLowerInvariant().Replace("-", string.Empty);

    private static IReadOnlyList<StatusHistoryItem> GetStatusHistory(DemoReportRecord report)
    {
        var submitted = report.GeneratedAt.AddMinutes(-8);
        var history = new List<StatusHistoryItem>
        {
            new("Request submitted", $"{report.Type} report request accepted", submitted.ToString("dd MMM, HH:mm"), false),
            new("Data prepared", $"{report.Source} source data loaded", submitted.AddMinutes(3).ToString("dd MMM, HH:mm"), false)
        };

        history.Add(report.Status switch
        {
            "Completed" => new("Report completed", "Main and anomaly files are ready", report.GeneratedAt.ToString("dd MMM, HH:mm"), true),
            "Failed" => new("Generation failed", "Incomplete source data requires review", report.GeneratedAt.ToString("dd MMM, HH:mm"), true),
            _ => new("Processing report", "Validation and file generation in progress", "In progress", true)
        });

        return history;
    }

    private static string GetMainReportDataUri(DemoReportRecord report)
    {
        var csv = "Report ID,Report Name,Source,Type,Generated At,Requested By,Status,Description\r\n"
            + CsvRow(report.ReportId, report.Name, report.Source, report.Type, report.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss"), report.RequestedBy, report.Status, report.Description);
        return $"data:text/csv;charset=utf-8,{Uri.EscapeDataString(csv)}";
    }

    private static string GetAnomalyReportDataUri(DemoReportRecord report)
    {
        var rows = Enumerable.Range(1, Math.Max(report.AnomalyCount, 1))
            .Select(index => CsvRow(report.ReportId, $"ANM-{index:000}", report.Source, $"Interval anomaly {index}", "Review required"));
        var csv = "Report ID,Anomaly ID,Source,Finding,Action\r\n" + string.Join("\r\n", rows);
        return $"data:text/csv;charset=utf-8,{Uri.EscapeDataString(csv)}";
    }

    private static string GetMainFileName(DemoReportRecord report) => $"{report.ReportId}-main-report.csv";
    private static string GetAnomalyFileName(DemoReportRecord report) => $"{report.ReportId}-anomaly-report.csv";
    private static string CsvRow(params string[] values) => string.Join(",", values.Select(value => $"\"{value.Replace("\"", "\"\"")}\""));

    private sealed record StatusHistoryItem(string Label, string Detail, string Time, bool IsCurrent);
}
