namespace TestingSystem.Web.Services;

public static class CollectionSchedulePolicy
{
    public static string ApiDataType(string apiType, string scheduleDataType) =>
        apiType.Equals("MMF", StringComparison.OrdinalIgnoreCase)
            ? "MMF"
            : scheduleDataType switch
            {
                "Daily" => "Day Ahead",
                "Weekly" => "Week Ahead",
                "4 Month" => "4 Month Ahead",
                _ => scheduleDataType
            };

    public static bool IsDue(
        string dataType,
        string interval,
        string runTime,
        string scheduleDay,
        string scheduleMonths,
        DateTime localNow,
        DateTime? lastRunLocal)
    {
        if (dataType is "Rolling 24 Hour" or "MMF")
        {
            var intervalMinutes = ParseIntervalMinutes(interval);
            return lastRunLocal is null || localNow - lastRunLocal.Value >= TimeSpan.FromMinutes(intervalMinutes);
        }

        if (!TimeOnly.TryParse(runTime, out var scheduledTime)
            || TimeOnly.FromDateTime(localNow) < scheduledTime)
            return false;

        if (lastRunLocal?.Date == localNow.Date)
            return false;

        if (dataType == "Weekly")
            return Enum.TryParse<DayOfWeek>(scheduleDay, true, out var expectedDay)
                && localNow.DayOfWeek == expectedDay;

        if (dataType == "4 Month")
        {
            var months = scheduleMonths.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => int.TryParse(value, out var month) ? month : 0)
                .ToHashSet();
            return int.TryParse(scheduleDay, out var day)
                && localNow.Day == Math.Min(day, DateTime.DaysInMonth(localNow.Year, localNow.Month))
                && months.Contains(localNow.Month);
        }

        return true;
    }

    public static int ParseIntervalMinutes(string interval) => interval switch
    {
        "30 minutes" => 30,
        "1 hour" => 60,
        _ => 15
    };
}
