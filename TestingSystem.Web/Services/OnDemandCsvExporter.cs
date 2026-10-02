using System.Globalization;
using System.Text;
using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Models;

namespace TestingSystem.Web.Services;

public static class OnDemandCsvExporter
{
    public static string BuildDdq(string dataType, DateTime submissionDate,
        IReadOnlyList<LssPlant> plants, IReadOnlyList<SolarApiReading> readings, int? requestedInterval = null)
    {
        var interval = requestedInterval is 15 or 30 or 60
            ? requestedInterval.Value
            : DdqDatasetPolicy.Get(dataType).ReferenceTemplateIntervalMinutes;
        var latest = Latest(readings.Where(reading => reading.DataType == dataType
            && TotalMinutes(reading.IntervalStart) % interval == 0), includeParameter: false);
        var timestamps = latest.Select(reading => reading.IntervalStart).Distinct().OrderBy(value => value).ToList();
        var index = latest.ToDictionary(reading => (reading.PlantId, reading.IntervalStart));
        var output = new StringBuilder();
        AddRow(output, "SOLAR DATA SUBMISSION / DATA MONITORING");
        AddRow(output, "Data Type", DdqDatasetPolicy.Get(dataType).DisplayName);
        AddRow(output, "Submission Date & Time", submissionDate.ToString("dd/MM/yyyy HH:mm:ss"));
        AddRow(output, "Data Interval", IntervalText(interval));
        output.AppendLine();
        AddRow(output, ["Submission Date & Time", "Year", "Month", "Day", "Period",
            .. plants.Select(plant => TemplatePlantCode(plant.PlantId))]);
        foreach (var timestamp in timestamps)
        {
            var values = new List<object?>
            {
                submissionDate.ToString("dd/MM/yyyy HH:mm:ss"), timestamp.Year, timestamp.Month, timestamp.Day,
                TotalMinutes(timestamp) / interval + 1
            };
            values.AddRange(plants.Select(plant => (object?)index.GetValueOrDefault((plant.Id, timestamp))?.Value));
            AddRow(output, values);
        }
        return output.ToString();
    }

    public static string BuildMmf(DateTime submissionDate,
        IReadOnlyList<LssPlant> plants, IReadOnlyList<SolarApiReading> readings,
        DateTime? dataRangeStart = null, DateTime? dataRangeEnd = null)
    {
        var latest = Latest(readings.Where(reading => reading.DataType == "MMF"), includeParameter: true);
        var parameters = latest.Select(reading => reading.Parameter)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList();
        var output = new StringBuilder();
        AddRow(output, "MMF DATA MONITORING");
        if (dataRangeStart.HasValue && dataRangeEnd.HasValue)
            AddRow(output, "Data Timestamp Range",
                $"{dataRangeStart:dd/MM/yyyy HH:mm:ss} - {dataRangeEnd:dd/MM/yyyy HH:mm:ss}");
        else
            AddRow(output, "Submission Date & Time", submissionDate.ToString("dd/MM/yyyy HH:mm:ss"));
        AddRow(output, "Plant Count", plants.Count);
        output.AppendLine();

        foreach (var parameter in parameters)
        {
            var parameterReadings = latest.Where(reading => reading.Parameter.Equals(parameter, StringComparison.OrdinalIgnoreCase)).ToList();
            var timestamps = parameterReadings.Select(reading => reading.IntervalStart).Distinct().OrderBy(value => value).ToList();
            var index = parameterReadings.ToDictionary(reading => (reading.PlantId, reading.IntervalStart));
            var interval = DetectInterval(parameterReadings, timestamps);
            var unit = parameterReadings.Select(reading => reading.Unit).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
            AddRow(output, "Parameter", parameter);
            AddRow(output, "Data Interval", IntervalText(interval));
            AddRow(output, "Unit", unit);
            AddRow(output, ["LSS", .. timestamps.Select((_, position) => $"HR{position + 1}")]);
            foreach (var plant in plants)
            {
                var values = new List<object?> { TemplatePlantCode(plant.PlantId) };
                values.AddRange(timestamps.Select(timestamp => (object?)index.GetValueOrDefault((plant.Id, timestamp))?.Value));
                AddRow(output, values);
            }
            output.AppendLine();
        }
        return output.ToString();
    }

    private static List<SolarApiReading> Latest(IEnumerable<SolarApiReading> readings, bool includeParameter) =>
        readings.GroupBy(reading => new
            {
                reading.PlantId,
                Parameter = includeParameter ? reading.Parameter.ToUpperInvariant() : "",
                reading.IntervalStart
            })
            .Select(group => group.OrderByDescending(reading => reading.Id).First())
            .OrderBy(reading => reading.IntervalStart).ThenBy(reading => reading.PlantId).ToList();

    private static int DetectInterval(IReadOnlyList<SolarApiReading> readings, IReadOnlyList<DateTime> timestamps)
    {
        var stored = readings.Where(reading => reading.IntervalMinutes is > 0)
            .Select(reading => reading.IntervalMinutes!.Value).DefaultIfEmpty(0).Min();
        if (stored > 0) return stored;
        var detected = timestamps.Zip(timestamps.Skip(1), (left, right) => (int)(right - left).TotalMinutes)
            .Where(value => value > 0).DefaultIfEmpty(15).Min();
        return detected;
    }

    private static int TotalMinutes(DateTime timestamp) => timestamp.Hour * 60 + timestamp.Minute;
    private static string IntervalText(int minutes) => minutes == 60 ? "1 hour" : $"{minutes} minutes";
    private static string TemplatePlantCode(string plantId)
    {
        var normalized = new string(plantId.Where(char.IsLetterOrDigit).ToArray());
        if (normalized.StartsWith("LSS", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(normalized[3..], out var number)) return $"LSS{number}";
        return plantId;
    }

    private static void AddRow(StringBuilder output, params object?[] values) => AddRow(output, (IEnumerable<object?>)values);
    private static void AddRow(StringBuilder output, IEnumerable<object?> values) =>
        output.AppendLine(string.Join(',', values.Select(Csv)));
    private static string Csv(object? value)
    {
        var text = value switch
        {
            null => "",
            decimal number => number.ToString("0.######", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
            _ => value.ToString() ?? ""
        };
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
