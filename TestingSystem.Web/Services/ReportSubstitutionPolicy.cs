using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Models;

namespace TestingSystem.Web.Services;

public sealed record ReportSubstitutionSummary(
    int CurrentValues,
    int PreviousDayValues,
    int ZeroFallbackValues,
    int CapacityFallbackValues)
{
    public int TotalSubstituted => PreviousDayValues + ZeroFallbackValues + CapacityFallbackValues;
    public string Description =>
        $"{TotalSubstituted:N0} substitution(s): {PreviousDayValues:N0} previous-day, "
        + $"{ZeroFallbackValues:N0} zero fallback, {CapacityFallbackValues:N0} capacity fallback.";
}

public sealed record PreparedReportReadings(
    IReadOnlyList<SolarApiReading> Readings,
    ReportSubstitutionSummary Summary,
    IReadOnlyList<ReportSubstitutionDetail> Details);

public sealed record ReportSubstitutionDetail(
    int PlantId,
    string Parameter,
    DateTime DataTimestamp,
    decimal? OriginalValue,
    decimal ReportValue,
    string RuleCode,
    string RuleName,
    string AnomalyDetail,
    string SubstitutionSource,
    string SourceReference);

internal sealed record ValidationFailure(string RuleCode, string RuleName, string Reason);

public static class ReportSubstitutionPolicy
{
    public static PreparedReportReadings PrepareDdq(string dataType, DateTime submissionDate,
        IReadOnlyList<LssPlant> plants, IReadOnlyList<SolarApiReading> current,
        IReadOnlyList<SolarApiReading> previousDay,
        IReadOnlyCollection<ValidationRule>? validationRules = null)
    {
        var profile = DdqDatasetPolicy.Get(dataType);
        var interval = profile.ReferenceTemplateIntervalMinutes;
        var latest = Latest(current.Where(item => item.DataType == profile.DataType), false);
        var previous = Latest(previousDay.Where(item => item.DataType == profile.DataType), false)
            .ToDictionary(item => (item.PlantId, item.IntervalStart));
        var timestamps = ExpectedDdqTimestamps(profile.DataType, submissionDate, interval, latest);
        var currentIndex = latest.ToDictionary(item => (item.PlantId, item.IntervalStart));
        var output = new List<SolarApiReading>();
        var currentCount = 0;
        var previousCount = 0;
        var zeroCount = 0;
        var capacityCount = 0;
        var details = new List<ReportSubstitutionDetail>();

        foreach (var plant in plants)
            foreach (var timestamp in timestamps)
            {
                currentIndex.TryGetValue((plant.Id, timestamp), out var source);
                var sourceProblem = DdqProblem(source, plant.CapacityMw, validationRules, latest);
                if (sourceProblem is null)
                {
                    output.Add(source!);
                    currentCount++;
                    continue;
                }
                previous.TryGetValue((plant.Id, timestamp), out var fallback);
                if (DdqProblem(fallback, plant.CapacityMw, validationRules, previousDay) is null)
                {
                    var reason = $"Report substitution: previous-day submission used because {sourceProblem.Reason}.";
                    output.Add(Synthetic(fallback!, submissionDate, timestamp, reason));
                    details.Add(Detail(plant.Id, fallback!.Parameter, timestamp, source?.Value, fallback.Value,
                        sourceProblem, "Previous-day reading", ReadingReference(fallback)));
                    previousCount++;
                }
                else if (source is not null && plant.CapacityMw > 0 && source.Value > plant.CapacityMw)
                {
                    const string reason = "Report substitution: value above registered plant capacity was capped.";
                    output.Add(Synthetic(source, submissionDate, timestamp, reason, plant.CapacityMw));
                    details.Add(Detail(plant.Id, source.Parameter, timestamp, source.Value, plant.CapacityMw,
                        sourceProblem, "Plant configuration", $"Registered capacity: {plant.CapacityMw:0.######} MW"));
                    capacityCount++;
                }
                else
                {
                    const string reason = "Report substitution: previous-day value was unavailable; zero fallback used.";
                    output.Add(Synthetic(source, submissionDate, timestamp, reason, 0m,
                        plant.Id, profile.DataType, interval));
                    details.Add(Detail(plant.Id, source?.Parameter ?? "Value", timestamp, source?.Value, 0m,
                        sourceProblem, "Zero fallback", "No valid previous-day reading was available."));
                    zeroCount++;
                }
            }
        return new(output, new(currentCount, previousCount, zeroCount, capacityCount), details);
    }

