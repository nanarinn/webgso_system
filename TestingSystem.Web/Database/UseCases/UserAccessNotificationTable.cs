namespace TestingSystem.Web.Database.Entities;

public sealed class UserAccessNotification
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? RelatedUserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NotificationType { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string DeliveryStatus { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
