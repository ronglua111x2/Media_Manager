# Schema Inventory — Four-Pillars Initiative Baseline

**Status:** Sprint 0 deliverable (design only — no code)  
**Baseline date:** Initiative kickoff (Aug 2026)  
**Source:** Live `media-manager.db` export → [schema-export-initiative-start.sql](./schema-export-initiative-start.sql)  
**Related:** [01-database-migration-versioning.md](./01-database-migration-versioning.md) · [05-sprint-timeline.md](./05-sprint-timeline.md) (Sprint 2 implements runner)

---

## 1. Table list

11 tables at initiative start (10 application tables + 1 SQLite system table):

| # | Table | Purpose |
| --- | --- | --- |
| 1 | `SourceItems` | Scanned library files (shows/movies), parser metadata, match state |
| 2 | `SeriesMappings` | Parsed-title → provider identity mappings (anime absolute, etc.) |
| 3 | `TrackedShows` | TMDB-tracked series + Auto-Track settings |
| 4 | `TrackedSeasons` | Per-season pack/candidate selection, download folder |
| 5 | `TrackedEpisodes` | Episode availability, torrent state, selected candidates |
| 6 | `TrackedMovies` | TMDB-tracked movies + torrent/candidate state |
| 7 | `FetchJobs` | **Legacy** batch-fetch job queue (purged once in migration 002) |
| 8 | `TorrentCartOrders` | Cart order rows (episode/season/movie targets) |
| 9 | `TorrentCartOrderCandidates` | Ranked candidates per cart order |
| 10 | `TorrentBlacklist` | Rejected/suspicious torrent listings per show |
| 11 | `sqlite_sequence` | SQLite internal AUTOINCREMENT counter (system) |

**Note:** `SchemaMigrations` is created by `MigrationRunner` (Sprint 2). Embedded scripts: `MediaManager.Core/Migrations/001_baseline.sql`, `002_fetchjobs_legacy_purge.sql`.

---

## 2. Column definitions (PRAGMA `table_info` format)

Derived from [schema-export-initiative-start.sql](./schema-export-initiative-start.sql).  
Columns: `cid | name | type | notnull | dflt_value | pk`

### SourceItems

| cid | name | type | notnull | dflt_value | pk |
| --- | --- | --- | --- | --- | --- |
| 0 | Id | INTEGER | 0 | | 1 |
| 1 | SourceRootFolder | TEXT | 1 | | 0 |
| 2 | ParentFolder | TEXT | 1 | | 0 |
| 3 | FilePath | TEXT | 1 | | 0 |
| 4 | FileName | TEXT | 1 | | 0 |
| 5 | ScanText | TEXT | 1 | | 0 |
| 6 | ShowTitle | TEXT | 0 | | 0 |
| 7 | SeasonNumber | INTEGER | 0 | | 0 |
| 8 | EpisodeNumber | INTEGER | 0 | | 0 |
| 9 | EpisodeTitle | TEXT | 0 | | 0 |
| 10 | State | INTEGER | 1 | | 0 |
| 11 | Notes | TEXT | 0 | | 0 |
| 12 | LinkedPath | TEXT | 0 | | 0 |
| 13 | LastSeenUtc | TEXT | 1 | | 0 |
| 14 | MediaKind | INTEGER | 1 | 0 | 0 |
| 15 | MovieTitle | TEXT | 0 | | 0 |
| 16 | MovieYear | INTEGER | 0 | | 0 |
| 17 | ParserPattern | INTEGER | 1 | 0 | 0 |
| 18 | MatchedTitle | TEXT | 0 | | 0 |
| 19 | MatchedYear | INTEGER | 0 | | 0 |
| 20 | Provider | TEXT | 0 | | 0 |
| 21 | ProviderId | TEXT | 0 | | 0 |
| 22 | MatchConfidence | REAL | 0 | | 0 |
| 23 | MatchReason | TEXT | 0 | | 0 |
| 24 | RequiresManualReview | INTEGER | 1 | 0 | 0 |
| 25 | MatchAccepted | INTEGER | 1 | 0 | 0 |
| 26 | UseAbsoluteAnimeMapping | INTEGER | 1 | 0 | 0 |
| 27 | MappedSeasonNumber | INTEGER | 0 | | 0 |
| 28 | MappedEpisodeNumber | INTEGER | 0 | | 0 |
| 29 | EpisodeMappingSource | TEXT | 0 | | 0 |
| 30 | EpisodeMappingConfidence | REAL | 0 | | 0 |
| 31 | EpisodeMappingReason | TEXT | 0 | | 0 |
| 32 | AutoTorrentLinkKind | INTEGER | 0 | | 0 |
| 33 | AutoTorrentTorrentHash | TEXT | 0 | | 0 |
| 34 | AutoTorrentPackOwnerSeasonNumber | INTEGER | 0 | | 0 |
| 35 | IsExternalImport | INTEGER | 1 | 0 | 0 |
| 36 | SymlinkPath | TEXT | 0 | | 0 |
| 37 | IsOrphanPackSpecial | INTEGER | 1 | 0 | 0 |

