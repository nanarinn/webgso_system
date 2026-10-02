using System.Globalization;

namespace TestingSystem.Web.Services;

public static class ReportScheduleTimePolicy
{
    private static readonly string[] SupportedFormats = ["H:mm", "HH:mm"];

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var input = value.Trim();
        foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.CurrentCulture, CultureInfo.GetCultureInfo("en-US") })
        {
            if (TimeOnly.TryParseExact(input, SupportedFormats, culture,
                    DateTimeStyles.AllowWhiteSpaces, out var time)
                || TimeOnly.TryParse(input, culture, DateTimeStyles.AllowWhiteSpaces, out time))
            {
                normalized = time.ToString("HH:mm", CultureInfo.InvariantCulture);
                return true;
            }
        }

        return false;
    }
}
