using TestingSystem.Web.Models;

namespace TestingSystem.Web.Demo;

// All prototype records and generated sample values live in this file.
// Replace these sources with approved plant APIs before production use.
public static class DemoData
{
    public static IReadOnlyList<SolarDocument> SolarDocuments(DateTimeOffset now) =>
    [
        new() { ReferenceNumber = "LSS-PLN-001", Title = "LSS Project Development Plan", Category = "Planning", ProjectStage = "Active", UpdatedAt = now.AddDays(-2) },
        new() { ReferenceNumber = "LSS-PPA-002", Title = "Power Purchase Agreement Register", Category = "Commercial", ProjectStage = "Review", UpdatedAt = now.AddDays(-4) },
        new() { ReferenceNumber = "LSS-GRD-003", Title = "Grid Interconnection Assessment", Category = "Technical", ProjectStage = "Approved", UpdatedAt = now.AddDays(-7) },
        new() { ReferenceNumber = "LSS-COM-004", Title = "Commercial Operation Milestone Tracker", Category = "Operations", ProjectStage = "Active", UpdatedAt = now.AddDays(-10) }
    ];

    public static IReadOnlyList<LssPlant> LssPlants() =>
    [
        CreatePlant("PLT-BDR-01", "Bidor Solar Plant", "Perak", 50),
        CreatePlant("PLT-KLM-02", "Kulim Solar Plant", "Kedah", 100),
        CreatePlant("PLT-SPG-03", "Sepang Solar Plant", "Selangor", 75),
        CreatePlant("PLT-PGR-04", "Pengerang Solar Plant", "Johor", 90)
    ];

    public static IReadOnlyList<DemoCollectionJob> CollectionJobs(IReadOnlyList<string> plants, IReadOnlyList<string> states, DateTime now) =>
    [
        CreateCollectionJob(plants[0], states[0], "Auto", "Successful", now.AddMinutes(-18), 1),
        CreateCollectionJob(plants[1], states[1], "Manual", "Successful", now.AddHours(-2), 2),
        CreateCollectionJob(plants[2], states[2], "Auto", "Failed", now.AddHours(-5), 3),
        CreateCollectionJob(plants[3], states[3], "Auto", "Successful", now.AddDays(-1), 4),
        CreateCollectionJob(plants[0], states[0], "Manual", "Failed", now.AddDays(-1).AddHours(-3), 5),
        CreateCollectionJob(plants[1], states[1], "Auto", "Successful", now.AddDays(-2), 6)
    ];

    public static IReadOnlyList<DemoScheduleConfig> CollectionSchedules(IReadOnlyList<string> plants) =>
    [
        new("SCH-DDQ-001", "DDQ Quarter-Hour Collection", plants[0], "DDQ", "15 minutes", "00:15", 3, true),
        new("SCH-MMF-002", "Morning MMF Forecast", plants[1], "MMF", "Daily", "07:00", 2, true),
        new("SCH-DDQ-003", "Sepang DDQ Collection", plants[2], "DDQ", "30 minutes", "00:30", 3, true),
        new("SCH-MMF-004", "Pengerang MMF Collection", plants[3], "MMF", "1 hour", "01:00", 2, false)
    ];

    public static IReadOnlyList<DemoAnalysisResult> OnDemandReports(DateTime now) =>
    [
        CreateAnalysis("DDQ", now.Date.AddDays(-1), now.AddHours(-2), "Successful", 1),
        CreateAnalysis("MMF", now.Date.AddDays(-2), now.AddHours(-5), "Successful", 2),
        CreateAnalysis("DDQ", now.Date.AddDays(-3), now.AddDays(-1), "Failed", 3)
    ];

    public static DemoAnalysisResult CreateAnalysis(string type, DateTime submissionDate, DateTime generatedAt, string status, int sequence)
    {
        var reference = $"ODA-{type}-{generatedAt:yyyyMMdd}-{sequence:000}";
        var description = type == "DDQ"
            ? $"DDQ interval validation generated from the {submissionDate:dd MMM yyyy} submission."
            : $"MMF forecast and variance analysis generated from the {submissionDate:dd MMM yyyy} submission.";
        if (status == "Failed") description += " Generation completed with validation errors and requires review.";
        return new(reference, $"{type} On-Demand Analysis {sequence:000}", type, submissionDate, generatedAt, description, status);
    }

