using System.Globalization;
using System.Text;
using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Models;

namespace TestingSystem.Web.Services;

public sealed record ReportDetectedAnomaly(
    string CollectionId,
    int PlantKey,
    string FieldName,
    string? OriginalValue,
    string Reason,
    string Status,
    DateTimeOffset DetectedAt,
    string RuleCode = "",
    string RuleName = "");

public static class ReportAnomalyCsvExporter
{
    public static string Build(string apiType, string dataType, DateTime submissionDate,
        IReadOnlyList<LssPlant> plants, IReadOnlyList<SolarApiReading> currentReadings,
        PreparedReportReadings prepared, IReadOnlyList<ReportDetectedAnomaly> detectedAnomalies)
    {
        var output = new StringBuilder();
        AddRow(output, "ANOMALY AND REPORT SUBSTITUTION REPORT");
        AddRow(output, "API Type", apiType);
        AddRow(output, "Data Type", dataType);
        AddRow(output, "Submission Date & Time", submissionDate.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        AddRow(output, "Substitution Summary", prepared.Summary.Description);
        output.AppendLine();
        AddRow(output, "Record Type", "Collection ID", "Plant ID", "Plant Name", "Field / Parameter",
            "Data Timestamp", "Original Value", "Report Value", "Rule Code", "Rule Name",
            "Anomaly Detail", "Substitution Source", "Source Reference", "Status", "Detected At");

        var plantIndex = plants.ToDictionary(plant => plant.Id);
        foreach (var anomaly in detectedAnomalies.OrderBy(item => item.DetectedAt))
        {
            plantIndex.TryGetValue(anomaly.PlantKey, out var plant);
            AddRow(output, "Validation anomaly", anomaly.CollectionId, plant?.PlantId ?? anomaly.PlantKey.ToString(),
                plant?.Name ?? "Unknown plant", anomaly.FieldName, string.Empty,
                anomaly.OriginalValue ?? "Missing", string.Empty, anomaly.RuleCode, anomaly.RuleName,
                anomaly.Reason, string.Empty, string.Empty, anomaly.Status,
                anomaly.DetectedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        }

        foreach (var detail in prepared.Details
                     .OrderBy(item => item.DataTimestamp).ThenBy(item => item.PlantId).ThenBy(item => item.Parameter))
        {
            plantIndex.TryGetValue(detail.PlantId, out var plant);
            AddRow(output, "Report substitution", string.Empty, plant?.PlantId ?? detail.PlantId.ToString(),
                plant?.Name ?? "Unknown plant", detail.Parameter,
                detail.DataTimestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                detail.OriginalValue.HasValue ? FormatNumber(detail.OriginalValue.Value) : "Missing",
                FormatNumber(detail.ReportValue), detail.RuleCode, detail.RuleName, detail.AnomalyDetail,
                detail.SubstitutionSource, detail.SourceReference, "Applied", string.Empty);
        }

        return output.ToString();
    }

    private static string FormatNumber(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static void AddRow(StringBuilder output, params string[] values) =>
        output.AppendLine(string.Join(',', values.Select(value =>
        {
            var safe = CsvFieldSafety.Sanitize(value);
            return $"\"{safe.Replace("\"", "\"\"")}\"";
        })));
}
