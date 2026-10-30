# WEBGSO Java development guide

- Runtime: Java 21, Spring Boot, Spring MVC with Java component views, Spring Security, JDBC and PostgreSQL.
- Verify with `mvnw.cmd verify` on Windows or `./mvnw verify` elsewhere. Stop a running JAR before replacing it on Windows.
- Keep credentials in process/service environment or the existing local secret store. Never commit real passwords or tokens.
- Use the explicit `demo` profile for isolated synthetic data. Never enable schema initialization against the existing database as a migration shortcut.
- Preserve quoted PostgreSQL table/column names, local forecast timestamps and UTC lifecycle instants.
- Admin is the only application role. Read former assignments through Accounts compatibility mapping; never offer other roles. New local registrations activate only through their own single-use email verification link; verified Google identities can activate without another user's approval. Keep manual approval routes retired. Enforce CSRF, report/batch ownership and current account status/security stamps on the server.
- Public local signup, email/password login, Google sign-in and profile email changes accept only the exact @gmail.com domain. Login uses email, never username. Preserve email verification and current account status checks.
- Preserve plant API HTTPS, DNS, private-host, redirect and response-size restrictions.
- Test changes to business rules, permissions, parsing, scheduling and reports.
- Preserve GSO branding and responsive layouts. Keep product flows focused on repository operations.
- Java source: `src/main/java/my/webgso`; Java views: `src/main/java/my/webgso/ui`; tests: `src/test/java/my/webgso`.
- Build page content with the escaping Java component tree. Do not add raw HTML templates or unescaped user-provided markup. Keep business rules in Java. The user-requested location-filter enhancement may use the small plant-location-filters.js script solely to select, lock and remove filter controls without reload; retain server validation and native form fallback. Every POST form must use PageContext.post to retain CSRF tokens.
- Keep CSS and database SQL in their native formats. These are presentation assets and database definitions, not a second application programming language.
- `/health` is public. `/api/ping`, `/api/status` and `/api/dashboard` require authentication.
- Keep workers disabled while the old runtime is active. Real email requires explicit service configuration.