    public static IReadOnlyList<DemoReportRecord> ReportHistory(DateTime now) =>
    [
        new("RPT-20260906-018", "Daily DDQ Performance Summary", "DDQ", "Scheduled", now.Date.AddDays(-1), now.AddMinutes(-28), "Completed", "System Scheduler", 3, "Daily plant submission, interval completeness and validation performance."),
        new("RPT-20260906-017", "MMF Forecast Variance Analysis", "MMF", "On-Demand", now.Date.AddDays(-2), now.AddHours(-2), "Completed", "LSS Reviewer", 7, "Forecast versus actual generation with plant-level variance findings."),
        new("RPT-20260906-016", "Portfolio Data Quality Report", "DDQ", "Scheduled", now.Date.AddDays(-1), now.AddHours(-4), "Processing", "System Scheduler", 0, "Portfolio-wide DDQ quality checks are currently being processed."),
        new("RPT-20260905-015", "Bidor Collection Exception Review", "DDQ", "On-Demand", now.Date.AddDays(-3), now.AddDays(-1), "Failed", "SBLSS Administrator", 0, "Report generation stopped because the selected collection dataset was incomplete."),
        new("RPT-20260905-014", "Weekly LSS Operations Report", "BOTH", "Scheduled", now.Date.AddDays(-7), now.AddDays(-1).AddHours(-3), "Completed", "System Scheduler", 11, "Consolidated DDQ and MMF operational performance for all active plants."),
        new("RPT-20260904-013", "Kulim Irradiance Anomaly Review", "MMF", "On-Demand", now.Date.AddDays(-4), now.AddDays(-2), "Completed", "LSS Staff", 5, "Irradiance, GHI and POA anomaly review for Kulim Solar Plant.")
    ];

    public static IReadOnlyList<DemoDistributionList> DistributionLists() =>
    [
        new("DL-OPS-001", "LSS Operations Team", ["operations@sblss.local", "shiftlead@sblss.local"], true),
        new("DL-DQ-002", "Data Quality Reviewers", ["reviewer@sblss.local", "dataquality@sblss.local"], true),
        new("DL-MGT-003", "Management Reporting", ["management@sblss.local"], true)
    ];

    public static IReadOnlyList<DemoReportSchedule> ReportSchedules(IReadOnlyList<string> plants) =>
    [
        new("RSC-001", "Daily DDQ Delivery", "Daily DDQ Performance", plants.Take(4).ToList(), "Previous day", "Daily", "07:15", "DL-DQ-002", true),
        new("RSC-002", "Weekly Operations Pack", "Weekly LSS Operations", plants.Take(4).ToList(), "Previous week", "Weekly", "08:00", "DL-OPS-001", true),
        new("RSC-003", "Monthly Management Report", "Monthly Portfolio Compliance", plants.Take(4).ToList(), "Previous month", "Monthly", "09:00", "DL-MGT-003", false)
    ];

    public static IReadOnlyList<DemoReportDelivery> ReportDeliveries(IReadOnlyList<DemoReportSchedule> schedules, IReadOnlyList<DemoDistributionList> lists, DateTime now) =>
    [
        new("RDL-20260906-031", schedules[0].Name, "DDQ", now.AddMinutes(-35), 4, "Generated", "Sent", "Generated", "Sent", "Delivered", lists[1].Name, lists[1].Emails, "Main report and separate anomaly report were sent to all assigned recipients."),
        new("RDL-20260906-030", schedules[1].Name, "BOTH", now.AddHours(-2), 0, "Generated", "Sent", "Not required", "Not required", "Delivered", lists[0].Name, lists[0].Emails, "Main report was delivered. No anomalies were detected for this reporting period."),
        new("RDL-20260905-029", schedules[0].Name, "DDQ", now.AddDays(-1), 7, "Generated", "Sent", "Generated", "Failed", "Warning", lists[1].Name, lists[1].Emails, "The main report was sent, but anomaly-report email delivery failed and requires retry."),
        new("RDL-20260904-028", schedules[1].Name, "BOTH", now.AddDays(-2), 2, "Failed", "Not sent", "Not generated", "Not sent", "Failed", lists[0].Name, lists[0].Emails, "Report generation failed before email delivery because source data was incomplete.")
    ];