**Constraints:** `FilePath` UNIQUE

### SeriesMappings

| cid | name | type | notnull | dflt_value | pk |
| --- | --- | --- | --- | --- | --- |
| 0 | Id | INTEGER | 0 | | 1 |
| 1 | ParsedTitle | TEXT | 1 | | 0 |
| 2 | NormalizedParsedTitle | TEXT | 1 | | 0 |
| 3 | ParserPattern | INTEGER | 1 | | 0 |
| 4 | MatchedTitle | TEXT | 1 | | 0 |
| 5 | MatchedYear | INTEGER | 0 | | 0 |
| 6 | Provider | TEXT | 1 | | 0 |
| 7 | ProviderId | TEXT | 1 | | 0 |
| 8 | UseAbsoluteAnimeMapping | INTEGER | 1 | 0 | 0 |
| 9 | CreatedUtc | TEXT | 1 | | 0 |
| 10 | UpdatedUtc | TEXT | 1 | | 0 |

**Constraints:** UNIQUE(`NormalizedParsedTitle`, `ParserPattern`)

### TrackedShows

| cid | name | type | notnull | dflt_value | pk |
| --- | --- | --- | --- | --- | --- |
| 0 | Id | INTEGER | 0 | | 1 |
| 1 | TmdbId | INTEGER | 1 | | 0 |
| 2 | Title | TEXT | 1 | | 0 |
| 3 | FirstAirYear | INTEGER | 0 | | 0 |
| 4 | Overview | TEXT | 0 | | 0 |
| 5 | PosterPath | TEXT | 0 | | 0 |
| 6 | PreferredQuality | TEXT | 1 | '1080p' | 0 |
| 7 | CreatedUtc | TEXT | 1 | | 0 |
| 8 | UpdatedUtc | TEXT | 1 | | 0 |
| 9 | PreferredAudioCodec | TEXT | 1 | '' | 0 |
| 10 | MinimumSeeders | INTEGER | 1 | 0 | 0 |
| 11 | RecipeId | TEXT | 0 | | 0 |
| 12 | PackRecipeId | TEXT | 0 | | 0 |
| 13 | SeriesStatus | INTEGER | 1 | 0 | 0 |
| 14 | AutoTrackFromSeason | INTEGER | 0 | | 0 |
| 15 | AutoTrackFromEpisode | INTEGER | 0 | | 0 |
| 16 | AutoTrackDownloadFolder | TEXT | 0 | | 0 |
| 17 | AutoTrackAutoReconcileAndLink | INTEGER | 1 | 1 | 0 |
| 18 | AutoTrackAnchorDayOfWeek | INTEGER | 0 | | 0 |
| 19 | AutoTrackAnchorTimeLocal | TEXT | 0 | | 0 |
| 20 | AutoTrackLastTmdbWeekKey | TEXT | 0 | | 0 |
| 21 | AutoTrackTmdbState | INTEGER | 1 | 0 | 0 |
| 22 | AutoTrackMinQuality | TEXT | 0 | | 0 |
| 23 | AutoTrackMinSeeders | INTEGER | 0 | | 0 |
| 24 | AutoTrackMinFileSizeMb | INTEGER | 0 | | 0 |
| 25 | AutoTrackMaxFileSizeMb | INTEGER | 0 | | 0 |
| 26 | AutoTrackAllowedQualities | TEXT | 0 | | 0 |
| 27 | AlternativeTitlesJson | TEXT | 0 | | 0 |
| 28 | ExcludedAlternativeTitlesJson | TEXT | 0 | | 0 |
| 29 | WatchStatus | INTEGER | 1 | 0 | 0 |
| 30 | WatchedEpisodes | INTEGER | 1 | 0 | 0 |
| 31 | PlannedEpisodeCount | INTEGER | 1 | 0 | 0 |
| 32 | EpisodeGroupId | TEXT | 0 | | 0 |
| 33 | EpisodeGroupName | TEXT | 0 | | 0 |
| 34 | AutoTrackLastTmdbRefreshLocal | TEXT | 0 | | 0 |
| 35 | Rating | REAL | 0 | | 0 |
| 36 | Thought | TEXT | 0 | | 0 |

