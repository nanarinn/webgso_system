# Single Buyer Large Scale Solar Repository

A secure C# and .NET 8 Blazor repository for Large Scale Solar project records.
The local environment runs the web application and Microsoft SQL Server in
Docker containers.

## Included

- Blazor Web App frontend and protected repository dashboard
- ASP.NET Core Identity login with password hashing and account lockout
- Two roles: `Administrator` and `Reviewer`
- Entity Framework Core with Microsoft SQL Server
- SQL-aware `/health` endpoint
- Docker Compose for Blazor and SQL Server
- VS Code build, run, debug, and Docker tasks
- Single Buyer-inspired color palette sourced from the official website

## Start the system

1. Copy `.env.example` to `.env` if `.env` does not exist.
2. Change every password in `.env` before sharing or deployment.
3. Start the containers:

```powershell
docker compose up --build -d
```

4. Open `http://localhost:8088/login`.

The development usernames are configured by `SB_ADMIN_USERNAME` and
`SB_REVIEWER_USERNAME` in `.env`. Their passwords are also stored in `.env`,
which is excluded from Git. Only password hashes are stored in SQL Server.

## Verify the connections

```powershell
dotnet build .\SBLSS.System.sln
docker compose ps
```

Expected Docker services:

- `single-buyer-lss-web` is `Up` on port `8088`
- `single-buyer-lss-mssql` is `Up (healthy)` on port `1433`

After login, open these proof endpoints:

- `http://localhost:8088/health`
- `http://localhost:8088/api/status`

`/api/status` reports the live SQL connection, user count, and repository
document count. It requires an authenticated account.

## User access

- `Administrator` can access the repository and sees administrative actions.
- `Reviewer` can access and search the repository without administrative actions.
- Five failed login attempts lock the account for ten minutes.
- Login cookies are HTTP-only and expire after eight hours.

## Project structure

```text
TestingSystem.Web/
|-- Components/Pages/       Blazor pages, login, dashboard, repository
|-- Components/Layout/      Main and login layouts
|-- Data/                   EF Core context and database seeding
|-- HealthChecks/           Live SQL Server connection check
|-- Models/                 User and solar document entities
|-- Program.cs              Authentication, database, API and middleware setup
`-- wwwroot/                CSS and static assets
```

## Important

The included credentials and sample LSS records are for local development.
Use a secret manager, HTTPS, database backups, audit logging, migrations, and
organization-approved access rules before production deployment.
