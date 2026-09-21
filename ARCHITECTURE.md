# SBLSS simplified file map

Filenames use descriptive names without FSD numbers.

All application records are stored in relational Microsoft SQL Server tables. `DummyApiDataSeeder.cs` creates one connected development dataset for plants, API configurations, collections, readings, schedules, anomalies, reports, recipients and deliveries.

| Function | Razor frontend | C# logic | Connector | Database table |
| --- | --- | --- | --- | --- |
| Login | `Login.razor` | `Login.cs` | `LoginConnector.cs` | `UserTable.cs` |
| Dashboard | `Dashboard.razor` | `Dashboard.cs` | `SystemStatusConnector.cs` | `DashboardDocumentTable.cs` |
| DDQ analytics | `DdqAnalytics.razor` | `DdqAnalytics.cs` | `AnalyticsConnector.cs` | Stored operational data |
| MMF analytics | `MmfAnalytics.razor` | `MmfAnalytics.cs` | `AnalyticsConnector.cs` | Stored operational data |
| On-demand reports | `OnDemandReport.razor` | `OnDemandReport.cs` | EF Core | `ReportExecutions` |
| Report history | `ReportHistory.razor` | `ReportHistory.cs` | EF Core | `ReportExecutions` |
| Collection management | `CollectionManagement.razor` | `CollectionManagement.cs` | `PlantApiConnector.cs` | `CollectionJobs`, `RawApiResponses` |
| Staff accounts | `StaffAccounts.razor` | `StaffAccounts.cs` | ASP.NET Core Identity | `UserTable.cs` |
| LSS plants | `LssPlants.razor` | `LssPlants.cs` | `PlantApiConnector.cs` | `LssPlantTable.cs` |
| Collection schedules | `CollectionSchedules.razor` | `CollectionSchedules.cs` | EF Core | `CollectionSchedules` |
| Scheduled reports | `ScheduledReports.razor` | `ScheduledReports.cs` | EF Core | `ReportSchedules`, `ReportScheduleRecipients`, `ReportDeliveryAttempts`, `ReportDeliveryRecipients` |
| Data quality | `DataQuality.razor` | `DataQuality.cs` | `ExternalConnectors.cs` | `OperationalTables.cs` |
| Audit logs | `AuditLogs.razor` | `AuditLogs.cs` | `AuditConnector.cs` | `AuditEventTable.cs` |

## Required framework files

`Program.cs`, `App.razor`, `Routes.razor`, `_Imports.razor`, layouts, the project file and application settings remain separate because ASP.NET Core and Blazor require them.

## Database files

- `DatabaseConnection.cs` maps C# entities to SQL Server.
- `CreateTables.cs` creates and upgrades development tables.
- `DatabaseSettings.cs` represents database configuration.
- Operational relationships use foreign keys; generic JSON storage is no longer used.
