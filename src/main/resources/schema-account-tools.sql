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
CREATE TABLE IF NOT EXISTS "GsoEmailChanges" (
 "UserId" varchar(450) PRIMARY KEY REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
 "NewEmail" varchar(256) NOT NULL, "OldEmail" varchar(256) NOT NULL,
 "CodeHash" varchar(256) NOT NULL, "SecurityStamp" varchar(256) NOT NULL,
 "RequestedAt" timestamp with time zone NOT NULL, "ExpiresAt" timestamp with time zone NOT NULL,
 "Attempts" integer NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS "GsoPasswordChanges" (
 "UserId" varchar(450) PRIMARY KEY REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
 "Email" varchar(256) NOT NULL, "CodeHash" varchar(256) NOT NULL,
 "SecurityStamp" varchar(256) NOT NULL, "RequestedAt" timestamp with time zone NOT NULL,
 "ExpiresAt" timestamp with time zone NOT NULL, "Attempts" integer NOT NULL DEFAULT 0,
 "VerifiedAt" timestamp with time zone
);
