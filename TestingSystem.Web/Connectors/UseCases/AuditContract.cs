using System.Security.Claims;

namespace TestingSystem.Web.Backend.UseCases.UCLSS027Audit;

public interface IAuditTrailService
{
    Task RecordAsync(string actor, string action, string result, string? entityType = null,
        string? entityReference = null, string? detail = null, string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task RecordAsync(ClaimsPrincipal actor, string action, string result, string? entityType = null,
        string? entityReference = null, string? detail = null, string? correlationId = null,
        CancellationToken cancellationToken = default);
}
