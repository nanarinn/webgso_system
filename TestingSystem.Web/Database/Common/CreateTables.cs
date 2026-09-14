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
        await EnsureFsdCoreTablesAsync(database);
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

    private static async Task EnsureFsdCoreTablesAsync(ApplicationDbContext database)
    {
        // These idempotent statements upgrade the existing prototype database. A formal
        // migration chain should replace this bootstrap before production deployment.
        await database.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'[AuditEvents]', N'U') IS NULL
            CREATE TABLE [AuditEvents] (
                [Id] bigint IDENTITY PRIMARY KEY, [OccurredAt] datetimeoffset NOT NULL,
                [Actor] nvarchar(160) NOT NULL, [Action] nvarchar(160) NOT NULL,
                [EntityType] nvarchar(80) NULL, [EntityReference] nvarchar(160) NULL,
                [Result] nvarchar(40) NOT NULL, [Detail] nvarchar(2000) NULL,
                [CorrelationId] nvarchar(100) NULL);

            IF OBJECT_ID(N'[CollectionJobs]', N'U') IS NULL
            CREATE TABLE [CollectionJobs] (
                [Id] bigint IDENTITY PRIMARY KEY, [CollectionId] nvarchar(60) NOT NULL UNIQUE,
                [PlantId] int NOT NULL, [DataType] nvarchar(10) NOT NULL, [Mode] nvarchar(20) NOT NULL,
                [SubmissionDate] date NOT NULL, [StartedAt] datetimeoffset NOT NULL,
                [CompletedAt] datetimeoffset NULL, [Status] nvarchar(30) NOT NULL,
                [ErrorMessage] nvarchar(1000) NULL);

            IF OBJECT_ID(N'[RawApiResponses]', N'U') IS NULL
            CREATE TABLE [RawApiResponses] (
                [Id] bigint IDENTITY PRIMARY KEY, [CollectionJobId] bigint NOT NULL,
                [ReceivedAt] datetimeoffset NOT NULL, [HttpStatusCode] int NOT NULL,
                [ContentType] nvarchar(100) NOT NULL, [Payload] nvarchar(max) NOT NULL,
                [Sha256] nvarchar(64) NOT NULL);

            IF OBJECT_ID(N'[ValidationRules]', N'U') IS NULL
            CREATE TABLE [ValidationRules] (
                [Id] int IDENTITY PRIMARY KEY, [Code] nvarchar(40) NOT NULL UNIQUE,
                [DataType] nvarchar(10) NOT NULL, [FieldName] nvarchar(100) NOT NULL,
                [Operator] nvarchar(30) NOT NULL, [MinimumValue] decimal(18,4) NULL,
                [MaximumValue] decimal(18,4) NULL, [Severity] nvarchar(20) NOT NULL,
                [IsActive] bit NOT NULL);

            IF OBJECT_ID(N'[DataAnomalies]', N'U') IS NULL
            CREATE TABLE [DataAnomalies] (
                [Id] bigint IDENTITY PRIMARY KEY, [CollectionJobId] bigint NOT NULL,
                [ValidationRuleId] int NULL, [DataType] nvarchar(10) NOT NULL,
                [FieldName] nvarchar(100) NOT NULL, [OriginalValue] nvarchar(500) NULL,
                [Reason] nvarchar(1000) NOT NULL, [Status] nvarchar(30) NOT NULL,
                [DetectedAt] datetimeoffset NOT NULL);

            IF OBJECT_ID(N'[CollectionSchedules]', N'U') IS NULL
            CREATE TABLE [CollectionSchedules] (
                [Id] int IDENTITY PRIMARY KEY, [PlantId] int NOT NULL,
                [DataType] nvarchar(10) NOT NULL, [CronExpression] nvarchar(100) NOT NULL,
                [TimeZoneId] nvarchar(100) NOT NULL, [RetryCount] int NOT NULL,
                [IsActive] bit NOT NULL);

            IF OBJECT_ID(N'[ReportExecutions]', N'U') IS NULL
            CREATE TABLE [ReportExecutions] (
                [Id] bigint IDENTITY PRIMARY KEY, [ReportId] nvarchar(60) NOT NULL UNIQUE,
                [ReportType] nvarchar(30) NOT NULL, [DataType] nvarchar(10) NOT NULL,
                [RequestedBy] nvarchar(160) NOT NULL, [RequestedAt] datetimeoffset NOT NULL,
                [GenerationStatus] nvarchar(30) NOT NULL, [DeliveryStatus] nvarchar(30) NOT NULL,
                [MainFilePath] nvarchar(500) NULL, [AnomalyFilePath] nvarchar(500) NULL,
                [ErrorMessage] nvarchar(1000) NULL);

            IF OBJECT_ID(N'[StoredDataRecords]', N'U') IS NULL
            CREATE TABLE [StoredDataRecords] (
                [Id] bigint IDENTITY PRIMARY KEY, [Category] nvarchar(80) NOT NULL,
                [RecordKey] nvarchar(160) NOT NULL, [JsonData] nvarchar(max) NOT NULL,
                [UpdatedAt] datetimeoffset NOT NULL,
                CONSTRAINT [UQ_StoredDataRecords_Category_RecordKey] UNIQUE ([Category], [RecordKey]));
            """);
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
