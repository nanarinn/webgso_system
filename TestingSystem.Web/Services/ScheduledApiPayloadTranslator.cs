using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using TestingSystem.Web.Database.Entities;

namespace TestingSystem.Web.Services;

public static class ScheduledApiPayloadTranslator
{
    private static readonly string[] TimestampNames =
        ["timestamp", "datetime", "dateTime", "date", "time", "startTime", "intervalStart", "readingTime", "recordedAt", "submissionDateTime"];
    private static readonly string[] EndTimestampNames = ["endTime", "intervalEnd"];
    private static readonly string[] ParameterNames = ["parameter", "metric", "field", "measurement", "sensorType", "name"];
    private static readonly string[] ValueNames = ["value", "forecastValue", "measurementValue", "reading"];
    private static readonly string[] UnitNames = ["unit", "valueUnit", "measurementUnit"];

    public static TranslationResult Translate(string payload, string apiType, string dataType, int plantId,
        long collectionJobId, DateTime collectedAt, int intervalMinutes, string? contentType = null)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return Build([], [], "Empty", apiType, dataType, plantId, collectionJobId, collectedAt, intervalMinutes);

        var candidates = new List<ReadingCandidate>();
        var issues = new List<TranslationIssue>();
        var trimmed = payload.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        var format = DetectFormat(trimmed, contentType);

        try
        {
            switch (format)
            {
                case "JSON": ReadJson(trimmed, collectedAt, candidates); break;
                case "XML": ReadXml(trimmed, collectedAt, candidates); break;
                case "CSV": ReadDelimited(trimmed, collectedAt, candidates); break;
                case "HTML":
                    throw new FormatException("The endpoint returned an HTML page instead of API data. Check that the URL points to the data endpoint and does not require an interactive login.");
                default:
                    throw new FormatException($"Unsupported API response format '{contentType ?? "unknown"}'. Use JSON, XML or CSV data.");
            }
        }
        catch (JsonException exception)
        {
            throw new FormatException($"The API returned invalid JSON: {exception.Message}", exception);
        }
        catch (System.Xml.XmlException exception)
        {
            throw new FormatException($"The API returned invalid XML: {exception.Message}", exception);
        }

        if (candidates.Count == 0)
            issues.Add(new(apiType.Equals("MMF", StringComparison.OrdinalIgnoreCase) ? "MMF-001" : "DDQ-001",
                "API payload", "0", $"The {format} response was saved, but no numeric measurement fields were found."));

