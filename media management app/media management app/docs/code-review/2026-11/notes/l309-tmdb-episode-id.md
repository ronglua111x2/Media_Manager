# L3-09: track linked episodes by TMDB episode id

Session: side session started by local_dae9158f. Worktree `<repo>\.claude\worktrees\backup-audit-e0f13c`,
branch `claude/tmdb-episode-identity-3f2516` (base 03deec0 = `claude/fix-parser-integration` HEAD). Local only, never pushed.

## TMDB id check
- Could not run a TMDB request: the app's TMDB token is only in the state folder (`settings.json`), which this task may not touch.
  api.tmdb.org answers 401 without a key (sibling note tmdb-findings.md).
- Public search (TMDB talk, Plex/Jellyfin forums, TVMaze) found no official statement on ids across renumbering.
- ASSUMPTION used: an episode `id` is the TMDB record id. Editing `episode_number`/`season_number` of a record keeps its id;
  deleting an episode and adding a new one gives a new id. Editors may do a season split either way, so the code must
  handle both: id kept -> link moves; id gone -> keep link, flag removed upstream, log, never guess.
- Episode-group responses (`tv/episode_group/{id}`) list the base episode objects, which carry the same `id`.

## Design (as built)
- `TmdbEpisodeDetails.TmdbEpisodeId`, `TrackedEpisode.TmdbEpisodeId`, migration 011, upsert keeps the stored id when TMDB
  gives none (COALESCE).
- Refresh (`TrackedShowService.ImportShow`, existing show): plan BEFORE the upsert.
  - Rows (not only the torrent fields) follow their id: the whole TrackedEpisodes row is renumbered (Id kept), so torrent
    hash/state/candidate, rating/thought and cart orders (EpisodeId) follow; cart order Season/Episode updated too.
    The recap gets a fresh row from the upsert.
  - Target key held by a row that does not move away: unused -> deleted first; in use -> move cancelled (cascade).
  - Links: `AutoTorrentLinkService.MoveEpisodeLinks` renames library hardlinks via temp names (chains/swaps), all-or-nothing
    with rollback; per-key blocks for inconsistent multi-episode ranges and stray files at the target.
    Imports outside the app naming: record + symlink move only, file untouched.
  - Symlinks/NFO: new hub event `HardlinksMoved` -> `SymlinkSyncService.MoveItems` (remove all old symlinks, then create
    new) + coordinator deletes old NFO/subtitles, writes new NFO, queues Jellyfin refresh.
  - Rows that hold download state (link/torrent/cart order) whose id vanished or whose move was blocked are PINNED: keep id,
    skip the upsert of their key and id, flag RemovedUpstreamUtc, warn. Next refresh retries.
  - First refresh after update: ids are filled, nothing moves (no old ids).

## Progress
- [x] eedef63 plumbing (model/provider/migration 011/db), provider test `TmdbEpisodeIdParsingTests` (default season + episode group).
- [x] red tests `Link/TmdbEpisodeIdentityTests.cs`: 9 of 10 failed on behaviour before the fix (the 10th,
      first refresh without ids, is a guard that already held).
- [x] 7b89bce implementation (planner, link moves, symlink MoveItems, coordinator handler). Suites green.
- [x] 44ab3b2 one DB transaction for rows + link records (`ApplyEpisodeRenumbering`), files put back if it fails.
      Red test first: imported library files swapping numbers hit UNIQUE(FilePath) halfway, files already renamed.
      + `RenumberedEpisodeNfoTests` through the real SymlinkCoordinatorService (mutation-checked: fails without handler).
- [x] 278b328 blocked move: unused row on the pinned episode's new number is removed (was Missing -> double download).
      Red test first.
- Suites at 278b328: App 446/446, Core 405 passed + 4 skipped (Debug, x64 for App).
- [x] independent review (subagent) of 03deec0..278b328: 1 high, 2 medium, 4 low. All verified in code, red tests
      written, fixed:
  - 1d0184b: auto-link selected/validated the download's file by the row's NEW number -> range file / batch torrent
    re-linked back to old numbers on the next reconcile; download finishing after the move refused forever.
    Fix: migration 012 `DownloadSeasonNumber/DownloadEpisodeNumber` (set on move when a torrent is assigned, cleared
    when the assigned hash changes); LinkEpisodeCoreAsync finds the file by it and links at the current number,
    shifting a range by the same offset. Two refreshes at once: per-show import lock + DB guard (row must still be at
    From, else throw -> files put back; mutation-checked). Season merged into another (season no longer returned):
    ids found elsewhere are followed. Two ids moved onto one TMDB number: not moved, flagged (was UNIQUE failure on
    every refresh). Auto-Track start (AutoTrackFromSeason/Episode) follows its episode.
  - 5cd097f: subtitles next to a moved symlink are renamed with it (parked under temp names first), not deleted.
- FINAL (5cd097f): App 453/453 (+22 new: 19 identity, 1 coordinator NFO/subtitle, 2 provider), Core 405 passed +
  4 skipped. Commits: eedef63 7b89bce 44ab3b2 278b328 1d0184b 5cd097f on claude/tmdb-episode-identity-3f2516.

## Known limits (for the report)
- No live TMDB check (token is in the forbidden state folder). Assumption above.
- A pinned row (id gone) stays flagged while its link exists; the new TMDB episode at that number is not tracked
  until the user resets that episode (rule: never guess). Logged as a warning each refresh.
- "Auto-Track from SxxEyy" set on a number TMDB has not listed yet cannot follow (no id to follow).
- A season TMDB stops returning keeps its (now empty) TrackedSeasons row (as before this change).
- A manual pack link (user applies a pack again) maps files by their file names, so it would undo a move for a pack
  whose files carry the old numbers. The automatic pack path (PackLinkCoordinatorService) only inspects, never
  re-links; single-episode auto-link refuses a file whose name says another episode (NamesAnotherEpisode), and the
  recap row cannot adopt the old torrent because its hash is assigned to the moved row (test covers reconcile).
- Crash window left: between the file renames and the DB commit (ms). Then the records point at old paths:
  link-status check marks them missing; files stay in the library under the new names.
