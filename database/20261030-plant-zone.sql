-- Preserve existing plant information; leave zone unassigned until explicitly selected.
ALTER TABLE "LssPlants" ADD COLUMN IF NOT EXISTS "Zone" text NOT NULL DEFAULT '';
