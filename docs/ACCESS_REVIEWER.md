# Access request owner

Only Affiqah's designated existing account can view `/admin/users` or approve/reject requests. An approved account can use the workspace without gaining approval authority. Admin remains the only application role.

Apply `database/20261030-access-reviewer.sql` once on an existing PostgreSQL database. It creates a singleton reviewer record and selects the existing approved account with email `affiqahadrianna@gmail.com`. It grants no access to new registrations and does not replace an existing reviewer assignment.

The assignment stores the immutable user ID. Changing display names, usernames or emails does not grant reviewer privileges to another account. There is no public or profile endpoint to change this assignment. Restoring a lost reviewer requires a deliberate database administrator operation; absence of a reviewer fails closed.

The server checks the designated account, current approval status, role and security stamp. Both GET and POST access to the approval routes are denied to other users, even when they enter URLs manually. New-request emails go only to the approved designated reviewer. User request-received and decision emails continue using the user's email.