        return Build(candidates, issues, format, apiType, dataType, plantId, collectionJobId, collectedAt, intervalMinutes);
    }

    private static TranslationResult Build(List<ReadingCandidate> candidates, List<TranslationIssue> issues, string format,
        string apiType, string dataType, int plantId, long collectionJobId, DateTime collectedAt, int intervalMinutes)
    {
        var normalized = candidates
            .GroupBy(item => new { item.Timestamp, Parameter = NormalizeParameter(item.Parameter).ToUpperInvariant(), Unit = item.Unit.ToUpperInvariant() })
            .Select(group =>
            {
                if (group.Count() > 1 && !apiType.Equals("MMF", StringComparison.OrdinalIgnoreCase))
                    issues.Add(new("DDQ-002", group.Key.Parameter, group.Count().ToString(CultureInfo.InvariantCulture),
                        "Duplicate readings were received for the same plant, parameter and timestamp."));
                return new SolarApiReading
                {
                    CollectionJobId = collectionJobId, PlantId = plantId,
                    DataType = apiType.Equals("MMF", StringComparison.OrdinalIgnoreCase) ? "MMF" : dataType,
                    SubmissionDateTime = collectedAt, IntervalStart = group.Key.Timestamp,
                    IntervalEnd = group.First().EndTimestamp ?? group.Key.Timestamp.AddMinutes(intervalMinutes),
                    IntervalMinutes = intervalMinutes, Parameter = NormalizeParameter(group.First().Parameter),
                    Value = decimal.Round(group.Average(item => item.Value), 6), Unit = group.First().Unit,
                    ErrorCode = 0, ErrorMessage = "OK", IsSynthetic = false
                };
            }).OrderBy(item => item.IntervalStart).ThenBy(item => item.Parameter).ToList();
        return new(normalized, issues, format);
    }

    private static string DetectFormat(string payload, string? contentType)
    {
        var type = contentType?.ToLowerInvariant() ?? string.Empty;
        if (type.Contains("html") || payload.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || payload.StartsWith("<html", StringComparison.OrdinalIgnoreCase)) return "HTML";
        if (type.Contains("json") || payload.StartsWith('{') || payload.StartsWith('[')) return "JSON";
        if (type.Contains("xml") || payload.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) || payload.StartsWith('<')) return "XML";
        if (type.Contains("csv") || type.Contains("tab-separated") || LooksDelimited(payload)) return "CSV";
        return "Unknown";
    }

    private static bool LooksDelimited(string payload)
    {
        var first = payload.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Take(2).ToArray();
        return first.Length == 2 && new[] { ',', ';', '\t', '|' }.Any(delimiter =>
            first[0].Count(character => character == delimiter) > 0
            && first[0].Count(character => character == delimiter) == first[1].Count(character => character == delimiter));
    }

    private static void ReadJson(string payload, DateTime fallback, List<ReadingCandidate> readings)
    {
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        WalkJson(document.RootElement, fallback, readings);
    }

    private static void WalkJson(JsonElement element, DateTime inheritedTimestamp, List<ReadingCandidate> readings)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray()) WalkJson(child, inheritedTimestamp, readings);
            return;
        }
        if (element.ValueKind != JsonValueKind.Object) return;

        var timestamp = ReadTimestamp(element, TimestampNames) ?? inheritedTimestamp;
        var endTimestamp = ReadTimestamp(element, EndTimestampNames);
        var parameter = ReadString(element, ParameterNames);
        var unit = ReadString(element, UnitNames) ?? string.Empty;
        var explicitValue = ReadDecimal(element, ValueNames);
        if (!string.IsNullOrWhiteSpace(parameter) && explicitValue.HasValue)
            readings.Add(new(timestamp, endTimestamp, parameter, explicitValue.Value, unit));
        else
            foreach (var property in element.EnumerateObject())
                if (TryDecimal(property.Value, out var value) && LooksLikeMeasurement(property.Name))
                    readings.Add(new(timestamp, endTimestamp, property.Name, value, unit));

        foreach (var property in element.EnumerateObject())
            if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                WalkJson(property.Value, timestamp, readings);
    }

    private static void ReadXml(string payload, DateTime fallback, List<ReadingCandidate> readings)
    {
        var document = XDocument.Parse(payload, LoadOptions.None);
        foreach (var row in document.Descendants().Where(element => element.Elements().Any()
                     && !element.Elements().Any(child => child.Elements().Any())))
        {
            var fields = row.Elements().GroupBy(item => item.Name.LocalName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);
            AddFields(fields, fallback, readings);
        }
    }

    private static void ReadDelimited(string payload, DateTime fallback, List<ReadingCandidate> readings)
    {
        var lines = payload.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return;
        var delimiter = new[] { ',', ';', '\t', '|' }.OrderByDescending(item => lines[0].Count(character => character == item)).First();
        var headers = SplitDelimited(lines[0], delimiter);
        foreach (var line in lines.Skip(1))
        {
            var values = SplitDelimited(line, delimiter);
            var fields = headers.Select((header, index) => (header, value: index < values.Count ? values[index] : string.Empty))
                .GroupBy(item => item.header, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().value, StringComparer.OrdinalIgnoreCase);
            AddFields(fields, fallback, readings);
        }
    }

    private static void AddFields(IReadOnlyDictionary<string, string> fields, DateTime fallback, List<ReadingCandidate> readings)
    {
        var timestamp = ParseNamedTimestamp(fields, TimestampNames) ?? fallback;
        var end = ParseNamedTimestamp(fields, EndTimestampNames);
        var parameter = First(fields, ParameterNames);
        var unit = First(fields, UnitNames) ?? string.Empty;
        var explicitValue = ParseNamedDecimal(fields, ValueNames);
        if (!string.IsNullOrWhiteSpace(parameter) && explicitValue.HasValue)
            readings.Add(new(timestamp, end, parameter, explicitValue.Value, unit));
        else
            foreach (var field in fields)
                if (LooksLikeMeasurement(field.Key) && decimal.TryParse(field.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                    readings.Add(new(timestamp, end, field.Key, value, unit));
    }

    private static List<string> SplitDelimited(string line, char delimiter)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"') { current.Append('"'); index++; }
                else quoted = !quoted;
            }
            else if (line[index] == delimiter && !quoted) { values.Add(current.ToString().Trim()); current.Clear(); }
            else current.Append(line[index]);
        }
        values.Add(current.ToString().Trim());
        return values;
    }

    private static bool LooksLikeMeasurement(string name)
    {
        var normalized = name.ToLowerInvariant().Replace("_", string.Empty).Replace("-", string.Empty);
        if (TimestampNames.Concat(EndTimestampNames).Any(item => item.Replace("_", string.Empty).Equals(normalized, StringComparison.OrdinalIgnoreCase))
            || normalized.EndsWith("id") || normalized.Contains("code") || normalized.Contains("status") || normalized.Contains("error")
            || normalized.Contains("count") || normalized.Contains("interval") || normalized.Contains("latitude") || normalized.Contains("longitude")) return false;
        return normalized.Contains("value") || normalized.Contains("power") || normalized.Contains("production") || normalized.Contains("forecast")
            || normalized.Contains("ghi") || normalized.Contains("poa") || normalized.Contains("temp") || normalized.Contains("pressure")
            || normalized.Contains("humidity") || normalized.Contains("wind") || normalized.Contains("irradiance")
            || normalized.Contains("precipitation") || normalized.Contains("energy") || normalized.Contains("generation");
    }

    private static DateTime? ParseNamedTimestamp(IReadOnlyDictionary<string, string> fields, IEnumerable<string> names)
    {
        var value = First(fields, names);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var timestamp) ? timestamp.LocalDateTime : null;
    }
    private static decimal? ParseNamedDecimal(IReadOnlyDictionary<string, string> fields, IEnumerable<string> names)
    {
        var value = First(fields, names);
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number) ? number : null;
    }
    private static string? First(IReadOnlyDictionary<string, string> fields, IEnumerable<string> names)
    {
        foreach (var name in names) if (fields.TryGetValue(name, out var value)) return value;
        return null;
    }
    private static DateTime? ReadTimestamp(JsonElement element, IEnumerable<string> names)
    {
        foreach (var name in names)
            if (TryGetProperty(element, name, out var property) && property.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var timestamp)) return timestamp.LocalDateTime;
        return null;
    }
    private static string? ReadString(JsonElement element, IEnumerable<string> names)
    {
        foreach (var name in names) if (TryGetProperty(element, name, out var property) && property.ValueKind == JsonValueKind.String) return property.GetString();
        return null;
    }
    private static decimal? ReadDecimal(JsonElement element, IEnumerable<string> names)
    {
        foreach (var name in names) if (TryGetProperty(element, name, out var property) && TryDecimal(property, out var number)) return number;
        return null;
    }
    private static bool TryDecimal(JsonElement element, out decimal value)
    {
        value = 0;
        return (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out value))
            || (element.ValueKind == JsonValueKind.String
                && decimal.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value));
    }
    private static bool TryGetProperty(JsonElement element, string name, out JsonElement result)
    {
        foreach (var property in element.EnumerateObject()) if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { result = property.Value; return true; }
        result = default; return false;
    }
    private static string NormalizeParameter(string value) => string.IsNullOrWhiteSpace(value) ? "Value" : value.Trim();
    private sealed record ReadingCandidate(DateTime Timestamp, DateTime? EndTimestamp, string Parameter, decimal Value, string Unit);
}

public sealed record TranslationResult(IReadOnlyList<SolarApiReading> Readings, IReadOnlyList<TranslationIssue> Issues, string PayloadFormat);
public sealed record TranslationIssue(string RuleCode, string FieldName, string? OriginalValue, string Reason);
