namespace TestingSystem.Web.Demo;

// View records used by Blazor pages. Persistent data is loaded from relational MSSQL tables.
public static class DemoData
{
    public static DemoAnalysisResult CreateAnalysis(string type, DateTime submissionDate, DateTime generatedAt, string status, int sequence,
        string dataSelection = "", int plantCount = 0, int recordCount = 0)
    {
        var reference = $"ODA-{type}-{generatedAt:yyyyMMdd}-{sequence:000}";
        var selection = string.IsNullOrWhiteSpace(dataSelection) ? (type == "DDQ" ? "Rolling 24 Hour" : "All MMF parameters") : dataSelection;
        var description = $"{type} {selection} data generated for all {plantCount} active LSS plant(s) from the {submissionDate:dd MMM yyyy} submission ({recordCount:N0} records).";
        if (status == "Failed") description += " Generation completed with validation errors and requires review.";
        return new(reference, $"{type} On-Demand Analysis {sequence:000}", type, submissionDate, generatedAt, description, status, selection, plantCount, recordCount);
    }

    public static DemoCollectionJob CreateCollectionJob(string plant, string state, string mode, string status, DateTime runAt, int sequence)
    {
        var id = $"DDQ-{runAt:yyyyMMdd}-{sequence:000}";
        var description = status == "Successful" ? "DDQ interval records collected and validated." : "Plant API did not return a complete interval dataset.";
        var response = status == "Successful" ? $"DDQ data from {plant} was received and validated successfully." : $"Collection from {plant} failed validation. Review the API configuration before retrying.";
        return new(id, plant, state, mode, runAt.Date, runAt, status, description, response, 1.4 + sequence % 6 * 0.7);
    }
}

public sealed record DemoCollectionJob(string CollectionId, string PlantName, string State, string Mode, DateTime SubmissionDate, DateTime RunAt,
    string Status, string Description, string ResponseMessage, double DurationSeconds, string ApiType = "DDQ", string DataType = "",
    string ScheduleName = "", int ReadingCount = 0, int AnomalyCount = 0, int? HttpStatusCode = null, string ContentType = "",
    long DatabaseId = 0, string ErrorMessage = "");
public sealed record DemoAnalysisResult(string Reference, string Name, string Type, DateTime SubmissionDate, DateTime GeneratedAt,
    string Description, string Status, string DataSelection = "", int PlantCount = 0, int RecordCount = 0);
public sealed record DemoReportRecord(string ReportId, string Name, string Source, string Type, DateTime SubmissionDate, DateTime GeneratedAt, string Status, string RequestedBy, int AnomalyCount, string Description);
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
