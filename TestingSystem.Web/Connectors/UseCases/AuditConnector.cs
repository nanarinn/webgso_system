using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Database.Entities;

namespace TestingSystem.Web.Backend.UseCases.UCLSS027Audit;

public sealed class AuditTrailService(IDbContextFactory<ApplicationDbContext> contextFactory) : IAuditTrailService
{
    private static readonly string[] SecretTerms = ["password", "token", "credential", "secret"];

    public async Task RecordAsync(string actor, string action, string result, string? entityType = null,
        string? entityReference = null, string? detail = null, string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        database.AuditEvents.Add(new AuditEvent
        {
            Actor = Limit(actor, 160) ?? "System",
            Action = Limit(action, 160) ?? "Unknown",
            Result = Limit(result, 40) ?? "Unknown",
            EntityType = Limit(entityType, 80),
            EntityReference = Limit(entityReference, 160),
            Detail = Redact(detail),
            CorrelationId = Limit(correlationId, 100)
        });
        await database.SaveChangesAsync(cancellationToken);
    }

    private static string? Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        return SecretTerms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase))
            ? "[Sensitive detail removed]"
            : Limit(value, 2000);
    }

    private static string? Limit(string? value, int length) =>
        string.IsNullOrWhiteSpace(value) ? value : value[..Math.Min(value.Length, length)];
}
