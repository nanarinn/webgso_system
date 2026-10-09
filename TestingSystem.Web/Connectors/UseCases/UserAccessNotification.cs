using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Models;
using TestingSystem.Web.Services;

namespace TestingSystem.Web.Connectors.UseCases;

public interface IUserAccessNotificationService
{
    Task QueueRequestAsync(ApplicationUser requester, CancellationToken cancellationToken = default);
    Task QueueDecisionAsync(ApplicationUser user, string status, string? role, CancellationToken cancellationToken = default);
}

public sealed class UserAccessNotificationService(
    IDbContextFactory<ApplicationDbContext> contextFactory,
    IReportEmailSender graphEmailSender,
    IConfiguration configuration,
    ILogger<UserAccessNotificationService> logger) : IUserAccessNotificationService
{
    public async Task QueueRequestAsync(ApplicationUser requester, CancellationToken cancellationToken = default)
    {
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var superAdministratorRoleId = await database.Roles.AsNoTracking()
            .Where(role => role.Name == SystemRoles.SuperAdministrator)
            .Select(role => role.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(superAdministratorRoleId)) return;

        var recipients = await (
            from user in database.Users.AsNoTracking()
            join userRole in database.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
            where userRole.RoleId == superAdministratorRoleId
                && user.AccessStatus == UserAccessStatus.Approved
                && user.Email != null && user.Email != ""
            select new NotificationRecipient(user.Id, user.Email!))
            .Distinct()
            .ToListAsync(cancellationToken);

        var message = UserAccessNotificationContent.Request(requester);
        foreach (var recipient in recipients)
        {
            var duplicate = await database.UserAccessNotifications.AsNoTracking().AnyAsync(item =>
                item.UserId == recipient.UserId
                && item.RelatedUserId == requester.Id
                && item.NotificationType == message.Type, cancellationToken);
            if (duplicate) continue;

            var notification = Create(recipient.UserId, requester.Id, recipient.Email, message);
            database.UserAccessNotifications.Add(notification);
            await database.SaveChangesAsync(cancellationToken);
            await DeliverAsync(database, notification, cancellationToken);
        }
    }

    public async Task QueueDecisionAsync(ApplicationUser user, string status, string? role,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(user.Email)) return;
        var notification = Create(user.Id, user.Id, user.Email,
            UserAccessNotificationContent.Decision(user, status, role));

        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        database.UserAccessNotifications.Add(notification);
        await database.SaveChangesAsync(cancellationToken);
        await DeliverAsync(database, notification, cancellationToken);
    }

    private UserAccessNotification Create(string recipientUserId, string relatedUserId, string email,
        UserAccessMessage message) => new()
    {
        UserId = recipientUserId,
        RelatedUserId = relatedUserId,
        Email = email,
        NotificationType = message.Type,
        Subject = message.Subject,
        Body = message.Body,
        DeliveryStatus = EmailEnabled ? "Queued" : "Pending configuration"
    };

    private bool EmailEnabled => configuration.GetValue("ApprovalEmail:Enabled", true);

    private async Task DeliverAsync(ApplicationDbContext database, UserAccessNotification notification,
        CancellationToken cancellationToken)
    {
        if (!EmailEnabled) return;

        try
        {
            ReportEmailSendResult result;
            if (graphEmailSender.IsConfigured)
            {
                var body = $"<p>{WebUtility.HtmlEncode(notification.Body)}</p>";
                result = await graphEmailSender.SendAsync(notification.Subject, body, [notification.Email], [], cancellationToken);
            }
            else
            {
                result = await SendWithSmtpAsync(notification, cancellationToken);
            }

            notification.DeliveryStatus = result.Succeeded ? "Sent" : "Failed";
            notification.SentAt = result.Succeeded ? DateTimeOffset.UtcNow : null;
            notification.ErrorMessage = result.Succeeded ? null : Limit(result.Message);
        }
        catch (Exception exception)
        {
            notification.DeliveryStatus = "Failed";
            notification.ErrorMessage = Limit(exception.GetBaseException().Message);
            logger.LogWarning(exception, "Unable to send user access notification to {Email}.", notification.Email);
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task<ReportEmailSendResult> SendWithSmtpAsync(UserAccessNotification notification,
        CancellationToken cancellationToken)
    {
        var host = configuration["ApprovalEmail:Host"];
        var sender = configuration["ApprovalEmail:Sender"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(sender))
            return new(false, "Microsoft Graph and the approval SMTP gateway are not configured.");

        using var message = new MailMessage(sender, notification.Email, notification.Subject, notification.Body);
        using var client = new SmtpClient(host, configuration.GetValue("ApprovalEmail:Port", 587))
        {
            EnableSsl = configuration.GetValue("ApprovalEmail:UseSsl", true)
        };
        var username = configuration["ApprovalEmail:UserName"];
        var password = configuration["ApprovalEmail:Password"];
        if (!string.IsNullOrWhiteSpace(username)) client.Credentials = new NetworkCredential(username, password);
        await client.SendMailAsync(message, cancellationToken);
        return new(true, "Email sent through the approval SMTP gateway.");
    }

    private static string Limit(string value) => value[..Math.Min(value.Length, 1000)];
    private sealed record NotificationRecipient(string UserId, string Email);
}
