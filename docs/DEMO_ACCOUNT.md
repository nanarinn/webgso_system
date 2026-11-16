# Password-only demo account

Login ID: `demo.admin@example.test`. This is a reserved test address, not a Gmail mailbox.
The public registration form still accepts Gmail only. The login form also accepts
this exact demo identity, provided its username is `demo.admin`.

The explicit startup option `--webgso.demo.replace-affiqah=true` replaces the login
identity `affiqahadrianna@gmail.com` with Demo Admin. It preserves the database user
ID and plant/API records, replaces the password hash, clears lockout, changes the
security stamp to invalidate old sessions and verification codes, and removes the
old Google binding and signup verification token. The old email/password no longer
logs in. No email is sent. If that email is absent, a new demo account is created.
An existing conflicting demo identity aborts replacement rather than merging users.

Passwords are never built into the production code. Set `WEBGSO_DEMO_PASSWORD`
securely before the one-time startup. Use at least 12 characters with uppercase,
lowercase, a number and a symbol. Subsequent starts do not overwrite an existing
demo password. Remove the setup option and variable after successful setup.

On AWS, use the already configured PostgreSQL connection environment:

```bash
read -r -s -p 'New demo password: ' WEBGSO_DEMO_PASSWORD
export WEBGSO_DEMO_PASSWORD
echo
java -jar target/webgso-system-2.0.0-SNAPSHOT.jar --webgso.demo.replace-affiqah=true
```

This requires a newly built JAR containing DemoAccountSetup. Keep the normal
PostgreSQL profile: do not use the isolated `demo` profile or initialize the schema
on the existing database. Local Windows and AWS databases must each be updated.
Changing one does not update the other.

The demo identity has full Admin access and should be used on a restricted testing
deployment. Its reserved email cannot receive password-reset messages; an operator
must recover a forgotten password. Ordinary password changes follow the current
account settings workflow until a dedicated password-only change flow is added.
