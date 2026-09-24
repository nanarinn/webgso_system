namespace TestingSystem.Web.Services;

public static class MmfTestApiPayload
{
    private static readonly (string Parameter, string Unit)[] Parameters =
    [
        ("AmbientTemp", "C"),
        ("AtmPressure", "atm"),
        ("AirPressure", "hPa"),
        ("HorIrradiance", "W/m2"),
        ("Humidity", "%"),
        ("IncIrradiance", "W/m2"),
        ("MWPerArea", "MW"),
        ("Precipitation", "mm/h"),
        ("PVTemperature", "C"),
        ("TotRadiation", "Wh/m2"),
        ("WindDir", "degree"),
        ("WindDirSD", "degree"),
        ("WindSpeed", "m/s")
    ];

    public static MmfTestApiResponse Build(string plantCode, DateOnly date, int intervalMinutes)
    {
        if (intervalMinutes is not (15 or 30 or 60))
            throw new ArgumentOutOfRangeException(nameof(intervalMinutes), "Use a 15, 30 or 60 minute interval.");

        var normalizedPlantCode = NormalizePlantCode(plantCode);
        var readings = new List<MmfTestSensorReading>();
        var start = date.ToDateTime(TimeOnly.MinValue);
        var periods = 24 * 60 / intervalMinutes;
        for (var period = 0; period < periods; period++)
        {
            var timestamp = start.AddMinutes(period * intervalMinutes);
            var hour = timestamp.TimeOfDay.TotalHours;
            var solarFactor = Math.Max(0d, Math.Sin(Math.PI * (hour - 6d) / 12d));
            foreach (var (parameter, unit) in Parameters)
                for (var sensor = 1; sensor <= 5; sensor++)
                    readings.Add(new(timestamp, parameter, SensorValue(parameter, hour, solarFactor, sensor, intervalMinutes),
                        unit, $"MMF{sensor}"));
        }

        return new("SBLSS sample MMF test API", normalizedPlantCode, DateTime.Now, intervalMinutes, readings);
    }

    private static decimal SensorValue(string parameter, double hour, double solarFactor, int sensor, int intervalMinutes)
    {
        var sensorFactor = 1d + (sensor - 3) * 0.008d;
        var sensorOffset = (sensor - 3) * 0.12d;
        var value = parameter switch
        {
            "AmbientTemp" => 24d + 8d * solarFactor + sensorOffset,
            "AtmPressure" => (0.998d - 0.003d * solarFactor) * sensorFactor,
            "AirPressure" => (1012d - 4d * solarFactor) * sensorFactor,
            "HorIrradiance" => 950d * solarFactor * sensorFactor,
            "Humidity" => (82d - 35d * solarFactor) * sensorFactor,
            "IncIrradiance" => 1020d * solarFactor * sensorFactor,
            "MWPerArea" => 50d * solarFactor * sensorFactor,
            "Precipitation" => hour is >= 16 and < 17 ? 0.2d * sensorFactor : 0d,
            "PVTemperature" => 24d + 25d * solarFactor + sensorOffset,
            "TotRadiation" => 950d * solarFactor * intervalMinutes / 60d * sensorFactor,
            "WindDir" => (110d + hour * 6d + (sensor - 3) * 2d) % 360d,
            "WindDirSD" => 5d + 2d * solarFactor + Math.Abs(sensorOffset),
            "WindSpeed" => (2d + 3d * solarFactor) * sensorFactor,
            _ => 0d
        };
        return decimal.Round((decimal)Math.Max(0d, value), 6);
    }

    private static string NormalizePlantCode(string plantCode)
    {
        var value = new string((plantCode ?? string.Empty).Where(character => char.IsLetterOrDigit(character) || character == '-')
            .Take(40).ToArray());
        return string.IsNullOrWhiteSpace(value) ? "LSS1" : value.ToUpperInvariant();
    }
}

public sealed record MmfTestApiResponse(
    string Source,
    string PlantCode,
    DateTime SubmissionDateTime,
    int IntervalMinutes,
    IReadOnlyList<MmfTestSensorReading> SensorReadings);

public sealed record MmfTestSensorReading(
    DateTime Timestamp,
    string Parameter,
    decimal Value,
    string Unit,
    string Sensor);
