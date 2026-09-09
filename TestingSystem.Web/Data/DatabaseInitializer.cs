using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Models;

namespace TestingSystem.Web.Data;

public static class DatabaseInitializer
{
    public static async Task InitialiseAsync(IServiceProvider services, IConfiguration configuration)
    {
        using var scope = services.CreateScope();
        var serviceProvider = scope.ServiceProvider;
        var database = serviceProvider.GetRequiredService<ApplicationDbContext>();

        await database.Database.EnsureCreatedAsync();
        await EnsurePlantTableAsync(database);
        await SeedDocumentsAsync(database);
        await SeedPlantsAsync(database);
        await SeedUsersAsync(serviceProvider, configuration);
    }

    private static async Task SeedUsersAsync(IServiceProvider services, IConfiguration configuration)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var seedUsers = configuration.GetSection("SeedUsers").Get<List<SeedUser>>() ?? [];

        foreach (var role in seedUsers.Select(user => user.Role).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole(role)), $"create role '{role}'");
            }
        }

        foreach (var seedUser in seedUsers)
        {
            var user = await userManager.FindByNameAsync(seedUser.UserName);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = seedUser.UserName,
                    DisplayName = seedUser.DisplayName,
                    Email = seedUser.Email,
                    EmailConfirmed = true
                };

                EnsureSucceeded(
                    await userManager.CreateAsync(user, seedUser.Password),
                    $"create user '{seedUser.UserName}'");
            }
            else if (!await userManager.CheckPasswordAsync(user, seedUser.Password))
            {
                var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
                EnsureSucceeded(
                    await userManager.ResetPasswordAsync(user, resetToken, seedUser.Password),
                    $"synchronise password for '{seedUser.UserName}'");
            }

            if (string.IsNullOrWhiteSpace(user.Email) && !string.IsNullOrWhiteSpace(seedUser.Email))
            {
                user.Email = seedUser.Email;
                user.EmailConfirmed = true;
                EnsureSucceeded(await userManager.UpdateAsync(user), $"set email for '{seedUser.UserName}'");
            }

            if (!await userManager.IsInRoleAsync(user, seedUser.Role))
            {
                EnsureSucceeded(
                    await userManager.AddToRoleAsync(user, seedUser.Role),
                    $"assign role '{seedUser.Role}'");
            }
        }
    }

    private static async Task SeedDocumentsAsync(ApplicationDbContext database)
    {
        if (await database.SolarDocuments.AnyAsync())
        {
            return;
        }

        database.SolarDocuments.AddRange(
            new SolarDocument
            {
                ReferenceNumber = "LSS-PLN-001",
                Title = "LSS Project Development Plan",
                Category = "Planning",
                ProjectStage = "Active",
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2)
            },
            new SolarDocument
            {
                ReferenceNumber = "LSS-PPA-002",
                Title = "Power Purchase Agreement Register",
                Category = "Commercial",
                ProjectStage = "Review",
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-4)
            },
            new SolarDocument
            {
                ReferenceNumber = "LSS-GRD-003",
                Title = "Grid Interconnection Assessment",
                Category = "Technical",
                ProjectStage = "Approved",
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-7)
            },
            new SolarDocument
            {
                ReferenceNumber = "LSS-COM-004",
                Title = "Commercial Operation Milestone Tracker",
                Category = "Operations",
                ProjectStage = "Active",
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-10)
            });

        await database.SaveChangesAsync();
    }

    private static async Task EnsurePlantTableAsync(ApplicationDbContext database)
    {
        await database.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'[LssPlants]', N'U') IS NULL
            BEGIN
                CREATE TABLE [LssPlants] (
                    [Id] int NOT NULL IDENTITY,
                    [PlantId] nvarchar(40) NOT NULL,
                    [Name] nvarchar(160) NOT NULL,
                    [State] nvarchar(80) NOT NULL,
                    [CapacityMw] decimal(10,2) NOT NULL,
                    [DdqApiEndpoint] nvarchar(500) NOT NULL,
                    [MmfApiEndpoint] nvarchar(500) NOT NULL,
                    [IsActive] bit NOT NULL,
                    [ApiConnectionStatus] nvarchar(30) NOT NULL CONSTRAINT [DF_LssPlants_ApiConnectionStatus] DEFAULT N'Not tested',
                    [LastApiTestSource] nvarchar(10) NULL,
                    [LastApiTestMessage] nvarchar(500) NULL,
                    [LastApiTestAt] datetimeoffset NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_LssPlants] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_LssPlants_PlantId] ON [LssPlants] ([PlantId]);
            END

            IF COL_LENGTH('LssPlants', 'ApiConnectionStatus') IS NULL
                ALTER TABLE [LssPlants] ADD [ApiConnectionStatus] nvarchar(30) NOT NULL CONSTRAINT [DF_LssPlants_ApiConnectionStatus] DEFAULT N'Not tested';
            IF COL_LENGTH('LssPlants', 'LastApiTestSource') IS NULL
                ALTER TABLE [LssPlants] ADD [LastApiTestSource] nvarchar(10) NULL;
            IF COL_LENGTH('LssPlants', 'LastApiTestMessage') IS NULL
                ALTER TABLE [LssPlants] ADD [LastApiTestMessage] nvarchar(500) NULL;
            IF COL_LENGTH('LssPlants', 'LastApiTestAt') IS NULL
                ALTER TABLE [LssPlants] ADD [LastApiTestAt] datetimeoffset NULL;
            """);
    }

    private static async Task SeedPlantsAsync(ApplicationDbContext database)
    {
        if (await database.LssPlants.AnyAsync())
        {
            return;
        }

        database.LssPlants.AddRange(
            CreatePlant("PLT-BDR-01", "Bidor Solar Plant", "Perak", 50),
            CreatePlant("PLT-KLM-02", "Kulim Solar Plant", "Kedah", 100),
            CreatePlant("PLT-SPG-03", "Sepang Solar Plant", "Selangor", 75),
            CreatePlant("PLT-PGR-04", "Pengerang Solar Plant", "Johor", 90));
        await database.SaveChangesAsync();
    }

    private static LssPlant CreatePlant(string plantId, string name, string state, decimal capacityMw)
    {
        var slug = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
        return new LssPlant
        {
            PlantId = plantId,
            Name = name,
            State = state,
            CapacityMw = capacityMw,
            DdqApiEndpoint = $"https://api.sblss.local/ddq/{slug}",
            MmfApiEndpoint = $"https://api.sblss.local/mmf/{slug}",
            IsActive = true
        };
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = string.Join("; ", result.Errors.Select(error => error.Description));
        throw new InvalidOperationException($"Unable to {operation}: {errors}");
    }

    private sealed class SeedUser
    {
        public string UserName { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
    }
}
