# SBLSS simplified file map

Filenames use descriptive names without FSD numbers.

All prototype records and generated sample readings are kept in `TestingSystem.Web/Demo/DemoData.cs`. Change that file when adjusting demo content; production data must come from SQL Server and approved plant APIs.

| Function | Razor frontend | C# logic | Connector | Database table |
| --- | --- | --- | --- | --- |
| Login | `Login.razor` | `Login.cs` | `LoginConnector.cs` | `UserTable.cs` |
| Dashboard | `Dashboard.razor` | `Dashboard.cs` | `SystemStatusConnector.cs` | `DashboardDocumentTable.cs` |
| DDQ analytics | `DdqAnalytics.razor` | `DdqAnalytics.cs` | `AnalyticsConnector.cs` | Stored operational data |
| MMF analytics | `MmfAnalytics.razor` | `MmfAnalytics.cs` | `AnalyticsConnector.cs` | Stored operational data |
| On-demand reports | `OnDemandReport.razor` | `OnDemandReport.cs` | `StoredDataConnector.cs` | `StoredDataTable.cs` |
| Report history | `ReportHistory.razor` | `ReportHistory.cs` | `StoredDataConnector.cs` | `StoredDataTable.cs` |
| Collection management | `CollectionManagement.razor` | `CollectionManagement.cs` | `PlantApiConnector.cs` | `StoredDataTable.cs` |
| Staff accounts | `StaffAccounts.razor` | `StaffAccounts.cs` | ASP.NET Core Identity | `UserTable.cs` |
| LSS plants | `LssPlants.razor` | `LssPlants.cs` | `PlantApiConnector.cs` | `LssPlantTable.cs` |
| Collection schedules | `CollectionSchedules.razor` | `CollectionSchedules.cs` | `StoredDataConnector.cs` | `StoredDataTable.cs` |
| Scheduled reports | `ScheduledReports.razor` | `ScheduledReports.cs` | `StoredDataConnector.cs` | `StoredDataTable.cs` |
| Data quality | `DataQuality.razor` | `DataQuality.cs` | `ExternalConnectors.cs` | `OperationalTables.cs` |
| Audit logs | `AuditLogs.razor` | `AuditLogs.cs` | `AuditConnector.cs` | `AuditEventTable.cs` |

## Required framework files

`Program.cs`, `App.razor`, `Routes.razor`, `_Imports.razor`, layouts, the project file and application settings remain separate because ASP.NET Core and Blazor require them.

## Database files

- `DatabaseConnection.cs` maps C# entities to SQL Server.
- `CreateTables.cs` creates and upgrades development tables.
- `DatabaseSettings.cs` represents database configuration.
- `StoredDataTable.cs` stores report, schedule, collection and delivery records as database-backed JSON documents.