**Constraints:** `TmdbId` UNIQUE

### TrackedSeasons

| cid | name | type | notnull | dflt_value | pk |
| --- | --- | --- | --- | --- | --- |
| 0 | Id | INTEGER | 0 | | 1 |
| 1 | ShowId | INTEGER | 1 | | 0 |
| 2 | SeasonNumber | INTEGER | 1 | | 0 |
| 3 | EpisodeCount | INTEGER | 1 | 0 | 0 |
| 4 | DownloadFolder | TEXT | 0 | | 0 |
| 5 | ManagementMode | INTEGER | 1 | 0 | 0 |
| 6 | SelectedPackCandidateName | TEXT | 0 | | 0 |
| 7 | SelectedPackCandidateUrl | TEXT | 0 | | 0 |
| 8 | SelectedPackCandidatePlugin | TEXT | 0 | | 0 |
| 9 | SelectedPackCandidateFileSize | INTEGER | 1 | 0 | 0 |
| 10 | SelectedPackCandidateSeeders | INTEGER | 1 | 0 | 0 |
| 11 | SelectedPackCandidateQuality | TEXT | 0 | | 0 |
| 12 | SelectedPackCandidateAudioCodec | TEXT | 0 | | 0 |
| 13 | SelectedPackCoveredSeasons | TEXT | 0 | | 0 |
| 14 | SelectedPackOwnerSeasonNumber | INTEGER | 0 | | 0 |
| 15 | PackTorrentHash | TEXT | 0 | | 0 |
| 16 | PackTorrentName | TEXT | 0 | | 0 |
| 17 | PackTorrentState | TEXT | 0 | | 0 |
| 18 | PackTorrentProgress | REAL | 1 | 0 | 0 |
| 19 | IsHidden | INTEGER | 1 | 0 | 0 |
| 20 | SelectedPackContentProfile | TEXT | 0 | | 0 |
| 21 | LastPackLinkTorrentHash | TEXT | 0 | | 0 |
| 22 | LastPackLinkUtc | TEXT | 0 | | 0 |

**Constraints:** UNIQUE(`ShowId`, `SeasonNumber`); FK `ShowId` → `TrackedShows(Id)` ON DELETE CASCADE

### TrackedEpisodes

| cid | name | type | notnull | dflt_value | pk |
| --- | --- | --- | --- | --- | --- |
| 0 | Id | INTEGER | 0 | | 1 |
| 1 | ShowId | INTEGER | 1 | | 0 |
| 2 | SeasonNumber | INTEGER | 1 | | 0 |
| 3 | EpisodeNumber | INTEGER | 1 | | 0 |
| 4 | Title | TEXT | 1 | | 0 |
| 5 | AirDate | TEXT | 0 | | 0 |
| 6 | Availability | INTEGER | 1 | 0 | 0 |
| 7 | IsWanted | INTEGER | 1 | 0 | 0 |
| 8 | CreatedUtc | TEXT | 1 | | 0 |
| 9 | UpdatedUtc | TEXT | 1 | | 0 |
| 10 | TorrentHash | TEXT | 0 | | 0 |
| 11 | TorrentName | TEXT | 0 | | 0 |
| 12 | TorrentState | TEXT | 0 | | 0 |
| 13 | TorrentProgress | REAL | 1 | 0 | 0 |
| 14 | TorrentUpdatedUtc | TEXT | 0 | | 0 |
| 15 | SelectedCandidateName | TEXT | 0 | | 0 |
| 16 | SelectedCandidateUrl | TEXT | 0 | | 0 |
| 17 | SelectedCandidatePlugin | TEXT | 0 | | 0 |
| 18 | SelectedCandidateFileSize | INTEGER | 1 | 0 | 0 |
| 19 | SelectedCandidateSeeders | INTEGER | 1 | 0 | 0 |
| 20 | SelectedCandidateQuality | TEXT | 0 | | 0 |
| 21 | SelectedCandidateAudioCodec | TEXT | 0 | | 0 |
| 22 | Overview | TEXT | 0 | | 0 |
| 23 | VoteAverage | REAL | 0 | | 0 |
| 24 | StillPath | TEXT | 0 | | 0 |
| 25 | AutoTrackLastEnrichLocal | TEXT | 0 | | 0 |

