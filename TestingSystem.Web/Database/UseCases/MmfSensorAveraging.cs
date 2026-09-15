namespace TestingSystem.Web.Data;

public static class MmfSensorAveraging
{
    public static decimal AverageNonZero(IEnumerable<decimal> sensorValues)
    {
        var validValues = sensorValues.Where(value => value != 0).ToList();
        return validValues.Count == 0 ? 0 : decimal.Round(validValues.Average(), 6);
    }
}
