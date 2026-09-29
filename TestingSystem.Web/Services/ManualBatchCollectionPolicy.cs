namespace TestingSystem.Web.Services;

public static class ManualBatchCollectionPolicy
{
    public static string StoredDataType(string apiType, string configuredDataType) =>
        apiType.Equals("MMF", StringComparison.OrdinalIgnoreCase) ? "MMF" : configuredDataType.Trim();

    public static string Mode(string apiType) =>
        apiType.Equals("MMF", StringComparison.OrdinalIgnoreCase) ? "Manual MMF" : "Manual DDQ";
}
