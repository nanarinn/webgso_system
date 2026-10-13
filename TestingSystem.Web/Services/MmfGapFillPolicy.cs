namespace TestingSystem.Web.Services;

public static class MmfGapFillPolicy
{
    public static decimal? Resolve(decimal? exactValue, decimal? previousIntervalValue,
        decimal? nextIntervalValue)
    {
        if (exactValue.HasValue) return exactValue.Value;
        if (!previousIntervalValue.HasValue || !nextIntervalValue.HasValue) return null;
        return Math.Round((previousIntervalValue.Value + nextIntervalValue.Value) / 2m,
            6, MidpointRounding.AwayFromZero);
    }
}
