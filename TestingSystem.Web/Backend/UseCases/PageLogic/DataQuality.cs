using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Database.Entities;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class DataQuality : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
    private List<ValidationRule> Rules { get; set; } = [];
    private List<DataAnomaly> Anomalies { get; set; } = [];
    private bool IsRuleDialogOpen { get; set; }
    private string Code { get; set; } = string.Empty;
    private string DataType { get; set; } = "DDQ";
    private string FieldName { get; set; } = string.Empty;
    private decimal? Minimum { get; set; }
    private decimal? Maximum { get; set; }
    private string Severity { get; set; } = "Warning";
    private string Message { get; set; } = string.Empty;

    protected override async Task OnInitializedAsync() => await LoadAsync();
    private async Task LoadAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        Rules = await database.ValidationRules.AsNoTracking().OrderBy(x => x.Code).ToListAsync();
        Anomalies = await database.DataAnomalies.AsNoTracking().OrderByDescending(x => x.DetectedAt).Take(500).ToListAsync();
    }
    private void OpenRuleDialog() { Code = FieldName = string.Empty; DataType = "DDQ"; Minimum = Maximum = null; Severity = "Warning"; IsRuleDialogOpen = true; }
    private void CloseRuleDialog() => IsRuleDialogOpen = false;
    private async Task SaveRuleAsync()
    {
        if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(FieldName)) { Message = "Code and field are required."; return; }
        await using var database = await ContextFactory.CreateDbContextAsync();
        if (await database.ValidationRules.AnyAsync(x => x.Code == Code.Trim())) { Message = $"Rule {Code.Trim()} already exists."; return; }
        database.ValidationRules.Add(new ValidationRule { Code = Code.Trim(), DataType = DataType, FieldName = FieldName.Trim(), Operator = "Range", MinimumValue = Minimum, MaximumValue = Maximum, Severity = Severity });
        await database.SaveChangesAsync(); IsRuleDialogOpen = false; Message = $"Rule {Code.Trim()} saved."; await LoadAsync();
    }
    private async Task ToggleRuleAsync(ValidationRule selected)
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        var rule = await database.ValidationRules.SingleAsync(x => x.Id == selected.Id); rule.IsActive = !rule.IsActive; await database.SaveChangesAsync(); await LoadAsync();
    }
    private static string FormatRange(ValidationRule rule) => $"{rule.MinimumValue?.ToString() ?? "-"} to {rule.MaximumValue?.ToString() ?? "-"}";
}
