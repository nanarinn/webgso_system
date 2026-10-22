# Single Buyer Large Scale Solar Repository

A .NET 8 Blazor system backed by PostgreSQL and managed locally with pgAdmin 4.

## Run locally

Requirements:

- .NET 8 SDK (installed globally or in the project `.dotnet` folder)
- PostgreSQL Server 18 and pgAdmin 4

For first-time database setup, double-click `setup-postgresql.cmd`. Enter the password created for the PostgreSQL `postgres` account during installation. The setup creates the `webgso_system` database, creates a restricted `webgso_app` login, stores its generated password in .NET user-secrets, creates every EF Core table, and adds linked demo data.

After setup, start the connected system with:

```powershell
.\run-postgresql.cmd
```

Open `http://localhost:5186`. In pgAdmin, refresh **Servers > PostgreSQL 18 > Databases** to see `webgso_system` and its tables.

To review the interface without PostgreSQL, run `run-ui-only.cmd`. UI-only changes are temporary and are not written to a database.

Database credentials are never committed. `setup-postgresql.ps1` stores the application connection string in .NET user-secrets.

Open `http://localhost:5186/login` for the connected application.

For local MMF collection testing, register an MMF API link such as:

`http://localhost:5186/api/test-data/mmf/LSS1?interval=15`

The optional query parameters are `date=yyyy-MM-dd` and `interval=15`, `30`, or `60`. The endpoint returns the 13 MMF parameters represented by the supplied MMF sample, with five sensor readings per parameter and timestamp.

Plant API endpoints are fetched by the backend. In Development, loopback/private test endpoints are allowed. Outside Development, endpoints must use HTTPS and resolve to public addresses unless an administrator explicitly lists an internal hostname under `LssApi:AllowedPrivateHosts`; HTTP exceptions must be listed separately under `LssApi:AllowInsecureHosts`. Redirects from configured plant API URLs are not followed.

### Local account sign-in

The application uses PostgreSQL-backed local accounts rather than Microsoft Entra ID. Create or
reset the first Super Admin account with:

```powershell
.\setup-local-admin.cmd
```

The script requests the username, email, display name and password interactively. The password is
passed to the application only for that setup process and is not written to Git. Users can then
create an account from `/register`; new accounts remain `Pending` until reviewed in User Management.
An Administrator can approve Staff access, while a Super Admin can approve Staff or Administrator
access. Approved users sign in at `http://localhost:5186/login` with their username/email and password.

Access request, approval and rejection notifications are stored in `UserAccessNotifications` and
shown from the notification bell. New requests notify every active Super Admin; decisions notify
the requesting user. Email uses the Microsoft Graph configuration below when available and falls
back to the `ApprovalEmail` SMTP settings. Never commit credentials.

### Report and access notification email through Microsoft Graph

Report Automation and User Management send email through Microsoft Graph. In the Entra
app registration, add the Microsoft Graph **Application** permission `Mail.Send` and grant tenant
administrator consent. The configured sender must be a valid Microsoft 365 mailbox. The report
service can reuse the `EntraId` tenant, client ID and client secret; only the following settings are
then required:

```powershell
dotnet user-secrets set "ReportEmail:Enabled" "true" --project .\TestingSystem.Web\TestingSystem.Web.csproj
dotnet user-secrets set "ReportEmail:SenderMailbox" "reports@your-company.example" --project .\TestingSystem.Web\TestingSystem.Web.csproj
```

To use a separate app registration, also set `ReportEmail:TenantId`, `ReportEmail:ClientId`, and
`ReportEmail:ClientSecret` in user secrets. Never put the client secret in `appsettings.json`.
Delivery attempts appear under **Report Automation > Delivery monitor** as Processing, Delivered,
or Failed. A report is attached only when the selected DDQ or MMF readings exist in PostgreSQL.

Development accounts are configured in `TestingSystem.Web/appsettings.Development.json`. Change their passwords before sharing or deployment.

## Code folders

- `TestingSystem.Web/Components/Frontend/UseCases` - Razor frontend markup.
- `TestingSystem.Web/Backend/UseCases/PageLogic` - C# page logic.
- `TestingSystem.Web/Connectors/UseCases` - frontend/backend, API and service connectors.
- `TestingSystem.Web/Database/UseCases` - PostgreSQL entity/table definitions.
- `TestingSystem.Web/Database/Common` - database connection and table creation.
- `TestingSystem.Web/Database/Common/DummyApiDataSeeder.cs` - creates the complete related development workflow in PostgreSQL.
- `TestingSystem.Web/wwwroot/styles` - CSS.

All operational data is stored in dedicated relational PostgreSQL tables. The application no longer uses generic JSON records for schedules, jobs, reports, recipients or delivery attempts.
