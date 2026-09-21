USE [SingleBuyerLssRepository];
GO

-- 1. Confirm the selected SQL Server and database.
SELECT
    @@SERVERNAME AS [SqlServer],
    DB_NAME() AS [DatabaseName],
    SUSER_SNAME() AS [ConnectedAs];
GO

-- 2. List application tables and views.
SELECT
    s.[name] AS [SchemaName],
    o.[name] AS [ObjectName],
    CASE o.[type] WHEN 'U' THEN 'TABLE' WHEN 'V' THEN 'VIEW' END AS [ObjectType]
FROM sys.objects o
INNER JOIN sys.schemas s ON s.[schema_id] = o.[schema_id]
WHERE o.[type] IN ('U', 'V')
  AND o.[is_ms_shipped] = 0
ORDER BY [ObjectType], [ObjectName];
GO

-- 3. Show every column, data type and null setting.
SELECT
    s.[name] AS [SchemaName],
    t.[name] AS [TableName],
    c.[column_id] AS [ColumnOrder],
    c.[name] AS [ColumnName],
    TYPE_NAME(c.[user_type_id]) AS [DataType],
    CASE
        WHEN TYPE_NAME(c.[user_type_id]) IN ('nvarchar', 'varchar', 'varbinary') AND c.[max_length] = -1 THEN 'MAX'
        WHEN TYPE_NAME(c.[user_type_id]) IN ('nvarchar', 'nchar') THEN CONVERT(varchar(10), c.[max_length] / 2)
        WHEN TYPE_NAME(c.[user_type_id]) IN ('varchar', 'char', 'varbinary') THEN CONVERT(varchar(10), c.[max_length])
        WHEN TYPE_NAME(c.[user_type_id]) IN ('decimal', 'numeric') THEN CONCAT(c.[precision], ',', c.[scale])
        ELSE NULL
    END AS [SizeOrPrecision],
    c.[is_nullable] AS [AllowsNull],
    c.[is_identity] AS [IsIdentity]
FROM sys.tables t
INNER JOIN sys.schemas s ON s.[schema_id] = t.[schema_id]
INNER JOIN sys.columns c ON c.[object_id] = t.[object_id]
WHERE t.[is_ms_shipped] = 0
ORDER BY t.[name], c.[column_id];
GO

-- 4. Show primary keys, unique constraints and indexes.
SELECT
    OBJECT_SCHEMA_NAME(i.[object_id]) AS [SchemaName],
    OBJECT_NAME(i.[object_id]) AS [TableName],
    i.[name] AS [IndexName],
    i.[is_primary_key] AS [IsPrimaryKey],
    i.[is_unique] AS [IsUnique],
    STRING_AGG(c.[name], ', ') WITHIN GROUP (ORDER BY ic.[key_ordinal]) AS [IndexedColumns]
FROM sys.indexes i
INNER JOIN sys.index_columns ic
    ON ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id]
INNER JOIN sys.columns c
    ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
WHERE i.[name] IS NOT NULL
  AND OBJECTPROPERTY(i.[object_id], 'IsUserTable') = 1
GROUP BY i.[object_id], i.[name], i.[is_primary_key], i.[is_unique]
ORDER BY [TableName], [IndexName];
GO

-- 5. Show declared foreign-key relationships, if present.
SELECT
    fk.[name] AS [ForeignKey],
    OBJECT_NAME(fkc.[parent_object_id]) AS [FromTable],
    COL_NAME(fkc.[parent_object_id], fkc.[parent_column_id]) AS [FromColumn],
    OBJECT_NAME(fkc.[referenced_object_id]) AS [ToTable],
    COL_NAME(fkc.[referenced_object_id], fkc.[referenced_column_id]) AS [ToColumn]
FROM sys.foreign_keys fk
INNER JOIN sys.foreign_key_columns fkc ON fkc.[constraint_object_id] = fk.[object_id]
ORDER BY [FromTable], [ForeignKey];
GO

