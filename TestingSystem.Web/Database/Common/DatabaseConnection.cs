using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Models;
using TestingSystem.Web.Database.Entities;

namespace TestingSystem.Web.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<LssPlant> LssPlants => Set<LssPlant>();
    public DbSet<PlantApiConfiguration> PlantApiConfigurations => Set<PlantApiConfiguration>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<CollectionJobEntity> CollectionJobs => Set<CollectionJobEntity>();
    public DbSet<RawApiResponse> RawApiResponses => Set<RawApiResponse>();
    public DbSet<ValidationRule> ValidationRules => Set<ValidationRule>();
    public DbSet<DataAnomaly> DataAnomalies => Set<DataAnomaly>();
    public DbSet<CollectionSchedule> CollectionSchedules => Set<CollectionSchedule>();
    public DbSet<ReportExecution> ReportExecutions => Set<ReportExecution>();
    public DbSet<SolarApiReading> SolarApiReadings => Set<SolarApiReading>();
    public DbSet<ReportScheduleEntity> ReportSchedules => Set<ReportScheduleEntity>();
    public DbSet<ReportScheduleRecipient> ReportScheduleRecipients => Set<ReportScheduleRecipient>();
    public DbSet<ReportDeliveryAttempt> ReportDeliveryAttempts => Set<ReportDeliveryAttempt>();
    public DbSet<ReportDeliveryRecipient> ReportDeliveryRecipients => Set<ReportDeliveryRecipient>();
    public DbSet<UserAccessNotification> UserAccessNotifications => Set<UserAccessNotification>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>()
            .Property(user => user.DisplayName)
            .HasMaxLength(160);
        builder.Entity<ApplicationUser>().Property(user => user.AccessStatus).HasMaxLength(20);
        builder.Entity<ApplicationUser>().Property(user => user.EntraObjectId).HasMaxLength(36);
        builder.Entity<ApplicationUser>().Property(user => user.AccessReviewedBy).HasMaxLength(160);
        builder.Entity<ApplicationUser>().Property(user => user.AccessReviewNote).HasMaxLength(500);

        builder.Entity<UserAccessNotification>(item =>
        {
            item.Property(x => x.UserId).HasMaxLength(450);
            item.Property(x => x.Email).HasMaxLength(320);
            item.Property(x => x.NotificationType).HasMaxLength(40);
            item.Property(x => x.Subject).HasMaxLength(300);
            item.Property(x => x.Body).HasMaxLength(2000);
            item.Property(x => x.DeliveryStatus).HasMaxLength(30);
            item.Property(x => x.ErrorMessage).HasMaxLength(1000);
            item.HasIndex(x => x.CreatedAt);
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

        builder.Entity<PlantApiConfiguration>(api =>
        {
            api.Property(x => x.ApiType).HasMaxLength(10);
            api.Property(x => x.DataType).HasMaxLength(40);
            api.Property(x => x.ApiLink).HasMaxLength(1000);
            api.HasIndex(x => new { x.PlantId, x.ApiType, x.DataType }).IsUnique();
            api.HasOne(x => x.Plant).WithMany(x => x.ApiConfigurations).HasForeignKey(x => x.PlantId).OnDelete(DeleteBehavior.Cascade);
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
            item.Property(x => x.DataType).HasColumnType("nvarchar(max)");
            item.Property(x => x.Mode).HasColumnType("nvarchar(max)");
            item.Property(x => x.Status).HasMaxLength(30);
            item.Property(x => x.ErrorMessage).HasColumnType("nvarchar(max)");
            item.Property(x => x.ResponseMessage).HasMaxLength(1000);
            item.HasIndex(x => x.CollectionId).IsUnique();
            item.HasOne<LssPlant>().WithMany().HasForeignKey(x => x.PlantId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RawApiResponse>(item =>
        {
            item.Property(x => x.ContentType).HasColumnType("nvarchar(max)");
            item.Property(x => x.Payload).HasColumnType("nvarchar(max)");
            item.Property(x => x.Sha256).HasMaxLength(64);
            item.HasIndex(x => x.CollectionJobId);
            item.HasOne<CollectionJobEntity>().WithMany().HasForeignKey(x => x.CollectionJobId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ValidationRule>(item =>
        {
            item.Property(x => x.Code).HasMaxLength(40);
            item.Property(x => x.DataType).HasMaxLength(10);
            item.Property(x => x.FieldName).HasMaxLength(100);
            item.Property(x => x.Name).HasMaxLength(200);
            item.Property(x => x.Description).HasMaxLength(1000);
            item.Property(x => x.Condition).HasMaxLength(300);
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
            item.HasOne<CollectionJobEntity>().WithMany().HasForeignKey(x => x.CollectionJobId).OnDelete(DeleteBehavior.Cascade);
            item.HasOne<ValidationRule>().WithMany().HasForeignKey(x => x.ValidationRuleId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<CollectionSchedule>(item =>
        {
            item.Property(x => x.ScheduleCode).HasMaxLength(40);
            item.Property(x => x.Name).HasMaxLength(160);
            item.Property(x => x.ApiType).HasMaxLength(10);
            item.Property(x => x.DataType).HasMaxLength(40);
            item.Property(x => x.Interval).HasMaxLength(30);
            item.Property(x => x.RunTime).HasMaxLength(5);
            item.Property(x => x.ScheduleDay).HasMaxLength(20);
            item.Property(x => x.ScheduleMonths).HasMaxLength(50);
            item.Property(x => x.CronExpression).HasMaxLength(100);
            item.Property(x => x.TimeZoneId).HasMaxLength(100);
            item.HasIndex(x => x.ScheduleCode).IsUnique();
            item.HasOne<LssPlant>().WithMany().HasForeignKey(x => x.PlantId).OnDelete(DeleteBehavior.Cascade);
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
            item.Property(x => x.Name).HasMaxLength(200);
            item.Property(x => x.DataSelection).HasMaxLength(100);
            item.Property(x => x.Description).HasMaxLength(2000);
            item.HasIndex(x => x.ReportId).IsUnique();
        });

        builder.Entity<ReportScheduleEntity>(item =>
        {
            item.Property(x => x.ScheduleCode).HasMaxLength(60); item.HasIndex(x => x.ScheduleCode).IsUnique();
            item.Property(x => x.Name).HasMaxLength(200); item.Property(x => x.ApiType).HasMaxLength(10);
            item.Property(x => x.DataType).HasMaxLength(50); item.Property(x => x.ScheduleType).HasMaxLength(30);
            item.Property(x => x.ScheduleDay).HasMaxLength(20); item.Property(x => x.ScheduleMonths).HasMaxLength(50);
            item.Property(x => x.ExecutionTime).HasMaxLength(5);
        });
        builder.Entity<ReportScheduleRecipient>(item =>
        {
            item.Property(x => x.Email).HasMaxLength(320); item.HasIndex(x => new { x.ReportScheduleId, x.Email }).IsUnique();
            item.HasOne(x => x.ReportSchedule).WithMany(x => x.Recipients).HasForeignKey(x => x.ReportScheduleId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<ReportDeliveryAttempt>(item =>
        {
            item.Property(x => x.DeliveryCode).HasMaxLength(60); item.HasIndex(x => x.DeliveryCode).IsUnique();
            item.Property(x => x.ScheduleName).HasMaxLength(200); item.Property(x => x.ApiType).HasMaxLength(10);
            item.Property(x => x.ReportStatus).HasMaxLength(30); item.Property(x => x.EmailStatus).HasMaxLength(30);
            item.Property(x => x.OverallStatus).HasMaxLength(30); item.Property(x => x.Message).HasMaxLength(2000);
            item.HasOne(x => x.ReportSchedule).WithMany().HasForeignKey(x => x.ReportScheduleId).OnDelete(DeleteBehavior.SetNull);
        });
        builder.Entity<ReportDeliveryRecipient>(item =>
        {
            item.Property(x => x.Email).HasMaxLength(320);
            item.HasOne(x => x.ReportDeliveryAttempt).WithMany(x => x.Recipients).HasForeignKey(x => x.ReportDeliveryAttemptId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SolarApiReading>(item =>
        {
            item.Property(x => x.DataType).HasMaxLength(30);
            item.Property(x => x.Parameter).HasColumnType("nvarchar(max)");
            item.Property(x => x.Value).HasPrecision(18, 6);
            item.Property(x => x.Unit).HasColumnType("nvarchar(max)");
            item.Property(x => x.ErrorMessage).HasColumnType("nvarchar(max)");
            item.Property(x => x.SyntheticReason).HasColumnType("nvarchar(max)");
            item.HasIndex(x => new { x.DataType, x.PlantId, x.IntervalStart });
            item.HasOne<CollectionJobEntity>().WithMany().HasForeignKey(x => x.CollectionJobId).OnDelete(DeleteBehavior.Cascade);
            item.HasOne<LssPlant>().WithMany().HasForeignKey(x => x.PlantId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