**Constraints:** UNIQUE(`ShowId`, `SeasonNumber`, `EpisodeNumber`); FK `ShowId` → `TrackedShows(Id)` ON DELETE CASCADE

**Added after baseline** (`004_episode_rating_thought`):

| name | type | notes |
| --- | --- | --- |
| UserRating | REAL NULL | Personal 0–10 score; `NULL` = unset |
| Thought | TEXT NULL | Personal note; `NULL` / empty = unset |

### FetchJobs *(legacy — purged in migration 002)*

| cid | name | type | notnull | dflt_value | pk |
| --- | --- | --- | --- | --- | --- |
| 0 | Id | INTEGER | 0 | | 1 |
| 1 | ShowId | INTEGER | 1 | | 0 |
| 2 | ShowTitle | TEXT | 1 | | 0 |
| 3 | Status | INTEGER | 1 | 0 | 0 |
| 4 | TotalEpisodes | INTEGER | 1 | 0 | 0 |
| 5 | ProcessedEpisodes | INTEGER | 1 | 0 | 0 |
| 6 | ErrorSummary | TEXT | 0 | | 0 |
| 7 | CreatedUtc | TEXT | 1 | | 0 |
| 8 | StartedUtc | TEXT | 0 | | 0 |
| 9 | FinishedUtc | TEXT | 0 | | 0 |
| 10 | TargetKind | INTEGER | 1 | 1 | 0 |

### TrackedMovies

| cid | name | type | notnull | dflt_value | pk |
| --- | --- | --- | --- | --- | --- |
| 0 | Id | INTEGER | 0 | | 1 |
| 1 | TmdbId | INTEGER | 1 | | 0 |
| 2 | Title | TEXT | 1 | | 0 |
| 3 | ReleaseYear | INTEGER | 0 | | 0 |
| 4 | Overview | TEXT | 0 | | 0 |
| 5 | PosterPath | TEXT | 0 | | 0 |
| 6 | PreferredQuality | TEXT | 1 | '1080p' | 0 |
| 7 | PreferredAudioCodec | TEXT | 1 | '' | 0 |
| 8 | MinimumSeeders | INTEGER | 1 | 0 | 0 |
| 9 | Availability | INTEGER | 1 | 0 | 0 |
| 10 | IsWanted | INTEGER | 1 | 1 | 0 |
| 11 | TorrentHash | TEXT | 0 | | 0 |
| 12 | TorrentName | TEXT | 0 | | 0 |
| 13 | TorrentState | TEXT | 0 | | 0 |
| 14 | TorrentProgress | REAL | 1 | 0 | 0 |
| 15 | TorrentUpdatedUtc | TEXT | 0 | | 0 |
| 16 | SelectedCandidateName | TEXT | 0 | | 0 |
| 17 | SelectedCandidateUrl | TEXT | 0 | | 0 |
| 18 | SelectedCandidatePlugin | TEXT | 0 | | 0 |
| 19 | SelectedCandidateFileSize | INTEGER | 1 | 0 | 0 |
| 20 | SelectedCandidateSeeders | INTEGER | 1 | 0 | 0 |
| 21 | SelectedCandidateQuality | TEXT | 0 | | 0 |
| 22 | SelectedCandidateAudioCodec | TEXT | 0 | | 0 |
| 23 | CreatedUtc | TEXT | 1 | | 0 |
| 24 | UpdatedUtc | TEXT | 1 | | 0 |
| 25 | RecipeId | TEXT | 0 | | 0 |
| 26 | AlternativeTitlesJson | TEXT | 0 | | 0 |
| 27 | ExcludedAlternativeTitlesJson | TEXT | 0 | | 0 |
| 28 | WatchStatus | INTEGER | 1 | 0 | 0 |
| 29 | Rating | REAL | 0 | | 0 |
| 30 | Thought | TEXT | 0 | | 0 |

**Constraints:** `TmdbId` UNIQUE

### TorrentCartOrders

