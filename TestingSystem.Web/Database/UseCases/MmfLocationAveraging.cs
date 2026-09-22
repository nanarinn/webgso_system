using TestingSystem.Web.Database.Entities;

namespace TestingSystem.Web.Data;

public static class MmfLocationAveraging
{
    public static List<SolarApiReading> AverageLatestByLocation(
        IEnumerable<SolarApiReading> readings,
        int aggregatePlantId)
    {
        var latestPlantReadings = readings
            .GroupBy(reading => new
            {
                reading.PlantId,
                Parameter = reading.Parameter.ToUpperInvariant(),
                reading.IntervalStart
            })
            .Select(group => group.OrderByDescending(reading => reading.Id).First())
            .ToList();

        return latestPlantReadings
            .GroupBy(reading => new
            {
                Parameter = reading.Parameter.ToUpperInvariant(),
                reading.IntervalStart,
                Unit = reading.Unit.ToUpperInvariant()
            })
            .Select(group =>
            {
                var source = group.OrderByDescending(reading => reading.Id).First();
                var plantCount = group.Select(reading => reading.PlantId).Distinct().Count();
                return new SolarApiReading
                {
                    PlantId = aggregatePlantId,
                    DataType = "MMF",
                    SubmissionDateTime = group.Max(reading => reading.SubmissionDateTime),
                    IntervalStart = group.Key.IntervalStart,
                    IntervalEnd = source.IntervalEnd,
                    IntervalMinutes = source.IntervalMinutes,
                    Parameter = source.Parameter,
                    Value = decimal.Round(group.Average(reading => reading.Value), 6),
                    Unit = source.Unit,
                    ErrorCode = 0,
                    ErrorMessage = "OK",
                    IsSynthetic = true,
                    SyntheticReason = $"Location average calculated from {plantCount} LSS plant(s)."
                };
            })
            .OrderBy(reading => reading.Parameter)
            .ThenBy(reading => reading.IntervalStart)
            .ToList();
    }
}
