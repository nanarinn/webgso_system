using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Models;
using TestingSystem.Web.Services;

namespace TestingSystem.Web.Components.Frontend.UseCases;

internal static class OnDemandAnomalyPreviewBuilder
{
    public static DdqWorkbookSheet Build(string apiType, string dataType, DateTime submissionDate,
        IReadOnlyList<LssPlant> plants, PreparedReportReadings prepared,
        IReadOnlyList<ReportDetectedAnomaly> detectedAnomalies)
    {
        var plantIndex = plants.ToDictionary(plant => plant.Id);
        var rows = new List<IReadOnlyList<object?>>();

        foreach (var anomaly in detectedAnomalies.OrderBy(item => item.DetectedAt))
        {
            plantIndex.TryGetValue(anomaly.PlantKey, out var plant);
            rows.Add(new object?[]
            {
                "Validation anomaly", anomaly.CollectionId,
                plant?.PlantId ?? anomaly.PlantKey.ToString(), plant?.Name ?? "Unknown plant",
                anomaly.FieldName, null, anomaly.OriginalValue ?? "Missing", null,
                anomaly.RuleCode, anomaly.RuleName, anomaly.Reason, null, null,
                anomaly.Status, anomaly.DetectedAt.LocalDateTime
            });
        }

        foreach (var detail in prepared.Details
                     .OrderBy(item => item.DataTimestamp)
                     .ThenBy(item => item.PlantId)
                     .ThenBy(item => item.Parameter))
        {
            plantIndex.TryGetValue(detail.PlantId, out var plant);
            rows.Add(new object?[]
            {
                "Report substitution", null,
                plant?.PlantId ?? detail.PlantId.ToString(), plant?.Name ?? "Unknown plant",
                detail.Parameter, detail.DataTimestamp, detail.OriginalValue, detail.ReportValue,
                detail.RuleCode, detail.RuleName, detail.AnomalyDetail,
                detail.SubstitutionSource, detail.SourceReference, "Applied", null
            });
        }

        var metadata = new (string Label, string Value)[]
        {
            ("API Type", apiType),
            ("Data Type", dataType),
            ("Submission / Range Start", submissionDate.ToString("dd/MM/yyyy HH:mm:ss")),
            ("Validation Anomalies", detectedAnomalies.Count.ToString("N0")),
            ("Report Substitutions", prepared.Details.Count.ToString("N0"))
        };
        string[] headers =
        [
            "Record Type", "Collection ID", "Plant ID", "Plant Name", "Field / Parameter",
            "Data Timestamp", "Original Value", "Report Value", "Rule Code", "Rule Name",
            "Anomaly Detail", "Substitution Source", "Source Reference", "Status", "Detected At"
        ];
        return new("Anomaly Preview", "ANOMALY AND REPORT SUBSTITUTION PREVIEW",
            metadata, headers, rows, 4);
    }
}
