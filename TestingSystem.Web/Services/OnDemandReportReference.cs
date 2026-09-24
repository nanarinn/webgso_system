namespace TestingSystem.Web.Services;

public static class OnDemandReportReference
{
    public static string Create(string type, DateTime generatedAt) =>
        $"ODA-{type.ToUpperInvariant()}-{generatedAt:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..52];
}
