using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Backend.UseCases.UCLSS027Audit;
using TestingSystem.Web.Data;
using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Services;
using TestingSystem.Web.Components.Shared;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class DataQuality : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] private IAuditTrailService AuditTrail { get; set; } = default!;

    private List<ValidationRule> Rules { get; set; } = [];
    private List<string> DetectedMmfParameters { get; set; } = [];
    private string SelectedApiType { get; set; } = "DDQ";
    private RuleEditor Editor { get; set; } = new();
    private bool EditorOpen { get; set; }
    private ValidationRule? DeleteTarget { get; set; }
    private string? FormError { get; set; }
    private string? Notice { get; set; }
    private int RulePage { get; set; } = 1;
    private bool SavingRule { get; set; }

    private IReadOnlyList<ValidationRule> FixedRules => Rules.Where(IsFixed).ToList();
    private IReadOnlyList<ValidationRule> ConfiguredRules => Rules.Where(IsUserConfigured).ToList();
    private IReadOnlyList<ValidationRule> VisibleRules => Rules
        .Where(rule => rule.DataType == SelectedApiType)
        .OrderBy(rule => IsFixed(rule) ? 0 : 1).ThenBy(rule => rule.Code).ToList();

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        await ValidationRuleCatalog.SynchronizeAsync(database);
        Rules = await database.ValidationRules.AsNoTracking().OrderBy(x => x.Code).ToListAsync();
        DetectedMmfParameters = await database.SolarApiReadings.AsNoTracking()
            .Where(reading => reading.DataType == "MMF")
            .Select(reading => reading.Parameter)
            .Where(parameter => parameter != null && parameter != "")
            .Distinct().OrderBy(parameter => parameter).ToListAsync();
        RulePage = Math.Min(RulePage, Math.Max(1,
            (int)Math.Ceiling(VisibleRules.Count / (double)TablePaging.DefaultPageSize)));
    }

    private void SelectApiType(string type) { SelectedApiType = type; RulePage = 1; }
    private void CreateRule()
    {
        Editor = new RuleEditor { DataType = SelectedApiType, CheckMissing = true, IsActive = true };
        FormError = null;
        EditorOpen = true;
    }

    private void EditRule(ValidationRule rule)
    {
        if (!CanEdit(rule)) return;
        Editor = new RuleEditor
        {
            Id = rule.Id, Code = rule.Code, Name = rule.Name, DataType = rule.DataType,
            FieldName = rule.FieldName, Description = rule.Description,
            UseMinimum = rule.MinimumValue.HasValue, MinimumValue = rule.MinimumValue,
            UseMaximum = rule.MaximumValue.HasValue, MaximumValue = rule.MaximumValue,
            UseChange = rule.ChangeThreshold.HasValue, ChangeThreshold = rule.ChangeThreshold,
            CheckMissing = rule.CheckMissing, IsActive = rule.IsActive
        };
        FormError = null;
        EditorOpen = true;
    }

    private async Task SaveRuleAsync()
    {
        if (SavingRule) return;
        FormError = Validate(Editor);
        if (FormError is not null) return;

        SavingRule = true;
        try
        {
            await using var database = await ContextFactory.CreateDbContextAsync();
            var minimum = Editor.UseMinimum ? Editor.MinimumValue : null;
            var maximum = Editor.UseMaximum ? Editor.MaximumValue : null;
            var change = Editor.UseChange ? Editor.ChangeThreshold : null;
            var fieldName = Editor.FieldName.Trim();
            var existingRules = await database.ValidationRules.AsNoTracking()
                .Where(item => item.DataType == Editor.DataType && (!Editor.Id.HasValue || item.Id != Editor.Id.Value))
                .ToListAsync();
            var duplicate = existingRules.FirstOrDefault(item =>
                string.Equals(item.FieldName.Trim(), fieldName, StringComparison.OrdinalIgnoreCase)
                && item.MinimumValue == minimum
                && item.MaximumValue == maximum
                && item.ChangeThreshold == change
                && item.CheckMissing == Editor.CheckMissing);
            if (duplicate is not null)
            {
                FormError = $"A matching rule already exists: {duplicate.Code} — {duplicate.Name}. Edit that rule or change its field/check values.";
                return;
            }

            var principal = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
            var actor = principal.Identity?.Name
                ?? principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "Administrator";
            ValidationRule rule;
            var action = "Create validation rule";
            if (Editor.Id.HasValue)
            {
                rule = await database.ValidationRules.SingleAsync(item => item.Id == Editor.Id.Value);
                if (!CanEdit(rule)) { FormError = "Fixed structural rules cannot be edited."; return; }
                action = "Update validation rule";
            }
            else
            {
                rule = new ValidationRule
                {
                    Code = await NextCodeAsync(database, Editor.DataType),
                    RuleSource = ValidationRuleCatalog.Configured,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedBy = actor
                };
                database.ValidationRules.Add(rule);
            }
            rule.Name = Editor.Name.Trim();
            rule.DataType = Editor.DataType;
            rule.FieldName = fieldName;
            rule.Description = Editor.Description.Trim();
            rule.CheckMissing = Editor.CheckMissing;
            rule.Operator = "Configured";
            rule.Severity = "Error";
            rule.MinimumValue = minimum;
            rule.MaximumValue = maximum;
            rule.ChangeThreshold = change;
            rule.Condition = BuildCondition(rule);
            rule.IsActive = Editor.IsActive;
            rule.RuleSource = ValidationRuleCatalog.Configured;
            rule.UpdatedAt = DateTimeOffset.UtcNow;
            try
            {
                await database.SaveChangesAsync();
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                FormError = "This rule already exists or was saved by another administrator. Refresh the rule list and try again.";
                return;
            }
            await AuditTrail.RecordAsync(principal, action, "Success", "ValidationRule", rule.Code,
                $"{rule.DataType} rule '{rule.Name}' saved; condition: {rule.Condition}.");
            EditorOpen = false;
            Notice = $"{rule.Code} saved successfully.";
            await LoadAsync();
        }
        finally
        {
            SavingRule = false;
        }
    }

    private async Task ToggleRuleAsync(ValidationRule selected)
    {
        var principal = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
        await using var database = await ContextFactory.CreateDbContextAsync();
        var rule = await database.ValidationRules.SingleAsync(item => item.Id == selected.Id);
        rule.IsActive = !rule.IsActive;
        rule.UpdatedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync();
        await AuditTrail.RecordAsync(principal,
            rule.IsActive ? "Activate validation rule" : "Deactivate validation rule",
            "Success", "ValidationRule", rule.Code,
            $"Rule '{rule.Name}' is now {(rule.IsActive ? "active" : "inactive")}.");
        Notice = $"{rule.Code} is now {(rule.IsActive ? "active" : "inactive")}.";
        await LoadAsync();
    }

    private async Task DeleteRuleAsync()
    {
        if (DeleteTarget is null || !CanDelete(DeleteTarget)) return;
        var principal = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
        await using var database = await ContextFactory.CreateDbContextAsync();
        var rule = await database.ValidationRules.SingleOrDefaultAsync(item => item.Id == DeleteTarget.Id);
        if (rule is null) { DeleteTarget = null; return; }
        var code = rule.Code;
        var name = rule.Name;
        database.ValidationRules.Remove(rule);
        await database.SaveChangesAsync();
        await AuditTrail.RecordAsync(principal, "Delete validation rule", "Success",
            "ValidationRule", code, $"Configured rule '{name}' was deleted.");
        DeleteTarget = null;
        Notice = $"{code} deleted.";
        await LoadAsync();
    }

    private static string? Validate(RuleEditor editor)
    {
        if (string.IsNullOrWhiteSpace(editor.Name)) return "Enter a rule name.";
        if (string.IsNullOrWhiteSpace(editor.FieldName))
            return "Enter a field/parameter, or use * for all parameters.";
        if (!editor.UseMinimum && !editor.UseMaximum && !editor.UseChange && !editor.CheckMissing)
            return "Select at least one check: minimum, maximum, changing value or missing/null.";
        if (editor.UseMinimum && !editor.MinimumValue.HasValue) return "Enter the minimum threshold.";
        if (editor.UseMaximum && !editor.MaximumValue.HasValue) return "Enter the maximum threshold.";
        if (editor.UseMinimum && editor.UseMaximum && editor.MinimumValue > editor.MaximumValue)
            return "Minimum threshold cannot be greater than maximum threshold.";
        if (editor.UseChange && (!editor.ChangeThreshold.HasValue || editor.ChangeThreshold < 0))
            return "Enter a valid changing-value threshold of zero or greater.";
        return null;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

    private static string BuildCondition(ValidationRule rule)
    {
        var conditions = new List<string>();
        if (rule.MinimumValue.HasValue)
            conditions.Add($"Value ≥ {rule.MinimumValue.Value.ToString(CultureInfo.InvariantCulture)}");
        if (rule.MaximumValue.HasValue)
            conditions.Add($"Value ≤ {rule.MaximumValue.Value.ToString(CultureInfo.InvariantCulture)}");
        if (rule.ChangeThreshold.HasValue)
            conditions.Add($"Change ≤ {rule.ChangeThreshold.Value.ToString(CultureInfo.InvariantCulture)}");
        if (rule.CheckMissing) conditions.Add("Value is required");
        return string.Join("; ", conditions);
    }

    private static async Task<string> NextCodeAsync(ApplicationDbContext database, string dataType)
    {
        var prefix = $"USR-{dataType}-";
        var codes = await database.ValidationRules.AsNoTracking()
            .Where(rule => rule.Code.StartsWith(prefix)).Select(rule => rule.Code).ToListAsync();
        var next = codes.Select(code => int.TryParse(code[prefix.Length..], out var number) ? number : 0)
            .DefaultIfEmpty().Max() + 1;
        return $"{prefix}{next:0000}";
    }

    private static bool IsFixed(ValidationRule rule) =>
        rule.RuleSource.Equals(ValidationRuleCatalog.Fixed, StringComparison.OrdinalIgnoreCase);
    private static bool IsUserConfigured(ValidationRule rule) =>
        rule.RuleSource.Equals(ValidationRuleCatalog.Configured, StringComparison.OrdinalIgnoreCase);
    private static bool CanEdit(ValidationRule rule) => IsUserConfigured(rule);
    private static bool CanDelete(ValidationRule rule) => IsUserConfigured(rule);
    private static string SourceLabel(ValidationRule rule) => IsFixed(rule) ? "Fixed" : "Admin created";

    private static string SubstitutionFor(ValidationRule rule)
    {
        if (rule.DataType.Equals("DDQ", StringComparison.OrdinalIgnoreCase))
            return rule.Code switch
            {
                "DDQ-002" => "Use the latest successful extraction.",
                "DDQ-005" => "Use previous-day value; if unavailable, cap at plant capacity.",
                "DDQ-001" or "DDQ-004" or "DDQ-009" => "Use previous-day value; if unavailable, use 0 MW.",
                _ when !IsFixed(rule) => "Use previous-day value; if unavailable, use 0 MW.",
                _ => "Keep the stored value and flag it in the report."
            };
        return rule.Code switch
        {
            "MMF-001" => "Use previous-day average; if unavailable, use 0.",
            _ when !IsFixed(rule) => "Exclude failed value; if no valid average remains, use previous day then 0.",
            _ => "Keep the stored value and flag it in the report."
        };
    }

    private sealed class RuleEditor
    {
        public int? Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = "DDQ";
        public string FieldName { get; set; } = "*";
        public string Description { get; set; } = string.Empty;
        public bool UseMinimum { get; set; }
        public decimal? MinimumValue { get; set; }
        public bool UseMaximum { get; set; }
        public decimal? MaximumValue { get; set; }
        public bool UseChange { get; set; }
        public decimal? ChangeThreshold { get; set; }
        public bool CheckMissing { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
