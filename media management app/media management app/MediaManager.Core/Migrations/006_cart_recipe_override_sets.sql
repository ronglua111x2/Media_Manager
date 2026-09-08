-- Per-media cart override sets for manual Run Cart. Values persist when override is toggled off.
-- enabled=false keeps the last value; missing keys mean the property has never been overridden.
ALTER TABLE TrackedShows ADD COLUMN CartEpisodeOverridesJson TEXT;
ALTER TABLE TrackedShows ADD COLUMN CartPackOverridesJson TEXT;
ALTER TABLE TrackedMovies ADD COLUMN CartOverridesJson TEXT;

UPDATE TrackedShows
SET CartEpisodeOverridesJson = '{"maxCandidates":{"enabled":true,"value":' || CartEpisodeMaxCandidatesOverride || '}}'
WHERE CartEpisodeMaxCandidatesOverride IS NOT NULL;

UPDATE TrackedShows
SET CartPackOverridesJson = '{"maxCandidates":{"enabled":true,"value":' || CartPackMaxCandidatesOverride || '}}'
WHERE CartPackMaxCandidatesOverride IS NOT NULL;

UPDATE TrackedMovies
SET CartOverridesJson = '{"maxCandidates":{"enabled":true,"value":' || CartMaxCandidatesOverride || '}}'
WHERE CartMaxCandidatesOverride IS NOT NULL;
