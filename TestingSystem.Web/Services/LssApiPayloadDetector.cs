namespace TestingSystem.Web.Services;

public sealed record LssApiPayloadDetection(bool IsValid, string ApiType,
    string DataType, int ReadingCount, string Message);

public static class LssApiPayloadDetector
{
    private static readonly (string ApiType, string DataType, int IntervalMinutes)[] Candidates =
    [
        ("MMF", "MMF", 15),
        ("DDQ", "Rolling 24 Hour", 15),
        ("DDQ", "Day Ahead", 30),
        ("DDQ", "Week Ahead", 60),
        ("DDQ", "4 Month Ahead", 60)
    ];

    public static LssApiPayloadDetection Detect(string payload, string contentType)
    {
        var matches = Candidates.Select(candidate =>
        {
            var verification = LssApiPayloadVerifier.Verify(payload, contentType,
                candidate.ApiType, candidate.DataType, candidate.IntervalMinutes);
            return (candidate.ApiType, candidate.DataType, Verification: verification);
        }).Where(result => result.Verification.IsValid)
          .OrderByDescending(result => result.Verification.ReadingCount)
          .ToList();

        if (matches.Count == 0)
            return new(false, string.Empty, string.Empty, 0,
                "The link responded, but no supported DDQ or MMF data payload was detected.");

        var match = matches[0];
        var label = match.ApiType == "MMF" ? "MMF" : $"DDQ {match.DataType}";
        return new(true, match.ApiType, match.DataType, match.Verification.ReadingCount,
            $"Detected {label} with {match.Verification.ReadingCount:N0} readable value(s).");
    }
}
