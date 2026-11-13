-- TMDB id of the episode a TrackedEpisodes row belongs to. When TMDB renumbers episodes (a recap inserted, a
-- season split), the row, its library link and its download follow this id to the new number instead of staying
-- on the old number, which now names another episode (audit L3-09). NULL until the next TMDB refresh fills it.
ALTER TABLE TrackedEpisodes ADD COLUMN TmdbEpisodeId INTEGER NULL;
