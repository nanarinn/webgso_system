namespace TestingSystem.Web.Services;

public sealed record DdqScheduledSubmissionSource(
    string DataType,
    DateTime SubmissionDateTime,
    string ScheduleName,
    int PlantId);

public sealed record DdqScheduledSubmissionOption(
    string Key,
    string DataType,
    DateTime SubmissionDateTime,
    string ScheduleName,
    int PlantCount)
{
    public string DisplayLabel =>
        $"{SubmissionDateTime:dd MMM yyyy HH:mm} · {DataType} · {ScheduleName} · {PlantCount} plant{(PlantCount == 1 ? string.Empty : "s")}";
}

public static class DdqScheduledSubmissionPolicy
{
    public static IReadOnlyList<DdqScheduledSubmissionOption> Build(
        IEnumerable<DdqScheduledSubmissionSource> sources,
        int maximumOptions = 120)
    {
        if (maximumOptions <= 0) return [];

        return sources
            .Select(source => source with
            {
                DataType = DdqDatasetPolicy.CanonicalName(source.DataType),
                SubmissionDateTime = TruncateToMinute(source.SubmissionDateTime),
                ScheduleName = source.ScheduleName.Trim()
            })
            .Where(source => DdqDatasetPolicy.Profiles.Any(profile => profile.DataType == source.DataType))
            .GroupBy(source => new { source.DataType, source.SubmissionDateTime })
            .Select(group =>
            {
                var scheduleNames = group.Select(source => source.ScheduleName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var scheduleName = scheduleNames.Length == 1
                    ? scheduleNames[0]
                    : $"{group.Key.DataType} scheduled collection";
                var key = $"{group.Key.DataType}|{group.Key.SubmissionDateTime:yyyyMMddHHmm}";
                return new DdqScheduledSubmissionOption(key, group.Key.DataType,
                    group.Key.SubmissionDateTime, scheduleName,
                    group.Select(source => source.PlantId).Distinct().Count());
            })
            .OrderByDescending(option => option.SubmissionDateTime)
            .ThenBy(option => option.DataType)
            .Take(maximumOptions)
            .ToArray();
    }

    private static DateTime TruncateToMinute(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Kind);
}