| cid | name | type | notnull | dflt_value | pk |
| --- | --- | --- | --- | --- | --- |
| 0 | Id | INTEGER | 0 | | 1 |
| 1 | TargetKind | INTEGER | 1 | | 0 |
| 2 | MediaId | INTEGER | 1 | | 0 |
| 3 | EpisodeId | INTEGER | 0 | | 0 |
| 4 | SeasonNumber | INTEGER | 0 | | 0 |
| 5 | EpisodeNumber | INTEGER | 0 | | 0 |
| 6 | Title | TEXT | 1 | | 0 |
| 7 | Summary | TEXT | 1 | | 0 |
| 8 | Status | INTEGER | 1 | | 0 |
| 9 | StatusDetail | TEXT | 1 | '' | 0 |
| 10 | SelectedCandidateName | TEXT | 1 | '' | 0 |
| 11 | SelectedCandidateUrl | TEXT | 1 | '' | 0 |
| 12 | SelectedCandidatePlugin | TEXT | 1 | '' | 0 |
| 13 | SelectedCandidateFileSize | INTEGER | 1 | 0 | 0 |
| 14 | SelectedCandidateSeeders | INTEGER | 1 | 0 | 0 |
| 15 | SelectedCandidateLeechers | INTEGER | 1 | 0 | 0 |
| 16 | SelectedCandidateQuality | TEXT | 1 | '' | 0 |
| 17 | SelectedCandidateAudioCodec | TEXT | 1 | '' | 0 |
| 18 | SelectedCandidateCoveredSeasons | TEXT | 1 | '' | 0 |
| 19 | SelectedCandidateTotalScore | INTEGER | 1 | 0 | 0 |
| 20 | TorrentHash | TEXT | 1 | '' | 0 |
| 21 | TorrentName | TEXT | 1 | '' | 0 |
| 22 | TorrentState | TEXT | 1 | '' | 0 |
| 23 | TorrentProgress | REAL | 1 | 0 | 0 |
| 24 | CreatedUtc | TEXT | 1 | | 0 |
| 25 | UpdatedUtc | TEXT | 1 | | 0 |
| 26 | Source | INTEGER | 1 | 0 | 0 |
| 27 | SelectedCandidateContentProfile | TEXT | 1 | '' | 0 |
| 28 | FailedCandidateUrls | TEXT | 1 | '' | 0 |
| 29 | LastFailureReason | TEXT | 1 | '' | 0 |

### TorrentCartOrderCandidates

| cid | name | type | notnull | dflt_value | pk |
| --- | --- | --- | --- | --- | --- |
| 0 | Id | INTEGER | 0 | | 1 |
| 1 | OrderId | INTEGER | 1 | | 0 |
| 2 | Rank | INTEGER | 1 | 0 | 0 |
| 3 | IsSelected | INTEGER | 1 | 0 | 0 |
| 4 | IsAccepted | INTEGER | 1 | 0 | 0 |
| 5 | Name | TEXT | 1 | '' | 0 |
| 6 | Url | TEXT | 1 | '' | 0 |
| 7 | PluginName | TEXT | 1 | '' | 0 |
| 8 | FileSize | INTEGER | 1 | 0 | 0 |
| 9 | Seeders | INTEGER | 1 | 0 | 0 |
| 10 | Leechers | INTEGER | 1 | 0 | 0 |
| 11 | Quality | TEXT | 1 | '' | 0 |
| 12 | AudioCodec | TEXT | 1 | '' | 0 |
| 13 | CoveredSeasons | TEXT | 1 | '' | 0 |
| 14 | TotalScore | INTEGER | 1 | 0 | 0 |
| 15 | ContentProfileJson | TEXT | 1 | '' | 0 |

### TorrentBlacklist

| cid | name | type | notnull | dflt_value | pk |
| --- | --- | --- | --- | --- | --- |
| 0 | Id | INTEGER | 0 | | 1 |
| 1 | ListingUrl | TEXT | 1 | '' | 0 |
| 2 | InfoHash | TEXT | 1 | '' | 0 |
| 3 | ShowId | INTEGER | 1 | | 0 |
| 4 | Reason | TEXT | 1 | | 0 |
| 5 | SuspiciousFilesJson | TEXT | 0 | | 0 |
| 6 | DateAddedUtc | TEXT | 1 | | 0 |
| 7 | IsActive | INTEGER | 1 | 1 | 0 |
| 8 | Notes | TEXT | 0 | | 0 |

### sqlite_sequence *(SQLite system table)*

