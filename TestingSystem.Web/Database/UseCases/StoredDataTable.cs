namespace TestingSystem.Web.Database.Entities;

public sealed class StoredDataRecord
{
    public long Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string RecordKey { get; set; } = string.Empty;
    public string JsonData { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
