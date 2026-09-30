namespace TestingSystem.Web.Services;

public sealed record MmfParameterDefinition(string Name, string Unit);

public static class MmfParameterCatalog
{
    public static IReadOnlyList<MmfParameterDefinition> Parameters { get; } =
    [
        new("AirPressure", "hPa"),
        new("AmbientTemp", "C"),
        new("AtmPressure", "atm"),
        new("HorIrradiance", "W/m2"),
        new("Humidity", "%"),
        new("IncIrradiance", "W/m2"),
        new("MWPerArea", "MW"),
        new("Precipitation", "mm/h"),
        new("PVTemperature", "C"),
        new("TotRadiation", "Wh/m2"),
        new("WindDir", "o"),
        new("WindDirSD", "o"),
        new("WindSpeed", "m/s")
    ];

    public static IReadOnlyList<string> Names { get; } = Parameters.Select(parameter => parameter.Name).ToArray();

    public static string DisplayName(string? parameter)
    {
        var value = parameter?.Trim() ?? string.Empty;
        return Parameters.FirstOrDefault(item => item.Name.Equals(value, StringComparison.OrdinalIgnoreCase))?.Name ?? value;
    }

    public static string UnitFor(string? parameter) =>
        Parameters.FirstOrDefault(item => item.Name.Equals(parameter?.Trim(), StringComparison.OrdinalIgnoreCase))?.Unit ?? string.Empty;

    public static int OrderOf(string? parameter)
    {
        for (var index = 0; index < Parameters.Count; index++)
            if (Parameters[index].Name.Equals(parameter?.Trim(), StringComparison.OrdinalIgnoreCase)) return index;
        return int.MaxValue;
    }
}