| cid | name | type | notnull | dflt_value | pk |
| --- | --- | --- | --- | --- | --- |
| 0 | name | | 0 | | 0 |
| 1 | seq | | 0 | | 0 |

---

## 3. Migration file layout

Planned folder (implemented Sprint 2):

```
Migrations/
├── 001_baseline.sql              # Effective schema at initiative start (this inventory)
├── 002_fetchjobs_legacy_purge.sql # One-time DELETE FROM FetchJobs; mark legacy
└── 003_*.sql                     # Optional later (e.g. TorrentBlacklist rebuild extraction)
```

| File | Purpose | When applied |
| --- | --- | --- |
| `001_baseline.sql` | CREATE TABLE statements matching §2 above; replaces ad-hoc `EnsureColumn` for fresh installs | Sprint 2 — first migration in chain |
| `002_fetchjobs_legacy_purge.sql` | `DELETE FROM FetchJobs`; records one-time legacy cleanup | Sprint 2 — immediately after 001 |
| `003_*` (optional) | Extract inline TorrentBlacklist rebuild from `DatabaseService` | Sprint 2 if time, else deferred |

**History table** (created by runner, not in baseline export):

```sql
CREATE TABLE SchemaMigrations (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL UNIQUE,
    AppliedUtc TEXT NOT NULL
);
```

---

## 4. Migration numbering rules

These rules are **locked** for the four-pillars initiative. See also [01-database-migration-versioning.md](./01-database-migration-versioning.md).

| Rule | Detail |
| --- | --- |
| **Integer prefix** | Files named `001_`, `002_`, `003_`, … (three-digit zero-padded) |
| **Never edit applied scripts** | Once a migration is merged and may have run on any DB, add a **new** numbered file — do not modify the old one |
| **One migration = one transaction** | Runner wraps each script in a single SQLite transaction; failure rolls back that migration only |
| **Fresh install path** | Empty DB runs the **full chain** from `001` — no separate “create all tables” code path |
| **001 = baseline** | Schema as of initiative start (this document + `schema-export-initiative-start.sql`) |
| **002 = FetchJobs purge** | One-time row delete; removes harmful every-boot `PurgeLegacyFetchJobs()` from init hot path |
| **Idempotent re-run** | Runner checks `SchemaMigrations`; already-applied names are skipped |
| **Failure policy** | Block app startup; error dialog points to `CreateSafeSnapshot()` / Google Drive restore |
| **Table drop deferred** | `DROP TABLE FetchJobs` is cosmetic — optional migration 003+ after purge is stable |

---

## 5. Core type move list

Approved move order for `MediaManager.Core` extraction. **Move, do not copy.**  
Reference: [02-unit-tests-critical-paths.md](./02-unit-tests-critical-paths.md) · [05-sprint-timeline.md](./05-sprint-timeline.md) Sprint 1 & 3.

### Sprint 1 — Parser boundary + shared deps

| Type | Current location | Notes |
| --- | --- | --- |
| `TorrentCandidateParser` | `Services/TorrentCandidateParser.cs` | Static pure parsing; ≥15 unit tests |
| `TorrentCandidateParseResult` | same file | Parser output DTO |
| `TorrentReleaseKind` | `Services/TorrentReleaseKind.cs` | Used by parser/evaluation |
| `ParserPattern` | `Common/AppEnums.cs` | Parser pattern enum |
| `CandidateRejectReason` | `Models/RecipeRunRequest.cs` | Evaluation reject reasons (move with Sprint 1 or 3 — prefer Sprint 1 if parser tests need it) |
| `TorrentSearchResult` | `Models/TorrentSearchResult.cs` | Search result DTO for fixtures |
| `SearchRecipe` | `Models/SearchRecipe.cs` | Recipe model (partial — blocks needed by parser tests) |
| `RecipeBlockType` | `Models/SearchRecipe.cs` | Recipe module enum |
| `TrackedShow` | `Models/` | Fixture builder dependency |
| `TrackedEpisode` | `Models/` | Fixture builder dependency |

**Sprint 1 deliverables:** `MediaManager.Core` (net8.0) + `MediaManager.Core.Tests` (xUnit, FluentAssertions); WPF project references Core; ≥15 parser tests.

**Sprint 1 explicitly out:** evaluation, search plan, pack, validation services (Sprint 3).

### Sprint 3 — Critical-path services + test matrix