    public static PreparedReportReadings PrepareMmf(DateTime submissionDate, int intervalMinutes,
        IReadOnlyList<LssPlant> plants, IReadOnlyList<SolarApiReading> current,
        IReadOnlyList<SolarApiReading> previousDay,
        IReadOnlyCollection<ValidationRule>? validationRules = null)
    {
        var safeInterval = Math.Max(1, intervalMinutes);
        var latest = Latest(current.Where(item => item.DataType == "MMF"), true);
        var previous = Latest(previousDay.Where(item => item.DataType == "MMF"), true)
            .ToDictionary(item => (item.PlantId, item.Parameter.ToUpperInvariant(),
                TimeOnly.FromDateTime(item.IntervalStart)));
        var parameters = latest.Select(item => item.Parameter)
            .Concat(previousDay.Select(item => item.Parameter))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList();
        var currentIndex = latest.ToDictionary(item => (item.PlantId,
            item.Parameter.ToUpperInvariant(), item.IntervalStart));
        var output = new List<SolarApiReading>();
        var currentCount = 0;
        var previousCount = 0;
        var zeroCount = 0;
        var details = new List<ReportSubstitutionDetail>();
        var start = submissionDate.Date;
        var timestamps = Enumerable.Range(0, 24 * 60 / safeInterval)
            .Select(index => start.AddMinutes(index * safeInterval)).ToList();

        foreach (var parameter in parameters)
            foreach (var plant in plants)
                foreach (var timestamp in timestamps)
                {
                    currentIndex.TryGetValue((plant.Id, parameter.ToUpperInvariant(), timestamp), out var source);
                    var sourceProblem = MmfProblem(source, parameter, validationRules, latest);
                    if (sourceProblem is null)
                    {
                        output.Add(source!);
                        currentCount++;
                        continue;
                    }
                    if (previous.TryGetValue((plant.Id, parameter.ToUpperInvariant(),
                        TimeOnly.FromDateTime(timestamp)), out var fallback)
                        && MmfProblem(fallback, parameter, validationRules, previousDay) is null)
                    {
                        var reason = $"Report substitution: previous-day MMF average used because {sourceProblem.Reason}.";
                        output.Add(Synthetic(fallback, submissionDate, timestamp, reason));
                        details.Add(Detail(plant.Id, parameter, timestamp, source?.Value, fallback.Value,
                            sourceProblem, "Previous-day MMF reading", ReadingReference(fallback)));
                        previousCount++;
                    }
                    else
                    {
                        const string reason = "Report substitution: no valid current or previous-day MMF average; zero fallback used.";
                        output.Add(Synthetic(null, submissionDate, timestamp, reason,
                            0m, plant.Id, "MMF", safeInterval, parameter,
                            latest.FirstOrDefault(item => item.Parameter.Equals(parameter,
                                StringComparison.OrdinalIgnoreCase))?.Unit ?? string.Empty));
                        details.Add(Detail(plant.Id, parameter, timestamp, source?.Value, 0m,
                            sourceProblem, "Zero fallback", "No valid current or previous-day MMF reading was available."));
                        zeroCount++;
                    }
                }
        return new(output, new(currentCount, previousCount, zeroCount, 0), details);
    }

    public static PreparedReportReadings PrepareMmfRange(DateTime start, DateTime end, int intervalMinutes,
        IReadOnlyList<LssPlant> plants, IReadOnlyList<SolarApiReading> current,
        IReadOnlyList<SolarApiReading> previousDay, IReadOnlyCollection<ValidationRule>? validationRules = null)
    {
        var readings = new List<SolarApiReading>();
        var details = new List<ReportSubstitutionDetail>();
        var currentCount = 0;
        var previousCount = 0;
        var zeroCount = 0;
        var capacityCount = 0;
        for (var day = start.Date; day <= end.Date; day = day.AddDays(1))
        {
            var dayCurrent = current.Where(item => item.IntervalStart.Date == day).ToList();
            var previousDate = day.AddDays(-1);
            var dayPrevious = previousDay.Where(item => item.IntervalStart.Date == previousDate).ToList();
            var prepared = PrepareMmf(day, intervalMinutes, plants, dayCurrent, dayPrevious, validationRules);
            readings.AddRange(prepared.Readings.Where(item => item.IntervalStart >= start && item.IntervalStart <= end));
            details.AddRange(prepared.Details.Where(item => item.DataTimestamp >= start && item.DataTimestamp <= end));
            currentCount += prepared.Readings.Count(item => !item.IsSynthetic && item.IntervalStart >= start && item.IntervalStart <= end);
            previousCount += prepared.Details.Count(item => item.SubstitutionSource == "Previous-day MMF reading"
                && item.DataTimestamp >= start && item.DataTimestamp <= end);
            zeroCount += prepared.Details.Count(item => item.SubstitutionSource == "Zero fallback"
                && item.DataTimestamp >= start && item.DataTimestamp <= end);
            capacityCount += prepared.Details.Count(item => item.SubstitutionSource == "Plant configuration"
                && item.DataTimestamp >= start && item.DataTimestamp <= end);
        }
        return new(readings, new(currentCount, previousCount, zeroCount, capacityCount), details);
    }

