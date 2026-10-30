# Test email verification from a phone

This uses the same running Java GSO application and PostgreSQL database through a temporary HTTPS Cloudflare Quick Tunnel. The phone can use mobile data. No hosting subscription or new database is created.

1. Install `cloudflared` from https://developers.cloudflare.com/tunnel/downloads/ or use the portable executable in the workspace's parent `.tools` folder.
2. In PowerShell, open this project and run `./start-phone-verification.ps1`. This starts a tunnel and restarts only this project's application on port 5187 with the returned HTTPS origin. Local encrypted Gmail credentials are loaded normally; do not put an App Password in this script or Git.
3. Open the printed HTTPS `/register` address. Sign up with a Gmail address. Existing Pending accounts can use **Resend verification email** on the HTTPS login page.
4. On the phone, open the newest GSO email, follow its link and select **Verify email**. Then sign in. Opening the link alone does not consume the token.
5. Run `./stop-phone-verification.ps1` when finished. Restart `./run-java.ps1 -Port 5187` normally to use localhost links again.

The URL changes after a tunnel restart. Request a new email after changing the URL; old links still contain their previous hostname. Keep the PC awake, online and running GSO/cloudflared. This is temporary development access, not an always-on production service. Anyone with the URL can reach the login/register pages; protected pages retain the existing GSO authentication and authorization.

The application remains bound to loopback. The runner accepts an explicit HTTPS origin, uses it for email links and sets secure session cookies. Use the HTTPS address for browser testing. Google OAuth needs its own registered redirect URI; creating this tunnel does not configure Google sign-in.

Logs and the current URL/process IDs are in ignored `.tools/phone-*` files. Do not share verification-token URLs or credentials.

Official Quick Tunnel documentation: https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/