    public static decimal DdqPower(LssPlant plant, string dataType, DateTime timestamp, int index, string[] dataTypes)
    {
        var daylight = Math.Max(0, Math.Sin((timestamp.TimeOfDay.TotalHours - 6) / 13 * Math.PI));
        var dataTypeFactor = Array.IndexOf(dataTypes, dataType) * 2.8;
        var variation = Math.Sin((index + plant.Id) * 0.73) * 3.2;
        var value = Math.Min((double)plant.CapacityMw,
            Math.Max(0, 5 + daylight * (double)plant.CapacityMw * 0.82 + dataTypeFactor + variation));
        return Math.Round((decimal)value, 2);
    }

    public static string[] MmfStreamNames(string parameter) =>
        [$"{parameter} Forecast", "Operational Availability", "Dispatch Performance"];

    public static DemoMmfReading MmfReading(double capacityMw, string parameter, DateTime timestamp, int index, int datasetId)
    {
        var parameterFactor = parameter switch { "GHI" => 0.92, "POA" => 1.08, _ => 1d };
        var solarCurve = Math.Max(0, Math.Sin((timestamp.TimeOfDay.TotalHours - 6) / 13 * Math.PI));
        var forecast = Math.Min(capacityMw, solarCurve * capacityMw * 0.9 * parameterFactor);
        var variation = Math.Sin((index + datasetId) * 0.57) * 4.8;
        var actual = Math.Clamp(forecast * (1 + variation / 100), 0, capacityMw);
        var variance = forecast <= 0 ? 0 : (actual - forecast) / forecast * 100;
        return new(Math.Round(forecast, 2), Math.Round(actual, 2), Math.Round(variance, 2), Math.Abs(variance) <= 5 ? "Within" : "Alert");
    }

    private static LssPlant CreatePlant(string plantId, string name, string state, decimal capacityMw)
    {
        var slug = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
        return new LssPlant { PlantId = plantId, Name = name, State = state, CapacityMw = capacityMw,
            DdqApiEndpoint = $"https://api.sblss.local/ddq/{slug}", MmfApiEndpoint = $"https://api.sblss.local/mmf/{slug}", IsActive = true };
    }

    public static DemoCollectionJob CreateCollectionJob(string plant, string state, string mode, string status, DateTime runAt, int sequence)
    {
        var id = $"DDQ-{runAt:yyyyMMdd}-{sequence:000}";
        var description = status == "Successful" ? "DDQ interval records collected and validated." : "Plant API did not return a complete interval dataset.";
        var response = status == "Successful" ? $"DDQ data from {plant} was received and validated successfully." : $"Collection from {plant} failed validation. Review the API configuration before retrying.";
        return new(id, plant, state, mode, runAt.Date, runAt, status, description, response, 1.4 + sequence % 6 * 0.7);
    }
}

public sealed record DemoCollectionJob(string CollectionId, string PlantName, string State, string Mode, DateTime SubmissionDate, DateTime RunAt, string Status, string Description, string ResponseMessage, double DurationSeconds);
public sealed record DemoScheduleConfig(string ScheduleId, string Name, string Plant, string Source, string Interval, string RunTime, int RetryCount, bool IsActive);
public sealed record DemoAnalysisResult(string Reference, string Name, string Type, DateTime SubmissionDate, DateTime GeneratedAt, string Description, string Status);
public sealed record DemoReportRecord(string ReportId, string Name, string Source, string Type, DateTime SubmissionDate, DateTime GeneratedAt, string Status, string RequestedBy, int AnomalyCount, string Description);
public sealed record DemoDistributionList(string Id, string Name, List<string> Emails, bool IsActive);
public sealed record DemoReportSchedule(string Id, string Name, string Template, List<string> Plants, string ReportingPeriod, string Frequency, string ExecutionTime, string DistributionListId, bool IsActive);
public sealed record DemoReportDelivery(string Id, string ScheduleName, string Source, DateTime GeneratedAt, int AnomalyCount, string MainReportStatus, string MainEmailStatus, string AnomalyReportStatus, string AnomalyEmailStatus, string OverallStatus, string DistributionListName, List<string> Recipients, string Message);
public sealed record DemoMmfReading(double ForecastMw, double ActualMw, double VariancePercent, string Status);
