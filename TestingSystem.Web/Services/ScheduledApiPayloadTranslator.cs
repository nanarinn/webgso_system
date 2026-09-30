using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using TestingSystem.Web.Database.Entities;

namespace TestingSystem.Web.Services;

public static class ScheduledApiPayloadTranslator
{
    private static readonly string[] TimestampNames =
        ["timestamp", "dataTimestamp", "forecastTimestamp", "datetime", "dateTime", "date", "time", "startTime", "intervalStart", "intervalStartTime", "readingTime", "recordedAt"];
    private static readonly string[] SubmissionTimestampNames =
        ["submissionDateTime", "submissionTimestamp", "submittedAt", "submissionDate"];
    private static readonly string[] EndTimestampNames = ["endTime", "intervalEnd", "intervalEndTime"];
    private static readonly string[] ParameterNames = ["parameter", "forecastResultParameter", "metric", "field", "measurement", "sensorType", "name"];
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
                case "JSON": ReadJson(trimmed, collectedAt, intervalMinutes, candidates); break;
                case "XML": ReadXml(trimmed, collectedAt, intervalMinutes, candidates); break;
                case "CSV": ReadDelimited(trimmed, collectedAt, intervalMinutes, candidates); break;
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
        var sourceIntervalMinutes = DetectSourceIntervalMinutes(candidates, intervalMinutes);
        var normalized = candidates
            .GroupBy(item => new
            {
                Timestamp = AlignToInterval(item.Timestamp, sourceIntervalMinutes),
                Parameter = NormalizeParameter(item.Parameter).ToUpperInvariant(),
                Unit = item.Unit.ToUpperInvariant()
            })
            .Select(group =>
            {
                if (group.Count() > 1 && !apiType.Equals("MMF", StringComparison.OrdinalIgnoreCase))
                    issues.Add(new("DDQ-002", group.Key.Parameter, group.Count().ToString(CultureInfo.InvariantCulture),
                        "Duplicate readings were received for the same plant, parameter and timestamp."));
                return new SolarApiReading
                {
                    CollectionJobId = collectionJobId, PlantId = plantId,
                    DataType = apiType.Equals("MMF", StringComparison.OrdinalIgnoreCase) ? "MMF" : dataType,
                    SubmissionDateTime = group.Max(item => item.SubmissionDateTime), IntervalStart = group.Key.Timestamp,
                    IntervalEnd = group.First().EndTimestamp ?? group.Key.Timestamp.AddMinutes(sourceIntervalMinutes),
                    IntervalMinutes = sourceIntervalMinutes, Parameter = NormalizeParameter(group.First().Parameter),
                    Value = decimal.Round(group.Average(item => item.Value), 6), Unit = group.First().Unit,
                    ErrorCode = 0, ErrorMessage = "OK", IsSynthetic = false
                };
            }).OrderBy(item => item.IntervalStart).ThenBy(item => item.Parameter).ToList();
        if (!apiType.Equals("MMF", StringComparison.OrdinalIgnoreCase) && normalized.Count > 1)
        {
            var timestamps = normalized.Select(item => item.IntervalStart).Distinct().OrderBy(value => value).ToList();
            var missingIntervals = timestamps.Zip(timestamps.Skip(1), (left, right) =>
                    Math.Max(0, (int)Math.Round((right - left).TotalMinutes / sourceIntervalMinutes) - 1))
                .Sum();
            if (missingIntervals > 0)
                issues.Add(new("DDQ-009", "Forecast timestamps", missingIntervals.ToString(CultureInfo.InvariantCulture),
                    $"The API response has {missingIntervals} missing {sourceIntervalMinutes}-minute timestamp(s) inside its supplied range."));
        }
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

    private static void ReadJson(string payload, DateTime fallback, int intervalMinutes, List<ReadingCandidate> readings)
    {
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        WalkJson(document.RootElement, fallback, fallback, intervalMinutes, readings, null);
    }

