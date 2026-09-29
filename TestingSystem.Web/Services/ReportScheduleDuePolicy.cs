using System.Globalization;

namespace TestingSystem.Web.Services;

public static class ReportScheduleDuePolicy
{
    public static string? GetDueSlot(string scheduleType, int intervalMinutes, string executionTime,
        string scheduleDay, string scheduleMonths, DateTime localNow)
    {
        if (scheduleType.Equals("Rolling 24 Hour", StringComparison.OrdinalIgnoreCase))
        {
            var interval = Math.Clamp(intervalMinutes, 1, 1440);
            var minuteOfDay = localNow.Hour * 60 + localNow.Minute;
            var slotMinute = minuteOfDay - minuteOfDay % interval;
            return localNow.Date.AddMinutes(slotMinute).ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        }

        if (!ReportScheduleTimePolicy.TryNormalize(executionTime, out var normalized)
            || !TimeOnly.TryParseExact(normalized, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var scheduledTime)
            || TimeOnly.FromDateTime(localNow) < scheduledTime)
            return null;

        if (scheduleType.Equals("Weekly", StringComparison.OrdinalIgnoreCase)
            && !localNow.DayOfWeek.ToString().Equals(scheduleDay, StringComparison.OrdinalIgnoreCase))
            return null;

        if (scheduleType.Equals("4 Month", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(scheduleDay, out var day) || localNow.Day != day)
                return null;
            var months = scheduleMonths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => int.TryParse(value, out var month) ? month : 0);
            if (!months.Contains(localNow.Month))
                return null;
        }

        return $"{localNow:yyyyMMdd}{scheduledTime:HHmm}";
    }
}
