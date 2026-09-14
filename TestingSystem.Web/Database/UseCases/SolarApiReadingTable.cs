namespace TestingSystem.Web.Database.Entities;

public sealed class SolarApiReading
{
    public long Id { get; set; }
    public long CollectionJobId { get; set; }
    public int PlantId { get; set; }
    public string DataType { get; set; } = string.Empty;
    public DateTime SubmissionDateTime { get; set; }
    public DateTime IntervalStart { get; set; }
    public DateTime? IntervalEnd { get; set; }
    public int? IntervalMinutes { get; set; }
    public string Parameter { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsSynthetic { get; set; }
    public string? SyntheticReason { get; set; }
}