    private static List<SolarApiReading> Latest(IEnumerable<SolarApiReading> readings, bool includeParameter) =>
        readings.GroupBy(item => new
            {
                item.PlantId,
                Parameter = includeParameter ? item.Parameter.ToUpperInvariant() : string.Empty,
                item.IntervalStart
            })
            .Select(group => group.OrderByDescending(item => item.Id).First()).ToList();

    private static List<DateTime> ExpectedDdqTimestamps(string dataType, DateTime submissionDate,
        int interval, IReadOnlyList<SolarApiReading> current)
    {
        // A submission identifies the collection run. The forecast timestamps themselves
        // belong to the API payload and can start on a different date. Build the report grid
        // from the timestamps actually returned for every DDQ dataset so valid forecasts are
        // not discarded and replaced with zero merely because their dates differ from the
        // collection-schedule submission date.
        if (current.Count > 0)
        {
            var start = current.Min(item => item.IntervalStart);
            var end = current.Max(item => item.IntervalStart).AddMinutes(interval);
            return Range(start, end, interval);
        }
        var window = DdqDatasetPolicy.ReferenceWindow(dataType, submissionDate);
        return Range(window.Start, window.EndExclusive, interval);
    }

    private static List<DateTime> Range(DateTime start, DateTime endExclusive, int interval)
    {
        var result = new List<DateTime>();
        for (var timestamp = start; timestamp < endExclusive; timestamp = timestamp.AddMinutes(interval))
            result.Add(timestamp);
        return result;
    }

    private static ValidationFailure? DdqProblem(SolarApiReading? reading, decimal capacity,
        IReadOnlyCollection<ValidationRule>? rules, IReadOnlyList<SolarApiReading> context)
    {
        if (reading is null) return MissingFailure("DDQ", "Value", rules, "the current value was missing");
        var negativeRule = Rule("DDQ-004", rules);
        var minimum = negativeRule?.MinimumValue ?? 0m;
        if (reading.Value < minimum && Enabled("DDQ-004", rules))
            return Failure(negativeRule, "DDQ-004", "Negative or below-minimum value",
                $"the current value was below the administrator minimum of {minimum}");
        if (capacity > 0 && reading.Value > capacity && Enabled("DDQ-005", rules))
            return Failure(Rule("DDQ-005", rules), "DDQ-005", "Value above plant capacity",
                "the current value was above plant capacity");
        var rampRule = Rule("DDQ-007", rules);
        if (rampRule?.ChangeThreshold is decimal rampThreshold)
        {
            var previous = PreviousReading(reading, context);
            var allowedChange = capacity > 0 ? capacity * rampThreshold / 100m : rampThreshold;
            if (previous is not null && Math.Abs(reading.Value - previous.Value) > allowedChange)
                return Failure(rampRule, "DDQ-007", "Forecast ramp exceeded",
                    $"the forecast change exceeded the administrator rate of {rampThreshold}% of capacity");
        }
        return ConfiguredProblem(reading, "DDQ", rules, context);
    }

    private static ValidationFailure? MmfProblem(SolarApiReading? reading, string parameter,
        IReadOnlyCollection<ValidationRule>? rules, IReadOnlyList<SolarApiReading> context)
    {
        if (reading is null) return MissingFailure("MMF", parameter, rules,
            "no valid current MMF average was available");
        var fixedResult = MmfSensorQualityPolicy.Evaluate(reading.Parameter, reading.IntervalStart,
            [reading.Value], rules);
        if (fixedResult.ValidValues.Count == 0)
        {
            var issue = fixedResult.Issues.FirstOrDefault();
            return new(issue?.RuleCode ?? "MMF-VALIDATION", Rule(issue?.RuleCode ?? "", rules)?.Name ?? "MMF validation",
                issue?.Reason ?? "the current MMF value failed validation");
        }
        return ConfiguredProblem(reading, "MMF", rules, context);
    }

