namespace TestingSystem.Web.Models;

public sealed class LssPlant
{
    public int Id { get; set; }
    public string PlantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal CapacityMw { get; set; }
    public string DdqApiEndpoint { get; set; } = string.Empty;
    public string MmfApiEndpoint { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string ApiConnectionStatus { get; set; } = "Not tested";
    public string? LastApiTestSource { get; set; }
    public string? LastApiTestMessage { get; set; }
    public DateTimeOffset? LastApiTestAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
