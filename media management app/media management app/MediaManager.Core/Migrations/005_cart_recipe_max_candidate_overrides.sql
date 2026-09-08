-- Optional per-media limits used only by manual Torrent Cart runs.
-- NULL means the selected recipe's maxCandidatesPerFetch remains effective.
ALTER TABLE TrackedShows ADD COLUMN CartEpisodeMaxCandidatesOverride INTEGER;
ALTER TABLE TrackedShows ADD COLUMN CartPackMaxCandidatesOverride INTEGER;
ALTER TABLE TrackedMovies ADD COLUMN CartMaxCandidatesOverride INTEGER;