    private static ValidationFailure? ConfiguredProblem(SolarApiReading reading, string apiType,
        IReadOnlyCollection<ValidationRule>? rules, IReadOnlyList<SolarApiReading> context)
    {
        if (rules is null) return null;
        foreach (var rule in rules.Where(rule => rule.IsActive
            && !rule.RuleSource.Equals("Fixed", StringComparison.OrdinalIgnoreCase)
            && rule.DataType.Equals(apiType, StringComparison.OrdinalIgnoreCase)
            && (rule.FieldName is "*" or "All"
                || rule.FieldName.Equals(reading.Parameter, StringComparison.OrdinalIgnoreCase))))
        {
            if (rule.MinimumValue.HasValue && reading.Value < rule.MinimumValue.Value)
                return new(rule.Code, rule.Name, $"the value was below configured minimum {rule.MinimumValue.Value}");
            if (rule.MaximumValue.HasValue && reading.Value > rule.MaximumValue.Value)
                return new(rule.Code, rule.Name, $"the value exceeded configured maximum {rule.MaximumValue.Value}");
            if (rule.ChangeThreshold.HasValue)
            {
                var previous = context.Where(item => item.PlantId == reading.PlantId
                        && item.Parameter.Equals(reading.Parameter, StringComparison.OrdinalIgnoreCase)
                        && item.IntervalStart < reading.IntervalStart)
                    .OrderByDescending(item => item.IntervalStart).FirstOrDefault();
                if (previous is not null && Math.Abs(reading.Value - previous.Value) > rule.ChangeThreshold.Value)
                    return new(rule.Code, rule.Name, $"the value change exceeded configured hysteresis {rule.ChangeThreshold.Value}");
            }
        }
        return null;
    }

    private static bool Enabled(string code, IReadOnlyCollection<ValidationRule>? rules) =>
        rules is null || rules.Any(rule => rule.Code.Equals(code, StringComparison.OrdinalIgnoreCase) && rule.IsActive);
    private static ValidationRule? Rule(string code, IReadOnlyCollection<ValidationRule>? rules) =>
        rules?.FirstOrDefault(rule => rule.Code.Equals(code, StringComparison.OrdinalIgnoreCase) && rule.IsActive);
    private static SolarApiReading? PreviousReading(SolarApiReading reading,
        IReadOnlyList<SolarApiReading> context) => context.Where(item => item.PlantId == reading.PlantId
            && item.Parameter.Equals(reading.Parameter, StringComparison.OrdinalIgnoreCase)
            && item.IntervalStart < reading.IntervalStart)
        .OrderByDescending(item => item.IntervalStart).FirstOrDefault();

    private static ValidationFailure Failure(ValidationRule? rule, string fallbackCode, string fallbackName, string reason) =>
        new(rule?.Code ?? fallbackCode, rule?.Name ?? fallbackName, reason);

    private static ValidationFailure MissingFailure(string apiType, string parameter,
        IReadOnlyCollection<ValidationRule>? rules, string reason)
    {
        var rule = rules?.FirstOrDefault(item => item.IsActive && item.CheckMissing
            && !item.RuleSource.Equals("Fixed", StringComparison.OrdinalIgnoreCase)
            && item.DataType.Equals(apiType, StringComparison.OrdinalIgnoreCase)
            && (item.FieldName is "*" or "All"
                || item.FieldName.Equals(parameter, StringComparison.OrdinalIgnoreCase)));
        return new(rule?.Code ?? "SYSTEM-MISSING", rule?.Name ?? "Missing reading", reason);
    }

    private static ReportSubstitutionDetail Detail(int plantId, string parameter, DateTime timestamp,
        decimal? originalValue, decimal reportValue, ValidationFailure failure, string source, string reference) =>
        new(plantId, parameter, timestamp, originalValue, reportValue, failure.RuleCode, failure.RuleName,
            failure.Reason, source, reference);

    private static string ReadingReference(SolarApiReading reading) =>
        $"Reading ID {reading.Id}; submission {reading.SubmissionDateTime:dd/MM/yyyy HH:mm:ss}; data timestamp {reading.IntervalStart:dd/MM/yyyy HH:mm:ss}.";

    private static SolarApiReading Synthetic(SolarApiReading? source, DateTime submissionDate,
        DateTime timestamp, string reason, decimal? value = null, int? plantId = null,
        string? dataType = null, int? interval = null, string? parameter = null, string? unit = null) => new()
    {
        PlantId = plantId ?? source?.PlantId ?? 0,
        DataType = dataType ?? source?.DataType ?? string.Empty,
        SubmissionDateTime = submissionDate,
        IntervalStart = timestamp,
        IntervalEnd = timestamp.AddMinutes(interval ?? source?.IntervalMinutes ?? 15),
        IntervalMinutes = interval ?? source?.IntervalMinutes,
        Parameter = parameter ?? source?.Parameter ?? "Value",
        Value = value ?? source?.Value ?? 0m,
        Unit = unit ?? source?.Unit ?? string.Empty,
        ErrorCode = source?.ErrorCode,
        ErrorMessage = source?.ErrorMessage,
        IsSynthetic = true,
        SyntheticReason = reason
    };
}
