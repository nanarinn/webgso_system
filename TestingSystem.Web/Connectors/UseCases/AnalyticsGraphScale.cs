namespace TestingSystem.Web.Services;

public static class AnalyticsGraphScale
{
    private static readonly double[] Steps = [1, 1.2, 1.5, 2, 2.5, 5, 10];

    public static decimal NiceUpperBound(decimal maximum)
    {
        if (maximum <= 0) return 1;
        var value = (double)maximum;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var fraction = value / magnitude;
        var step = Steps.First(candidate => fraction <= candidate);
        return (decimal)(step * magnitude);
    }

    public static (decimal Minimum, decimal Maximum) Bounds(IEnumerable<decimal> values)
    {
        var readings = values.ToList();
        if (readings.Count == 0) return (0, 1);
        var rawMinimum = readings.Min();
        var rawMaximum = readings.Max();
        var rawRange = rawMaximum - rawMinimum;
        if (rawRange == 0)
            rawRange = Math.Max(Math.Abs(rawMaximum) * 0.1m, 0.001m);

        var lower = rawMinimum <= 0 || rawMinimum < rawMaximum * 0.2m
            ? 0
            : rawMinimum - rawRange * 0.08m;
        var upper = rawMaximum + rawRange * 0.08m;
        var step = NiceStep((upper - lower) / 5m);
        var minimum = lower == 0 ? 0 : Math.Floor(lower / step) * step;
        var maximum = Math.Ceiling(upper / step) * step;
        return maximum > minimum ? (minimum, maximum) : (minimum, minimum + step);
    }

    private static decimal NiceStep(decimal target)
    {
        if (target <= 0) return 1;
        var value = (double)target;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var fraction = value / magnitude;
        var step = Steps.First(candidate => fraction <= candidate);
        return (decimal)(step * magnitude);
    }

    public static string TickFormat(decimal maximum) => maximum switch
    {
        >= 100 => "N0",
        >= 10 => "N1",
        >= 1 => "N2",
        >= 0.001m => "N3",
        _ => "N6"
    };
}
