# WEBGSO Java development guide

- Latest signup policy: valid SAE registrations become Approved with the User role and initial password CHANGEME. First login requires setting and confirming a new password before any system access, then logs out for a fresh login. Preserve existing accounts. Do not send or require signup email verification. Keep emailConfirmed false because mailbox ownership was not verified. Preserve existing account statuses and own-profile password flows. Local and AWS share the database.

- Runtime: Java 21, Spring Boot, Spring MVC with Java component views, Spring Security, JDBC and PostgreSQL.
- Verify with `mvnw.cmd verify` on Windows or `./mvnw verify` elsewhere. Stop a running JAR before replacing it on Windows.
- Keep credentials in process/service environment or the existing local secret store. Never commit real passwords or tokens.
- Use the explicit `demo` profile for isolated synthetic data. Never enable schema initialization against the existing database as a migration shortcut.
- Preserve quoted PostgreSQL table/column names, local forecast timestamps and UTC lifecycle instants.
- Preserve plant API HTTPS, DNS, private-host, redirect and response-size restrictions.
- Test changes to business rules, permissions, parsing, scheduling and reports.
- Preserve GSO branding and responsive layouts. Keep product flows focused on repository operations.
- Java source: `src/main/java/my/webgso`; Java views: `src/main/java/my/webgso/ui`; tests: `src/test/java/my/webgso`.
- Build page content with the escaping Java component tree. Do not add raw HTML templates or unescaped user-provided markup. Keep business rules in Java. The user-requested location-filter enhancement may use the small plant-location-filters.js script solely to select, lock and remove filter controls without reload; retain server validation and native form fallback. Every POST form must use PageContext.post to retain CSRF tokens.
- Keep CSS and database SQL in their native formats. These are presentation assets and database definitions, not a second application programming language.
- `/health` is public. `/api/ping`, `/api/status` and `/api/dashboard` require authentication.
- Keep workers disabled while the old runtime is active. Real email requires explicit service configuration.
- Latest role policy supersedes Admin-only: Admin manages plant/API records; User can view plants and test URLs through /user routes with no plant/API mutations. Public signup receives User. Retain own-profile access, CSRF, status and security-stamp checks.
- Admin console provides a read-only registered-user list restricted to Admin. Server controls and in-application restart have been removed at the user request; do not restore them.
- User navigation includes Plant Config at /user/plant-config for viewing configured plants and testing individual APIs through existing User test routes. Keep plant/API create, edit and delete restricted to Admin.
- Both roles can edit their own name and change their own password or SAE email after confirming their current password, without email codes. Email changes retain roles and identity, rotate security stamps, and require signing in again with the new email. Keep an atomic old/new email history visible only to Admin in Registered users; never accept a submitted user ID as the owner of a profile change.
- Latest User Management policy supersedes the read-only console: Admin may activate, deactivate or delete User accounts with CSRF and explicit delete confirmation; protect own/Admin accounts. Rotate security stamps on status changes. Keep atomic name and email change history visible only to Admin. Place User Management immediately above My Profile. Never restore server controls.
- SAE signup/login accepts exact @sae-malaysia.com addresses. All valid addresses on that exact domain may sign up; no per-address allowlist applies. Signup assigns User and becomes Approved without email verification. Preserve CSRF, lockout, current status and security stamps. Legacy non-SAE records cannot sign in under production policy; compatibility tests explicitly disable it. Do not restore Google sign-in.


