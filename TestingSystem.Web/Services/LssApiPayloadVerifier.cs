namespace TestingSystem.Web.Services;

public sealed record LssApiPayloadVerification(bool IsValid, int ReadingCount, string Message);

public static class LssApiPayloadVerifier
{
    public static LssApiPayloadVerification Verify(
        string payload,
        string contentType,
        string apiType,
        string dataType,
        int intervalMinutes)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return new(false, 0, "The endpoint returned an empty response instead of API data.");

        try
        {
            var translated = ScheduledApiPayloadTranslator.Translate(payload, apiType, dataType,
                0, 0, DateTime.UtcNow, intervalMinutes, contentType);
            if (translated.Readings.Count == 0)
                return new(false, 0,
                    $"The endpoint returned {translated.PayloadFormat}, but no supported numeric {apiType} readings were found.");

            return new(true, translated.Readings.Count,
                $"Verified {apiType} {dataType} payload with {translated.Readings.Count:N0} readable value(s).");
        }
        catch (FormatException exception)
        {
            return new(false, 0, exception.Message);
        }
    }
}
