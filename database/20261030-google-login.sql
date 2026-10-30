-- Additive migration only. Run against an existing database before enabling Google login.
CREATE TABLE IF NOT EXISTS "GsoGoogleIdentities" (
  "Subject" varchar(255) PRIMARY KEY,
  "UserId" text NOT NULL UNIQUE REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE
);
