# Email verification sign-up

New local accounts stay Pending until their own email is verified. No other user approves the registration.

Apply `database/20261030-email-verification.sql` to an existing database before starting this version. This adds the token table and cancels unsent old approval-request notifications. It does not change existing account statuses or plant data. Do not enable schema initialization on an existing database.

Configure Gmail SMTP as before. The existing local encrypted Gmail credential is loaded by `run-java.ps1`. Verification emails use the `EmailVerification` notification queue and configured sender. GSO must remain running to deliver queued email. SMTP acceptance is not an inbox-delivery guarantee.

Set `WEBGSO_PUBLIC_BASE_URL` to the trusted application origin used in emailed links, for example `https://gso.example.com`. It is never inferred from incoming request headers. The localhost default is `http://localhost:5187`; these links work only from the same computer running GSO. Other computers need an accessible HTTPS deployment address.

## User flow

1. Sign up using username, Gmail address and password. The username is used as the initial display name.
2. Open the verification email and its link, then select **Verify email**. Opening the GET link alone does not activate an account, preventing email scanners from consuming it.
3. The account becomes active and the user signs in normally. Verification does not sign the user in automatically.
4. The link expires in 24 hours and is single-use. The server stores a SHA-256 token hash, binds it to the account's email/security stamp and atomically consumes it. A replaced link cannot be used.
5. Use **Resend verification email** on login for an expired/missing link or a legacy Pending account. Resend responses do not reveal whether the address exists. At most one new verification email is queued per account per minute.

Previously approved accounts remain active. Rejected/inactive accounts cannot activate through verification. Verified Google accounts activate directly through Google's validated OpenID Connect identity. Existing email/password accounts are not linked to Google automatically by matching email.

Verification URLs must not be posted in chat or public logs. Verification pages send `Cache-Control: no-store` and `Referrer-Policy: no-referrer`. The outbox necessarily contains the email body for delivery; protect database access as with other mail credentials and user data.

For temporary HTTPS testing from a phone over mobile data, see [PHONE_VERIFICATION.md](PHONE_VERIFICATION.md).
