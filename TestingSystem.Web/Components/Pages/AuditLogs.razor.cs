using Microsoft.AspNetCore.Components;

namespace TestingSystem.Web.Components.Pages;

public partial class AuditLogs : ComponentBase
{
    private List<CollectionJob> Jobs { get; set; } = [];
    private CollectionJob? SelectedJob { get; set; }
    private string SearchTerm { get; set; } = string.Empty;
    private string SourceFilter { get; set; } = "All";
    private string ModeFilter { get; set; } = "All";
    private string StatusFilter { get; set; } = "All";

    private List<CollectionJob> FilteredJobs => Jobs.Where(job =>
            (string.IsNullOrWhiteSpace(SearchTerm)
                || job.JobId.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || job.PlantName.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || job.Source.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || job.Message.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
            && (SourceFilter == "All" || job.Source == SourceFilter)
            && (ModeFilter == "All" || job.Mode == ModeFilter)
            && (StatusFilter == "All" || job.Status == StatusFilter))
        .ToList();

    protected override void OnInitialized()
    {
        var now = DateTime.Now;
        Jobs =
        [
            new("JOB-DDQ-260906-041", "Bidor Solar Plant", "Perak", "DDQ", "Scheduled", now.AddMinutes(-7), 78, "Running", 1872, "00:07:14", "System Scheduler", "1 of 1", "Interval data is being validated. 528 records remain in the queue."),
            new("JOB-MMF-260906-040", "Kulim Solar Plant", "Kedah", "MMF", "Manual", now.AddMinutes(-24), 100, "Completed", 2880, "00:03:42", "LSS Staff", "1 of 1", "Forecast and irradiance parameters were collected successfully."),
            new("JOB-DDQ-260906-039", "Sepang Solar Plant", "Selangor", "DDQ", "Scheduled", now.AddMinutes(-43), 100, "Warning", 2784, "00:05:18", "System Scheduler", "1 of 1", "96 intervals were received late and marked for data quality review."),
            new("JOB-MMF-260906-038", "Pengerang Solar Plant", "Johor", "MMF", "Retry", now.AddHours(-1), 42, "Failed", 1210, "00:02:09", "SBLSS Administrator", "2 of 3", "Plant API stopped responding before the complete MMF dataset was received."),
            new("JOB-DDQ-260906-037", "Kulim Solar Plant", "Kedah", "DDQ", "Manual", now.AddHours(-2), 100, "Completed", 2880, "00:04:01", "LSS Reviewer", "1 of 1", "All DDQ intervals passed completeness and timestamp validation."),
            new("JOB-MMF-260906-036", "Bidor Solar Plant", "Perak", "MMF", "Scheduled", now.AddHours(-4), 100, "Completed", 576, "00:02:55", "System Scheduler", "1 of 1", "MMF GHI, POA and irradiance data collected successfully."),
            new("JOB-DDQ-260905-035", "Pengerang Solar Plant", "Johor", "DDQ", "Retry", now.AddDays(-1), 100, "Completed", 2880, "00:04:33", "SBLSS Administrator", "2 of 3", "Retry completed and replaced the incomplete scheduled collection."),
            new("JOB-MMF-260905-034", "Sepang Solar Plant", "Selangor", "MMF", "Scheduled", now.AddDays(-1).AddHours(-2), 100, "Warning", 564, "00:03:12", "System Scheduler", "1 of 1", "12 irradiance intervals exceeded the configured variance threshold.")
        ];
    }

    private void ShowAllJobs() => SetStatusFilter("All");
    private void ShowRunningJobs() => SetStatusFilter("Running");
    private void ShowWarningJobs() => SetStatusFilter("Warning");
    private void ShowFailedJobs() => SetStatusFilter("Failed");

    private void SetStatusFilter(string status)
    {
        StatusFilter = status;
        SourceFilter = "All";
        ModeFilter = "All";
        SearchTerm = string.Empty;
    }

    private void ViewJob(CollectionJob job) => SelectedJob = job;
    private void CloseJob() => SelectedJob = null;

    private static string GetPlantCode(string plantName) => string.Concat(
        plantName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => word[0]));

    private static string GetMessageLabel(string status) => status switch
    {
        "Failed" => "Failure",
        "Warning" => "Warning",
        "Running" => "Progress",
        _ => "Result"
    };

    private static string GetResultTitle(string status) => status switch
    {
        "Failed" => "Collection requires intervention",
        "Warning" => "Collection completed with warnings",
        "Running" => "Collection is still running",
        _ => "Collection completed successfully"
    };

    private static IReadOnlyList<ExecutionStep> GetExecutionTrail(CollectionJob job)
    {
        var trail = new List<ExecutionStep>
        {
            new("Job created", $"{job.Mode} {job.Source} collection accepted", job.StartedAt.ToString("HH:mm:ss"), false),
            new("API connection", $"Connected to {job.PlantName}", job.StartedAt.AddSeconds(12).ToString("HH:mm:ss"), false),
            new("Data received", $"{job.RecordsReceived:N0} records received", job.StartedAt.AddMinutes(1).ToString("HH:mm:ss"), false)
        };

        trail.Add(new(
            job.Status == "Running" ? "Validation in progress" : $"Job {job.Status.ToLowerInvariant()}",
            job.Message,
            job.Status == "Running" ? "Now" : job.StartedAt.AddMinutes(3).ToString("HH:mm:ss"),
            true));
        return trail;
    }

    private sealed record CollectionJob(string JobId, string PlantName, string State, string Source,
        string Mode, DateTime StartedAt, int Progress, string Status, int RecordsReceived,
        string Duration, string RequestedBy, string Attempt, string Message);

    private sealed record ExecutionStep(string Title, string Description, string Time, bool IsCurrent);
}
