using System.Globalization;
using TestingSystem.Web.Database.Entities;

namespace TestingSystem.Web.Services;

public sealed record MmfSensorQualityResult(
    IReadOnlyList<decimal> ValidValues,
    IReadOnlyList<TranslationIssue> Issues);

/// <summary>
/// Applies only administrator-configured range rules before sensor readings are averaged.
/// Parameter names and their limits come from API readings and user configuration; the
/// application does not assume sensor-specific physical ranges.
/// </summary>
public static class MmfSensorQualityPolicy
{
    public static MmfSensorQualityResult Evaluate(string parameter, DateTime timestamp,
        IReadOnlyList<decimal> values, IReadOnlyCollection<ValidationRule>? rules = null)
    {
        var valid = new List<decimal>();
        var issues = new List<TranslationIssue>();
        var applicableRules = rules?.Where(rule => rule.IsActive
            && !rule.RuleSource.Equals("Fixed", StringComparison.OrdinalIgnoreCase)
            && rule.DataType.Equals("MMF", StringComparison.OrdinalIgnoreCase)
            && (IsAll(rule.FieldName)
                || rule.FieldName.Equals(parameter, StringComparison.OrdinalIgnoreCase)))
            .ToList() ?? [];

        foreach (var value in values)
        {
            var failure = applicableRules.Select(rule => CheckRange(rule, value)).FirstOrDefault(result => result is not null);
            if (failure is null) valid.Add(value);
            else issues.Add(new(failure.Value.Code, parameter,
                value.ToString(CultureInfo.InvariantCulture), failure.Value.Reason));
        }
        return new(valid, issues);
    }

    private static (string Code, string Reason)? CheckRange(ValidationRule rule, decimal value)
    {
        if (rule.MinimumValue.HasValue && value < rule.MinimumValue.Value)
            return (rule.Code, $"Value is below the configured minimum of {rule.MinimumValue.Value.ToString(CultureInfo.InvariantCulture)}.");
        if (rule.MaximumValue.HasValue && value > rule.MaximumValue.Value)
            return (rule.Code, $"Value exceeds the configured maximum of {rule.MaximumValue.Value.ToString(CultureInfo.InvariantCulture)}.");
        return null;
    }

    private static bool IsAll(string field) => string.IsNullOrWhiteSpace(field)
        || field.Trim() is "*" or "All";
}
