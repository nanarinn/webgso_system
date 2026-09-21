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
    public string ResponseMessage { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
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
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
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
    public string ScheduleCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ApiType { get; set; } = "DDQ";
    public string DataType { get; set; } = "Rolling 24 Hour";
    public string Interval { get; set; } = "15 minutes";
    public string RunTime { get; set; } = "00:15";
    public string ScheduleDay { get; set; } = string.Empty;
    public string ScheduleMonths { get; set; } = string.Empty;
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
    public string Name { get; set; } = string.Empty;
    public DateTime SubmissionDate { get; set; }
    public string DataSelection { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int PlantCount { get; set; }
    public int RecordCount { get; set; }
    public int AnomalyCount { get; set; }
}

public sealed class ReportScheduleEntity
{
    public int Id { get; set; }
    public string ScheduleCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ApiType { get; set; } = "DDQ";
    public string DataType { get; set; } = "Rolling 24 Hour";
    public int IntervalMinutes { get; set; } = 15;
    public string ScheduleType { get; set; } = "Daily";
    public string ScheduleDay { get; set; } = string.Empty;
    public string ScheduleMonths { get; set; } = string.Empty;
    public string ExecutionTime { get; set; } = "07:00";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ReportScheduleRecipient> Recipients { get; set; } = [];
}

public sealed class ReportScheduleRecipient
{
    public int Id { get; set; }
    public int ReportScheduleId { get; set; }
    public string Email { get; set; } = string.Empty;
    public ReportScheduleEntity ReportSchedule { get; set; } = null!;
}

public sealed class ReportDeliveryAttempt
{
    public long Id { get; set; }
    public string DeliveryCode { get; set; } = string.Empty;
    public int? ReportScheduleId { get; set; }
    public string ScheduleName { get; set; } = string.Empty;
    public string ApiType { get; set; } = "DDQ";
    public DateTimeOffset GeneratedAt { get; set; }
    public string ReportStatus { get; set; } = "Pending";
    public string EmailStatus { get; set; } = "Not sent";
    public string OverallStatus { get; set; } = "Pending";
    public string Message { get; set; } = string.Empty;
    public ReportScheduleEntity? ReportSchedule { get; set; }
    public List<ReportDeliveryRecipient> Recipients { get; set; } = [];
}

public sealed class ReportDeliveryRecipient
{
    public long Id { get; set; }
    public long ReportDeliveryAttemptId { get; set; }
    public string Email { get; set; } = string.Empty;
    public ReportDeliveryAttempt ReportDeliveryAttempt { get; set; } = null!;
}
