namespace TestingSystem.Web.Services;

public static class CollectionCommandIdentity
{
    public static string Create(string apiType, DateTimeOffset createdAt, string? requestedId = null)
    {
        var source = string.IsNullOrWhiteSpace(requestedId)
            ? $"CMD-{apiType}-{createdAt:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"
            : requestedId.Trim();
        return source[..Math.Min(source.Length, 60)];
    }
}
