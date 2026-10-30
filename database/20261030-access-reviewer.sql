-- Pin approval authority to Affiqah's existing account ID, not to editable profile fields.
CREATE TABLE IF NOT EXISTS "GsoAccessReviewer" (
  "Id" integer PRIMARY KEY CHECK ("Id" = 1),
  "UserId" text NOT NULL UNIQUE REFERENCES "AspNetUsers"("Id")
);
INSERT INTO "GsoAccessReviewer" ("Id", "UserId")
SELECT 1, "Id" FROM "AspNetUsers"
WHERE "NormalizedEmail" = 'AFFIQAHADRIANNA@GMAIL.COM' AND "AccessStatus" = 'Approved'
AND NOT EXISTS (SELECT 1 FROM "GsoAccessReviewer")
ORDER BY "Id" LIMIT 1;
