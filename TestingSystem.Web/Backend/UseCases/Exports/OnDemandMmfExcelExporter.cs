using TestingSystem.Web.Models;
using TestingSystem.Web.Database.Entities;

namespace TestingSystem.Web.Components.Frontend.UseCases;

internal static class OnDemandMmfExcelExporter
{
    public static byte[] Build(DateTime submissionDate, IReadOnlyList<LssPlant> plants,
        IReadOnlyList<SolarApiReading> readings, DateTime? dataRangeStart = null,
        DateTime? dataRangeEnd = null)
        => DdqExcelWorkbookExporter.Build(BuildSheets(submissionDate, plants, readings,
            dataRangeStart, dataRangeEnd));

    public static IReadOnlyList<DdqWorkbookSheet> BuildSheets(DateTime submissionDate,
        IReadOnlyList<LssPlant> plants, IReadOnlyList<SolarApiReading> readings,
        DateTime? dataRangeStart = null, DateTime? dataRangeEnd = null)
    {
        var latest = readings.Where(reading => reading.DataType == "MMF")
            .GroupBy(reading => new
            {
                reading.PlantId,
                Parameter = reading.Parameter.ToUpperInvariant(),
                reading.IntervalStart
            })
            .Select(group => group.OrderByDescending(reading => reading.Id).First())
            .ToList();
        var parameters = latest.Select(reading => reading.Parameter)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(parameter => parameter)
            .ToList();
        var sheets = new List<DdqWorkbookSheet>();

        foreach (var parameter in parameters)
        {
            var parameterReadings = latest
                .Where(reading => reading.Parameter.Equals(parameter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var timestamps = parameterReadings.Select(reading => reading.IntervalStart)
                .Distinct().OrderBy(timestamp => timestamp).ToList();
            var index = parameterReadings.ToDictionary(reading => (reading.PlantId, reading.IntervalStart));
            var interval = DetectInterval(parameterReadings, timestamps);
            var unit = parameterReadings.Select(reading => reading.Unit)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
            var metadata = new List<(string Label, string Value)>
            {
                ("Parameter", parameter),
                ("Data Interval", IntervalText(interval)),
                ("Unit", unit),
                ("Plant Count", plants.Count.ToString())
            };
            if (dataRangeStart.HasValue && dataRangeEnd.HasValue)
                metadata.Add(("Data Timestamp Range",
                    $"{dataRangeStart:dd/MM/yyyy HH:mm:ss} - {dataRangeEnd:dd/MM/yyyy HH:mm:ss}"));
            else
                metadata.Add(("Submission Date & Time", submissionDate.ToString("dd/MM/yyyy HH:mm:ss")));

            var headers = new List<string> { "LSS" };
            headers.AddRange(timestamps.Select((_, position) => $"HR{position + 1}"));
            var rows = plants.Select(plant =>
            {
                var values = new List<object?> { TemplatePlantCode(plant.PlantId) };
                values.AddRange(timestamps.Select(timestamp =>
                    (object?)index.GetValueOrDefault((plant.Id, timestamp))?.Value));
                return (IReadOnlyList<object?>)values;
            }).ToList();
            sheets.Add(new DdqWorkbookSheet(parameter, $"MMF DATA MONITORING - {parameter}",
                metadata, headers, rows, 1));
        }

        if (sheets.Count == 0)
            throw new InvalidOperationException("No MMF parameter readings are available for preview or export.");
        return sheets;
    }

    private static int DetectInterval(IReadOnlyList<SolarApiReading> readings,
        IReadOnlyList<DateTime> timestamps)
    {
        var stored = readings.Where(reading => reading.IntervalMinutes is > 0)
            .Select(reading => reading.IntervalMinutes!.Value).DefaultIfEmpty(0).Min();
        if (stored > 0) return stored;
        return timestamps.Zip(timestamps.Skip(1), (left, right) => (int)(right - left).TotalMinutes)
            .Where(value => value > 0).DefaultIfEmpty(15).Min();
    }

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
