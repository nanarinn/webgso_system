# Retired manual approval flow

Manual approval is no longer part of sign-up. The Access requests menu is removed and its GET/POST routes deny all access, including Affiqah's account. The historical reviewer table is retained for database compatibility and is not used to activate new registrations.

Local sign-up activates through a single-use email verification link. Google sign-in activates after Google verifies the identity and email. Existing approved accounts are preserved; inactive/rejected accounts are not reactivated by email links or Google login.

See `EMAIL_VERIFICATION.md` for configuration and verification steps.