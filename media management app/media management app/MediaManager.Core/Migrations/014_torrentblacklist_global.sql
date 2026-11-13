-- SEC-A07: a torrent the add gate found malicious (bad files, or a known-bad infohash) is blocked for every show and
-- movie, not only the one it was first offered for. IsGlobal = 1 rows match whatever ShowId the check is for.
-- Manual rejects ("wrong content for this show") stay per show. Not related to ResetTorrentHashes (migration 013):
-- a reset means "do not attach again to this target", never "malicious".
ALTER TABLE TorrentBlacklist ADD COLUMN IsGlobal INTEGER NOT NULL DEFAULT 0;
UPDATE TorrentBlacklist SET IsGlobal = 1 WHERE Reason LIKE 'Malware:%' OR Reason = 'Blacklisted infohash';
