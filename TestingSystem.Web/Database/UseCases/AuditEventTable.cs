namespace TestingSystem.Web.Database.Entities;

public sealed class AuditEvent
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string Actor { get; set; } = "System";
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityReference { get; set; }
    public string Result { get; set; } = "Success";
    public string? Detail { get; set; }
    public string? CorrelationId { get; set; }
}
