# Single Buyer Large Scale Solar Repository

A .NET 8 Blazor system backed by Microsoft SQL Server Express.

## Run locally

Requirements:

- .NET 8 SDK
- SQL Server Express instance named `SQLEXPRESS`

Start the system from PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\run-local-sqlserver.ps1
```

Open `http://localhost:5186/login`.

Development accounts are configured in `TestingSystem.Web/appsettings.Development.json`. Change their passwords before sharing or deployment.

## Code folders

- `TestingSystem.Web/Components/Frontend/UseCases` - Razor frontend markup.
- `TestingSystem.Web/Backend/UseCases/PageLogic` - C# page logic.
- `TestingSystem.Web/Connectors/UseCases` - frontend/backend, API and service connectors.
- `TestingSystem.Web/Database/UseCases` - SQL Server entity/table definitions.
- `TestingSystem.Web/Database/Common` - database connection and table creation.
- `TestingSystem.Web/Database/Common/DummyApiDataSeeder.cs` - creates the complete related development workflow in MSSQL.
- `TestingSystem.Web/wwwroot/styles` - CSS.

All operational data is stored in dedicated relational SQL Server tables. The application no longer uses generic JSON records for schedules, jobs, reports, recipients or delivery attempts.
