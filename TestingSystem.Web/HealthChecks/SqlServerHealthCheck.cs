using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TestingSystem.Web.Data;

namespace TestingSystem.Web.HealthChecks;

public sealed class SqlServerHealthCheck(IDbContextFactory<ApplicationDbContext> contextFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
            return await database.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Connected to Microsoft SQL Server.")
                : HealthCheckResult.Unhealthy("Microsoft SQL Server connection failed.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Microsoft SQL Server connection failed.", exception);
        }
    }
}
