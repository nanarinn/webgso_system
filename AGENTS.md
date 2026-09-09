# Single Buyer LSS Repository - Codex Guide

## Project

- Main solution: `SBLSS.System.sln`
- Web application: `TestingSystem.Web`
- Stack: C#, .NET 8, Blazor Web App, ASP.NET Core Identity, EF Core, Docker Compose, Microsoft SQL Server 2022

## Working rules

- Build with `dotnet build SBLSS.System.sln` after code changes.
- Keep secrets in `.env` or user secrets; never commit real passwords.
- Run the complete local stack with `docker compose up --build -d`.
- Preserve the Single Buyer color system and responsive visual style.
- Use role-based authorization for Administrator-only operations.
- Add or update tests when implementing business logic.

## Useful checks

- App health endpoint: `/health`
- API connectivity endpoint: `/api/ping`
- Protected database proof endpoint: `/api/status`
- Docker web port: `${APP_PORT:-8088}`
- Local SQL Server port: `${MSSQL_PORT:-1433}`
