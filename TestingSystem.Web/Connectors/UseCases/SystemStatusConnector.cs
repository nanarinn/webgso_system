using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;

namespace TestingSystem.Web.Connectors.UseCases;

public static class SystemStatusConnector
{
    public static IEndpointRouteBuilder MapSystemStatusEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/status", GetStatusAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> GetStatusAsync(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        CancellationToken cancellationToken)
    {
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        return Results.Ok(new
        {
            database = "Microsoft SQL Server",
            connected = await database.Database.CanConnectAsync(cancellationToken),
            users = await database.Users.CountAsync(cancellationToken),
            plants = await database.LssPlants.CountAsync(cancellationToken),
            collectionJobs = await database.CollectionJobs.CountAsync(cancellationToken),
            readings = await database.SolarApiReadings.CountAsync(cancellationToken),
            collectionSchedules = await database.CollectionSchedules.CountAsync(cancellationToken),
            reportSchedules = await database.ReportSchedules.CountAsync(cancellationToken),
            reportExecutions = await database.ReportExecutions.CountAsync(cancellationToken),
            openAnomalies = await database.DataAnomalies.CountAsync(x => x.Status == "Open", cancellationToken)
        });
    }
}
