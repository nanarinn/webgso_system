namespace TestingSystem.Web.Services;

public sealed record DashboardDdqSummarySource(
    string DataType,
    string Status,
    DateTime SubmissionDateTime,
    DateTimeOffset LastCollectedAt,
    int Count);

public sealed record DashboardDdqTypeSummary(
    string DataType,
    DateTimeOffset? LastCollectedAt,
    int SuccessfulCount,
    int FailedCount,
    DateTime? SubmissionDateTime = null);

public static class DashboardDdqSummaryPolicy
{
    public static IReadOnlyList<DashboardDdqTypeSummary> Build(
        IEnumerable<DashboardDdqSummarySource> sources)
    {
        var normalized = sources.Select(source => source with
        {
            DataType = DdqDatasetPolicy.CanonicalName(source.DataType)
        }).ToList();

        return DdqDatasetPolicy.Profiles.Select(profile =>
        {
            var matching = normalized.Where(source => source.DataType == profile.DataType).ToList();
            var latestSubmission = matching
                .GroupBy(source => source.SubmissionDateTime)
                .OrderByDescending(group => group.Max(source => source.LastCollectedAt))
                .Select(group => (DateTime?)group.Key)
                .FirstOrDefault();
            var latest = latestSubmission.HasValue
                ? matching.Where(source => source.SubmissionDateTime == latestSubmission.Value).ToList()
                : [];
            return new DashboardDdqTypeSummary(
                profile.DisplayName,
                latest.Count == 0 ? null : latest.Max(source => source.LastCollectedAt),
                latest.Where(source => source.Status == "Completed").Sum(source => source.Count),
                latest.Where(source => source.Status == "Failed").Sum(source => source.Count),
                latestSubmission);
        }).ToList();
    }
}
