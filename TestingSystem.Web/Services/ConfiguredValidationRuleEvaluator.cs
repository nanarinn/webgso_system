using System.Globalization;
using TestingSystem.Web.Database.Entities;

namespace TestingSystem.Web.Services;

public static class ConfiguredValidationRuleEvaluator
{
    public static IReadOnlyList<TranslationIssue> Evaluate(IReadOnlyCollection<ValidationRule> rules,
        IReadOnlyList<SolarApiReading> readings, string apiType)
    {
        var issues = new List<TranslationIssue>();
        foreach (var rule in rules.Where(rule => rule.IsActive
            && !rule.RuleSource.Equals("Fixed", StringComparison.OrdinalIgnoreCase)
            && rule.DataType.Equals(apiType, StringComparison.OrdinalIgnoreCase)))
        {
            var matched = readings.Where(reading => Matches(rule.FieldName, reading.Parameter))
                .OrderBy(reading => reading.IntervalStart).ToList();
            foreach (var reading in matched)
            {
                if (rule.MinimumValue.HasValue && reading.Value < rule.MinimumValue.Value)
                    issues.Add(Issue(rule, reading, $"Value is below the configured minimum of {rule.MinimumValue.Value.ToString(CultureInfo.InvariantCulture)}."));
                if (rule.MaximumValue.HasValue && reading.Value > rule.MaximumValue.Value)
                    issues.Add(Issue(rule, reading, $"Value exceeds the configured maximum of {rule.MaximumValue.Value.ToString(CultureInfo.InvariantCulture)}."));
            }
            foreach (var group in matched.GroupBy(reading => reading.Parameter, StringComparer.OrdinalIgnoreCase))
            {
                var ordered = group.OrderBy(reading => reading.IntervalStart).ToList();
                if (rule.ChangeThreshold.HasValue)
                    for (var index = 1; index < ordered.Count; index++)
                    {
                        var change = Math.Abs(ordered[index].Value - ordered[index - 1].Value);
                        if (change > rule.ChangeThreshold.Value)
                            issues.Add(Issue(rule, ordered[index], $"Change of {change.ToString(CultureInfo.InvariantCulture)} exceeds the configured hysteresis limit of {rule.ChangeThreshold.Value.ToString(CultureInfo.InvariantCulture)}."));
                    }
                if (rule.CheckMissing) AddMissingIssues(rule, ordered, issues);
            }
            if (rule.CheckMissing && matched.Count == 0 && !IsAll(rule.FieldName))
                issues.Add(new(rule.Code, rule.FieldName, null, $"No value was returned for configured parameter '{rule.FieldName}'."));
        }
        return issues;
    }

    private static void AddMissingIssues(ValidationRule rule, List<SolarApiReading> ordered,
        List<TranslationIssue> issues)
    {
        if (ordered.Count < 2) return;
        var interval = ordered.Select(x => x.IntervalMinutes ?? 0).FirstOrDefault(x => x > 0);
        if (interval <= 0)
            interval = ordered.Zip(ordered.Skip(1), (a, b) => (int)(b.IntervalStart - a.IntervalStart).TotalMinutes)
                .Where(value => value > 0).DefaultIfEmpty(0).Min();
        if (interval <= 0) return;
        for (var index = 1; index < ordered.Count; index++)
        {
            var gap = (int)(ordered[index].IntervalStart - ordered[index - 1].IntervalStart).TotalMinutes;
            if (gap > interval)
                issues.Add(new(rule.Code, ordered[index].Parameter, null,
                    $"Missing value(s) detected between {ordered[index - 1].IntervalStart:dd MMM yyyy HH:mm} and {ordered[index].IntervalStart:dd MMM yyyy HH:mm}; expected every {interval} minute(s)."));
        }
    }

    private static TranslationIssue Issue(ValidationRule rule, SolarApiReading reading, string reason) =>
        new(rule.Code, reading.Parameter, reading.Value.ToString(CultureInfo.InvariantCulture), reason);
    private static bool Matches(string field, string parameter) => IsAll(field)
        || field.Trim().Equals(parameter.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool IsAll(string field) => string.IsNullOrWhiteSpace(field) || field.Trim() is "*" or "All";
}
