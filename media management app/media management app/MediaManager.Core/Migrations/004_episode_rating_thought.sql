-- Personal per-episode rating (0–10) and thought notes.
-- NULL means unset; clearing the UI writes NULL so empty rows stay compact.
ALTER TABLE TrackedEpisodes ADD COLUMN UserRating REAL;
ALTER TABLE TrackedEpisodes ADD COLUMN Thought TEXT;