| Type | Current location | Lines (approx) | Test target |
| --- | --- | --- | --- |
| `CandidateEvaluationService` | `Services/CandidateEvaluationService.cs` | ~298 | ≥12 tests — accept/reject matrix |
| `ICandidateEvaluationService` | `Services/ICandidateEvaluationService.cs` | | Interface moves with impl |
| `SearchPlanBuilder` | `Services/SearchPlanBuilder.cs` | ~202 | ≥8 tests — query templates |
| `ISearchPlanBuilder` | `Services/ISearchPlanBuilder.cs` | | Interface moves with impl |
| `SearchTitleResolver` | `Services/SearchTitleResolver.cs` | ~84 | Alias/title resolution |
| `ISearchTitleResolver` | `Services/ISearchTitleResolver.cs` | | Mock in evaluation tests |
| `PackSeasonFileGrouper` | `Services/PackSeasonFileGrouper.cs` | ~101 | ≥5 tests — folder grouping |
| `PackEpisodePatternInferrer` | `Services/PackEpisodePatternInferrer.cs` | ~418 | ≥10 tests — stem inference |
| `TorrentContentValidationService` | `Services/ITorrentContentValidationService.cs` | ~313 | ≥8 tests — `ValidateFilesAsync` only |
| `ITorrentContentValidationService` | same file | | Interface moves with impl |
| `TorrentContentFile` | `Models/` | | Validation input DTO |
| `RecipeRuntimeSettings` | `Services/RecipeRuntimeSettings.cs` | | Recipe flag helpers (if needed by evaluation) |

**Sprint 3 targets:** ≥40 total unit tests; advisory ≥80% line coverage on parser + evaluation (coverlet locally); no WPF reference from test project.

**Sprint 3 explicitly out:** `FetchJobService`, `AutoTrackService` (integration tests later).

### Types staying in WPF (initiative scope)

| Type | Reason |
| --- | --- |
| `FetchJobService` | Orchestration + qBittorrent polling — Sprint 9 split candidate |
| `AutoTrackService` | Scheduling, WARP, notifications — Sprint 7 split |
| `DatabaseService` | Migration runner Sprint 2; repo split Sprint 9 |
| All ViewModels | Sprint 4+ (hooks, then splits) |

---

## 6. Branch strategy

Aligned with [06-ai-execution-guide.md §2.5](./06-ai-execution-guide.md#25-branch-strategy).

```
main
 └── initiative/four-pillars          ← optional long-lived integration branch
      ├── sprint/00-kickoff-design     ← Sprint 0 (this doc) → merge to main
      ├── sprint/01-core-parser
      ├── sprint/02-migration-runner
      ├── sprint/03-critical-path-tests
      └── … through sprint/10-closeout
```

| Practice | Detail |
| --- | --- |
| **Branch naming** | `sprint/NN-short-description` off `main` (or off `initiative/four-pillars` if using integration branch) |
| **Tags** | After each sprint merge: `four-pillars-sprint-00`, `four-pillars-sprint-01`, … `four-pillars-sprint-10` |
| **Pre-initiative anchor** | Optional tag `four-pillars-pre-sprint-0` before Sprint 0 merge |
| **Merge policy** | One sprint per PR; never combine migration runner (S2) with Library/Torrent VM splits (S8/S9) |
| **Sprint 0 merge** | Docs only — no app rebuild required |

---

## 7. Sprint 0 Definition of Done

- [x] Schema inventory checked into `docs/planning/` (this file)
- [x] Migration numbering rules written (§4)
- [x] Core type move list approved (§5)
- [x] [00-integrated-roadmap.md](./00-integrated-roadmap.md) locked decisions complete

**Fixtures:** Parser golden inputs in [fixtures/torrent-release-names.json](./fixtures/torrent-release-names.json) (28 cases).

**Next:** Sprint 3 code complete — 79 Core.Tests. Next is Sprint 4 (navigation hooks) after human cart smoke + tag `four-pillars-sprint-03`.

---

## 8. Sprint 1 status (Aug 2026)

- [x] `MediaManager.Core` + `MediaManager.Core.Tests` created
- [x] Moved: `TorrentCandidateParser`, `TorrentReleaseKind`, `TorrentQuality` (detect), `MediaKind`
- [x] 20 parser tests green; WPF smoke parse verified
- [x] `AI_CONTEXT.md` updated
- [x] Git commit + tag `four-pillars-sprint-01`
- [x] `RecipeBuilder` / `TrackedShowBuilder` — added in Sprint 3
