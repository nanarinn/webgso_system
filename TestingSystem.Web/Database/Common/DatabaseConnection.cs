using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Models;
using TestingSystem.Web.Database.Entities;

namespace TestingSystem.Web.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<SolarDocument> SolarDocuments => Set<SolarDocument>();
    public DbSet<LssPlant> LssPlants => Set<LssPlant>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<CollectionJobEntity> CollectionJobs => Set<CollectionJobEntity>();
    public DbSet<RawApiResponse> RawApiResponses => Set<RawApiResponse>();
    public DbSet<ValidationRule> ValidationRules => Set<ValidationRule>();
    public DbSet<DataAnomaly> DataAnomalies => Set<DataAnomaly>();
    public DbSet<CollectionSchedule> CollectionSchedules => Set<CollectionSchedule>();
    public DbSet<ReportExecution> ReportExecutions => Set<ReportExecution>();
    public DbSet<StoredDataRecord> StoredDataRecords => Set<StoredDataRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>()
            .Property(user => user.DisplayName)
            .HasMaxLength(160);

        builder.Entity<SolarDocument>(document =>
        {
            document.Property(item => item.ReferenceNumber).HasMaxLength(40);
            document.Property(item => item.Title).HasMaxLength(240);
            document.Property(item => item.Category).HasMaxLength(80);
            document.Property(item => item.ProjectStage).HasMaxLength(80);
            document.HasIndex(item => item.ReferenceNumber).IsUnique();
        });

        builder.Entity<LssPlant>(plant =>
        {
            plant.Property(item => item.PlantId).HasMaxLength(40);
            plant.Property(item => item.Name).HasMaxLength(160);
            plant.Property(item => item.State).HasMaxLength(80);
            plant.Property(item => item.CapacityMw).HasPrecision(10, 2);
            plant.Property(item => item.DdqApiEndpoint).HasMaxLength(500);
            plant.Property(item => item.MmfApiEndpoint).HasMaxLength(500);
            plant.Property(item => item.ApiConnectionStatus).HasMaxLength(30);
            plant.Property(item => item.LastApiTestSource).HasMaxLength(10);
            plant.Property(item => item.LastApiTestMessage).HasMaxLength(500);
            plant.HasIndex(item => item.PlantId).IsUnique();
        });

        builder.Entity<AuditEvent>(item =>
        {
            item.Property(x => x.Actor).HasMaxLength(160);
            item.Property(x => x.Action).HasMaxLength(160);
            item.Property(x => x.EntityType).HasMaxLength(80);
            item.Property(x => x.EntityReference).HasMaxLength(160);
            item.Property(x => x.Result).HasMaxLength(40);
            item.Property(x => x.Detail).HasMaxLength(2000);
            item.Property(x => x.CorrelationId).HasMaxLength(100);
            item.HasIndex(x => x.OccurredAt);
        });

        builder.Entity<CollectionJobEntity>(item =>
        {
            item.Property(x => x.CollectionId).HasMaxLength(60);
            item.Property(x => x.DataType).HasMaxLength(10);
            item.Property(x => x.Mode).HasMaxLength(20);
            item.Property(x => x.Status).HasMaxLength(30);
            item.Property(x => x.ErrorMessage).HasMaxLength(1000);
            item.HasIndex(x => x.CollectionId).IsUnique();
        });

        builder.Entity<RawApiResponse>(item =>
        {
            item.Property(x => x.ContentType).HasMaxLength(100);
            item.Property(x => x.Sha256).HasMaxLength(64);
            item.HasIndex(x => x.CollectionJobId);
        });

        builder.Entity<ValidationRule>(item =>
        {
            item.Property(x => x.Code).HasMaxLength(40);
            item.Property(x => x.DataType).HasMaxLength(10);
            item.Property(x => x.FieldName).HasMaxLength(100);
            item.Property(x => x.Operator).HasMaxLength(30);
            item.Property(x => x.Severity).HasMaxLength(20);
            item.Property(x => x.MinimumValue).HasPrecision(18, 4);
            item.Property(x => x.MaximumValue).HasPrecision(18, 4);
            item.HasIndex(x => x.Code).IsUnique();
        });

        builder.Entity<DataAnomaly>(item =>
        {
            item.Property(x => x.DataType).HasMaxLength(10);
            item.Property(x => x.FieldName).HasMaxLength(100);
            item.Property(x => x.OriginalValue).HasMaxLength(500);
            item.Property(x => x.Reason).HasMaxLength(1000);
            item.Property(x => x.Status).HasMaxLength(30);
            item.HasIndex(x => x.DetectedAt);
        });

        builder.Entity<CollectionSchedule>(item =>
        {
            item.Property(x => x.DataType).HasMaxLength(10);
            item.Property(x => x.CronExpression).HasMaxLength(100);
            item.Property(x => x.TimeZoneId).HasMaxLength(100);
        });

        builder.Entity<ReportExecution>(item =>
        {
            item.Property(x => x.ReportId).HasMaxLength(60);
            item.Property(x => x.ReportType).HasMaxLength(30);
            item.Property(x => x.DataType).HasMaxLength(10);
            item.Property(x => x.RequestedBy).HasMaxLength(160);
            item.Property(x => x.GenerationStatus).HasMaxLength(30);
            item.Property(x => x.DeliveryStatus).HasMaxLength(30);
            item.Property(x => x.MainFilePath).HasMaxLength(500);
            item.Property(x => x.AnomalyFilePath).HasMaxLength(500);
            item.Property(x => x.ErrorMessage).HasMaxLength(1000);
            item.HasIndex(x => x.ReportId).IsUnique();
        });

        builder.Entity<StoredDataRecord>(item =>
        {
            item.Property(x => x.Category).HasMaxLength(80);
            item.Property(x => x.RecordKey).HasMaxLength(160);
            item.HasIndex(x => new { x.Category, x.RecordKey }).IsUnique();
        });
    }
}
