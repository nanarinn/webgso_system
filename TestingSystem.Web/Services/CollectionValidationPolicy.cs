using System.Globalization;
using TestingSystem.Web.Database.Entities;

namespace TestingSystem.Web.Services;

public static class CollectionValidationPolicy
{
    public static List<TranslationIssue> Evaluate(IReadOnlyCollection<ValidationRule> rules,
        IReadOnlyList<SolarApiReading> readings, string apiType, decimal capacityMw,
        IReadOnlyCollection<TranslationIssue> translatedIssues)
    {
        var activeCodes = rules.Where(rule => rule.IsActive)
            .Select(rule => rule.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var issues = translatedIssues.Where(issue => activeCodes.Contains(issue.RuleCode)).ToList();
        if (apiType.Equals("DDQ", StringComparison.OrdinalIgnoreCase))
            foreach (var reading in readings)
            {
                if (reading.Value < 0 && activeCodes.Contains("DDQ-004"))
                    issues.Add(new("DDQ-004", reading.Parameter,
                        reading.Value.ToString(CultureInfo.InvariantCulture),
                        "A negative forecast value was received."));
                if (capacityMw > 0 && reading.Value > capacityMw && activeCodes.Contains("DDQ-005"))
                    issues.Add(new("DDQ-005", reading.Parameter,
                        reading.Value.ToString(CultureInfo.InvariantCulture),
                        $"The forecast exceeds the configured plant capacity of {capacityMw:N2} MW."));
            }
        issues.AddRange(ConfiguredValidationRuleEvaluator.Evaluate(rules, readings, apiType));
        return issues.DistinctBy(issue =>
            (issue.RuleCode, issue.FieldName, issue.OriginalValue, issue.Reason)).ToList();
    }
}
