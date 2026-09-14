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
- `TestingSystem.Web/Demo/DemoData.cs` - all prototype/demo records and generated sample values.
- `TestingSystem.Web/wwwroot/styles` - CSS.

Operational data is stored in SQL Server. Initial development records are inserted into `StoredDataRecords` on first use and all later changes are saved back to the database.
