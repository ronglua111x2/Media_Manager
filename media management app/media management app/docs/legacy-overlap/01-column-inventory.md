# 01 — Column and API inventory

**Live DB snapshot:** 2026-08-27, `D:\MediaManagerState\media-manager.db`  
**Counts:** 33 shows, 10 movies, 0 `FetchJobs` rows.

Classification:

- **Live conflict** — leftover still wins over recipes for some matching
- **Dead leftover** — stored/defaulted, not used for matching (or no callers)
- **Current core** — keep; not leftover

---

## Live conflict

### `TrackedShows.PreferredQuality` / `TrackedMovies.PreferredQuality`

| | |
|--|--|
| Type | `TEXT NOT NULL DEFAULT '1080p'` |
| Live values | **33/33 shows `1080p`**, **10/10 movies `1080p`**, none empty, none `2160p` |
| Written | Add/import hardcodes `"1080p"` ([`TrackedShowService.ImportShow`](../../Services/TrackedShowService.cs), [`TrackedMovieService`](../../Services/TrackedMovieService.cs)). Upsert preserves existing. Refresh copies persisted value. |
| Intended editor | Old “Auto Torrent preferences” (`UpdatePreferences` logs that name). |
| UI today | **None.** `UpdatePreferredQuality` / `UpdatePreferences` have **no ViewModel callers**. |
| Read for matching | **Shows:** snapshot + pack in [`FetchJobService`](../../Services/FetchJobService.cs) via `ParseQualities(show.PreferredQuality)`. **Movies:** `GetMovieCandidateRejectionReason` still contains `movie.PreferredQuality` but **has no callers**. |
| Display leftover | `LibraryShowDetailViewModel.PreferencesSummary` / movie equivalent — **not bound in XAML**. |

Same family:

| Column | Live | Matching readers |
|--------|------|------------------|
| `PreferredAudioCodec` | all empty | Snapshot + `CandidateMatcher` (unused method) score audio from **show** column. Parallel sequential search uses **recipe** filter audio via `EvaluateEpisode`. |
| `MinimumSeeders` | all `0` | Snapshot + pack use **show** column. Parallel sequential uses **recipe** `filter.MinimumSeeders`. |

Because live seeders/audio are empty/zero, they do not currently clip 4K. Quality does.

DB APIs with no UI:

- `IDatabaseService.UpdateTrackedShowPreferredQuality`
- `UpdateTrackedShowPreferences` / `UpdateTrackedMoviePreferences`
- `ITrackedShowService.UpdatePreferredQuality` / `UpdatePreferences`
- `ITrackedMovieService.UpdatePreferences`

---

## Dead leftover (keep columns; low matching risk)

### `FetchJobs` table

| | |
|--|--|
| Status | Table still `CREATE TABLE IF NOT EXISTS`. Migration **002** purged rows once. Live count **0**. |
| Writers at runtime | `CreateFetchJob` / `UpdateFetchJob` / `GetFetchJobs` exist on `IDatabaseService` with **no service callers** except library delete (`DeleteFetchJobsForMedia` / `DeleteAllFetchJobs`). |
| Matching | None. In-memory candidate cache in `FetchJobService` is unrelated. |
| Docs | [STATE_FOLDER.md](../STATE_FOLDER.md) FetchJobs purge |

Do not drop in the first decouple pass.

### Unused matching helpers (code, not columns)

| Symbol | Note |
|--------|------|
| `FetchJobService.MapEpisodeCandidates` | `IDE0051` unused; uses `CandidateMatcher` + `selectedQualities` (show prefs). |
| `CandidateMatcher.MatchEpisodeCandidate` | Only called from that unused method. Parallel live path uses `EvaluateEpisode`. |
| `FetchJobService.IsUsableMovieCandidate` / `GetMovieCandidateRejectionReason` | No callers; would use `movie.PreferredQuality` if revived. |
| `GetMovieCandidates` cache | Interface exists; movie cart does not fill it via this leftover helper. |

---

## Confusing but not a DB leftover

### Recipe `qualityAllowList` on every module

Each `RecipeModuleConfig` has `qualityAllowList` (default `["1080p"]`). **Matching that uses recipes** reads **Candidate Filter** only ([`CandidateEvaluationService.GetCommonRejectReason`](../../MediaManager.Core/Services/CandidateEvaluationService.cs)). Query Builder list is used for `{quality}` in **search queries** ([`SearchPlanBuilder.GetQualities`](../../MediaManager.Core/Services/SearchPlanBuilder.cs)).

Identity / Search Source / Parser / Scoring lists on user recipes can disagree (High Quality Filter has 2160p; Scoring still 1080p). Snapshot matching ignores all of them and uses the **show** column instead — that is the 4K TV bug, not Scoring’s 1080p list.

### `settings.json` → `AutoTorrent` search defaults

[`AutoTorrentSettings`](../../MediaManager.Core/Models/AutoTorrentSettings.cs) still has `MaxCandidatesPerFetch`, `UseShowSnapshotSearch`, snapshot/parallel timeouts, etc. Recipes copy those into Search Source `extensionData` when creating defaults. Runtime prefers **recipe** keys, then these as fallback ([`RecipeRuntimeSettings`](../../MediaManager.Core/Services/RecipeRuntimeSettings.cs)).

They are **fallbacks**, not a second quality allow list. Settings UI does **not** expose most of these (they live on the recipe). Two owners for timeouts/caps, not for 2160p vs 1080p.

---

## Current core (do not treat as leftover)

### Auto-Track quality (show + global)

| Fields | Role |
|--------|------|
| Show: `AutoTrackMinQuality`, `AutoTrackMinSeeders`, `AutoTrackMinFileSizeMb`, `AutoTrackMaxFileSizeMb`, `AutoTrackAllowedQualities` | Per-show override; UI on Auto-Track cards |
| Settings: `AutoTrack.Quality.*` | Global hunt policy |
| Code | [`AutoTrackCandidatePolicyService`](../../Services/AutoTrackCandidatePolicyService.cs) **after** fetch |

Live: **11** shows have Auto-Track quality fields set; Breaking Bad does **not**. Hunt failures like President Curtis S01E05 (`MinFileSizeMb=600`) are this layer, not `PreferredQuality`.

### Recipe assignment

`TrackedShows.RecipeId` / `PackRecipeId`, `TrackedMovies.RecipeId` — current. Cart recipe dropdowns write these.

### Cart vs episode selected-candidate copies

`TorrentCartOrders` + `TorrentCartOrderCandidates` are the cart working set. `TrackedEpisodes` / `TrackedMovies` `SelectedCandidate*` and torrent hash/state are **copied on accept/add** and used by reconciliation. Dual write is **current design**, not the 4K allow-list bug. Do not fold into “drop leftover prefs” without a separate design.

### `AutoTrack.Search.ForceParallelEpisodeSearch`

Current hunt option (Settings). Live file: **`false`**. When `true`, hunt skips snapshot and uses recipe `EvaluateEpisode`. When `false`, hunt uses the recipe’s snapshot flag → leftover quality on snapshot recipes.
