using System.Globalization;

namespace TestingSystem.Web.Services;

public static class TimeDisplay
{
    public static string TwentyFourHour(DateTime value) => value.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string TwentyFourHour(TimeOnly value) => value.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string DateTime(DateTime value) => value.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string DateTimeLong(DateTime value) => value.ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);

    public static string DateTimeWithSeconds(DateTime value) => value.ToString("dd MMM yyyy, HH:mm:ss", CultureInfo.InvariantCulture);

    public static string DateTimeWithSeconds(DateTimeOffset value) =>
        value.ToLocalTime().ToString("dd MMM yyyy, HH:mm:ss", CultureInfo.InvariantCulture);

    public static string TwentyFourHour(string? value)
    {
        return TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var time)
            ? TwentyFourHour(time)
            : value ?? "00:00";
    }
}
