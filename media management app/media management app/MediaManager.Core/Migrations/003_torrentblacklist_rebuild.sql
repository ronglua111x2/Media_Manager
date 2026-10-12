-- Marker / documentation for migration 003.
-- The rebuild is conditional on a legacy TorrentHash column and is implemented in
-- TorrentBlacklistRebuildMigration.cs (SQLite cannot branch on PRAGMA from plain SQL).
-- This file is intentionally a no-op so SchemaMigrations ordering stays file-visible.
SELECT 1 WHERE 0;
