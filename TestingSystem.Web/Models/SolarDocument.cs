namespace TestingSystem.Web.Models;

public sealed class SolarDocument
{
    public int Id { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string ProjectStage { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}
