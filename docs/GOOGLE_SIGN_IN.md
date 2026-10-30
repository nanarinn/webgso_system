# Google sign-in setup

Google sign-in is optional and disabled until its OAuth credentials are configured. Gmail SMTP credentials continue to handle email delivery separately.

## Create the OAuth client

1. Open https://console.cloud.google.com/ and select or create a project named **GSO Web Services**.
2. Open **Google Auth Platform**. Complete **Branding** with the application name, support email and developer contact email.
3. In **Audience**, choose **External** for personal Gmail accounts. While the application is in Testing, add the Google accounts that will test it as test users.
4. In **Data Access**, use only `openid`, `email`, and `profile`. GSO does not request Gmail inbox or sending permissions through this login.
5. In **Clients**, create a client with application type **Web application**, name **GSO local**.
6. Add this exact **Authorized redirect URI**:

   ```text
   http://localhost:5187/login/oauth2/code/google
   ```

7. Copy the Client ID and Client secret into the local setup script below. Keep the secret out of chat, screenshots and GitHub.

Official documentation: https://developers.google.com/identity/protocols/oauth2/web-server#creatingcred

## Configure Windows

Apply `database/20261030-google-login.sql` once to an existing PostgreSQL database before enabling Google login. This adds only the Google identity binding table; do not enable schema initialization on an existing database. New demo/test databases already include the table.

From the Java project folder, run:

```powershell
.\setup-google-login.ps1
```

The script prompts locally and saves a Windows DPAPI-encrypted credential outside the repository. Restart the app using:

```powershell
.\run-java.ps1 -Port 5187
```

For a hosted service, set `GOOGLE_LOGIN_ENABLED=true`, `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET` and `GOOGLE_REDIRECT_URI` through the service secret store. Register the exact HTTPS callback URL in Google Cloud. The Windows script is for localhost port 5187; other ports need a matching `GOOGLE_REDIRECT_URI`.

## Approval and email

- First Google sign-in verifies the identity through Google and creates a **Pending** request. It does not grant workspace access.
- Only the designated reviewer, Affiqah's existing account, receives approval request emails and can review requests. Apply `database/20261030-access-reviewer.sql` on an existing database to establish this assignment.
- The new user receives a request-received email and an approval/rejection email after review in **Access requests**.
- Repeated Google sign-ins while pending do not create duplicate users or approval requests.
- After approval, Google login establishes the same local security-stamped session used by email login. Rejected, inactive or locked accounts cannot enter.
- Existing email/password accounts are not automatically linked by matching email. They must continue using email/password; an authenticated account-linking flow would be needed to add Google to those existing accounts.
- Email delivery requires GSO and Gmail SMTP to be running/configured; SMTP acceptance does not guarantee inbox placement.

## Verify

Use a test Google account whose email is not already registered. Confirm the pending page appears and protected pages remain inaccessible. Sign in as Affiqah, approve the request, check the user's email, and retry Google sign-in. Verify rejected users remain blocked. Never share Client secrets or Gmail App Passwords in screenshots.
