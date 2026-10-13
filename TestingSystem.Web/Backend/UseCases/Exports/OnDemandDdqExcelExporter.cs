using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Models;
using TestingSystem.Web.Services;

namespace TestingSystem.Web.Components.Frontend.UseCases;

internal static class OnDemandDdqExcelExporter
{
    public static byte[] Build(string dataType, DateTime submissionDate,
        IReadOnlyList<LssPlant> plants, IReadOnlyList<SolarApiReading> readings,
        int requestedInterval)
        => DdqExcelWorkbookExporter.Build(BuildSheets(dataType, submissionDate,
            plants, readings, requestedInterval));

    public static IReadOnlyList<DdqWorkbookSheet> BuildSheets(string dataType,
        DateTime submissionDate, IReadOnlyList<LssPlant> plants,
        IReadOnlyList<SolarApiReading> readings, int requestedInterval)
    {
        var interval = requestedInterval is 15 or 30 or 60
            ? requestedInterval
            : DdqDatasetPolicy.Get(dataType).ReferenceTemplateIntervalMinutes;
        var latest = readings.Where(reading => reading.DataType == dataType
                && TotalMinutes(reading.IntervalStart) % interval == 0)
            .GroupBy(reading => new { reading.PlantId, reading.IntervalStart })
            .Select(group => group.OrderByDescending(reading => reading.Id).First())
            .OrderBy(reading => reading.IntervalStart)
            .ThenBy(reading => reading.PlantId)
            .ToList();
        if (latest.Count == 0)
            throw new InvalidOperationException("No DDQ readings are available for preview or export.");

        var timestamps = latest.Select(reading => reading.IntervalStart)
            .Distinct().OrderBy(timestamp => timestamp).ToList();
        var index = latest.ToDictionary(reading => (reading.PlantId, reading.IntervalStart));
        var headers = new List<string>
            { "Submission Date & Time", "Year", "Month", "Day", "Period" };
        headers.AddRange(plants.Select(plant => TemplatePlantCode(plant.PlantId)));
        var rows = timestamps.Select(timestamp =>
        {
            var values = new List<object?>
            {
                submissionDate.ToString("dd/MM/yyyy HH:mm:ss"),
                timestamp.Year,
                timestamp.Month,
                timestamp.Day,
                TotalMinutes(timestamp) / interval + 1
            };
            values.AddRange(plants.Select(plant =>
                (object?)index.GetValueOrDefault((plant.Id, timestamp))?.Value));
            return (IReadOnlyList<object?>)values;
        }).ToList();
        var metadata = new (string Label, string Value)[]
        {
            ("Data Type", DdqDatasetPolicy.Get(dataType).DisplayName),
            ("Submission Date & Time", submissionDate.ToString("dd/MM/yyyy HH:mm:ss")),
            ("Data Interval", IntervalText(interval)),
            ("Data Timestamp Range", $"{timestamps[0]:dd/MM/yyyy HH:mm:ss} - {timestamps[^1]:dd/MM/yyyy HH:mm:ss}")
        };
        return
        [
            new DdqWorkbookSheet(dataType, "SOLAR DATA SUBMISSION / DATA MONITORING",
                metadata, headers, rows, 5)
        ];
    }

    private static int TotalMinutes(DateTime timestamp) => timestamp.Hour * 60 + timestamp.Minute;
    private static string IntervalText(int minutes) => minutes == 60 ? "1 hour" : $"{minutes} minutes";

    private static string TemplatePlantCode(string plantId)
    {
        var normalized = new string(plantId.Where(char.IsLetterOrDigit).ToArray());
        return normalized.StartsWith("LSS", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(normalized[3..], out var number)
                ? $"LSS{number}"
                : plantId;
    }
}
