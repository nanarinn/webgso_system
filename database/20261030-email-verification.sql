CREATE TABLE IF NOT EXISTS "GsoEmailVerifications" (
 "UserId" text PRIMARY KEY REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
 "TokenHash" varchar(64) NOT NULL UNIQUE,
 "Email" text NOT NULL,
 "SecurityStamp" text NOT NULL,
 "ExpiresAt" timestamp with time zone NOT NULL,
 "RequestedAt" timestamp with time zone NOT NULL,
 "UsedAt" timestamp with time zone
);
-- Retire unsent approval notices. Existing account statuses and data are preserved.
UPDATE "UserAccessNotifications" SET "DeliveryStatus"='Cancelled'
WHERE "DeliveryStatus"='Pending' AND ("NotificationType"='AccessRequest'
 OR ("NotificationType"='AccessDecision' AND "Subject"='GSO Web Services - Access request received'));
