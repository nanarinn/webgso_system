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

The command synchronises the MSSQL schema, starts the application with `dotnet watch`, and automatically rebuilds and restarts the application when a code file changes.

To pull the latest `origin/main` code first, synchronise the database, and start the watcher with one command:

```powershell
.\run-local-sqlserver.ps1 -Pull
```

The `-Pull` option stops if uncommitted local changes are present, preventing a Git pull from overwriting current work. Database data is retained during schema synchronisation; dummy data is only replaced when `reset-dummy-api-data.ps1` is run explicitly.

Open `http://localhost:5186/login`.

### Share on the same local network

Run PowerShell or the VS Code terminal as Administrator, then start the system with:

```powershell
.\run-local-sqlserver.ps1 -Share
```

To use a specific address already assigned to the host computer:

```powershell
.\run-local-sqlserver.ps1 -Share -ShareAddress 192.168.0.140
```

The launcher prints a URL such as `http://192.168.1.20:5186`. Teammates connected to the same
Wi-Fi/LAN can open that URL while the host terminal remains running. The firewall rule only allows
TCP 5186 from `LocalSubnet`, including when the active Windows network profile is Public. Local network access supports the development username/password
accounts. Microsoft Entra sign-in still uses the registered `https://localhost:8088/signin-oidc`
callback; use a deployed HTTPS hostname with a matching redirect URI when Entra sign-in is required
from multiple computers.

For temporary same-network Microsoft Entra access, run `setup-lan-entra.ps1` as Administrator on
the host. Register `https://sblss-lan.local:8088/signin-oidc` as an additional Web redirect URI.
Give each teammate the exported `sblss-lan.cer` public certificate and
`trust-sblss-lan-certificate.ps1`; they run the trust script as Administrator once. Start the host:

```powershell
.\run-local-sqlserver.ps1 -Share -ShareAddress 192.168.0.140 -ShareEntra -ShareHostName sblss-lan.local
```

Teammates then use `https://sblss-lan.local:8088/login`. Never share the `.pfx`, its password, or
the Entra Client Secret.

### Microsoft Entra ID sign-in

The application uses single-tenant Microsoft Entra ID to verify identity. Repository approval and
the `Super Admin`, `Administrator`, and `Staff` roles are controlled in Microsoft SQL Server. Store
the application client secret only in .NET user secrets:

For the reviewed SAE Malaysia `sblss_system` registration, run the guided local setup and paste
the Client Secret **Value** only when prompted:

```powershell
.\setup-entra-local.ps1
```

The script stores the secret in .NET user secrets, trusts the local HTTPS certificate and starts
the system at `https://localhost:8088/login`. The equivalent manual commands are:

```powershell
dotnet dev-certs https --trust
dotnet user-secrets set "EntraId:Enabled" "true" --project .\TestingSystem.Web\TestingSystem.Web.csproj
dotnet user-secrets set "EntraId:ClientSecret" "<secret-value>" --project .\TestingSystem.Web\TestingSystem.Web.csproj
.\run-local-sqlserver.ps1
```

The registered Web redirect URI is `https://localhost:8088/signin-oidc`. If the Enterprise
Application has **Assignment required** enabled, assign the company user to `sblss_system`; no Entra
app role is required. Open `https://localhost:8088/login` for Microsoft sign-in. The first verified
sign-in creates a `Pending` access request in MS SQL. An Administrator can approve Staff access;
a Super Admin can approve Staff or Administrator access. Approved users sign in again to enter the
system. Local username and password sign-in continues to work at `http://localhost:5186/login` when
Entra ID is not configured.

Approval and rejection email records are stored in `UserAccessNotifications`. To send them through
an SMTP gateway, set `ApprovalEmail:Enabled`, `Host`, `Port`, `Sender`, `UseSsl`, and any required
`UserName`/`Password` through environment variables or .NET user secrets. Never commit credentials.

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
