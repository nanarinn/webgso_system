# Changing a profile email

Open My Profile → Edit profile → Change email. Enter the new Gmail address and current
password, then select Send code. Enter the six-digit code received at the new address
and select Verify & change email. The existing address remains active until verification.
After verification, sign in with the new address; existing sessions are invalidated.

Codes expire after ten minutes, allow five attempts, and can be resent after sixty
seconds. Resending replaces the previous code. Challenges are bound to the signed-in
account, its current email and security stamp. Codes are salted PBKDF2 hashes in the
challenge table. The email queue holds the message until delivery; code text is removed
from delivered messages. Gmail SMTP must be configured and enabled for delivery.

The standard profile save cannot change email. Google-managed accounts retain their
identity-provider email. A change notice is sent to the previous email address.
The additive GsoEmailChanges table is created by AccountToolsSchema at startup.
