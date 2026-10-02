using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Models;
using TestingSystem.Web.Services;

namespace TestingSystem.Web.Data;

public static class DatabaseInitializer
{
    public static async Task InitialiseAsync(IServiceProvider services, IConfiguration configuration)
    {
        using var scope = services.CreateScope();
        var serviceProvider = scope.ServiceProvider;
        var database = serviceProvider.GetRequiredService<ApplicationDbContext>();

        // Schema upgrades can legitimately take longer than normal page queries when an
        // existing installation contains a large reading history. Keep the larger timeout
        // scoped to startup; normal application commands retain their configured timeout.
        database.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));

        await database.Database.EnsureCreatedAsync();
        await EnsureUserAccessSchemaAsync(database);
        await EnsurePlantTableAsync(database);
        await EnsurePlantApiConfigurationsAsync(database);
        await EnsureSolarApiReadingTableAsync(database);
        await EnsureFsdCoreTablesAsync(database);
        await ValidationRuleCatalog.SynchronizeAsync(database);
        await SeedUsersAsync(serviceProvider, configuration);
        if (configuration.GetValue<bool>("Authentication:MicrosoftOnly"))
            await RemoveLocalUsersAsync(serviceProvider);
    }

    private static async Task RemoveLocalUsersAsync(IServiceProvider services)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var localUsers = await userManager.Users.Where(user => !user.IsEntraUser).ToListAsync();
        foreach (var user in localUsers)
            EnsureSucceeded(await userManager.DeleteAsync(user), $"remove local account '{user.UserName}'");
    }

    private static async Task EnsureUserAccessSchemaAsync(ApplicationDbContext database)
    {
        await database.Database.ExecuteSqlRawAsync("""
            IF COL_LENGTH('AspNetUsers', 'AccessStatus') IS NULL
                ALTER TABLE [AspNetUsers] ADD [AccessStatus] nvarchar(20) NOT NULL CONSTRAINT [DF_AspNetUsers_AccessStatus] DEFAULT N'Approved';
            IF COL_LENGTH('AspNetUsers', 'IsEntraUser') IS NULL
                ALTER TABLE [AspNetUsers] ADD [IsEntraUser] bit NOT NULL CONSTRAINT [DF_AspNetUsers_IsEntraUser] DEFAULT 0;
            IF COL_LENGTH('AspNetUsers', 'EntraObjectId') IS NULL ALTER TABLE [AspNetUsers] ADD [EntraObjectId] nvarchar(36) NULL;
            IF COL_LENGTH('AspNetUsers', 'AccessRequestedAt') IS NULL ALTER TABLE [AspNetUsers] ADD [AccessRequestedAt] datetimeoffset NULL;
            IF COL_LENGTH('AspNetUsers', 'AccessReviewedAt') IS NULL ALTER TABLE [AspNetUsers] ADD [AccessReviewedAt] datetimeoffset NULL;
            IF COL_LENGTH('AspNetUsers', 'AccessReviewedBy') IS NULL ALTER TABLE [AspNetUsers] ADD [AccessReviewedBy] nvarchar(160) NULL;
            IF COL_LENGTH('AspNetUsers', 'AccessReviewNote') IS NULL ALTER TABLE [AspNetUsers] ADD [AccessReviewNote] nvarchar(500) NULL;

            IF OBJECT_ID(N'[UserAccessNotifications]', N'U') IS NULL
            BEGIN
                CREATE TABLE [UserAccessNotifications] (
                    [Id] bigint IDENTITY PRIMARY KEY, [UserId] nvarchar(450) NOT NULL,
                    [Email] nvarchar(320) NOT NULL, [NotificationType] nvarchar(40) NOT NULL,
                    [Subject] nvarchar(300) NOT NULL, [Body] nvarchar(2000) NOT NULL,
                    [DeliveryStatus] nvarchar(30) NOT NULL, [ErrorMessage] nvarchar(1000) NULL,
                    [CreatedAt] datetimeoffset NOT NULL, [SentAt] datetimeoffset NULL);
                CREATE INDEX [IX_UserAccessNotifications_CreatedAt] ON [UserAccessNotifications] ([CreatedAt]);
            END
            """);
    }

    private static async Task SeedUsersAsync(IServiceProvider services, IConfiguration configuration)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var seedUsers = configuration.GetSection("SeedUsers").Get<List<SeedUser>>() ?? [];

        foreach (var role in new[] { SystemRoles.SuperAdministrator, SystemRoles.Administrator, SystemRoles.Staff, SystemRoles.LegacyReviewer })
        {
            if (!await roleManager.RoleExistsAsync(role))
                EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole(role)), $"create role '{role}'");
        }

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
                    EmailConfirmed = true,
                    AccessStatus = UserAccessStatus.Approved
                };

                EnsureSucceeded(
                    await userManager.CreateAsync(user, seedUser.Password),
                    $"create user '{seedUser.UserName}'");
            }

            if (user.AccessStatus != UserAccessStatus.Approved)
            {
                user.AccessStatus = UserAccessStatus.Approved;
                EnsureSucceeded(await userManager.UpdateAsync(user), $"approve seed user '{seedUser.UserName}'");
            }
            if (!await userManager.CheckPasswordAsync(user, seedUser.Password))
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

            var currentRoles = await userManager.GetRolesAsync(user);
            var obsoleteRoles = currentRoles.Where(role =>
                role is SystemRoles.SuperAdministrator or SystemRoles.Administrator or SystemRoles.Staff or SystemRoles.LegacyReviewer
                && !role.Equals(seedUser.Role, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (obsoleteRoles.Length > 0)
                EnsureSucceeded(await userManager.RemoveFromRolesAsync(user, obsoleteRoles), $"synchronise roles for '{seedUser.UserName}'");
        }
    }

    private static async Task EnsureSolarApiReadingTableAsync(ApplicationDbContext database)
    {
        await database.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'[SolarApiReadings]', N'U') IS NULL
            BEGIN
                CREATE TABLE [SolarApiReadings] (
                    [Id] bigint IDENTITY PRIMARY KEY, [CollectionJobId] bigint NOT NULL,
                    [PlantId] int NOT NULL, [DataType] nvarchar(30) NOT NULL,
                    [SubmissionDateTime] datetime2 NOT NULL, [IntervalStart] datetime2 NOT NULL,
                    [IntervalEnd] datetime2 NULL, [IntervalMinutes] int NULL,
                    [Parameter] nvarchar(100) NOT NULL, [Value] decimal(18,6) NOT NULL,
                    [Unit] nvarchar(30) NOT NULL, [ErrorCode] int NULL,
                    [ErrorMessage] nvarchar(500) NULL, [IsSynthetic] bit NOT NULL,
                    [SyntheticReason] nvarchar(500) NULL);
                CREATE INDEX [IX_SolarApiReadings_Query]
                    ON [SolarApiReadings] ([DataType], [PlantId], [IntervalStart]);
            END

            ALTER TABLE [SolarApiReadings] ALTER COLUMN [Parameter] nvarchar(100) NOT NULL;
            ALTER TABLE [SolarApiReadings] ALTER COLUMN [Unit] nvarchar(30) NOT NULL;
            ALTER TABLE [SolarApiReadings] ALTER COLUMN [ErrorMessage] nvarchar(500) NULL;
            ALTER TABLE [SolarApiReadings] ALTER COLUMN [SyntheticReason] nvarchar(500) NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SolarApiReadings_ReadModel' AND object_id = OBJECT_ID(N'[SolarApiReadings]'))
                CREATE INDEX [IX_SolarApiReadings_ReadModel] ON [SolarApiReadings]
                    ([DataType], [PlantId], [Parameter], [IntervalStart])
                    INCLUDE ([Value], [Unit], [SubmissionDateTime], [IntervalMinutes], [CollectionJobId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SolarApiReadings_Report' AND object_id = OBJECT_ID(N'[SolarApiReadings]'))
                CREATE INDEX [IX_SolarApiReadings_Report] ON [SolarApiReadings]
                    ([DataType], [SubmissionDateTime])
                    INCLUDE ([PlantId], [Parameter], [IntervalStart], [Value], [CollectionJobId]);

            EXEC(N'CREATE OR ALTER VIEW [dbo].[DdqApiReadings] AS
                SELECT * FROM [dbo].[SolarApiReadings] WHERE [DataType] <> N''MMF'';');
            EXEC(N'CREATE OR ALTER VIEW [dbo].[MmfApiReadings] AS
                SELECT * FROM [dbo].[SolarApiReadings] WHERE [DataType] = N''MMF'';');
            """);
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

            IF OBJECT_ID(N'[SchemaUpgrades]', N'U') IS NULL
            CREATE TABLE [SchemaUpgrades] (
                [Name] nvarchar(160) NOT NULL PRIMARY KEY,
                [CompletedAt] datetimeoffset NOT NULL);

            IF OBJECT_ID(N'[CollectionJobs]', N'U') IS NULL
            CREATE TABLE [CollectionJobs] (
                [Id] bigint IDENTITY PRIMARY KEY, [CollectionId] nvarchar(60) NOT NULL UNIQUE,
                [PlantId] int NOT NULL, [CollectionScheduleId] int NULL,
                [DataType] nvarchar(10) NOT NULL, [Mode] nvarchar(20) NOT NULL,
                [SubmissionDate] date NOT NULL, [SubmissionDateTime] datetime2 NOT NULL,
                [StartedAt] datetimeoffset NOT NULL,
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
                [Name] nvarchar(200) NOT NULL, [Description] nvarchar(1000) NOT NULL, [Condition] nvarchar(300) NOT NULL,
                [Operator] nvarchar(30) NOT NULL, [MinimumValue] decimal(18,4) NULL,
                [MaximumValue] decimal(18,4) NULL, [ChangeThreshold] decimal(18,4) NULL,
                [CheckMissing] bit NOT NULL DEFAULT 0, [RuleSource] nvarchar(20) NOT NULL DEFAULT N'Fixed',
                [Severity] nvarchar(20) NOT NULL, [IsActive] bit NOT NULL,
                [CreatedBy] nvarchar(160) NULL, [CreatedAt] datetimeoffset NOT NULL DEFAULT SYSUTCDATETIME(),
                [UpdatedAt] datetimeoffset NULL);
            IF COL_LENGTH('ValidationRules', 'Name') IS NULL ALTER TABLE [ValidationRules] ADD [Name] nvarchar(200) NOT NULL CONSTRAINT [DF_ValidationRules_Name] DEFAULT '';
            IF COL_LENGTH('ValidationRules', 'Description') IS NULL ALTER TABLE [ValidationRules] ADD [Description] nvarchar(1000) NOT NULL CONSTRAINT [DF_ValidationRules_Description] DEFAULT '';
            IF COL_LENGTH('ValidationRules', 'Condition') IS NULL ALTER TABLE [ValidationRules] ADD [Condition] nvarchar(300) NOT NULL CONSTRAINT [DF_ValidationRules_Condition] DEFAULT '';
            IF COL_LENGTH('ValidationRules', 'ChangeThreshold') IS NULL ALTER TABLE [ValidationRules] ADD [ChangeThreshold] decimal(18,4) NULL;
            IF COL_LENGTH('ValidationRules', 'CheckMissing') IS NULL ALTER TABLE [ValidationRules] ADD [CheckMissing] bit NOT NULL CONSTRAINT [DF_ValidationRules_CheckMissing] DEFAULT 0;
            IF COL_LENGTH('ValidationRules', 'RuleSource') IS NULL ALTER TABLE [ValidationRules] ADD [RuleSource] nvarchar(20) NOT NULL CONSTRAINT [DF_ValidationRules_RuleSource] DEFAULT N'Fixed';
            IF COL_LENGTH('ValidationRules', 'CreatedBy') IS NULL ALTER TABLE [ValidationRules] ADD [CreatedBy] nvarchar(160) NULL;
            IF COL_LENGTH('ValidationRules', 'CreatedAt') IS NULL ALTER TABLE [ValidationRules] ADD [CreatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_ValidationRules_CreatedAt] DEFAULT SYSUTCDATETIME();
            IF COL_LENGTH('ValidationRules', 'UpdatedAt') IS NULL ALTER TABLE [ValidationRules] ADD [UpdatedAt] datetimeoffset NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ValidationRules_ConfiguredSignature' AND [object_id] = OBJECT_ID(N'[ValidationRules]'))
               AND NOT EXISTS (
                    SELECT [DataType], UPPER(LTRIM(RTRIM([FieldName]))), [MinimumValue], [MaximumValue], [ChangeThreshold], [CheckMissing]
                    FROM [ValidationRules]
                    WHERE [RuleSource] = N'Configured'
                    GROUP BY [DataType], UPPER(LTRIM(RTRIM([FieldName]))), [MinimumValue], [MaximumValue], [ChangeThreshold], [CheckMissing]
                    HAVING COUNT(*) > 1)
                CREATE UNIQUE INDEX [IX_ValidationRules_ConfiguredSignature]
                    ON [ValidationRules] ([DataType], [FieldName], [MinimumValue], [MaximumValue], [ChangeThreshold], [CheckMissing])
                    WHERE [RuleSource] = N'Configured';

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
                [ScheduleCode] nvarchar(40) NOT NULL DEFAULT '', [Name] nvarchar(160) NOT NULL DEFAULT '',
                [ApiType] nvarchar(10) NOT NULL DEFAULT 'DDQ', [DataType] nvarchar(40) NOT NULL,
                [Interval] nvarchar(30) NOT NULL DEFAULT '15 minutes', [RunTime] nvarchar(5) NOT NULL DEFAULT '00:15', [ScheduleDay] nvarchar(20) NOT NULL DEFAULT '', [ScheduleMonths] nvarchar(50) NOT NULL DEFAULT '',
                [CronExpression] nvarchar(100) NOT NULL,
                [TimeZoneId] nvarchar(100) NOT NULL, [RetryCount] int NOT NULL,
                [IsActive] bit NOT NULL);

            IF COL_LENGTH('CollectionSchedules', 'ScheduleCode') IS NULL ALTER TABLE [CollectionSchedules] ADD [ScheduleCode] nvarchar(40) NOT NULL CONSTRAINT [DF_CollectionSchedules_ScheduleCode] DEFAULT '';
            IF COL_LENGTH('CollectionSchedules', 'Name') IS NULL ALTER TABLE [CollectionSchedules] ADD [Name] nvarchar(160) NOT NULL CONSTRAINT [DF_CollectionSchedules_Name] DEFAULT '';
            IF COL_LENGTH('CollectionSchedules', 'ApiType') IS NULL ALTER TABLE [CollectionSchedules] ADD [ApiType] nvarchar(10) NOT NULL CONSTRAINT [DF_CollectionSchedules_ApiType] DEFAULT 'DDQ';
            IF COL_LENGTH('CollectionSchedules', 'Interval') IS NULL ALTER TABLE [CollectionSchedules] ADD [Interval] nvarchar(30) NOT NULL CONSTRAINT [DF_CollectionSchedules_Interval] DEFAULT '15 minutes';
            IF COL_LENGTH('CollectionSchedules', 'RunTime') IS NULL ALTER TABLE [CollectionSchedules] ADD [RunTime] nvarchar(5) NOT NULL CONSTRAINT [DF_CollectionSchedules_RunTime] DEFAULT '00:15';
            IF COL_LENGTH('CollectionSchedules', 'RunTime') IS NOT NULL ALTER TABLE [CollectionSchedules] ALTER COLUMN [RunTime] nvarchar(5) NOT NULL;
            IF COL_LENGTH('CollectionSchedules', 'ScheduleDay') IS NULL ALTER TABLE [CollectionSchedules] ADD [ScheduleDay] nvarchar(20) NOT NULL CONSTRAINT [DF_CollectionSchedules_ScheduleDay] DEFAULT '';
            IF COL_LENGTH('CollectionSchedules', 'ScheduleMonths') IS NULL ALTER TABLE [CollectionSchedules] ADD [ScheduleMonths] nvarchar(50) NOT NULL CONSTRAINT [DF_CollectionSchedules_ScheduleMonths] DEFAULT '';
            IF COL_LENGTH('CollectionSchedules', 'DataType') IS NOT NULL ALTER TABLE [CollectionSchedules] ALTER COLUMN [DataType] nvarchar(40) NOT NULL;
            ;WITH duplicateSchedules AS (
                SELECT [Id], [ScheduleCode], ROW_NUMBER() OVER (PARTITION BY [ScheduleCode] ORDER BY [Id]) AS [DuplicateNumber]
                FROM [CollectionSchedules]
            )
            UPDATE scheduleRow SET [ScheduleCode] = CONCAT(N'SCH-MIGRATED-', scheduleRow.[Id])
            FROM [CollectionSchedules] scheduleRow
            INNER JOIN duplicateSchedules duplicateRow ON duplicateRow.[Id] = scheduleRow.[Id]
            WHERE NULLIF(LTRIM(RTRIM(scheduleRow.[ScheduleCode])), N'') IS NULL OR duplicateRow.[DuplicateNumber] > 1;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_CollectionSchedules_ScheduleCode' AND [object_id] = OBJECT_ID(N'[CollectionSchedules]'))
                CREATE UNIQUE INDEX [IX_CollectionSchedules_ScheduleCode] ON [CollectionSchedules] ([ScheduleCode]);

            IF COL_LENGTH('CollectionJobs', 'CollectionScheduleId') IS NULL
                EXEC(N'ALTER TABLE [CollectionJobs] ADD [CollectionScheduleId] int NULL;');
            EXEC(N'UPDATE job SET [CollectionScheduleId] = scheduleRow.[Id]
                FROM [CollectionJobs] job
                INNER JOIN [CollectionSchedules] scheduleRow
                    ON job.[CollectionId] LIKE CONCAT(N''COL-SCH-'', scheduleRow.[Id], N''-%'')
                WHERE job.[CollectionScheduleId] IS NULL;');
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_CollectionJobs_CollectionSchedule' AND [object_id] = OBJECT_ID(N'[CollectionJobs]'))
                EXEC(N'CREATE INDEX [IX_CollectionJobs_CollectionSchedule]
                    ON [CollectionJobs] ([CollectionScheduleId], [SubmissionDateTime], [Status]);');

            IF OBJECT_ID(N'[ReportExecutions]', N'U') IS NULL
            CREATE TABLE [ReportExecutions] (
                [Id] bigint IDENTITY PRIMARY KEY, [ReportId] nvarchar(60) NOT NULL UNIQUE,
                [ReportType] nvarchar(30) NOT NULL, [DataType] nvarchar(10) NOT NULL,
                [RequestedBy] nvarchar(160) NOT NULL, [RequestedAt] datetimeoffset NOT NULL,
                [GenerationStatus] nvarchar(30) NOT NULL, [DeliveryStatus] nvarchar(30) NOT NULL,
                [MainFilePath] nvarchar(500) NULL, [AnomalyFilePath] nvarchar(500) NULL,
                [ErrorMessage] nvarchar(1000) NULL);

            IF COL_LENGTH('CollectionJobs', 'ResponseMessage') IS NULL ALTER TABLE [CollectionJobs] ADD [ResponseMessage] nvarchar(1000) NOT NULL CONSTRAINT [DF_CollectionJobs_ResponseMessage] DEFAULT '';
            IF COL_LENGTH('CollectionJobs', 'SubmissionDateTime') IS NULL
            BEGIN
                ALTER TABLE [CollectionJobs] ADD [SubmissionDateTime] datetime2 NULL;
                EXEC(N'UPDATE [CollectionJobs] SET [SubmissionDateTime] = DATEADD(minute, DATEDIFF(minute, 0, CONVERT(datetime2, SWITCHOFFSET([StartedAt], ''+08:00''))), 0) WHERE [SubmissionDateTime] IS NULL');
                ALTER TABLE [CollectionJobs] ALTER COLUMN [SubmissionDateTime] datetime2 NOT NULL;
            END;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CollectionJobs_SubmissionDateTime' AND object_id = OBJECT_ID(N'[CollectionJobs]'))
                CREATE INDEX [IX_CollectionJobs_SubmissionDateTime] ON [CollectionJobs] ([DataType], [SubmissionDateTime], [PlantId]);
            IF NOT EXISTS (SELECT 1 FROM [SchemaUpgrades] WHERE [Name] = N'AlignReadingSubmissionDateTimeWithSchedule')
            BEGIN
                UPDATE reading SET [SubmissionDateTime] = job.[SubmissionDateTime]
                FROM [SolarApiReadings] reading
                INNER JOIN [CollectionJobs] job ON job.[Id] = reading.[CollectionJobId]
                WHERE reading.[SubmissionDateTime] <> job.[SubmissionDateTime];

                INSERT INTO [SchemaUpgrades] ([Name], [CompletedAt])
                VALUES (N'AlignReadingSubmissionDateTimeWithSchedule', SYSDATETIMEOFFSET());
            END;
            IF COL_LENGTH('CollectionJobs', 'DurationSeconds') IS NULL ALTER TABLE [CollectionJobs] ADD [DurationSeconds] float NOT NULL CONSTRAINT [DF_CollectionJobs_DurationSeconds] DEFAULT 0;
            IF COL_LENGTH('CollectionJobs', 'RequestedBy') IS NULL ALTER TABLE [CollectionJobs] ADD [RequestedBy] nvarchar(160) NOT NULL CONSTRAINT [DF_CollectionJobs_RequestedBy] DEFAULT N'System';
            IF COL_LENGTH('CollectionJobs', 'RetryCount') IS NULL ALTER TABLE [CollectionJobs] ADD [RetryCount] int NOT NULL CONSTRAINT [DF_CollectionJobs_RetryCount] DEFAULT 0;
            IF COL_LENGTH('CollectionJobs', 'ReplaceExisting') IS NULL ALTER TABLE [CollectionJobs] ADD [ReplaceExisting] bit NOT NULL CONSTRAINT [DF_CollectionJobs_ReplaceExisting] DEFAULT 0;

            IF COL_LENGTH('ReportExecutions', 'Name') IS NULL ALTER TABLE [ReportExecutions] ADD [Name] nvarchar(200) NOT NULL CONSTRAINT [DF_ReportExecutions_Name] DEFAULT '';
            IF COL_LENGTH('ReportExecutions', 'SubmissionDate') IS NULL ALTER TABLE [ReportExecutions] ADD [SubmissionDate] datetime2 NOT NULL CONSTRAINT [DF_ReportExecutions_SubmissionDate] DEFAULT '2000-01-01';
            IF COL_LENGTH('ReportExecutions', 'DataSelection') IS NULL ALTER TABLE [ReportExecutions] ADD [DataSelection] nvarchar(100) NOT NULL CONSTRAINT [DF_ReportExecutions_DataSelection] DEFAULT '';
            IF COL_LENGTH('ReportExecutions', 'IntervalMinutes') IS NULL ALTER TABLE [ReportExecutions] ADD [IntervalMinutes] int NOT NULL CONSTRAINT [DF_ReportExecutions_IntervalMinutes] DEFAULT 15;
            IF COL_LENGTH('ReportExecutions', 'DataRangeStart') IS NULL ALTER TABLE [ReportExecutions] ADD [DataRangeStart] datetime2 NULL;
            IF COL_LENGTH('ReportExecutions', 'DataRangeEnd') IS NULL ALTER TABLE [ReportExecutions] ADD [DataRangeEnd] datetime2 NULL;
            IF COL_LENGTH('ReportExecutions', 'Description') IS NULL ALTER TABLE [ReportExecutions] ADD [Description] nvarchar(2000) NOT NULL CONSTRAINT [DF_ReportExecutions_Description] DEFAULT '';
            IF COL_LENGTH('ReportExecutions', 'PlantCount') IS NULL ALTER TABLE [ReportExecutions] ADD [PlantCount] int NOT NULL CONSTRAINT [DF_ReportExecutions_PlantCount] DEFAULT 0;
            IF COL_LENGTH('ReportExecutions', 'RecordCount') IS NULL ALTER TABLE [ReportExecutions] ADD [RecordCount] int NOT NULL CONSTRAINT [DF_ReportExecutions_RecordCount] DEFAULT 0;
            IF COL_LENGTH('ReportExecutions', 'AnomalyCount') IS NULL ALTER TABLE [ReportExecutions] ADD [AnomalyCount] int NOT NULL CONSTRAINT [DF_ReportExecutions_AnomalyCount] DEFAULT 0;

            IF OBJECT_ID(N'[ReportSchedules]', N'U') IS NULL CREATE TABLE [ReportSchedules] (
                [Id] int IDENTITY PRIMARY KEY, [ScheduleCode] nvarchar(60) NOT NULL UNIQUE, [Name] nvarchar(200) NOT NULL,
                [ApiType] nvarchar(10) NOT NULL, [DataType] nvarchar(50) NOT NULL, [IntervalMinutes] int NOT NULL,
                [ScheduleType] nvarchar(30) NOT NULL, [ScheduleDay] nvarchar(20) NOT NULL, [ScheduleMonths] nvarchar(50) NOT NULL,
                [ExecutionTime] nvarchar(5) NOT NULL, [IsActive] bit NOT NULL, [CreatedAt] datetimeoffset NOT NULL);
            IF COL_LENGTH('ReportSchedules', 'ExecutionTime') IS NOT NULL ALTER TABLE [ReportSchedules] ALTER COLUMN [ExecutionTime] nvarchar(5) NOT NULL;
            IF OBJECT_ID(N'[ReportScheduleRecipients]', N'U') IS NULL CREATE TABLE [ReportScheduleRecipients] (
                [Id] int IDENTITY PRIMARY KEY, [ReportScheduleId] int NOT NULL, [Email] nvarchar(320) NOT NULL,
                CONSTRAINT [FK_ReportScheduleRecipients_ReportSchedules] FOREIGN KEY ([ReportScheduleId]) REFERENCES [ReportSchedules]([Id]) ON DELETE CASCADE,
                CONSTRAINT [UQ_ReportScheduleRecipients] UNIQUE ([ReportScheduleId], [Email]));
            IF OBJECT_ID(N'[ReportDeliveryAttempts]', N'U') IS NULL CREATE TABLE [ReportDeliveryAttempts] (
                [Id] bigint IDENTITY PRIMARY KEY, [DeliveryCode] nvarchar(60) NOT NULL UNIQUE, [ReportScheduleId] int NULL,
                [ScheduleName] nvarchar(200) NOT NULL, [ApiType] nvarchar(10) NOT NULL, [GeneratedAt] datetimeoffset NOT NULL,
                [ReportStatus] nvarchar(30) NOT NULL, [EmailStatus] nvarchar(30) NOT NULL, [OverallStatus] nvarchar(30) NOT NULL,
                [Message] nvarchar(2000) NOT NULL,
                CONSTRAINT [FK_ReportDeliveryAttempts_ReportSchedules] FOREIGN KEY ([ReportScheduleId]) REFERENCES [ReportSchedules]([Id]) ON DELETE SET NULL);
            IF OBJECT_ID(N'[ReportDeliveryRecipients]', N'U') IS NULL CREATE TABLE [ReportDeliveryRecipients] (
                [Id] bigint IDENTITY PRIMARY KEY, [ReportDeliveryAttemptId] bigint NOT NULL, [Email] nvarchar(320) NOT NULL,
                CONSTRAINT [FK_ReportDeliveryRecipients_ReportDeliveryAttempts] FOREIGN KEY ([ReportDeliveryAttemptId]) REFERENCES [ReportDeliveryAttempts]([Id]) ON DELETE CASCADE);

            ALTER TABLE [CollectionJobs] ALTER COLUMN [DataType] nvarchar(50) NOT NULL;
            ALTER TABLE [CollectionJobs] ALTER COLUMN [Mode] nvarchar(40) NOT NULL;
            ALTER TABLE [CollectionJobs] ALTER COLUMN [ErrorMessage] nvarchar(1000) NULL;
            ALTER TABLE [RawApiResponses] ALTER COLUMN [ContentType] nvarchar(100) NOT NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CollectionJobs_Queue' AND object_id = OBJECT_ID(N'[CollectionJobs]'))
                CREATE INDEX [IX_CollectionJobs_Queue] ON [CollectionJobs] ([Status], [StartedAt])
                    INCLUDE ([PlantId], [DataType], [Mode], [RetryCount], [ReplaceExisting]);
            IF OBJECT_ID(N'[FK_CollectionJobs_LssPlants]', N'F') IS NULL ALTER TABLE [CollectionJobs] WITH CHECK ADD CONSTRAINT [FK_CollectionJobs_LssPlants] FOREIGN KEY ([PlantId]) REFERENCES [LssPlants]([Id]);
            IF OBJECT_ID(N'[FK_RawApiResponses_CollectionJobs]', N'F') IS NULL ALTER TABLE [RawApiResponses] WITH CHECK ADD CONSTRAINT [FK_RawApiResponses_CollectionJobs] FOREIGN KEY ([CollectionJobId]) REFERENCES [CollectionJobs]([Id]) ON DELETE CASCADE;
            IF OBJECT_ID(N'[FK_SolarApiReadings_CollectionJobs]', N'F') IS NULL ALTER TABLE [SolarApiReadings] WITH CHECK ADD CONSTRAINT [FK_SolarApiReadings_CollectionJobs] FOREIGN KEY ([CollectionJobId]) REFERENCES [CollectionJobs]([Id]) ON DELETE CASCADE;
            IF OBJECT_ID(N'[FK_SolarApiReadings_LssPlants]', N'F') IS NULL ALTER TABLE [SolarApiReadings] WITH CHECK ADD CONSTRAINT [FK_SolarApiReadings_LssPlants] FOREIGN KEY ([PlantId]) REFERENCES [LssPlants]([Id]);
            IF OBJECT_ID(N'[FK_DataAnomalies_CollectionJobs]', N'F') IS NULL ALTER TABLE [DataAnomalies] WITH CHECK ADD CONSTRAINT [FK_DataAnomalies_CollectionJobs] FOREIGN KEY ([CollectionJobId]) REFERENCES [CollectionJobs]([Id]) ON DELETE CASCADE;
            IF OBJECT_ID(N'[FK_DataAnomalies_ValidationRules]', N'F') IS NULL ALTER TABLE [DataAnomalies] WITH CHECK ADD CONSTRAINT [FK_DataAnomalies_ValidationRules] FOREIGN KEY ([ValidationRuleId]) REFERENCES [ValidationRules]([Id]) ON DELETE SET NULL;
            IF OBJECT_ID(N'[FK_CollectionSchedules_LssPlants]', N'F') IS NULL ALTER TABLE [CollectionSchedules] WITH CHECK ADD CONSTRAINT [FK_CollectionSchedules_LssPlants] FOREIGN KEY ([PlantId]) REFERENCES [LssPlants]([Id]) ON DELETE CASCADE;
            IF OBJECT_ID(N'[FK_CollectionJobs_CollectionSchedules]', N'F') IS NULL EXEC(N'ALTER TABLE [CollectionJobs] WITH CHECK ADD CONSTRAINT [FK_CollectionJobs_CollectionSchedules] FOREIGN KEY ([CollectionScheduleId]) REFERENCES [CollectionSchedules]([Id]) ON DELETE SET NULL;');
            IF OBJECT_ID(N'[StoredDataRecords]', N'U') IS NOT NULL DROP TABLE [StoredDataRecords];
            IF OBJECT_ID(N'[SolarDocuments]', N'U') IS NOT NULL DROP TABLE [SolarDocuments];

            EXEC(N'CREATE OR ALTER VIEW [dbo].[DdqRawApiResponses] AS
                SELECT rawResponse.*, job.[CollectionId], job.[PlantId], job.[DataType]
                FROM [dbo].[RawApiResponses] rawResponse
                INNER JOIN [dbo].[CollectionJobs] job ON job.[Id] = rawResponse.[CollectionJobId]
                WHERE job.[DataType] <> N''MMF'';');
            EXEC(N'CREATE OR ALTER VIEW [dbo].[MmfRawApiResponses] AS
                SELECT rawResponse.*, job.[CollectionId], job.[PlantId], job.[DataType]
                FROM [dbo].[RawApiResponses] rawResponse
                INNER JOIN [dbo].[CollectionJobs] job ON job.[Id] = rawResponse.[CollectionJobId]
                WHERE job.[DataType] = N''MMF'';');
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

    private static async Task EnsurePlantApiConfigurationsAsync(ApplicationDbContext database)
    {
        await database.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'[PlantApiConfigurations]', N'U') IS NULL
            BEGIN
                CREATE TABLE [PlantApiConfigurations] (
                    [Id] int IDENTITY PRIMARY KEY, [PlantId] int NOT NULL,
                    [ApiType] nvarchar(10) NOT NULL, [DataType] nvarchar(40) NOT NULL,
                    [ApiLink] nvarchar(1000) NOT NULL,
                    CONSTRAINT [FK_PlantApiConfigurations_LssPlants] FOREIGN KEY ([PlantId]) REFERENCES [LssPlants]([Id]) ON DELETE CASCADE,
                    CONSTRAINT [UQ_PlantApiConfigurations] UNIQUE ([PlantId], [ApiType], [DataType]));
                INSERT INTO [PlantApiConfigurations] ([PlantId], [ApiType], [DataType], [ApiLink])
                    SELECT [Id], N'DDQ', N'Rolling 24 Hour', [DdqApiEndpoint] FROM [LssPlants] WHERE NULLIF([DdqApiEndpoint], N'') IS NOT NULL;
                INSERT INTO [PlantApiConfigurations] ([PlantId], [ApiType], [DataType], [ApiLink])
                    SELECT [Id], N'MMF', N'MMF', [MmfApiEndpoint] FROM [LssPlants] WHERE NULLIF([MmfApiEndpoint], N'') IS NOT NULL;
            END
            """);
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
