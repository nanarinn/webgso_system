namespace TestingSystem.Web.Services;

public enum ManualCollectionExistingState
{
    None,
    ScheduledRunning,
    ScheduledFailed,
    ExistingData
}

public static class ManualDdqCollectionPolicy
{
    public static readonly IReadOnlyList<string> DataTypes = ["Day Ahead", "Week Ahead", "4 Month Ahead"];

    public static bool IsSupported(string? dataType) =>
        DataTypes.Contains(dataType ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    public static ManualCollectionExistingState Evaluate(int existingReadingCount, IEnumerable<string> scheduledStatuses)
    {
        var statuses = scheduledStatuses.ToList();
        if (statuses.Any(status => status.Equals("Running", StringComparison.OrdinalIgnoreCase)
                || status.Equals("Queued", StringComparison.OrdinalIgnoreCase)
                || status.Equals("Processing", StringComparison.OrdinalIgnoreCase)))
            return ManualCollectionExistingState.ScheduledRunning;
        if (existingReadingCount > 0 || statuses.Any(status => status.Equals("Completed", StringComparison.OrdinalIgnoreCase)))
            return ManualCollectionExistingState.ExistingData;
        if (statuses.Any(status => status.Equals("Failed", StringComparison.OrdinalIgnoreCase)))
            return ManualCollectionExistingState.ScheduledFailed;
        return ManualCollectionExistingState.None;
    }
}