-- 6. Check the main API pipeline totals.
SELECT 'LssPlants' AS [ObjectName], COUNT_BIG(*) AS [TotalRows] FROM dbo.LssPlants
UNION ALL SELECT 'CollectionJobs', COUNT_BIG(*) FROM dbo.CollectionJobs
UNION ALL SELECT 'RawApiResponses', COUNT_BIG(*) FROM dbo.RawApiResponses
UNION ALL SELECT 'SolarApiReadings', COUNT_BIG(*) FROM dbo.SolarApiReadings
UNION ALL SELECT 'CollectionSchedules', COUNT_BIG(*) FROM dbo.CollectionSchedules
UNION ALL SELECT 'ValidationRules', COUNT_BIG(*) FROM dbo.ValidationRules
UNION ALL SELECT 'DataAnomalies', COUNT_BIG(*) FROM dbo.DataAnomalies
UNION ALL SELECT 'ReportSchedules', COUNT_BIG(*) FROM dbo.ReportSchedules
UNION ALL SELECT 'ReportScheduleRecipients', COUNT_BIG(*) FROM dbo.ReportScheduleRecipients
UNION ALL SELECT 'ReportDeliveryAttempts', COUNT_BIG(*) FROM dbo.ReportDeliveryAttempts
UNION ALL SELECT 'ReportExecutions', COUNT_BIG(*) FROM dbo.ReportExecutions
UNION ALL SELECT 'DdqApiReadings view', COUNT_BIG(*) FROM dbo.DdqApiReadings
UNION ALL SELECT 'MmfApiReadings view', COUNT_BIG(*) FROM dbo.MmfApiReadings
UNION ALL SELECT 'DdqRawApiResponses view', COUNT_BIG(*) FROM dbo.DdqRawApiResponses
UNION ALL SELECT 'MmfRawApiResponses view', COUNT_BIG(*) FROM dbo.MmfRawApiResponses;
GO

-- 7. Verify the API -> raw -> translated-data relationship.
SELECT TOP (100)
    job.[Id] AS [CollectionJobId],
    job.[CollectionId],
    plant.[PlantId] AS [PlantCode],
    plant.[Name] AS [PlantName],
    job.[DataType],
    job.[Mode],
    job.[Status],
    job.[StartedAt],
    COUNT(DISTINCT rawResponse.[Id]) AS [RawResponseCount],
    COUNT(DISTINCT reading.[Id]) AS [TranslatedReadingCount]
FROM dbo.CollectionJobs job
INNER JOIN dbo.LssPlants plant ON plant.[Id] = job.[PlantId]
LEFT JOIN dbo.RawApiResponses rawResponse ON rawResponse.[CollectionJobId] = job.[Id]
LEFT JOIN dbo.SolarApiReadings reading ON reading.[CollectionJobId] = job.[Id]
GROUP BY job.[Id], job.[CollectionId], plant.[PlantId], plant.[Name],
    job.[DataType], job.[Mode], job.[Status], job.[StartedAt]
ORDER BY job.[Id] DESC;
GO

-- 8. MMF coverage by plant and parameter.
SELECT
    plant.[PlantId] AS [PlantCode],
    plant.[Name] AS [PlantName],
    reading.[Parameter],
    reading.[Unit],
    MIN(reading.[IntervalStart]) AS [FirstTimestamp],
    MAX(reading.[IntervalStart]) AS [LastTimestamp],
    COUNT_BIG(*) AS [ReadingCount]
FROM dbo.MmfApiReadings reading
INNER JOIN dbo.LssPlants plant ON plant.[Id] = reading.[PlantId]
GROUP BY plant.[PlantId], plant.[Name], reading.[Parameter], reading.[Unit]
ORDER BY plant.[Name], reading.[Parameter];
GO

-- 9. DDQ coverage by plant and data type.
SELECT
    plant.[PlantId] AS [PlantCode],
    plant.[Name] AS [PlantName],
    reading.[DataType],
    MIN(reading.[IntervalStart]) AS [FirstTimestamp],
    MAX(reading.[IntervalStart]) AS [LastTimestamp],
    COUNT_BIG(*) AS [ReadingCount]
FROM dbo.DdqApiReadings reading
INNER JOIN dbo.LssPlants plant ON plant.[Id] = reading.[PlantId]
GROUP BY plant.[PlantId], plant.[Name], reading.[DataType]
ORDER BY plant.[Name], reading.[DataType];
GO

-- 10. Inspect recent MMF translated rows. Payload remains in RawApiResponses.
SELECT TOP (100)
    reading.[Id],
    plant.[PlantId] AS [PlantCode],
    reading.[Parameter],
    reading.[SubmissionDateTime],
    reading.[IntervalStart] AS [DataTimestamp],
    reading.[IntervalMinutes],
    reading.[Value],
    reading.[Unit],
    reading.[IsSynthetic],
    reading.[SyntheticReason]
FROM dbo.MmfApiReadings reading
INNER JOIN dbo.LssPlants plant ON plant.[Id] = reading.[PlantId]
ORDER BY reading.[Id] DESC;
GO
