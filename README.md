# Single Buyer Large Scale Solar Repository

A .NET 8 Blazor system backed by Microsoft SQL Server. This local setup keeps the application on the Windows host and its database in VMware.

## Run locally

Requirements for the current host and VMware setup:

- .NET 8 SDK (installed globally or in the project `.dotnet` folder)
- The VMware SQL Server at `192.168.227.128:1433` and the saved .NET user secret for its connection

Double-click `run-sblss-system.cmd` to start the website on the host with VM SQL and LAN access. Double-click `update-github-and-connect-vm.cmd` to pull `origin/main` and check the VM connection without a Git commit or push. These CMD files also work from the Desktop when the project remains in `Documents\ChatGPT\SBLSS SYSTEM`.

For PowerShell, the equivalent start command is:

```powershell
powershell -ExecutionPolicy Bypass -File .\run-local-sqlserver.ps1 -VmSqlServer -Share -ShareAddress 10.122.147.3 -ShareEntra -ShareHostName sblss-lan.local
```

The command synchronises the MSSQL schema, starts the application with `dotnet watch`, and automatically rebuilds and restarts the application when a code file changes.

To run with a local SQL Server instead, use `run-local-sqlserver.ps1` without `-VmSqlServer`. Its `-Pull` option only works when the Git working tree is clean. The dedicated update CMD preserves local changes in a Git stash before pulling.

Database data is retained during schema synchronisation; dummy data is only replaced when `reset-dummy-api-data.ps1` is run explicitly.

Open `https://sblss-lan.local:8088/login` or `http://localhost:5186/login`.

For remote team access, join and authorize both computers on the same ZeroTier
network. The web server currently uses managed address `10.122.147.3/24`.
Distribute `SBLSS-ZeroTier-Team-Access.zip`; teammates extract it and double-click
`Install-SBLSS-Team-Access.cmd`. The installer trusts the public certificate,
maps `sblss-lan.local` to the ZeroTier address and opens the secure login page.

### Host the web application in IIS with the VMware database

On this Windows host, double-click `setup-iis-web-server.cmd` and approve the
Administrator prompt. The setup enables IIS and WebSockets, installs the
Microsoft ASP.NET Core Hosting Bundle, publishes the web application, and binds
the existing LAN certificate to `https://sblss-lan.local:8088`. It keeps Entra
ID enabled and uses the existing SQL connection to
`192.168.227.128:1433/SingleBuyerLssRepository`.

The setup copies the current .NET user-secrets file to
`C:\ProgramData\SBLSS\iis-secrets.json`, outside the repository, with access
restricted to Windows administrators, SYSTEM, and the SBLSS IIS application
pool. The IIS app pool runs the application in Production mode. IIS starts the
site automatically when Windows starts; teammates continue using the same LAN
URL. The existing Kestrel development launcher uses the same port, so do not
run it while the IIS site is active.

For local MMF collection testing, register an MMF API link such as:

`http://localhost:5186/api/test-data/mmf/LSS1?interval=15`

The optional query parameters are `date=yyyy-MM-dd` and `interval=15`, `30`, or `60`. The endpoint returns the 13 MMF parameters represented by the supplied MMF sample, with five sensor readings per parameter and timestamp.

Plant API endpoints are fetched by the backend. In Development, loopback/private test endpoints are allowed. Outside Development, endpoints must use HTTPS and resolve to public addresses unless an administrator explicitly lists an internal hostname under `LssApi:AllowedPrivateHosts`; HTTP exceptions must be listed separately under `LssApi:AllowInsecureHosts`. Redirects from configured plant API URLs are not followed.

### Keep the application on the host and the database in VMware

Restore `SingleBuyerLssRepository` inside the VM, enable SQL Server TCP/IP on
`192.168.227.128:1433`, and allow inbound TCP 1433 from the VMware host network.
Create a SQL Server login mapped to this database for the application. Run
`./setup-vm-sql-connection.ps1` on the host to enter the login and password;
it stores the connection string in .NET user secrets with server
`192.168.227.128,1433` and database `SingleBuyerLssRepository`. Keep the
password out of the repository and shell history.

Start the host application against the VM database with:

```powershell
.\run-local-sqlserver.ps1 -VmSqlServer
```

This option checks VM TCP connectivity and the saved connection string before
starting. It does not start or fall back to a SQL Server instance on the host.

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
or Failed. A report is attached only when the selected DDQ or MMF readings exist in MS SQL.

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
