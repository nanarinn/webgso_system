CREATE TABLE IF NOT EXISTS "GsoPasswordResets" (
 "UserId" varchar(450) PRIMARY KEY REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
 "TokenHash" varchar(64) NOT NULL UNIQUE, "Email" varchar(256) NOT NULL,
 "SecurityStamp" varchar(256) NOT NULL, "RequestedAt" timestamp with time zone NOT NULL,
 "ExpiresAt" timestamp with time zone NOT NULL, "UsedAt" timestamp with time zone
);
CREATE TABLE IF NOT EXISTS "GsoEmailPreferences" (
 "UserId" varchar(450) PRIMARY KEY REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
 "Failure" boolean NOT NULL DEFAULT true, "Recovery" boolean NOT NULL DEFAULT true,
 "Stale" boolean NOT NULL DEFAULT true, "Excel" boolean NOT NULL DEFAULT true,
 "Digest" boolean NOT NULL DEFAULT true
);
