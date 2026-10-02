using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Database.Entities;

namespace TestingSystem.Web.Services;

public sealed record FixedValidationRule(string Code, string ApiType, string Name, string AppliesTo,
    string Description, string Condition, string Severity);

public static class ValidationRuleCatalog
{
    public const string Fixed = "Fixed";
    public const string Managed = "Managed";
    public const string Configured = "Configured";

    public static readonly IReadOnlyList<FixedValidationRule> Definitions =
    [
        new("DDQ-001", "DDQ", "Missing interval reading", "All DDQ data types", "Every plant must contain a forecast value for each expected timestamp in the selected interval.", "Missing or null value", "Error"),
        new("DDQ-002", "DDQ", "Duplicate forecast reading", "Submission date & time + plant + data type + timestamp", "Only one forecast reading is allowed for the same submission date and time, plant, data type and data timestamp.", "Duplicate submission datetime composite key", "Error"),
        new("DDQ-003", "DDQ", "Invalid timestamp interval", "Data timestamp", "Forecast timestamps must align with the configured data interval.", "Timestamp not on interval", "Error"),
        new("DDQ-005", "DDQ", "Above plant capacity", "Forecast value", "A forecast value cannot be greater than the configured capacity of its plant.", "Value > plant capacity", "Error"),
        new("DDQ-006", "DDQ", "Unexpected night generation", "Forecast value", "A material solar generation value during expected night hours may indicate an incorrect timestamp or forecast.", "Night value > 2% capacity", "Error"),
        new("DDQ-008", "DDQ", "Flat forecast profile", "Consecutive forecast values", "An unchanged non-zero forecast over a long window can indicate copied or stale data.", "Same value for 8 intervals", "Error"),
        new("DDQ-009", "DDQ", "Missing API timestamps", "Rolling 24 Hour, Day Ahead, Week Ahead, 4 Month Ahead", "Timestamps must remain continuous at the source interval detected from the API response.", "One or more intervals missing inside the supplied range", "Error"),
        new("DDQ-010", "DDQ", "Invalid data type or unit", "Data type and unit", "The data type must be supported and forecast power must use the expected MW unit.", "Unsupported type or unit", "Error"),
        new("DDQ-011", "DDQ", "Submission timestamp mismatch", "Submission date and forecast horizon", "Forecast timestamps must fall within the valid horizon for the submission date and selected DDQ data type.", "Timestamp outside horizon", "Error"),
        new("DDQ-012", "DDQ", "Stale or future submission", "Submission datetime", "A submission cannot be unreasonably old or dated ahead of the system time.", "Outside submission window", "Error"),
        new("MMF-001", "MMF", "Missing interval reading", "All MMF parameters", "Every configured parameter must contain a reading at each source interval.", "Missing or null value", "Error"),
        new("MMF-002", "MMF", "Duplicate sensor reading", "Submission date & time + plant + parameter + timestamp", "Only one averaged reading is allowed for the same submission date and time, plant, parameter and data timestamp.", "Duplicate submission datetime composite key", "Error"),
        new("MMF-003", "MMF", "Invalid timestamp interval", "Data timestamp", "MMF timestamps must align with the detected source interval.", "Timestamp not on source interval", "Error"),
        new("MMF-016", "MMF", "Stale or future measurement", "Data timestamp", "Measurements cannot be unreasonably old or dated ahead of the collection time.", "Timestamp outside collection window", "Error")
    ];

    public static async Task SynchronizeAsync(ApplicationDbContext database, CancellationToken cancellationToken = default)
    {
        var stored = await database.ValidationRules.ToListAsync(cancellationToken);
        // Threshold and change-rate checks are created by administrators.
        // Remove old preset configurable rules without touching structural or user-created rules.
        var retiredPresetRuleCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DDQ-004", "DDQ-007",
            "MMF-004", "MMF-004A", "MMF-004B", "MMF-005", "MMF-006", "MMF-006A", "MMF-006B", "MMF-007",
            "MMF-008", "MMF-009", "MMF-010", "MMF-011", "MMF-012", "MMF-013", "MMF-014", "MMF-015"
        };
        database.ValidationRules.RemoveRange(stored.Where(rule => retiredPresetRuleCodes.Contains(rule.Code)
            && (rule.RuleSource.Equals(Managed, StringComparison.OrdinalIgnoreCase)
                || rule.RuleSource.Equals(Fixed, StringComparison.OrdinalIgnoreCase))));
        stored.RemoveAll(rule => retiredPresetRuleCodes.Contains(rule.Code)
            && (rule.RuleSource.Equals(Managed, StringComparison.OrdinalIgnoreCase)
                || rule.RuleSource.Equals(Fixed, StringComparison.OrdinalIgnoreCase)));
        foreach (var definition in Definitions)
        {
            var rule = stored.FirstOrDefault(x => x.Code.Equals(definition.Code, StringComparison.OrdinalIgnoreCase));
            var isNew = rule is null;
            if (rule is null)
            {
                rule = new ValidationRule { Code = definition.Code, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
                database.ValidationRules.Add(rule);
            }
            rule.DataType = definition.ApiType;
            rule.RuleSource = Fixed;
            if (isNew || rule.RuleSource == Fixed)
            {
                rule.Name = definition.Name;
                rule.FieldName = definition.AppliesTo;
                rule.Description = definition.Description;
                rule.Condition = definition.Condition;
                rule.Operator = "System";
            }
            rule.Severity = "Error";
        }
        foreach (var rule in stored) rule.Severity = "Error";
        await database.SaveChangesAsync(cancellationToken);
    }

}

