using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Database.Entities;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class AuditLogs : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
    private List<AuditEvent> Events { get; set; } = [];
    private string SearchTerm { get; set; } = string.Empty;
    private string ActorFilter { get; set; } = "All";
    private string ActionFilter { get; set; } = "All";
    private string ResultFilter { get; set; } = "All";
    private DateOnly? FromDate { get; set; }
    private DateOnly? ToDate { get; set; }
    private IEnumerable<string> AvailableActors => Events.Select(item => item.Actor)
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item);
    private IEnumerable<string> AvailableActions => Events.Select(item => item.Action)
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item);
    private List<AuditEvent> FilteredEvents => Events.Where(item =>
            (string.IsNullOrWhiteSpace(SearchTerm) || item.Actor.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || item.Action.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || (item.EntityType?.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ?? false)
                || (item.EntityReference?.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ?? false)
                || item.Result.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || (item.Detail?.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ?? false))
            && (ActorFilter == "All" || item.Actor.Equals(ActorFilter, StringComparison.OrdinalIgnoreCase))
            && (ActionFilter == "All" || item.Action.Equals(ActionFilter, StringComparison.OrdinalIgnoreCase))
            && (ResultFilter == "All" || item.Result.Equals(ResultFilter, StringComparison.OrdinalIgnoreCase))
            && (!FromDate.HasValue || DateOnly.FromDateTime(item.OccurredAt.ToLocalTime().DateTime) >= FromDate.Value)
            && (!ToDate.HasValue || DateOnly.FromDateTime(item.OccurredAt.ToLocalTime().DateTime) <= ToDate.Value))
        .ToList();

    protected override async Task OnInitializedAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        Events = await database.AuditEvents.AsNoTracking().OrderByDescending(item => item.OccurredAt).Take(5000).ToListAsync();
    }

    private void ResetAuditPage() => AuditPage = 1;
    private void ClearFilters()
    {
        SearchTerm = string.Empty;
        ActorFilter = "All";
        ActionFilter = "All";
        ResultFilter = "All";
        FromDate = null;
        ToDate = null;
        AuditPage = 1;
    }
}
