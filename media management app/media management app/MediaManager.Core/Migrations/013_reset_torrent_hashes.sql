-- Hashes of the torrents an episode, season pack or movie was reset from ("Reset for redownload"), comma-separated
-- and lower-case; every reset adds one. The reset keeps the torrent in qBittorrent, so reconcile adopted it again by
-- name and the reset was undone (cross-review 2, B). Reconcile skips these hashes for that row. Not the blacklist:
-- a blacklisted hash means "malicious" and the add gate deletes it with its files.
ALTER TABLE TrackedEpisodes ADD COLUMN ResetTorrentHashes TEXT NOT NULL DEFAULT '';
ALTER TABLE TrackedSeasons ADD COLUMN ResetTorrentHashes TEXT NOT NULL DEFAULT '';
ALTER TABLE TrackedMovies ADD COLUMN ResetTorrentHashes TEXT NOT NULL DEFAULT '';
