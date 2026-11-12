-- Last episode of a multi-episode file (S01E01-E02). NULL when the file holds one episode.
-- The link target becomes "Show - S01E01-E02.ext" and availability marks the whole range (PARSE-06).
ALTER TABLE SourceItems ADD COLUMN LastEpisodeNumber INTEGER NULL;
