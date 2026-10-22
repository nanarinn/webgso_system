# WEBGSO Java development guide

- Runtime: Java 21, Spring Boot, Spring MVC/Thymeleaf, Spring Security, JDBC and PostgreSQL.
- Verify with `mvnw.cmd verify` on Windows or `./mvnw verify` elsewhere. Stop a running JAR before replacing it on Windows.
- Keep credentials in process/service environment or the existing local secret store. Never commit real passwords or tokens.
- Use the explicit `demo` profile for isolated synthetic data. Never enable schema initialization against the existing database as a migration shortcut.
- Preserve quoted PostgreSQL table/column names, local forecast timestamps and UTC lifecycle instants.
- Enforce administration roles, CSRF, report ownership and current account approval/security stamps on the server.
- Preserve plant API HTTPS, DNS, private-host, redirect and response-size restrictions.
- Test changes to business rules, permissions, parsing, scheduling and reports.
- Preserve Single Buyer branding and responsive layouts. Keep product flows focused on repository operations.
- Java source: `src/main/java/my/webgso`; views: `src/main/resources/templates`; tests: `src/test/java/my/webgso`.
- `/health` is public. `/api/ping`, `/api/status` and `/api/dashboard` require authentication.
- Keep workers disabled while the old runtime is active. Real email requires explicit service configuration.
