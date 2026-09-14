using Microsoft.AspNetCore.Components;
using TestingSystem.Web.Services;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class ReportHistory : ComponentBase
{
    [Inject] private IStoredDataService DataStore { get; set; } = default!;
    private const string DataCategory = "ReportHistory";
    private List<ReportRecord> ReportsList { get; set; } = [];
    private ReportRecord? SelectedReport { get; set; }
    private string SearchTerm { get; set; } = string.Empty;
    private string StatusFilter { get; set; } = "All";
    private string TypeFilter { get; set; } = "All";

    private List<ReportRecord> FilteredReports => ReportsList.Where(report =>
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
        var now = DateTime.Now;
        ReportRecord[] seed =
        {
            new("RPT-20260906-018", "Daily DDQ Performance Summary", "DDQ", "Scheduled", now.Date.AddDays(-1), now.AddMinutes(-28), "Completed", "System Scheduler", 3, "Daily plant submission, interval completeness and validation performance."),
            new("RPT-20260906-017", "MMF Forecast Variance Analysis", "MMF", "On-Demand", now.Date.AddDays(-2), now.AddHours(-2), "Completed", "LSS Reviewer", 7, "Forecast versus actual generation with plant-level variance findings."),
            new("RPT-20260906-016", "Portfolio Data Quality Report", "DDQ", "Scheduled", now.Date.AddDays(-1), now.AddHours(-4), "Processing", "System Scheduler", 0, "Portfolio-wide DDQ quality checks are currently being processed."),
            new("RPT-20260905-015", "Bidor Collection Exception Review", "DDQ", "On-Demand", now.Date.AddDays(-3), now.AddDays(-1), "Failed", "SBLSS Administrator", 0, "Report generation stopped because the selected collection dataset was incomplete."),
            new("RPT-20260905-014", "Weekly LSS Operations Report", "BOTH", "Scheduled", now.Date.AddDays(-7), now.AddDays(-1).AddHours(-3), "Completed", "System Scheduler", 11, "Consolidated DDQ and MMF operational performance for all active plants."),
            new("RPT-20260904-013", "Kulim Irradiance Anomaly Review", "MMF", "On-Demand", now.Date.AddDays(-4), now.AddDays(-2), "Completed", "LSS Staff", 5, "Irradiance, GHI and POA anomaly review for Kulim Solar Plant.")
        };
        await DataStore.SeedAsync(DataCategory, seed.Select(item => (item.ReportId, item)));
        ReportsList = await DataStore.LoadAsync<ReportRecord>(DataCategory);
    }

    private void ViewReport(ReportRecord report) => SelectedReport = report;
    private void CloseReport() => SelectedReport = null;
    private static string GetCssValue(string value) => value.ToLowerInvariant().Replace("-", string.Empty);

    private static IReadOnlyList<StatusHistoryItem> GetStatusHistory(ReportRecord report)
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

    private static string GetMainReportDataUri(ReportRecord report)
    {
        var csv = "Report ID,Report Name,Source,Type,Generated At,Requested By,Status,Description\r\n"
            + CsvRow(report.ReportId, report.Name, report.Source, report.Type, report.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss"), report.RequestedBy, report.Status, report.Description);
        return $"data:text/csv;charset=utf-8,{Uri.EscapeDataString(csv)}";
    }

    private static string GetAnomalyReportDataUri(ReportRecord report)
    {
        var rows = Enumerable.Range(1, Math.Max(report.AnomalyCount, 1))
            .Select(index => CsvRow(report.ReportId, $"ANM-{index:000}", report.Source, $"Interval anomaly {index}", "Review required"));
        var csv = "Report ID,Anomaly ID,Source,Finding,Action\r\n" + string.Join("\r\n", rows);
        return $"data:text/csv;charset=utf-8,{Uri.EscapeDataString(csv)}";
    }

    private static string GetMainFileName(ReportRecord report) => $"{report.ReportId}-main-report.csv";
    private static string GetAnomalyFileName(ReportRecord report) => $"{report.ReportId}-anomaly-report.csv";
    private static string CsvRow(params string[] values) => string.Join(",", values.Select(value => $"\"{value.Replace("\"", "\"\"")}\""));

    private sealed record ReportRecord(string ReportId, string Name, string Source, string Type,
        DateTime SubmissionDate, DateTime GeneratedAt, string Status, string RequestedBy,
        int AnomalyCount, string Description);

    private sealed record StatusHistoryItem(string Label, string Detail, string Time, bool IsCurrent);
}
