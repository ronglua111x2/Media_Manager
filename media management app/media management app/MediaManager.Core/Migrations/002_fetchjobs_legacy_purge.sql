-- One-time cleanup of legacy FetchJobs rows. The table schema is kept;
-- FetchJobService no longer persists to this table.
DELETE FROM FetchJobs;
