namespace TestingSystem.Web.Services;

public sealed record PlantGraphSeries(string Name, List<double> Values)
{
    public static string Points(IReadOnlyList<double> values, double minimum, double maximum)
    {
        var range = Math.Max(maximum - minimum, 1);
        return string.Join(" ", values.Select((value, index) =>
            FormattableString.Invariant($"{(values.Count <= 1 ? 72 : 72 + 898d * index / (values.Count - 1)):F1},{325 - 280d * (value - minimum) / range:F1}")));
    }

    public static string Colour(int index) => new[] { "#1068b8", "#049f64", "#bc6510", "#bc3755", "#5964a9", "#137b86" }[index % 6];
}
