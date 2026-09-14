namespace TestingSystem.Web.Database.Entities;

public sealed class CollectionJobEntity
{
    public long Id { get; set; }
    public string CollectionId { get; set; } = string.Empty;
    public int PlantId { get; set; }
    public string DataType { get; set; } = "DDQ";
    public string Mode { get; set; } = "Manual";
    public DateOnly SubmissionDate { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string Status { get; set; } = "Pending";
    public string? ErrorMessage { get; set; }
}

public sealed class RawApiResponse
{
    public long Id { get; set; }
    public long CollectionJobId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public int HttpStatusCode { get; set; }
    public string ContentType { get; set; } = "application/json";
    public string Payload { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class ValidationRule
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DataType { get; set; } = "DDQ";
    public string FieldName { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public decimal? MinimumValue { get; set; }
    public decimal? MaximumValue { get; set; }
    public string Severity { get; set; } = "Warning";
    public bool IsActive { get; set; } = true;
}

public sealed class DataAnomaly
{
    public long Id { get; set; }
    public long CollectionJobId { get; set; }
    public int? ValidationRuleId { get; set; }
    public string DataType { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string? OriginalValue { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CollectionSchedule
{
    public int Id { get; set; }
    public int PlantId { get; set; }
    public string DataType { get; set; } = "DDQ";
    public string CronExpression { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "Asia/Kuala_Lumpur";
    public int RetryCount { get; set; } = 3;
    public bool IsActive { get; set; } = true;
}

public sealed class ReportExecution
{
    public long Id { get; set; }
    public string ReportId { get; set; } = string.Empty;
    public string ReportType { get; set; } = "OnDemand";
    public string DataType { get; set; } = "DDQ";
    public string RequestedBy { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public string GenerationStatus { get; set; } = "Pending";
    public string DeliveryStatus { get; set; } = "NotRequired";
    public string? MainFilePath { get; set; }
    public string? AnomalyFilePath { get; set; }
    public string? ErrorMessage { get; set; }
}
