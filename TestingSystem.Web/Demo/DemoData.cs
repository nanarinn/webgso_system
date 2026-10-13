namespace TestingSystem.Web.Demo;

// View records used by Blazor pages. Persistent data is loaded from relational MSSQL tables.
public static class DemoData
{
    public static DemoAnalysisResult CreateAnalysis(string type, DateTime submissionDate, DateTime generatedAt, string status, int sequence,
        string dataSelection = "", int plantCount = 0, int recordCount = 0, int anomalyCount = 0,
        int intervalMinutes = 15, DateTime? dataRangeStart = null, DateTime? dataRangeEnd = null)
    {
        var reference = $"ODA-{type}-{generatedAt:yyyyMMdd}-{sequence:000}";
        var selection = string.IsNullOrWhiteSpace(dataSelection) ? (type == "DDQ" ? "Rolling 24 Hour" : "All MMF parameters") : dataSelection;
        var interval = intervalMinutes == 60 ? "1 hour" : $"{intervalMinutes} minutes";
        var description = type == "MMF" && dataRangeStart.HasValue && dataRangeEnd.HasValue
            ? $"MMF {selection} data generated at {interval} interval for all {plantCount} active LSS plant(s) between {dataRangeStart:dd MMM yyyy HH:mm} and {dataRangeEnd:dd MMM yyyy HH:mm} ({recordCount:N0} records)."
            : $"{type} {selection} data generated at {interval} interval for all {plantCount} active LSS plant(s) from the {submissionDate:dd MMM yyyy HH:mm} scheduled submission ({recordCount:N0} records).";
        if (status == "Failed") description += " Generation completed with validation errors and requires review.";
        return new(reference, AnalysisDisplayName(type, selection, generatedAt, dataRangeStart, dataRangeEnd), type, submissionDate, generatedAt, description,
            status, selection, plantCount, recordCount, anomalyCount, intervalMinutes, dataRangeStart, dataRangeEnd);
    }

    public static string AnalysisDisplayName(string type, string dataSelection, DateTime generatedAt,
        DateTime? dataRangeStart = null, DateTime? dataRangeEnd = null)
    {
        if (type.Equals("MMF", StringComparison.OrdinalIgnoreCase)
            && dataRangeStart.HasValue && dataRangeEnd.HasValue)
        {
            var start = dataRangeStart.Value;
            var end = dataRangeEnd.Value;
            var range = start.Date == end.Date
                ? start.ToString("dd MMM yyyy")
                : start.Year == end.Year && start.Month == end.Month
                    ? $"{start:dd}–{end:dd MMM yyyy}"
                    : $"{start:dd MMM yyyy} – {end:dd MMM yyyy}";
            return $"MMF · Data {range}";
        }

        var detail = type.Equals("DDQ", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(dataSelection)
                ? $" {dataSelection}"
                : string.Empty;
        return $"{type}{detail} · {generatedAt:dd MMM yyyy HH:mm:ss}";
    }

    public static string SimplifyAnalysisName(string name) =>
        name.Replace(" On-Demand Analysis ", " Analysis ", StringComparison.OrdinalIgnoreCase);
}

public sealed record DemoCollectionJob(string CollectionId, string PlantName, string State, string Mode, DateTime SubmissionDate, DateTime RunAt,
    string Status, string Description, string ResponseMessage, double DurationSeconds, string ApiType = "DDQ", string DataType = "",
    string ScheduleName = "", int ReadingCount = 0, int AnomalyCount = 0, int? HttpStatusCode = null, string ContentType = "",
    long DatabaseId = 0, string ErrorMessage = "", string ScheduleCode = "");
public sealed record DemoAnalysisResult(string Reference, string Name, string Type, DateTime SubmissionDate, DateTime GeneratedAt,
    string Description, string Status, string DataSelection = "", int PlantCount = 0, int RecordCount = 0, int AnomalyCount = 0,
    int IntervalMinutes = 15, DateTime? DataRangeStart = null, DateTime? DataRangeEnd = null);
public sealed record DemoReportSchedule(
    string Id,
    string Name,
    string Template,
    List<string> Plants,
    string ReportingPeriod,
    string Frequency,
    string ExecutionTime,
    string DistributionListId,
    bool IsActive,
    string Source = "DDQ",
    List<string>? DataSelections = null,
    int IntervalMinutes = 15,
    List<string>? Recipients = null,
    string ScheduleType = "Daily",
    string ScheduleDay = "",
    List<int>? ScheduleMonths = null);
public sealed record DemoReportDelivery(string Id, string ScheduleName, string Source, DateTime GeneratedAt, int AnomalyCount, string MainReportStatus, string MainEmailStatus, string AnomalyReportStatus, string AnomalyEmailStatus, string OverallStatus, string DistributionListName, List<string> Recipients, string Message);
