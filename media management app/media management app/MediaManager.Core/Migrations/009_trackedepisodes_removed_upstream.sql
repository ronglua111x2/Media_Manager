-- When TMDB no longer lists an episode (renumbering, season split), the row is kept only if
-- something still refers to it (library link, torrent, cart order, rating or notes) and is
-- flagged here so the UI can show it. NULL means TMDB still lists the episode.
ALTER TABLE TrackedEpisodes ADD COLUMN RemovedUpstreamUtc TEXT;
