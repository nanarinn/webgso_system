using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Database.Entities;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class DataQuality : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;

    private static readonly IReadOnlyList<SystemValidationRule> SystemRuleDefinitions =
    [
        new("DDQ-001", "DDQ", "Missing interval reading", "All DDQ data types", "Every plant must contain a forecast value for each expected timestamp in the selected interval.", "Missing or null value", "Error"),
        new("DDQ-002", "DDQ", "Duplicate forecast reading", "Plant + data type + timestamp", "Only one forecast reading is allowed for the same plant, data type and timestamp.", "Duplicate composite key", "Error"),
        new("DDQ-003", "DDQ", "Invalid timestamp interval", "Data timestamp", "Forecast timestamps must align with the configured data interval.", "Timestamp not on interval", "Warning"),
        new("DDQ-004", "DDQ", "Negative forecast value", "Forecast value", "Solar power forecast values cannot be below zero.", "Value < 0 MW", "Error"),
        new("DDQ-005", "DDQ", "Above plant capacity", "Forecast value", "A forecast value cannot be greater than the configured capacity of its plant.", "Value > plant capacity", "Error"),
        new("DDQ-006", "DDQ", "Unexpected night generation", "Forecast value", "A material solar generation value during expected night hours may indicate an incorrect timestamp or forecast.", "Night value > 2% capacity", "Warning"),
        new("DDQ-007", "DDQ", "Sudden forecast ramp", "Consecutive forecast values", "A large change between adjacent intervals is flagged for review.", "Change > 60% capacity", "Warning"),
        new("DDQ-008", "DDQ", "Flat forecast profile", "Consecutive forecast values", "An unchanged non-zero forecast over a long window can indicate copied or stale data.", "Same value for 8 intervals", "Warning"),
        new("DDQ-009", "DDQ", "Missing API timestamps", "Rolling 24 Hour, Day Ahead, Week Ahead, 4 Month Ahead", "Timestamps must remain continuous at the source interval detected from the API response.", "One or more intervals missing inside the supplied range", "Error"),
        new("DDQ-010", "DDQ", "Invalid data type or unit", "Data type and unit", "The data type must be supported and forecast power must use the expected MW unit.", "Unsupported type or unit", "Error"),
        new("DDQ-011", "DDQ", "Submission timestamp mismatch", "Submission date and forecast horizon", "Forecast timestamps must fall within the valid horizon for the submission date and selected DDQ data type.", "Timestamp outside horizon", "Error"),
        new("DDQ-012", "DDQ", "Stale or future submission", "Submission datetime", "A submission cannot be unreasonably old or dated ahead of the system time.", "Outside submission window", "Warning"),

        new("MMF-001", "MMF", "Missing 15-minute reading", "All MMF parameters", "Every configured parameter must contain a reading for each plant at every expected 15-minute interval.", "Missing or null value", "Error"),
        new("MMF-002", "MMF", "Duplicate sensor reading", "Plant + parameter + timestamp", "Only one averaged reading is allowed for the same plant, parameter and timestamp.", "Duplicate composite key", "Error"),
        new("MMF-003", "MMF", "Invalid timestamp interval", "Data timestamp", "MMF timestamps must align with the fixed 15-minute collection interval.", "Minute not 00, 15, 30 or 45", "Warning"),
        new("MMF-004", "MMF", "Invalid atmospheric pressure", "AirPressure / AtmPressure", "Atmospheric pressure must remain within a physically plausible operating range.", "0.8–1.2 atm or 800–1200 hPa", "Warning"),
        new("MMF-005", "MMF", "Invalid irradiance", "GHI / POA / irradiance parameters", "Solar irradiance cannot be negative or exceed the configured physical ceiling.", "0–1,400 W/m²", "Error"),
        new("MMF-006", "MMF", "Invalid temperature", "AmbientTemp / PVTemperature", "Ambient and PV module temperatures must remain within plausible equipment limits.", "Ambient -20–60°C; PV -20–100°C", "Warning"),
        new("MMF-007", "MMF", "Invalid humidity", "Humidity / RelativeHumidity", "Relative humidity must be expressed as a percentage within its physical bounds.", "0–100%", "Error"),
        new("MMF-008", "MMF", "Invalid wind speed", "WindSpeed", "Wind speed cannot be negative or above the configured sensor operating range.", "0–75 m/s", "Warning"),
        new("MMF-009", "MMF", "Invalid wind direction", "WindDirection", "Wind direction must use degrees within one complete circle.", "0–360 degrees", "Warning"),
        new("MMF-010", "MMF", "Invalid precipitation", "Precipitation", "Accumulated precipitation cannot contain a negative measurement.", "Value < 0 mm", "Error"),
        new("MMF-011", "MMF", "Sudden measurement spike", "All numeric MMF parameters", "A sharp change against the recent rolling baseline may indicate a sensor or transmission issue.", "Deviation > configured rolling limit", "Warning"),
        new("MMF-012", "MMF", "Flatlined sensor", "All numeric MMF parameters", "An unchanged reading across a prolonged period may indicate a frozen sensor.", "Same value for 8 intervals", "Warning"),
        new("MMF-013", "MMF", "Sensor disagreement", "Sensors measuring the same parameter", "Readings from sensors in the same plant are compared before averaging to identify a faulty sensor.", "Spread > 20% of median", "Warning"),
        new("MMF-014", "MMF", "Night irradiance detected", "GHI / POA / irradiance parameters", "A material irradiance reading during expected night hours may indicate timestamp or sensor offset.", "Night value > 20 W/m²", "Warning"),
        new("MMF-015", "MMF", "Invalid parameter or unit", "Parameter name and unit", "Each MMF reading must use a supported parameter name and the unit configured for that parameter.", "Unsupported parameter or unit", "Error"),
        new("MMF-016", "MMF", "Stale or future measurement", "Data timestamp", "Measurements cannot be unreasonably old or dated ahead of the collection time.", "Timestamp outside collection window", "Warning")
    ];

    private List<SystemValidationRule> SystemRules { get; set; } = [];
    private List<DataAnomaly> Anomalies { get; set; } = [];
    private string ActiveSection { get; set; } = "Rules";
    private string SelectedApiType { get; set; } = "DDQ";
    private string SearchTerm { get; set; } = string.Empty;
    private IReadOnlyList<SystemValidationRule> VisibleRules => SystemRules.Where(x => x.ApiType == SelectedApiType).ToList();
    private List<DataAnomaly> VisibleAnomalies => Anomalies
        .Where(x => x.DataType.Equals(SelectedApiType, StringComparison.OrdinalIgnoreCase))
        .Where(x => string.IsNullOrWhiteSpace(SearchTerm)
            || x.FieldName.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
            || x.Reason.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
            || x.Status.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
        .ToList();

    protected override async Task OnInitializedAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        var storedRules = await database.ValidationRules.ToListAsync();
        var systemCodes = SystemRuleDefinitions.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var obsoleteRule in storedRules.Where(x => !systemCodes.Contains(x.Code)))
        {
            obsoleteRule.IsActive = false;
        }

        foreach (var definition in SystemRuleDefinitions)
        {
            var rule = storedRules.FirstOrDefault(x => x.Code == definition.Code);
            if (rule is null) { rule = new ValidationRule { Code = definition.Code }; database.ValidationRules.Add(rule); }
            rule.DataType = definition.ApiType; rule.Name = definition.Name; rule.FieldName = definition.AppliesTo;
            rule.Description = definition.Description; rule.Condition = definition.Condition; rule.Operator = "System";
            rule.Severity = definition.Severity; rule.IsActive = true;
        }
        await database.SaveChangesAsync();
        SystemRules = await database.ValidationRules.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code)
            .Select(x => new SystemValidationRule(x.Code, x.DataType, x.Name, x.FieldName, x.Description, x.Condition, x.Severity)).ToListAsync();
        Anomalies = await database.DataAnomalies.AsNoTracking()
            .OrderByDescending(x => x.DetectedAt).Take(500).ToListAsync();
    }

    private void ShowRules() => ActiveSection = "Rules";
    private void ShowHistory() => ActiveSection = "History";

    private sealed record SystemValidationRule(
        string Code, string ApiType, string Name, string AppliesTo,
        string Description, string Condition, string Severity);
}
