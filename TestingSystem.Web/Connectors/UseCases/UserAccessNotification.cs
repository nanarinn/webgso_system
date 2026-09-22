using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Models;

namespace TestingSystem.Web.Connectors.UseCases;

public interface IUserAccessNotificationService
{
    Task QueueDecisionAsync(ApplicationUser user, string status, string? role, CancellationToken cancellationToken = default);
}

public sealed class UserAccessNotificationService(
    IDbContextFactory<ApplicationDbContext> contextFactory,
    IConfiguration configuration,
    ILogger<UserAccessNotificationService> logger) : IUserAccessNotificationService
{
    public async Task QueueDecisionAsync(ApplicationUser user, string status, string? role,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(user.Email)) return;
        var approved = status == UserAccessStatus.Approved;
        var notification = new UserAccessNotification
        {
            UserId = user.Id,
            Email = user.Email,
            NotificationType = approved ? "AccessApproved" : "AccessRejected",
            Subject = approved ? "SBLSS repository access approved" : "SBLSS repository access request update",
            Body = approved
                ? $"Your SBLSS repository access has been approved with the {role} role. You may now sign in with Microsoft."
                : "Your SBLSS repository access request was not approved. Contact the system administrator if you need assistance.",
            DeliveryStatus = configuration.GetValue<bool>("ApprovalEmail:Enabled") ? "Queued" : "Pending configuration"
        };

        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        database.UserAccessNotifications.Add(notification);
        await database.SaveChangesAsync(cancellationToken);

        if (!configuration.GetValue<bool>("ApprovalEmail:Enabled")) return;
        try
        {
            var host = configuration["ApprovalEmail:Host"]
                ?? throw new InvalidOperationException("ApprovalEmail:Host is not configured.");
            var sender = configuration["ApprovalEmail:Sender"]
                ?? throw new InvalidOperationException("ApprovalEmail:Sender is not configured.");
            using var message = new MailMessage(sender, notification.Email, notification.Subject, notification.Body);
            using var client = new SmtpClient(host, configuration.GetValue("ApprovalEmail:Port", 587))
            {
                EnableSsl = configuration.GetValue("ApprovalEmail:UseSsl", true)
            };
            var username = configuration["ApprovalEmail:UserName"];
            var password = configuration["ApprovalEmail:Password"];
            if (!string.IsNullOrWhiteSpace(username))
                client.Credentials = new NetworkCredential(username, password);
            await client.SendMailAsync(message, cancellationToken);
            notification.DeliveryStatus = "Sent";
            notification.SentAt = DateTimeOffset.UtcNow;
        }
        catch (Exception exception)
        {
            notification.DeliveryStatus = "Failed";
            notification.ErrorMessage = exception.GetBaseException().Message[..Math.Min(exception.GetBaseException().Message.Length, 1000)];
            logger.LogWarning(exception, "Unable to send user access notification to {Email}.", notification.Email);
        }
        await database.SaveChangesAsync(cancellationToken);
    }
}
