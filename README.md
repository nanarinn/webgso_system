# Single Buyer Large Scale Solar Repository — Java

The application runs on **Java 21, Spring Boot, Spring Security, JDBC and Thymeleaf**. Java renders the HTML/CSS views; JavaScript provides optional navigation and batch progress updates. No C#, Blazor, ASP.NET runtime or .NET build is required.

## Start locally

```powershell
.\run-java.cmd
```

Open [http://localhost:5186](http://localhost:5186). The launcher reads the existing PostgreSQL connection from the local `sblss-system-local` secret store when `WEBGSO_DB_PASSWORD` is absent. It does not print or copy that password into Git. Java uses the existing database tables and accounts without recreating the database.

Java 21 must be available through `JAVA_HOME` or PATH. The launcher also detects a portable JDK in `.java-runtime/jdk` or a sibling `.tools/jdk` directory, supplied locally for this migration. The official Maven wrapper downloads Maven when needed.

```powershell
.\mvnw.cmd verify
.\run-java.ps1 -Port 5187    # Run alongside another application
.\run-java.ps1 -Build        # Verify and rebuild first
```

Stop the running JAR before rebuilding it on Windows. The Windows launcher defaults to the JDK's TCP pipe fallback for compatibility with packaged environments that cannot connect an AF_UNIX wakeup pipe. Set `WEBGSO_WINDOWS_TCP_FALLBACK=false` to disable that process-only workaround. Windows security settings are not changed.

## Configuration

Set environment variables before starting the service. [.env.example](.env.example) lists the supported keys; it is a reference file and is not automatically loaded. For the VS Code PostgreSQL debugger, supply an ignored `.env.local` with environment values.

| Variable | Purpose |
| --- | --- |
| `WEBGSO_DB_URL` | Normally `jdbc:postgresql://localhost:5432/webgso_system` |
| `WEBGSO_DB_USER`, `WEBGSO_DB_PASSWORD` | Restricted application credentials |
| `PORT` | HTTP port for direct JAR startup; the launcher uses `-Port` |
| `WEBGSO_ADDRESS` | Bind address; defaults to loopback |
| `WEBGSO_SECURE_COOKIE` | Set `true` behind production HTTPS |
| `WEBGSO_WORKERS_ENABLED` | Enable automated workers after stopping the former service |
| `WEBGSO_API_PRIVATE_HOSTS` | Explicit comma-separated private API host allowlist |
| `WEBGSO_API_HTTP_HOSTS` | Explicit HTTP host exceptions |
| `WEBGSO_ADMIN_USERNAME`, `WEBGSO_ADMIN_EMAIL`, `WEBGSO_ADMIN_PASSWORD` | Create a first Super Admin only when the username does not exist |

The launcher uses the `local` profile, which explicitly permits loopback API fixtures. Use `run-java.ps1 -Production` or run the JAR directly for production defaults. Production plant APIs require HTTPS and public destinations unless explicitly allowlisted. DNS is checked at connection time, redirects are rejected, and payload sizes are capped.

For an empty PostgreSQL database, create a restricted database owner using PostgreSQL administration tools, configure credentials and start once with `--webgso.schema.initialize=true`. This executes `src/main/resources/schema-java.sql`. Leave initialization disabled for the existing database. Accounts remain pending until approved. The Java password encoder reads existing Identity V2/V3 hashes and creates compatible PBKDF2-SHA512 hashes, preserving rollback compatibility.

## Isolated demo

```powershell
.\run-java-demo.cmd -Port 5187
```

This explicit profile uses synthetic data in an in-memory H2 database, separate from PostgreSQL. Login: `demo.admin` / `DemoAdmin123!`. Data is discarded on process exit. These credentials are created only in the demo profile and are not displayed on the production login page.

## Application areas

- Overview, recent collections and registered plants.
- DDQ/MMF analytics with timestamp, submission, plant, state, region and parameter filters; chart and paged readings.
- Manual and bounded concurrent batch collection, retained raw responses, validation anomalies, retries and explicitly requested DDQ replacement.
- Plant configuration, dataset-specific API links and connection tests.
- Collection schedules using timezone, interval/day/month or Spring cron controls.
- On-demand Excel/CSV reports, anomaly/substitution exports, previous-day fallback, capacity capping and zero fallback. Downloads enforce ownership. Legacy report selections can be regenerated.
- Report schedules, recipients and delivery monitoring through SMTP or Microsoft Graph.
- Registration, approval/rejection/deactivation, role restrictions, notifications, lockout and 15-minute idle sessions.
- Audit logs, configured validation rules and anomaly resolution.

Workers are disabled by default during cutover, preventing simultaneous collection or delivery by two runtimes. Enable `WEBGSO_WORKERS_ENABLED=true` after stopping the old runtime. Real email additionally requires `WEBGSO_MAIL_ENABLED=true` and a configured sender/service. Automated tests send no real email.

For SMTP configure `SMTP_HOST`, `SMTP_PORT`, `SMTP_USERNAME`, `SMTP_PASSWORD` and `WEBGSO_MAIL_FROM`. For Graph additionally set `WEBGSO_GRAPH_TENANT_ID`, `WEBGSO_GRAPH_CLIENT_ID`, `WEBGSO_GRAPH_CLIENT_SECRET` with application permission `Mail.Send`. These settings must be supplied to Java separately from the former .NET configuration. This implementation limits Graph simple-send attachments to 2.5 MB.

Generated snapshots stay in the ignored private `.reports` directory. Preserve it across deployments. If a snapshot is absent, downloads regenerate it from the database, as the former application's live exports did.

## Source and deployment

```text
pom.xml                            Dependencies and build
src/main/java/my/webgso/            Java application and workflows
src/main/resources/templates/       Server-rendered HTML views
src/main/resources/static/          Brand assets, styles and browser enhancements
src/main/resources/schema-java.sql  Optional fresh-database schema
src/test/java/my/webgso/            Policy/security/workflow integration tests
```

`Dockerfile` builds/tests the JAR and runs it as a non-root user. Supply environment variables, reverse-proxy HTTPS, secure cookies and persistent storage at `/app/.reports`. Browsers never receive database credentials.

The original C#/Blazor implementation remains recoverable in Git at `606bf85`. Java server forms replace Blazor component interactions, so layouts and HTTP interaction details differ. Core workflow tests do not establish exact parity with every historical interaction or real plant/email integration. Verify deployment-specific endpoints and delivery settings before operational cutover.
