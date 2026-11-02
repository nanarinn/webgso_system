# Change a password from My Profile

1. Open My Profile → Edit profile → Change password.
2. Enter the current GSO password and select Send code.
3. Open the current account's Gmail inbox and enter the six-digit code in GSO.
4. Select Verify code. Only successful verification opens the new password form.
5. Enter and confirm the new password, then select Change password.
6. Sign in again using the account email and new password.

The old password remains unchanged until the final step succeeds. The code and verified
permission expire ten minutes after the request. Codes allow five attempts and resending
requires sixty seconds. Codes are bound to the account ID, email and security stamp,
stored as salted PBKDF2 hashes and excluded from in-app notifications. Gmail SMTP must
be configured for delivery. Verification message content is cleared after delivery.

The additive GsoPasswordChanges table stores the temporary verification state. Successful
changes rotate security stamps to invalidate existing sessions and send a security notice.
Forgot password remains a separate email-link recovery flow for users unable to sign in.
