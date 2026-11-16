# Admin console

Admin accounts can open **Registered users** (`/admin/registered-users`) to see the total registered accounts and a paginated, read-only list of usernames, names, emails, roles, account status and registration time in MYT.

This page does not provide account approval or account mutations. User accounts cannot access it directly or through the menu.

The Email column shows whether a user has changed their email. Expand **Email changed · View history** to see the latest ten previous/new addresses and change times in MYT. The complete history remains stored in `GsoEmailChangeHistory`; changes made before this feature was introduced are not reconstructed.

Both Admin and User accounts can edit their own name in **My Profile → Edit profile**. **Change email** requires the current password and another exact `@sae-malaysia.com` address that is not already in use. **Change password** requires the current password, a new password and confirmation. Neither action sends email codes. Successful email or password changes invalidate existing sessions and require signing in again; email changes preserve the user's ID and role. Domain validation does not claim that a mailbox has been verified.

Server controls and the in-application restart feature have been removed. The previous server-controls GET and restart POST routes no longer have handlers.
