using System.Text;
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
    private string ResultFilter { get; set; } = "All";
    private List<AuditEvent> FilteredEvents => Events.Where(item =>
            (string.IsNullOrWhiteSpace(SearchTerm) || item.Actor.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || item.Action.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || (item.EntityReference?.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ?? false)
                || item.Result.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
            && (ResultFilter == "All" || item.Result == ResultFilter)).ToList();
    private string ExportDataUri => "data:text/csv;charset=utf-8," + Uri.EscapeDataString(BuildCsv(FilteredEvents));

    protected override async Task OnInitializedAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        Events = await database.AuditEvents.AsNoTracking().OrderByDescending(item => item.OccurredAt).Take(5000).ToListAsync();
    }

    private static string BuildCsv(IEnumerable<AuditEvent> events)
    {
        var csv = new StringBuilder("Timestamp,Actor,Action,Entity Type,Entity Reference,Result,Detail,Correlation ID\r\n");
        foreach (var item in events)
        {
            csv.AppendJoin(',', Csv(item.OccurredAt.ToString("O")), Csv(item.Actor), Csv(item.Action), Csv(item.EntityType), Csv(item.EntityReference), Csv(item.Result), Csv(item.Detail), Csv(item.CorrelationId));
            csv.Append("\r\n");
        }
        return csv.ToString();
    }

    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}
