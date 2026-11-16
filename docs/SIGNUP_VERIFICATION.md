# SAE signup verification

Email verification for public signup is temporarily disabled at the user's request. New accounts using the exact @sae-malaysia.com domain are created as Approved with the User role and can sign in with their password immediately. Signup does not queue a verification email or claim mailbox ownership (`emailConfirmed` remains false). Password policy, duplicate protection, CSRF, and existing account statuses are preserved.

The explicit legacy email verification flow remains available internally for existing pending accounts. The public registration route does not use it.

## Historical SES activation (6 November 2026; public signup verification now disabled)

Server: i-0169c2d5a6674e1ea, region ap-southeast-5. After user authorization, attached WebGsoSignupSesProfile with role WebGsoSignupSesRole and inline policy SendSignupVerification (ses-signup-policy.json). Trust principal: ec2.amazonaws.com with sts:AssumeRole. No AWS access keys are stored in application settings. The tested application was deployed: 218 tests passed; AWS and local login health checks returned HTTP 200 after restart.

Use these service settings alongside the existing database settings:

```properties
WEBGSO_MAIL_ENABLED=true
WEBGSO_MAIL_PROVIDER=ses
WEBGSO_MAIL_FROM=developer@sae-malaysia.com
WEBGSO_SES_REGION=ap-southeast-5
WEBGSO_MAIL_DISPATCH_ENABLED=true
WEBGSO_MAIL_VERIFICATION_ONLY=true
WEBGSO_PUBLIC_BASE_URL=https://56.69.52.36
```

Confirm the public HTTPS address works on recipient devices. An untrusted certificate must be corrected for a normal production verification experience.

Local and AWS share the database. Disable the local mail dispatcher with WEBGSO_MAIL_DISPATCH_ENABLED=false; let only AWS deliver queued verification messages. Verification-only mode avoids sending unrelated old queued alerts when email is re-enabled.

SES sender verification is complete, but the account is still in the SES sandbox. Recipients must also be verified in the same SES region during testing. Request SES production access before allowing delivery to arbitrary new SAE recipients. The SAE domain signup restriction does not bypass the sandbox restriction.

Deployment and service restart are complete. Inbox delivery has not yet been verified end to end. After SES recipient eligibility is ready, test an actual signup using an eligible mailbox: Pending before confirmation, email received from developer, passive GET does not activate, confirmation activates once, replay fails, then password login succeeds. Check delivery failures without logging tokens or credentials. SES acceptance alone does not prove inbox delivery.

The IAM Policy Autopilot CLI and its installers were unavailable in this environment; the prepared policy uses the official SES v2 service authorization reference: https://docs.aws.amazon.com/service-authorization/latest/reference/list_sesv2.html . The user authorized application of this policy; it is now attached as described above.


