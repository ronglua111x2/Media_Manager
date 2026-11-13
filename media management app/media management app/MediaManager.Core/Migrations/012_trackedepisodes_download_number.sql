-- The number an episode's assigned download is named with, when TMDB renumbered the episode after the download was
-- chosen (a recap inserted before it). Auto-link finds the episode's file in the download by this number and links it
-- at the episode's current number, instead of refusing it or moving the link back (audit L3-09).
-- NULL means the download is named with the episode's own number.
ALTER TABLE TrackedEpisodes ADD COLUMN DownloadSeasonNumber INTEGER NULL;
ALTER TABLE TrackedEpisodes ADD COLUMN DownloadEpisodeNumber INTEGER NULL;
