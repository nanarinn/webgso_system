# Account tools and local backups

## Password reset

Use **Forgot password?** on the login page. Only active local Gmail accounts with a password can request a reset. Unknown addresses, Google-only accounts and inactive accounts receive the same public confirmation; they are not activated by reset.

A cryptographically random, single-use link expires after 30 minutes. Only its SHA-256 hash is stored in GsoPasswordResets. It is bound to the account email and security stamp. A new request replaces the old link and is limited to once per minute per account. GET opens the form without consuming the link. The CSRF-protected POST validates matching passwords, updates the password hash, rotates the security stamp and signs out old sessions. Passwords are never sent by email.

Gmail SMTP and WEBGSO_PUBLIC_BASE_URL must remain configured. On a phone, use the current public HTTPS origin; localhost links refer to the phone itself. Temporary Cloudflare links require the server computer and tunnel to remain running. A changed tunnel URL requires a new reset/verification email.

## Email settings

**My Profile → Email settings** controls only the signed-in account. Saving preferences opts that Gmail account into the selected system notifications. The existing configured alert recipient keeps current defaults until changed. Other accounts default to optional API alerts off and automatic Excel reports on.

- Failure: first failure or success-to-failure transition.
- Recovery: failure-to-success transition.
- Stale: first stale-data warning; repeated stale results remain quiet until a fresh result clears the condition.
- Excel: automatic XLSX summary of the account owner's successful batch. The manual Email Excel action remains available.
- Digest: saved API status at 09:00 MYT while the server is running.

Preferences are checked again before queued optional alerts are delivered. Verification and password-security messages cannot be disabled. Global mail/alert switches still apply. No collection/report workers are enabled by these settings.

## API timing and guide

Plant Config shows Last tested in MYT and the next permitted test time per API. When no active Collection Schedule requires a delay, Next test reads Available now. These indicators do not perform API requests automatically.

**User Guide** in the sidebar covers signup, plant/API setup, individual tests, warning meanings, history, export, email, passwords and backup recovery. My Profile remains the final workspace menu item.

## Daily local PostgreSQL backups

The normal non-demo run-java.ps1 launcher enables WEBGSO_BACKUP_ENABLED=true unless explicitly configured. Other deployments must opt in using that environment variable. No full schema initialization is used: schema-account-tools.sql adds only the two account-tool tables.

The backup service checks one minute after startup and every five minutes thereafter. It creates one completed custom-format PostgreSQL archive per MYT calendar day; a missed day is not fabricated. The app and computer must be running. Seven latest completed archives are retained. Only filenames matching gso-YYYYMMDD-HHMMSS.dump in the configured directory are eligible for rotation. Unrelated files and incomplete dumps are not counted as successful backups. A file lock prevents overlapping runs on the same folder.

Default directory: java-migration/.tools/database-backups (ignored by Git).

Environment:

- WEBGSO_BACKUP_ENABLED: true/false.
- WEBGSO_BACKUP_DIRECTORY: private server directory, outside public static assets.
- webgso.backup.pg-dump: Spring property for an alternate pg_dump binary. Windows defaults to C:/Program Files/PostgreSQL/18/bin/pg_dump.exe; other systems use pg_dump on PATH.

The service reuses configured database credentials through the pg_dump child process environment, never command arguments or Git. It has a two-minute timeout, writes to a partial file, and publishes a completed archive only on success. Status appears in Email settings. A local backup on the same disk does not protect against loss of the computer: copy completed archives to secure external storage as appropriate. Archives contain sensitive application data; they are never exposed through a download endpoint or emailed.

### Recovery by a trusted maintainer

1. Stop application writes and preserve the current database before any restore.
2. Inspect the custom archive with pg_restore --list ARCHIVE.dump.
3. Create a separate empty PostgreSQL database and restore there using pg_restore --no-owner --dbname=NEW_DATABASE ARCHIVE.dump, with credentials entered securely through the usual local mechanism.
4. Verify users, plant/API counts and application behavior against that separate database before any deliberate production switch.

No automatic restore, database replacement or restore test against the live database is performed.
