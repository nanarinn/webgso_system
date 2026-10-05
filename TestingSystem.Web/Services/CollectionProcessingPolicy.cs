namespace TestingSystem.Web.Services;

public static class CollectionProcessingPolicy
{
    public const int DefaultMaxConcurrency = 6;
    public const int MaximumAllowedConcurrency = 8;
    public const int MaximumConcurrentDatabaseWrites = 2;

    public static int NormalizeConcurrency(int configuredValue) =>
        Math.Clamp(configuredValue, 1, MaximumAllowedConcurrency);
}