    private static void WalkJson(JsonElement element, DateTime inheritedTimestamp, DateTime inheritedSubmission,
        int intervalMinutes, List<ReadingCandidate> readings, string? inheritedParameter)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            var children = element.EnumerateArray().ToArray();
            if (!string.IsNullOrWhiteSpace(inheritedParameter) && !IsMetadataField(inheritedParameter)
                && children.All(child => TryDecimal(child, out _)))
            {
                for (var index = 0; index < children.Length; index++)
                    if (TryDecimal(children[index], out var value))
                        readings.Add(new(inheritedTimestamp.AddMinutes(index * Math.Max(1, intervalMinutes)), null,
                            inheritedSubmission, inheritedParameter, value, string.Empty));
                return;
            }
            foreach (var child in children)
                WalkJson(child, inheritedTimestamp, inheritedSubmission, intervalMinutes, readings, inheritedParameter);
            return;
        }
        if (element.ValueKind != JsonValueKind.Object) return;

        if (TryReadMatrixJson(element, inheritedTimestamp, inheritedSubmission, intervalMinutes, readings)
            || TryReadColumnarJson(element, inheritedTimestamp, inheritedSubmission, intervalMinutes, readings))
            return;

        var compositeTimestamp = ReadCompositeTimestamp(element, intervalMinutes);
        var explicitTimestamp = ReadTimestamp(element, TimestampNames);
        var timestamp = explicitTimestamp ?? compositeTimestamp ?? inheritedTimestamp;
        var submissionTimestamp = ReadTimestamp(element, SubmissionTimestampNames) ?? inheritedSubmission;
        var endTimestamp = ReadTimestamp(element, EndTimestampNames);
        var parameter = ReadString(element, ParameterNames);
        var unit = ReadString(element, UnitNames) ?? string.Empty;
        var explicitValue = ReadDecimal(element, ValueNames);
        if (!string.IsNullOrWhiteSpace(parameter) && explicitValue.HasValue)
            readings.Add(new(timestamp, endTimestamp, submissionTimestamp, parameter, explicitValue.Value, unit));
        else
        {
            var sensorValues = string.IsNullOrWhiteSpace(parameter) ? Array.Empty<decimal>() : element.EnumerateObject()
                .Where(property => IsMmfSensorField(property.Name) && TryDecimal(property.Value, out _))
                .Select(property => TryDecimal(property.Value, out var value) ? value : 0m).ToArray();
            if (sensorValues.Length > 0)
                readings.Add(new(timestamp, endTimestamp, submissionTimestamp, parameter!,
                    sensorValues.Average(), unit));
            else
                foreach (var property in element.EnumerateObject())
                    if (TryDecimal(property.Value, out var value)
                        && (LooksLikeMeasurement(property.Name)
                            || (explicitTimestamp.HasValue || compositeTimestamp.HasValue) && !IsMetadataField(property.Name)))
                        readings.Add(new(timestamp, endTimestamp, submissionTimestamp, property.Name, value, unit));
        }

        foreach (var property in element.EnumerateObject())
            if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
            {
                if (TryParseTimestamp(property.Name, out var keyedTimestamp))
                    WalkJson(property.Value, keyedTimestamp, submissionTimestamp, intervalMinutes, readings, inheritedParameter);
                else
                    WalkJson(property.Value, timestamp, submissionTimestamp, intervalMinutes, readings, property.Name);
            }
            else if (TryParseTimestamp(property.Name, out var propertyTimestamp)
                     && TryDecimal(property.Value, out var keyedValue))
                readings.Add(new(propertyTimestamp, null, submissionTimestamp,
                    inheritedParameter ?? "Value", keyedValue, unit));
    }

    private static bool TryReadColumnarJson(JsonElement element, DateTime fallback, DateTime inheritedSubmission,
        int intervalMinutes, List<ReadingCandidate> readings)
    {
        var columns = element.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.Array
                               && property.Value.EnumerateArray().All(item => item.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object)))
            .Select(property => new { property.Name, Values = property.Value.EnumerateArray().ToArray() })
            .Where(column => column.Values.Length > 0)
            .ToList();
        if (columns.Count < 2 || !columns.Any(column => IsTimestampField(column.Name) || IsCompositeTimeField(column.Name)))
            return false;

        var rowCount = columns.Max(column => column.Values.Length);
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in columns)
            {
                if (rowIndex >= column.Values.Length) continue;
                fields[column.Name] = JsonScalarText(column.Values[rowIndex]);
            }
            AddFields(fields, fallback.AddMinutes(rowIndex * Math.Max(1, intervalMinutes)), intervalMinutes,
                readings, inheritedSubmission);
        }
        return true;
    }

    private static bool TryReadMatrixJson(JsonElement element, DateTime fallback, DateTime inheritedSubmission,
        int intervalMinutes, List<ReadingCandidate> readings)
    {
        if (!TryGetProperty(element, "columns", out var columnsElement)
            && !TryGetProperty(element, "headers", out columnsElement)) return false;
        if (!TryGetProperty(element, "data", out var rowsElement)
            && !TryGetProperty(element, "rows", out rowsElement)) return false;
        if (columnsElement.ValueKind != JsonValueKind.Array || rowsElement.ValueKind != JsonValueKind.Array) return false;

        var headers = columnsElement.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : JsonScalarText(item))
            .ToArray();
        if (headers.Length == 0 || !headers.Any(header => IsTimestampField(header) || IsCompositeTimeField(header)))
            return false;

        var rowIndex = 0;
        foreach (var row in rowsElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array) return false;
            var values = row.EnumerateArray().ToArray();
            var fields = headers.Select((header, index) => new
                {
                    Header = header,
                    Value = index < values.Length ? JsonScalarText(values[index]) : string.Empty
                })
                .Where(field => !string.IsNullOrWhiteSpace(field.Header))
                .ToDictionary(field => field.Header, field => field.Value, StringComparer.OrdinalIgnoreCase);
            AddFields(fields, fallback.AddMinutes(rowIndex * Math.Max(1, intervalMinutes)), intervalMinutes,
                readings, inheritedSubmission);
            rowIndex++;
        }
        return rowIndex > 0;
    }

    private static void ReadXml(string payload, DateTime fallback, int intervalMinutes, List<ReadingCandidate> readings)
    {
        var document = XDocument.Parse(payload, LoadOptions.None);
        foreach (var row in document.Descendants().Where(element => element.Elements().Any()
                     && !element.Elements().Any(child => child.Elements().Any())))
        {
            var fields = row.Elements().GroupBy(item => item.Name.LocalName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);
            var inheritedTimestamp = ParseNamedTimestamp(fields, TimestampNames)
                ?? ReadAncestorTimestamp(row, TimestampNames)
                ?? fallback;
            var inheritedSubmission = ParseNamedTimestamp(fields, SubmissionTimestampNames)
                ?? ReadAncestorTimestamp(row, SubmissionTimestampNames)
                ?? fallback;
            var inheritedEndTimestamp = ParseNamedTimestamp(fields, EndTimestampNames)
                ?? ReadAncestorTimestamp(row, EndTimestampNames);
            AddFields(fields, inheritedTimestamp, intervalMinutes, readings, inheritedSubmission, inheritedEndTimestamp);
        }
    }

    private static DateTime? ReadAncestorTimestamp(XElement element, IEnumerable<string> names)
    {
        foreach (var ancestor in element.Ancestors())
        {
            var fields = ancestor.Elements().Where(child => !child.HasElements)
                .GroupBy(child => child.Name.LocalName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);
            var timestamp = ParseNamedTimestamp(fields, names);
            if (timestamp.HasValue) return timestamp;
        }
        return null;
    }

    private static void ReadDelimited(string payload, DateTime fallback, int intervalMinutes, List<ReadingCandidate> readings)
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
            AddFields(fields, fallback, intervalMinutes, readings);
        }
    }

    private static void AddFields(IReadOnlyDictionary<string, string> fields, DateTime fallback, int intervalMinutes,
        List<ReadingCandidate> readings, DateTime? inheritedSubmission = null, DateTime? fallbackEnd = null)
    {
        var compositeTimestamp = ParseCompositeTimestamp(fields, intervalMinutes);
        var explicitTimestamp = ParseNamedTimestamp(fields, TimestampNames);
        var timestamp = explicitTimestamp ?? compositeTimestamp ?? fallback;
        var submissionTimestamp = ParseNamedTimestamp(fields, SubmissionTimestampNames) ?? inheritedSubmission ?? fallback;
        var end = ParseNamedTimestamp(fields, EndTimestampNames) ?? fallbackEnd;
        var parameter = First(fields, ParameterNames);
        var unit = First(fields, UnitNames) ?? string.Empty;
        var explicitValue = ParseNamedDecimal(fields, ValueNames);
        if (!string.IsNullOrWhiteSpace(parameter) && explicitValue.HasValue)
            readings.Add(new(timestamp, end, submissionTimestamp, parameter, explicitValue.Value, unit));
        else
            foreach (var field in fields)
                if ((LooksLikeMeasurement(field.Key)
                     || (explicitTimestamp.HasValue || compositeTimestamp.HasValue) && !IsMetadataField(field.Key))
                    && decimal.TryParse(field.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                    readings.Add(new(timestamp, end, submissionTimestamp, field.Key, value, unit));
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
        if (TimestampNames.Concat(EndTimestampNames).Concat(SubmissionTimestampNames)
                .Any(item => item.Replace("_", string.Empty).Equals(normalized, StringComparison.OrdinalIgnoreCase))
            || normalized.EndsWith("id") || normalized.Contains("code") || normalized.Contains("status") || normalized.Contains("error")
            || normalized.Contains("count") || normalized.Contains("interval") || normalized.Contains("latitude") || normalized.Contains("longitude")) return false;
        return normalized.Contains("value") || normalized.Contains("power") || normalized.Contains("production") || normalized.Contains("forecast")
            || normalized.Contains("ghi") || normalized.Contains("poa") || normalized.Contains("temp") || normalized.Contains("pressure")
            || normalized.Contains("humidity") || normalized.Contains("wind") || normalized.Contains("irradiance")
            || normalized.Contains("precipitation") || normalized.Contains("energy") || normalized.Contains("generation");
    }

    private static bool IsMetadataField(string name)
    {
        var normalized = name.ToLowerInvariant().Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty);
        return normalized is "year" or "month" or "day" or "period"
            || TimestampNames.Concat(EndTimestampNames).Concat(SubmissionTimestampNames)
                .Any(item => item.Replace("_", string.Empty).ToLowerInvariant() == normalized)
            || normalized.EndsWith("id") || normalized.Contains("code") || normalized.Contains("status")
            || normalized.Contains("error") || normalized.Contains("count") || normalized.Contains("interval")
            || normalized.Contains("latitude") || normalized.Contains("longitude");
    }

    private static DateTime? ParseCompositeTimestamp(IReadOnlyDictionary<string, string> fields, int intervalMinutes)
    {
        if (!int.TryParse(First(fields, ["year"]), out var year)
            || !int.TryParse(First(fields, ["month"]), out var month)
            || !int.TryParse(First(fields, ["day"]), out var day)
            || !int.TryParse(First(fields, ["period"]), out var period)
            || period < 1) return null;
        try { return new DateTime(year, month, day).AddMinutes((period - 1) * Math.Max(1, intervalMinutes)); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static DateTime? ReadCompositeTimestamp(JsonElement element, int intervalMinutes)
    {
        if (!ReadInt32(element, "year", out var year)
            || !ReadInt32(element, "month", out var month)
            || !ReadInt32(element, "day", out var day)
            || !ReadInt32(element, "period", out var period)
            || period < 1) return null;
        try { return new DateTime(year, month, day).AddMinutes((period - 1) * Math.Max(1, intervalMinutes)); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static bool ReadInt32(JsonElement element, string name, out int value)
    {
        value = 0;
        return TryGetProperty(element, name, out var property)
            && ((property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value))
                || property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out value));
    }

    private static int DetectSourceIntervalMinutes(IReadOnlyList<ReadingCandidate> candidates, int fallback)
    {
        var timestamps = candidates.Select(item => item.Timestamp).Distinct().OrderBy(value => value).ToList();
        var intervals = timestamps.Zip(timestamps.Skip(1), (left, right) => (int)(right - left).TotalMinutes)
            .Where(minutes => minutes is > 0 and <= 1440)
            .GroupBy(minutes => minutes)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => group.Key)
            .ToList();
        return intervals.FirstOrDefault(Math.Max(1, fallback));
    }

    private static DateTime? ParseNamedTimestamp(IReadOnlyDictionary<string, string> fields, IEnumerable<string> names)
    {
        var value = First(fields, names);
        return TryParseTimestamp(value, out var timestamp) ? timestamp : null;
    }
    private static decimal? ParseNamedDecimal(IReadOnlyDictionary<string, string> fields, IEnumerable<string> names)
    {
        var value = First(fields, names);
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number) ? number : null;
    }
    private static string? First(IReadOnlyDictionary<string, string> fields, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (fields.TryGetValue(name, out var value)) return value;
            var normalizedName = NormalizeFieldKey(name);
            var match = fields.FirstOrDefault(field => NormalizeFieldKey(field.Key) == normalizedName);
            if (!string.IsNullOrEmpty(match.Key)) return match.Value;
        }
        return null;
    }
    private static DateTime? ReadTimestamp(JsonElement element, IEnumerable<string> names)
    {
        foreach (var name in names)
            if (TryGetProperty(element, name, out var property) && property.ValueKind == JsonValueKind.String
                && TryParseTimestamp(property.GetString(), out var timestamp)) return timestamp;
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
        var normalizedName = NormalizeFieldKey(name);
        foreach (var property in element.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                || NormalizeFieldKey(property.Name) == normalizedName)
            { result = property.Value; return true; }
        result = default; return false;
    }
    private static bool TryParseTimestamp(string? value, out DateTime timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string[] formats = ["dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "dd/MM/yyyy", "d/M/yyyy HH:mm:ss", "d/M/yyyy HH:mm", "d/M/yyyy"];
        if (value.Contains('/') && DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out timestamp))
            return true;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var parsed))
        {
            timestamp = parsed.LocalDateTime;
            return true;
        }
        return DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out timestamp);
    }
    private static bool IsTimestampField(string name)
    {
        var normalized = NormalizeFieldKey(name);
        return TimestampNames.Concat(SubmissionTimestampNames).Concat(EndTimestampNames)
            .Any(candidate => NormalizeFieldKey(candidate) == normalized);
    }
    private static bool IsCompositeTimeField(string name)
    {
        var normalized = NormalizeFieldKey(name);
        return normalized is "year" or "month" or "day" or "period";
    }
    private static bool IsMmfSensorField(string name)
    {
        var normalized = NormalizeFieldKey(name);
        return normalized.Length > 3 && normalized.StartsWith("mmf", StringComparison.Ordinal)
            && normalized[3..].All(char.IsDigit);
    }
    private static string JsonScalarText(JsonElement element) => element.ValueKind == JsonValueKind.String
        ? element.GetString() ?? string.Empty
        : element.GetRawText();
    private static string NormalizeFieldKey(string value) => new(value.Where(char.IsLetterOrDigit)
        .Select(char.ToLowerInvariant).ToArray());
    private static string NormalizeParameter(string value) => string.IsNullOrWhiteSpace(value) ? "Value" : value.Trim();
    private static DateTime AlignToInterval(DateTime value, int intervalMinutes)
    {
        var safeInterval = Math.Max(1, intervalMinutes);
        var alignedMinute = value.Minute / safeInterval * safeInterval;
        return new DateTime(value.Year, value.Month, value.Day, value.Hour, alignedMinute, 0, value.Kind);
    }
    private sealed record ReadingCandidate(DateTime Timestamp, DateTime? EndTimestamp, DateTime SubmissionDateTime,
        string Parameter, decimal Value, string Unit);
}

public sealed record TranslationResult(IReadOnlyList<SolarApiReading> Readings, IReadOnlyList<TranslationIssue> Issues, string PayloadFormat);
public sealed record TranslationIssue(string RuleCode, string FieldName, string? OriginalValue, string Reason);
