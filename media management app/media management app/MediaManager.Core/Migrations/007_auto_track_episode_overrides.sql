-- Independent Auto-Track hunt overrides for the assigned episode recipe.
-- Separate from CartEpisodeOverridesJson (manual Run Cart only).
ALTER TABLE TrackedShows ADD COLUMN AutoTrackEpisodeOverridesJson TEXT;

-- One-time backfill from per-show Use custom quality (min seeders + min size MB).
-- Min quality, max size, and allowed qualities are not migrated.
UPDATE TrackedShows
SET AutoTrackEpisodeOverridesJson =
    CASE
        WHEN AutoTrackMinSeeders IS NOT NULL AND IFNULL(AutoTrackMinFileSizeMb, 0) > 0 THEN
            '{"minSeeders":{"enabled":true,"value":' || AutoTrackMinSeeders ||
            '},"minSizeGb":{"enabled":true,"value":' || printf('%.2f', AutoTrackMinFileSizeMb / 1024.0) || '}}'
        WHEN AutoTrackMinSeeders IS NOT NULL THEN
            '{"minSeeders":{"enabled":true,"value":' || AutoTrackMinSeeders || '}}'
        ELSE
            '{"minSizeGb":{"enabled":true,"value":' || printf('%.2f', AutoTrackMinFileSizeMb / 1024.0) || '}}'
    END
WHERE AutoTrackEpisodeOverridesJson IS NULL
  AND (AutoTrackMinSeeders IS NOT NULL OR IFNULL(AutoTrackMinFileSizeMb, 0) > 0);
