namespace TestingSystem.Web.Services;

public sealed record DdqDatasetProfile(
    string DataType,
    string DisplayName,
    int ReferenceTemplateIntervalMinutes,
    string HorizonDescription);

public static class DdqDatasetPolicy
{
    public static readonly IReadOnlyList<DdqDatasetProfile> Profiles =
    [
        new("Rolling 24 Hour", "Rolling 24 Hour", 15,
            "All timestamps returned by the rolling API for its latest 24-hour window."),
        new("Day Ahead", "Day Ahead", 30,
            "Five forecast days are displayed from the selected submission start date using the API source interval."),
        new("Week Ahead", "Week Ahead", 60,
            "All forecast timestamps returned by the Week Ahead API are displayed using its detected source interval."),
        new("4 Month Ahead", "4 Month Ahead", 60,
            "All forecast timestamps returned by the Four Months Ahead API are displayed using its detected source interval.")
    ];

    public static DdqDatasetProfile Get(string? dataType)
    {
        var canonical = CanonicalName(dataType);
        return Profiles.First(profile => profile.DataType == canonical);
    }

    public static string CanonicalName(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.ToLowerInvariant() switch
        {
            "day ahead" or "five day ahead" or "5 day ahead" or "five-day ahead" => "Day Ahead",
            "week ahead" or "weekly" => "Week Ahead",
            "4 month ahead" or "4 months ahead" or "four month ahead" or "four months ahead"
                or "month ahead" or "4 month" => "4 Month Ahead",
            _ => "Rolling 24 Hour"
        };
    }

    public static int SourceIntervalMinutes(string? dataType, int configuredIntervalMinutes)
        => Math.Max(1, configuredIntervalMinutes);

    public static (DateTime Start, DateTime EndExclusive) ReferenceWindow(string dataType, DateTime submissionDate)
    {
        var day = submissionDate.Date;
        return CanonicalName(dataType) switch
        {
            "Day Ahead" => (day, day.AddDays(5)),
            "Week Ahead" => (day, day.AddDays(9)),
            "4 Month Ahead" =>
                (new DateTime(day.Year, day.Month, 1), new DateTime(day.Year, day.Month, 1).AddMonths(4)),
            _ => (submissionDate, submissionDate.AddHours(24))
        };
    }

    public static int? ExpectedIntervalCount(string dataType, DateTime submissionDate)
    {
        var profile = Get(dataType);
        if (profile.DataType == "Rolling 24 Hour") return null;
        var window = ReferenceWindow(profile.DataType, submissionDate);
        return (int)(window.EndExclusive - window.Start).TotalMinutes / profile.ReferenceTemplateIntervalMinutes;
    }
}
