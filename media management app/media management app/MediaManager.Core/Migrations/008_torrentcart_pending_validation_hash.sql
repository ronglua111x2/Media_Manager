-- Infohash of a torrent the add gate put into qBittorrent but has not finished validating.
-- Lets startup recovery re-validate or release it after a crash / power loss; reconcile never adopts it.
ALTER TABLE TorrentCartOrders ADD COLUMN PendingValidationHash TEXT NOT NULL DEFAULT '';
