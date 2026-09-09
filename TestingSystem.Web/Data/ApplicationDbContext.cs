using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Models;

namespace TestingSystem.Web.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<SolarDocument> SolarDocuments => Set<SolarDocument>();
    public DbSet<LssPlant> LssPlants => Set<LssPlant>();

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
    }
}
