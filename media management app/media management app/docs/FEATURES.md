# Media Manager — Feature Catalog

Exhaustive list of user-facing and background features. Each entry includes purpose, interaction, code locations, and database involvement.

**Legend:** DB tables abbreviated as shown in [APP_OVERVIEW.md](./APP_OVERVIEW.md).

---

## 1. Application Shell & Navigation

### 1.1 Main Window Shell
- **What:** Borderless window with sidebar navigation, workspace host, status bar
- **User interaction:** Click sidebar items to switch workspaces; collapse sidebar; custom title bar controls
- **Code:** `MainWindow.xaml`, `MainWindow.xaml.cs`, `ViewModels/MainViewModel.cs`
- **DB:** None

### 1.2 Workspace Navigation
- **What:** Eight workspaces (News, Auto, Find/Add, Library, Stats, Torrent, Recipe, System Settings)
- **User interaction:** Sidebar buttons; selection persisted visually
- **Code:** `ViewModels/MainViewModel.cs` (`NavigateCommand`), `Common/AppWorkspaceKind.cs`, `Resources/ViewTemplates.xaml`
- **DB:** None
- **Notes:** Sidebar `IconKind` must be a `PackIconLucideKind` name from [ui-polish/LUCIDE_ICONS.md](./ui-polish/LUCIDE_ICONS.md). Unknown names render as a blank tile.

### 1.3 Status Bar — Job Progress
- **What:** Shows current long-running operation text and active indicator
- **User interaction:** Read-only pill in status bar
- **Code:** `ViewModels/MainViewModel.cs` (binds `IOperationProgressService`), `Services/OperationProgressService.cs`
- **DB:** None

### 1.4 Status Bar — Drive Storage Pills
- **What:** Per-drive free/total space for torrent download folders; low-space warning (&lt;10%)
- **User interaction:** Click pill to open drive folder in Explorer
- **Code:** `ViewModels/MainViewModel.cs`, `ViewModels/StorageStatusViewModel.cs`, `Services/DownloadFolderCatalogService.cs`
- **DB:** None

### 1.5 Status Bar — Integration Pills
- **What:** Live status for qBittorrent, WARP, Jellyfin, Google Drive backup
- **User interaction:** Click qBittorrent/Jellyfin to open embedded WebViewer; click Drive to open backup folder in browser; click WARP to toggle connection
- **Code:** `ViewModels/MainViewModel.cs`, `Services/DeviceStatusService.cs`, `Views/Controls/WarpStatusPill.xaml`
- **DB:** None

### 1.6 Console Log Window
- **What:** Terminal-style rolling log viewer
- **User interaction:** Bottom bar terminal button; copy selected/all logs
- **Code:** `Views/ConsoleLogWindow.xaml`, `Services/ConsoleWindowService.cs`, `Services/AppLogger.cs`
- **DB:** None
- **Background:** Auto-closes on background mode if configured

### 1.7 System Tray
- **What:** Minimize/hide to tray; restore from tray; shutdown from tray menu
- **User interaction:** Enabled via Settings → System → Startup (Start Minimized, Close to Tray)
- **Code:** `Services/TrayIconService.cs`, `MainWindow.xaml.cs`
- **DB:** None

### 1.8 Single Instance
- **What:** Only one app instance allowed
- **User interaction:** Second launch shows info dialog and exits
- **Code:** `App.xaml.cs` (mutex `Local\MediaManager.SingleInstance`)
- **DB:** None

### 1.9 Theme (Light/Dark)
- **What:** Application-wide theme switching
- **User interaction:** Settings → System → Theme dropdown; applied on save
- **Code:** `Services/ThemeService.cs`, `Resources/AppThemeColors.*.xaml`, `Models/UiSettings.cs`
- **DB:** None (settings.json)

### 1.10 Windows Startup Registration
- **What:** Register app to run at Windows login
- **User interaction:** Settings → System → Run at startup checkbox
- **Code:** `Services/WindowsStartupService.cs`, `Models/AppStartupSettings.cs`
- **DB:** None

---

## 2. News Workspace

### 2.1 New Episodes This Week
- **What:** Cards for episodes aired in current week from tracked shows
- **User interaction:** Sort by air date, tracked show, or status; Update Dashboard button
- **Code:** `Views/NewsView.xaml`, `ViewModels/NewsViewModel.cs`, `ViewModels/NewsEpisodeCardViewModel.cs`
- **DB:** `TrackedShows`, `TrackedEpisodes`

### 2.2 Tracked Shows Overview
- **What:** Summary cards: pending counts, air day, anchor schedule
- **User interaction:** View modes: Full, Schedule only, Availability only, Title only
- **Code:** `ViewModels/NewsViewModel.cs`, `ViewModels/NewsShowCardViewModel.cs`, `Common/NewsTrackedShowViewMode.cs`
- **DB:** `TrackedShows`, `TrackedEpisodes`
- **Background:** Auto-refreshes when Auto-Track scheduler completes

### 2.3 Stats Overview
- **What:** Library-wide personal stats. First slice is personal scores: caption totals (mean show, movie, and episode scores), sliding billboard (highest-rated mix + random mix of episodes/titles), watch-status card (shows vs movies per status), title/episode band mix, movies then shows poster strips, TV heatmap wall, best/lowest episodes, show-vs-episode mismatch, best/lowest specials, empty opinions
- **User interaction:** Open the Stats sidebar tab; click a poster or list row to jump to that title in Library; heatmap cells are display-only; Pause/Play on the billboard stops/resumes slides for this app session only (billboard cards themselves are not clickable); icon-only Expand/Collapse beside Movies/Shows (3-row cap) and beside each heatmap show
- **Code:** `Views/StatsView.xaml`, `Views/Controls/StatsBillboardSlot.xaml`, `Views/Controls/StatsHeatmapSeasonStrip.xaml`, `Views/Controls/StatsPosterStrip.xaml`, `Views/Controls/WorkspaceViewCache.cs`, `ViewModels/StatsViewModel.cs`, `ViewModels/StatsPosterStripViewModel.cs`, `ViewModels/StatsBandPieSeries.cs`, `MediaManager.Core/Services/PersonalRatingOverviewBuilder.cs`, `MediaManager.Core/Services/HeatmapStripLayout.cs`
- **DB:** Reads `TrackedShows.Rating` / `WatchStatus` / `Thought`, `TrackedMovies.Rating` / `WatchStatus` / `Thought`, `TrackedEpisodes.UserRating` / `Thought` (no schema change; ignores TMDB `VoteAverage`)
- **Notes:** Layout (top to bottom): Stats header, billboard, watch status, caption totals, title/episode bands (LiveCharts2 doughnut pies beside the lists, same `EpisodeRatingBandCatalog` colors; transparent canvas; hover tooltip sits in the doughnut hole; pies are padded so slices and tooltips are not clipped; zero-count bands stay on the list only), best/lowest episodes, movie strip, show strip, episode heatmap, show score vs episode mean, best/lowest specials, empty opinions. Billboard has two sliding slots (native WPF `TranslateTransform`, not a content swap): **Highest rated** cycles the top 8 rated story episodes + rated shows/movies; **Random** shuffles the rest (or the same pool if fewer than 8). Episode cards show episode rating/thought; title cards show title rating/thought. Billboard cards are display-only (not clickable). Pause/Play is in-memory on the Stats VM (session only; not `settings.json`). Leaving Stats stops the timers; returning resumes only if not paused. Watch status is its own card (Library colors/icons: `Play`, `CircleCheck`, `Pause`, `CircleX`, `Bookmark`) with **N shows · M movies** per status; unset is omitted. Rated show/movie posters and heatmap show-score pills show that same status icon beside the band-colored score (hidden when status is unset). The Shows strip lists title-rated shows (covers), including those with no episode scores. Movies and Shows poster strips default to **three rows**; extra posters stay hidden until per-strip Expand (session only). Hidden posters are not decoded until expand; remaining cards load in batches of 8 with “Loading posters…”. Collapse returns to three rows without reloading images. Strip rebuilds skip when a fingerprint of ids+ratings matches. Shows missing episode scores are listed only under empty opinions (`Show rated, no episode ratings`), not a separate section. The heatmap still requires at least one episode `UserRating`. Each heatmap show stacks a wrapping title header above the cells (no ellipsis). Collapsed default is the first story season (`S1`) in one row at Library-style cell size (small `E{n}` over ExtraBold score; extras slightly smaller); leftover width stays empty. Expand appears when there are more seasons/`SP` extras or S1 cannot fit one row; Expand shows every season stacked (`WrapPanel`, no horizontal scroller). Large seasons realize tiles in batches of 48 with a per-show “Loading episodes…” bar. Heatmap cells are display-only (not clickable). The Stats view instance is kept across tab switches (`WorkspaceViewCache`); `OnNavigatedTo` still refreshes bands/billboard/lists but skips rebuilding heatmap rows when a fingerprint of show ids and episode counts matches. Refresh rebuilds the heatmap. Expand state is in-memory on the Stats singleton (survives leaving Stats; not `settings.json`). Hidden seasons (`TrackedSeasons.IsHidden`) are included in means, bands, mismatch, and coverage. The heatmap keeps story seasons (`S>=1`) on the main strip; rated TMDB S00 extras/OVA sit in a compact `SP` tail (unrated specials omitted from the tail). Per-row `X/Y rated` counts story seasons only (S00/extras excluded). Best/lowest episode lists are story seasons only; specials have their own lists. Unmatched pack extras (`SourceItems.IsOrphanPackSpecial`) are not `TrackedEpisodes` and never appear on Stats. Stats plots the current `TrackedEpisodes` S/E numbering (including alternative episode-group order). Switching episode organization rebuilds season/episode rows and clears episode scores. Refresh on every `OnNavigatedTo` (heatmap rows reused when the fingerprint matches).

---

## 3. Auto-Track Workspace

### 3.1 Scheduler Dashboard
- **What:** Shows scheduler status, last run summary, tracked show count, pending episodes, TMDB budget remaining
- **User interaction:** Read-only summary area
- **Code:** `Views/AutoTrackView.xaml`, `ViewModels/AutoTrackViewModel.cs`
- **DB:** `TrackedShows`; settings in `AutoTrackSettings`

### 3.2 Run Auto-Track Now
- **What:** Manual trigger of full Auto-Track cycle (TMDB discovery + hunt + reconcile)
- **User interaction:** Run Now button (disabled while running)
- **Code:** `ViewModels/AutoTrackViewModel.cs` → `Services/AutoTrackService.cs`
- **DB:** All tracked media tables; creates `TorrentCartOrders`

### 3.3 Per-Show Auto-Track Cards
- **What:** Inline editing of download folder, custom schedule, quality overrides per show
- **User interaction:** Edit fields on show cards; saves on change
- **Code:** `ViewModels/AutoTrackShowCardViewModel.cs`, `Services/TrackedShowService.cs`
- **DB:** `TrackedShows` (AutoTrack* columns)

### 3.4 Stop Tracking Show
- **What:** Disables auto-track for a show
- **User interaction:** Stop Tracking button on show card
- **Code:** `ViewModels/AutoTrackViewModel.cs`
- **DB:** `TrackedShows`

### 3.5 Reset Week
- **What:** Resets week satisfaction state and releases hunt-blocking cart orders
- **User interaction:** Reset Week button
- **Code:** `ViewModels/AutoTrackViewModel.cs`, `Services/TorrentCartService.cs`
- **DB:** `TrackedShows`, `TorrentCartOrders`

### 3.6 New Episodes List
- **What:** Recently discovered episodes in Auto workspace
- **User interaction:** Read-only list
- **Code:** `ViewModels/AutoTrackNewEpisodeViewModel.cs`
- **DB:** `TrackedEpisodes`

### 3.7 TMDB Eligibility & Weekly Air Day (background rules)

**TMDB refresh eligibility** (`AutoTrackTmdbEligibility.ShouldRefreshTmdb`):

| Condition | Effect |
|-----------|--------|
| Show not auto-tracked | Skip refresh |
| Weekly schedule enforced and before anchor this week | Skip (unless `bypassAnchor`, e.g. Run Now) |
| Already refreshed after anchor this week | Skip |
| Finished show + all checkpoint episodes Available | Skip |
| State `FinishedComplete` | Skip (unless bypass) |

**Dormant reset** (`ShouldResetDormantState`): State `DormantCaughtUp` + past anchor + not yet refreshed this week → reset to `Active`.

**Pending episode hunt** (`FindPendingEpisodes`): At/after checkpoint, aired (+ `HuntMinHoursAfterAirDate` delay), `Missing`, no torrent hash, no active cart order.

**TMDB state machine** (after refresh, `AutoTrackService`):

| State | Meaning |
|-------|---------|
| `Active` | Normal; may hunt pending episodes |
| `DormantCaughtUp` | Ongoing show fully caught up past anchor; skip TMDB until next anchor week |
| `FinishedComplete` | Ended show fully caught up; permanent skip |

**Weekly air day inference** (`ShowWeeklyAirDay.Infer`): Uses up to 12 recent non-special episodes with air dates; counts `DayOfWeek` in UTC+7 calendar; returns day only on **strict plurality** (tie → null). Respects auto-track checkpoint season/episode when provided.

**Code:** `Services/AutoTrackTmdbEligibility.cs`, `Services/ShowWeeklyAirDay.cs`, `Services/AutoTrackWeekAnchor.cs`, `Models/AutoTrackTmdbState.cs`

---

## 4. Find/Add Workspace

### 4.1 TMDB Unified Search
- **What:** Search TV shows and movies simultaneously via TMDB API
- **User interaction:** Search box, Cancel; sortable results table
- **Code:** `Views/FindAddView.xaml`, `ViewModels/FindAddViewModel.cs`, `Services/TmdbMetadataProvider.cs`
- **DB:** None (search only)

### 4.2 Search Result Details
- **What:** Poster, overview, stats for selected result; compact detail toggle
- **User interaction:** Select row; toggle compact mode
- **Code:** `ViewModels/FindAddViewModel.cs`
- **DB:** None

### 4.3 Add to Library
- **What:** Import selected TMDB show/movie with chosen recipe
- **User interaction:** Recipe picker + Add to Library button
- **Code:** `ViewModels/FindAddViewModel.cs` → `Services/TrackedShowService.cs`, `Services/TrackedMovieService.cs`
- **DB:** `TrackedShows`/`TrackedMovies`, `TrackedSeasons`, `TrackedEpisodes`

### 4.4 Existing Media Grid
- **What:** Shows already-tracked media with sort
- **User interaction:** Sort fields/direction; refresh
- **Code:** `ViewModels/FindAddMediaCardViewModel.cs`, `Services/MediaCardCatalogService.cs`
- **DB:** `TrackedShows`, `TrackedMovies`

### 4.5 Copy to Clipboard
- **What:** Copy TMDB IDs or text from UI
- **User interaction:** Context/copy actions
- **Code:** `ViewModels/FindAddViewModel.cs`
- **DB:** None

---

## 5. Library Workspace

### 5.1 Media Card Browser
- **What:** Grid of tracked shows/movies with search, sort, watch-status filter
- **User interaction:** Search, sort field/direction, watch status multi-filter, select card
- **Code:** `Views/LibraryView.xaml`, `ViewModels/LibraryViewModel.cs`, `ViewModels/LibraryMediaCardViewModel.cs`, `Views/WatchStatusFilterControl.xaml`
- **DB:** `TrackedShows`, `TrackedMovies`
- **Background:** Selection persisted in `UiSettings`

### 5.2 Show Detail Pane
- **What:** Seasons, episodes, poster, series status, watch progress, rating, thoughts, alt titles
- **User interaction:** Expand seasons; per-episode actions
- **Code:** `ViewModels/LibraryShowDetailViewModel.cs`, `ViewModels/LibrarySeasonViewModel.cs`, `ViewModels/LibraryEpisodeRowViewModel.cs`
- **DB:** `TrackedShows`, `TrackedSeasons`, `TrackedEpisodes`

### 5.3 Movie Detail Pane
- **What:** Movie metadata, torrent/link state, preferences
- **User interaction:** Link, reset, cart actions
- **Code:** `ViewModels/LibraryMovieDetailViewModel.cs`
- **DB:** `TrackedMovies`

### 5.4 Watch Status & Series Status
- **What:** User watch status (Watching, Completed, On Hold, etc.) and series status (Ongoing/Finished)
- **User interaction:** Cycle series status; watch status via filter and detail controls
- **Code:** `Common/AppEnums.cs` (`UserWatchStatus`, `ShowSeriesStatus`), `ViewModels/LibraryViewModel.cs`
- **DB:** `TrackedShows`, `TrackedMovies`

### 5.5 Watched Episode Counter & Rating
- **What:** Manual watch progress counter and 0–10 rating with thought notes
- **User interaction:** Increment/decrement buttons; edit thought popup
- **Code:** `ViewModels/LibraryViewModel.cs`
- **DB:** `TrackedShows` (`WatchedEpisodes`, `Rating`, `Thought`)

### 5.6 Alternative Titles
- **What:** TMDB alternative titles; exclude specific titles from recipe search
- **User interaction:** Toggle exclusion chips
- **Code:** `ViewModels/AlternativeTitleChipViewModel.cs`, `Services/SearchTitleResolver.cs`
- **DB:** `TrackedShows`/`TrackedMovies` (`AlternativeTitlesJson`, `ExcludedAlternativeTitlesJson`)

### 5.7 Add to Torrent Cart (from Library)
- **What:** Create cart orders for episode, season pack, all missing episodes, or movie
- **User interaction:** Add to Cart buttons on episodes/seasons/movies
- **Code:** `ViewModels/LibraryViewModel.cs` → `Services/TorrentCartService.cs`
- **DB:** `TorrentCartOrders`, `TorrentCartOrderCandidates`

### 5.8 Episode Linking
- **What:** Hardlink completed episode download into library layout
- **User interaction:** Link button per episode; Reset to unlink
- **Code:** `Services/AutoTorrentLinkService.cs`, `Services/HardlinkService.cs`, `Services/LibraryPathResolver.cs`
- **DB:** `SourceItems`, `TrackedEpisodes`

### 5.9 Season Pack Linking
- **What:** Link entire season pack torrent to library (normal or AI-assisted)
- **User interaction:** Normal Link, AI Link, Unlink, Cleanup buttons
- **Code:** `Services/AutoTorrentLinkService.cs`, `Services/PackLinkCoordinatorService.cs`, `Services/SpecialMappingOrchestrator.cs`
- **DB:** `TrackedSeasons`, `SourceItems`
- **Dialogs:** `Views/PackLinkReviewWindow.xaml`, `Views/PackLinkProgressWindow.xaml`
- **Heuristics:** See [Appendix A — Pack Link Matching](#appendix-a--pack-link-matching-heuristics)

### 5.10 Movie Linking
- **What:** Hardlink completed movie download
- **User interaction:** Link / Reset buttons
- **Code:** `Services/AutoTorrentLinkService.cs`
- **DB:** `TrackedMovies`, `SourceItems`

### 5.11 Reconcile Existing Torrents
- **What:** Match qBittorrent torrents to tracked episodes/movies and update state
- **User interaction:** Reconcile button
- **Code:** `Services/TorrentReconciliationService.cs`
- **DB:** `TrackedEpisodes`, `TrackedMovies`, `TrackedSeasons`, `TorrentCartOrders`

### 5.12 TMDB Metadata Refresh
- **What:** Refresh selected or all library media from TMDB
- **User interaction:** Refresh Selected / Refresh All buttons; Open TMDB page
- **Code:** `Services/MediaMetadataSyncService.cs`, `Services/TrackedShowService.cs`
- **DB:** `TrackedShows`, `TrackedSeasons`, `TrackedEpisodes`, `TrackedMovies`

### 5.13 Auto-Track Setup (per show)
- **What:** Enable auto-track from checkpoint episode with download folder
- **User interaction:** Set Auto-Track button → `SetAutoTrackDialog`
- **Code:** `Views/SetAutoTrackDialog.xaml`, `ViewModels/LibraryViewModel.cs`
- **DB:** `TrackedShows` (AutoTrack* columns)

### 5.14 Episode Organization
- **What:** Choose TMDB episode group ordering (e.g. absolute vs aired)
- **User interaction:** Change Episode Organization dialog
- **Code:** `Views/EpisodeOrganizationDialog.xaml`, `Services/TmdbMetadataProvider.cs`
- **DB:** `TrackedShows` (`EpisodeGroupId`, `EpisodeGroupName`)

### 5.15 Import Existing Media
- **What:** Scan external folders, match to TMDB, import with hardlinks
- **User interaction:** Import panel: add folders, scan, match candidates, import selected
- **Code:** `Services/MediaImportService.cs`, `ViewModels/MediaImportGroupViewModel.cs`
- **DB:** `SourceItems`, `TrackedShows`/`TrackedMovies`

### 5.16 Delete Media / Entire Library
- **What:** Remove tracked show/movie or wipe entire library with cleanup
- **User interaction:** Delete Selected / Delete Entire Library (with confirmation)
- **Code:** `Services/LibraryManagementService.cs`
- **DB:** Cascading deletes on tracked tables; `FetchJobs`, `TorrentCartOrders`

### 5.17 Hide Seasons
- **What:** Hide seasons from UI (e.g. specials)
- **User interaction:** Toggle season hidden; toggle show hidden seasons
- **Code:** `ViewModels/LibraryViewModel.cs`
- **DB:** `TrackedSeasons` (`IsHidden`)

### 5.18 Episode Rating & Thought
- **What:** Personal 0–10 rating and optional thought (max 250 chars) on each tracked episode
- **User interaction:** Library Seasons has a session-wide Availability / Rating toggle. Availability keeps cart, link, missing labels, and pack mode. Rating rows stay compact (saved `★ 8.0` and thought text only). The star on the right opens both editors together (one row at a time). The editor uses the same 0.1 minus/value/plus stepper as the show rating. Clearing a field writes `NULL`
- **Code:** `ViewModels/LibraryEpisodeRowViewModel.cs`, `ViewModels/LibraryViewModel.EpisodeRating.cs`, `Views/LibraryView.xaml` (`SeasonTemplate`, `EpisodeRowTemplate`)
- **DB:** `TrackedEpisodes` (`UserRating`, `Thought`); migration `004_episode_rating_thought`
- **Notes:** Applies to TMDB story seasons and TMDB S00 extras/specials/OVAs. Unmatched pack extras (linked files not in TMDB order) never show the star editor; see §5.20. TMDB `VoteAverage` is unchanged and is not the personal score

### 5.19 Episode Rating Chart
- **What:** Collapsible Library show-detail ratings for the selected season: column-chart episode bars from the baseline (default) or a color heatmap
- **User interaction:** Show/Hide; S1/S2/`SP` underline tabs with previous/next; Chart vs Heatmap (only one visible). Season average (`☆ 6.5 Average`) sits above the plot. Episode labels use `E1`, `E2`, …
- **Code:** `ViewModels/LibraryViewModel.EpisodeRating.cs`, `ViewModels/EpisodeRatingCellViewModel.cs`, `ViewModels/EpisodeRatingBandCatalog.cs`, `Views/LibraryView.xaml`
- **DB:** Reads `TrackedEpisodes.UserRating`
- **Notes:** Personal ratings only (no TMDB). Unrated episodes show `—` / `N/A`. The dashed chart line is the season mean at the same height as a column of that score. Heatmap **Fair** is the 6.0–6.9 color band (not the season average). Colors come from `EpisodeRatingBandCatalog`. No chart NuGet. Horizontal chart/heatmap scrollers use `NestedScrollViewer` so vertical mouse wheel still scrolls the Library page (Shift+wheel pans). The S00 tab uses `SP` / `Extras/Specials/OVAs` and plots TMDB specials only (unmatched extras excluded)

### 5.20 Per-Specials Rating
- **What:** Personal 0–10 + thought on TMDB extras/specials/OVAs (season 0), using the same Library Rating mode and chart as story episodes
- **User interaction:** Open the show’s **Extras/Specials/OVAs** season (unhide it if it was hidden). Switch Seasons to Rating and use the star on `S00Exx` rows. The chart/heatmap `SP` tab is the same season. Stats includes those scores in means/bands and draws rated specials in the heatmap `SP` tail plus Best/Lowest specials
- **Code:** `ViewModels/LibrarySeasonViewModel.cs`, `ViewModels/LibraryEpisodeRowViewModel.cs` (`CanRateEpisode`), `ViewModels/LibraryViewModel.cs` (`AppendOrphanPackRows`), `MediaManager.Core/Services/PersonalRatingOverviewBuilder.cs`
- **DB:** `TrackedEpisodes` where `SeasonNumber = 0` (`AppConstants.SpecialsSeasonNumber`)
- **Notes:** Rateable specials are TMDB episode rows (in the selected airing/DVD/episode-group order). Pack files that were hardlinked but **not** matched to a TMDB S00 episode (`SourceItems.IsOrphanPackSpecial`, Library “unmatched extras”) have no `TrackedEpisode` row, so they cannot be rated, do not appear on the chart or Stats, and show “Linked extra not in TMDB order — cannot rate.” If TMDB has no S00 but unmatched extras exist, Library still shows an Extras/Specials/OVAs section for those files

---

## 6. Torrent Workspace

### 6.1 Media Card Picker
- **What:** Same card browser as Library, scoped to torrent cart context
- **User interaction:** Select media to view its cart orders
- **Code:** `Views/TorrentWorkspaceView.xaml`, `ViewModels/TorrentWorkspaceViewModel.cs`
- **DB:** `TrackedShows`, `TrackedMovies`

### 6.2 Cart Order List
- **What:** Orders for selected media with status, candidates, progress
- **User interaction:** Per-order actions via `TorrentOrderViewModel`
- **Code:** `ViewModels/TorrentOrderViewModel.cs`, `Views/CartCandidatePickerControl.xaml`
- **DB:** `TorrentCartOrders`, `TorrentCartOrderCandidates`

### 6.3 Run Cart Pipeline
- **What:** Automated search → evaluate → select candidates for all draft orders
- **User interaction:** Run Cart / Stop buttons
- **Code:** `ViewModels/TorrentWorkspaceViewModel.cs`, `Services/FetchJobService.cs`, `Services/CandidateEvaluationService.cs`
- **DB:** `TorrentCartOrders`, `TorrentCartOrderCandidates`, `FetchJobs`

### 6.4 Accept Candidates
- **What:** Accept selected or all candidates for adding
- **User interaction:** Accept on order; Accept All button
- **Code:** `Services/TorrentCartService.cs`
- **DB:** `TorrentCartOrderCandidates`, `TorrentCartOrders`

### 6.5 Add to qBittorrent
- **What:** Add accepted orders to qBittorrent with disk assignment
- **User interaction:** Add button → `TorrentAddDiskDialog`
- **Code:** `Views/TorrentAddDiskDialog.xaml`, `Services/TorrentAddDiskAssignmentService.cs`, `Services/TorrentAddGateService.cs`
- **DB:** `TorrentCartOrders`; updates torrent state on episodes/movies/seasons

### 6.6 Per-Media Recipe Assignment
- **What:** Episode, pack, and movie recipe ComboBoxes per selected media, with compact/expanded recipe summaries
- **User interaction:** Compact mode shows recipe selection only. Expanded mode summarizes quality, seeders, size, search mode, plugins, debug, and title behavior in two synced overview cards (7 of 8 rows visible; scroll for Max candidates). Click Min seeders, Min size, Debug, or Max candidates to toggle a cart override (orange `AppBrushWarning` title). Editors appear only while overridden: NumericUpDown for seeders/candidates; GB text box for min size (Enter or leave the field to save); click Debug On/Off to flip from the current value (turns orange). Last override values stay in SQLite when toggled off.
- **Code:** `ViewModels/TorrentWorkspaceViewModel.cs`, `ViewModels/CartRecipeSummaryViewModel.cs`, `Views/TorrentWorkspaceView.xaml` (`CartRecipeSummaryTemplate`), `Services/RecipeService.cs`
- **DB:** `TrackedShows.CartEpisodeOverridesJson` / `CartPackOverridesJson`, `TrackedMovies.CartOverridesJson`
- **Notes:** Overrides affect manual Run Cart searches only; Auto-Track continues to use recipe values. Max candidates 1–20, min seeders 0–10000, min size 0–500 GB (0 = no floor). Candidate debug override can turn hunt logs on or off for that cart even when the recipe flag differs. See [RECIPE_SCHEMA.md](./RECIPE_SCHEMA.md) *Cart overview and overrides* to add a property. Layout and binding rules: [ui-polish](./ui-polish/README.md).

### 6.7 Candidate Picker Flyout
- **What:** Ranked torrent candidates with quality, seeders, warnings, blacklist option
- **User interaction:** Select candidate; blacklist malicious ones
- **Code:** `Views/CartCandidatePickerControl.xaml`, `ViewModels/TorrentOrderCandidateViewModel.cs`
- **DB:** `TorrentCartOrderCandidates`, `TorrentBlacklist`

### 6.8 Cart Management
- **What:** Clear cart, clear candidates, clear all carts, remove order
- **User interaction:** Header action buttons
- **Code:** `Services/TorrentCartService.cs`
- **DB:** `TorrentCartOrders`, `TorrentCartOrderCandidates`

### 6.9 Retry Actions
- **What:** Retry failed add or retry search for an order
- **User interaction:** Per-order Retry Add / Retry Search buttons
- **Code:** `ViewModels/TorrentOrderViewModel.cs`
- **DB:** `TorrentCartOrders`

### 6.10 Reconcile Pack
- **What:** Trigger season pack auto-reconcile for an order
- **User interaction:** Reconcile Pack button on pack orders
- **Code:** `Services/PackLinkCoordinatorService.cs`
- **DB:** `TrackedSeasons`, `SourceItems`

---

## 7. Recipe Workspace

### 7.1 Recipe List CRUD
- **What:** Create, duplicate, delete, refresh recipes from disk folder
- **User interaction:** List selection; Create/Duplicate/Delete/Refresh buttons
- **Code:** `Views/RecipeWorkspaceView.xaml`, `ViewModels/RecipeWorkspaceViewModel.cs`, `Services/RecipeService.cs`
- **DB:** None (JSON files on disk)

### 7.2 Recipe Module Editor
- **What:** Six pipeline modules: Identity, QueryBuilder, SearchSource, CandidateParser, CandidateFilter, Scoring
- **User interaction:** Tab selection; edit fields; Save Recipe
- **Code:** `ViewModels/RecipeWorkspaceViewModel.cs` (nested module editors)
- **Schema:** [RECIPE_SCHEMA.md](./RECIPE_SCHEMA.md)
- **DB:** None

### 7.3 Engine Picker Dialog
- **What:** Select qBittorrent search plugins or rank preferred engines
- **User interaction:** Checkboxes; rank reorder (Top/Up/Down/Bottom) in quality mode
- **Code:** `Views/EnginePickerDialog.xaml`, `ViewModels/EnginePickerDialogViewModel.cs`, `Services/QbittorrentSearchPluginService.cs`
- **DB:** None

### 7.4 Scoring Reset
- **What:** Restore default scoring weights
- **User interaction:** Reset Scoring to Defaults button
- **Code:** `Services/CandidateScoringWeights.cs`
- **DB:** None

### 7.5 Recipe Import/Export
- **What:** Import/export recipe JSON files
- **User interaction:** Via recipe service (programmatic; folder refresh)
- **Code:** `Services/RecipeService.cs`
- **DB:** None

---

## 8. System Settings

Settings organized in seven sections (`SettingsSection` enum). All saved to `settings.json` via Save button.

### 8.1 System Section
| Feature | Interaction | Code |
|---------|-------------|------|
| State folder | Browse path | `ViewModels/SettingsViewModel.cs` |
| Logging limits | Max lines, retention days | `Models/LogSettings.cs`, `Services/LogCleanupService.cs` |
| Startup/tray | Run at startup, start minimized, close to tray | `Models/AppStartupSettings.cs` |
| Theme | Light/Dark | `Services/ThemeService.cs` |

### 8.2 Library Section
| Feature | Interaction | Code |
|---------|-------------|------|
| Source folders | Add/remove scan folders | `Services/ScannerService.cs` |
| Library root mode | Auto per drive | `Services/LibraryPathResolver.cs` |
| Symlink unified root | Browse path, enable/disable | `Services/Symlink/SymlinkSyncService.cs` |
| Sync symlinks now | Manual trigger | `Services/SymlinkCoordinatorService.cs` |

### 8.3 Auto-Track Section
| Feature | Interaction | Code |
|---------|-------------|------|
| Enable scheduler | Master toggle | `Services/AutoTrackSchedulerService.cs` |
| Weekly anchor | Day + time | `Models/AutoTrackSettings.cs` |
| Intervals | TMDB, hunt, reconcile intervals | `Services/AutoTrackSchedulerService.cs` |
| TMDB daily budget | Max refreshes per day | `Services/AutoTrackService.cs` |
| Quality policy | Min quality, seeders, file size | `Models/AutoTrackQualityPolicy.cs` |
| Hunt limits | Max shows/episodes per cycle | `Models/AutoTrackSearchSettings.cs` |
| Jellyfin refresh | Enable, WARP hold, log path | `Models/JellyfinRefreshSettings.cs` |

### 8.4 Integrations Section
| Feature | Interaction | Code |
|---------|-------------|------|
| TMDB token | Enter/test | `Services/TmdbMetadataProvider.cs` |
| Gemini API | Key, model, fallbacks, test | `Services/Gemini/GeminiApiClient.cs` |
| qBittorrent | URL, credentials, API key, test | `Services/QbittorrentClient.cs` |
| qBittorrent restart | Opt-in process recovery | `Services/QbittorrentProcessRestartService.cs` |
| Jellyfin | URL, API key, test | `Services/JellyfinClient.cs` |
| WARP CLI | Path, test, auto-recover | `Services/WarpCliService.cs` |
| Flush Jellyfin queue | Manual | `Services/JellyfinLibraryRefreshService.cs` |

### 8.5 Torrent Storage Section
| Feature | Interaction | Code |
|---------|-------------|------|
| Download folders | Add/remove paths | `Models/AutoTorrentSettings.cs` |
| Categories | TV/movie category names | `Common/AppConstants.cs` |
| Search settings | Parallel limits, snapshot search | `Models/AutoTorrentSettings.cs` |
| Validation config | Allowed/dangerous extensions | `Models/TorrentValidationConfig.cs` |
| Clear selected candidates | Bulk DB cleanup | `Services/DatabaseService.cs` |

### 8.6 Notifications Section
| Feature | Interaction | Code |
|---------|-------------|------|
| Per-kind toggles | Enable/disable toast types | `Common/NotificationCatalog.cs`, `Models/NotificationSettings.cs` |
| Test notification | Send test toast | `Services/WindowsNotificationService.cs` |

### 8.7 Backup Section
| Feature | Interaction | Code |
|---------|-------------|------|
| Google Drive connect | Browse OAuth client JSON → Connect (browser consent) | `Services/Backup/GoogleDriveClient.cs` |
| Credentials path | Default `{StateFolder}/GoogleDrive/credentials.json` or custom | `Models/BackupSettings.cs` |
| Backup now | Manual backup | `Services/Backup/BackupService.cs` |
| Backup history | View and restore | `ViewModels/BackupHistoryItemViewModel.cs` |
| Schedule | Daily hour, debounce, throttle | `Services/Backup/BackupSchedulerService.cs` |

Setup details: [STATE_FOLDER.md](./STATE_FOLDER.md#google-drive-oauth)

---

## 9. Embedded Web Viewers

### 9.1 qBittorrent WebViewer
- **What:** WebView2 window embedding qBittorrent WebUI
- **User interaction:** Open from status bar pill; custom chrome, reload, WARP pill
- **Code:** `Views/WebViewerWindow.xaml`, `Services/QbittorrentViewerService.cs`
- **Background:** Auto-close on background if configured; confirm on close

### 9.2 Jellyfin WebViewer
- **What:** WebView2 window embedding Jellyfin web client
- **User interaction:** Open from status bar pill
- **Code:** `Services/JellyfinViewerService.cs`
- **Background:** Same lifecycle rules as qBittorrent viewer

---

## 10. Background Services (Hidden)

### 10.1 Auto-Track Scheduler
- **What:** Timer loop: TMDB discovery → torrent hunt → reconcile on configured intervals
- **Code:** `Services/AutoTrackSchedulerService.cs`
- **DB:** All tracked + cart tables
- **Notifications:** Run summary toast, hunt progress, new episode, hardlinked, blocked

### 10.2 TMDB Discovery (Auto-Track)
- **What:** Refresh shows, detect new episodes, enforce weekly anchor and daily budget
- **Code:** `Services/AutoTrackService.cs` (`RunTmdbDiscoveryAsync`)
- **DB:** `TrackedShows`, `TrackedSeasons`, `TrackedEpisodes`

### 10.3 Torrent Hunt (Auto-Track)
- **What:** Search qBittorrent for pending episodes, score candidates, create cart orders, add torrents
- **Code:** `Services/AutoTrackService.cs` (`RunTorrentHuntAsync`)
- **DB:** `TorrentCartOrders`, `TorrentCartOrderCandidates`
- **External:** WARP connect, qBittorrent restart if WebUI down

### 10.4 Background Reconcile (Auto-Track)
- **What:** Sync torrent progress, hardlink completed, pack link, release hunt blocks
- **Code:** `Services/AutoTrackService.cs` (`RunBackgroundReconcileAsync`)
- **DB:** `TrackedEpisodes`, `SourceItems`, `TorrentCartOrders`

### 10.5 Torrent Reconciliation
- **What:** Periodic/manual sync of qBittorrent torrent list → DB state
- **Code:** `Services/TorrentReconciliationService.cs`
- **DB:** Episodes, movies, seasons, cart orders

### 10.6 Torrent Add Gate (Validation Pipeline)
- **What:** Add torrent **running** → poll file list → validate → continue or blacklist+delete (commit `631c3d7`)
- **Steps:**
  1. Blacklist check (listing URL)
  2. Add via qBittorrent (not paused; `Paused = false`)
  3. Infohash blacklist check → delete if matched
  4. If `EnableContentValidation` false → return immediately
  5. Poll `GetTorrentFilesAsync` every 1s until non-empty or timeout (default 90s, clamp 5–120)
  6. Empty file list → delete torrent, throw (no blacklist)
  7. Validate files → malware → delete + blacklist + `MaliciousTorrentException`
  8. Return live torrent state (download continues; no explicit resume step)
- **Code:** `Services/TorrentAddGateService.cs` (in `ITorrentAddGateService.cs`), `Services/TorrentContentValidationService.cs`
- **Config:** `Models/TorrentValidationConfig.cs` (`ValidationTimeoutSeconds`, default 90 in code)
- **DB:** `TorrentBlacklist`

### 10.7 Fetch Job Orchestration
- **What:** In-memory candidate fetch/search for episodes, movies, season packs (cart workspace + auto-track)
- **Code:** `Services/FetchJobService.cs`
- **DB:** Writes candidates to `TorrentCartOrderCandidates` only; **`FetchJobs` table emptied on every DB init**
- **Purge:** `DatabaseService.PurgeLegacyFetchJobs()` runs `DELETE FROM FetchJobs` after schema init — legacy rows from pre-cart builds are removed; table kept for compatibility

### 10.8 Show Snapshot Search
- **What:** One bulk qBittorrent search per show; local episode matching
- **Code:** `Services/ShowSearchSnapshotService.cs`, `Services/SnapshotCandidateMatcher.cs`
- **DB:** Cart candidates

### 10.9 Symlink Coordinator
- **What:** Startup sync + manual sync of symlinks for all source items
- **Code:** `Services/Symlink/SymlinkCoordinatorService.cs`, `Services/Symlink/SymlinkSyncService.cs`
- **DB:** `SourceItems` (`SymlinkPath`)
- **Requires:** Administrator for symlink creation

### 10.10 Jellyfin Library Refresh
- **What:** Debounced path notifications after symlinks; WARP hold during refresh
- **Code:** `Services/JellyfinLibraryRefreshService.cs`, `Services/JellyfinLogTailer.cs`
- **DB:** None (API calls)

### 10.11 NFO Writer
- **What:** Write/delete Kodi NFO sidecars on link/unlink
- **Code:** `Services/NfoWriterService.cs`
- **DB:** `SourceItems`

### 10.12 Source Scanner & Parser
- **What:** Walk source folders, parse filenames (Sonarr parser), upsert source items
- **Code:** `Services/ScannerService.cs`, `Services/ParserService.cs`
- **DB:** `SourceItems`, `SeriesMappings`

### 10.13 Source Reconciliation
- **What:** Mark unseen files as missing after scan
- **Code:** `Services/SourceReconciliationService.cs`
- **DB:** `SourceItems` (`State`)

### 10.14 Pack Link Coordinator
- **What:** Inspect/reconcile season pack torrents when complete (manual, auto on download complete, or cart reconcile)
- **Preconditions:** Pack owner season, mapped pack hash, torrent complete in qBittorrent
- **Auto cooldown:** Skip re-inspect if same hash inspected within 30 minutes
- **Pipeline:** List complete video files → `PackTorrentInventoryAnalyzer` → update season inspection + pack cart orders
- **Code:** `Services/PackLinkCoordinatorService.cs`, `Services/PackTorrentInventoryAnalyzer.cs`
- **DB:** `TrackedSeasons`, `SourceItems`
- **Heuristics:** [Appendix A](#appendix-a--pack-link-matching-heuristics)

### 10.15 Gemini Special Mapping
- **What:** AI maps pack files to special/OVA episodes
- **Code:** `Services/Gemini/SpecialMappingOrchestrator.cs`, `Services/Gemini/GeminiSpecialMappingProvider.cs`
- **DB:** Episode mapping on `SourceItems`

### 10.16 Backup Scheduler
- **What:** Daily backup + event-driven debounced backup after DB changes
- **Code:** `Services/Backup/BackupSchedulerService.cs`, `Services/Backup/BackupService.cs`
- **DB:** Snapshot via `DatabaseService.CreateSafeSnapshot`

### 10.17 Log Cleanup
- **What:** Delete log files older than retention setting
- **Code:** `Services/LogCleanupService.cs`
- **DB:** None

### 10.18 Poster Cache Warmup
- **What:** Background download of TMDB posters on startup
- **Code:** `App.xaml.cs` (`WarmupPosterCache`), `Services/PosterImageService.cs`
- **DB:** Reads `TrackedShows`, `TrackedMovies`

### 10.19 Device Status Polling
- **What:** 30-second refresh of integration health (paused in background mode)
- **Code:** `Services/DeviceStatusService.cs`, `ViewModels/MainViewModel.cs`
- **DB:** None

### 10.20 WARP Log Status Watcher
- **What:** Monitor WARP CLI log for connection state changes
- **Code:** `Services/WarpLogStatusWatcher.cs`, `Services/WarpCliService.cs`
- **DB:** None

### 10.21 Windows Notifications
- **What:** Toast notifications for Auto-Track, symlinks, WARP, Jellyfin, qBittorrent events
- **Code:** `Services/WindowsNotificationService.cs`, `Common/NotificationCatalog.cs`
- **Kinds:** 15+ notification types with per-kind enable toggles

### 10.22 Automation Flow (Recipe Run)
- **What:** Dry-run or immediate recipe execution (search + add best candidate)
- **Code:** `Services/AutomationFlowService.cs`
- **DB:** Updates episode/movie torrent state

### 10.23 Candidate Evaluation & Scoring
- **What:** Score torrent results against recipe rules (quality, seeders, title match, etc.)
- **Code:** `Services/CandidateEvaluationService.cs`, `Services/CandidateMatcher.cs`, `Services/TorrentCandidateParser.cs`
- **DB:** None (in-memory during fetch)

### 10.24 Search Plan Builder
- **What:** Build qBittorrent query strings from recipes and show metadata
- **Code:** `Services/SearchPlanBuilder.cs`, `Services/SearchTitleResolver.cs`
- **DB:** Reads show alt titles

### 10.25 Torrent Cleanup & Blacklist
- **What:** Delete torrents from qBittorrent; manage per-show blacklist
- **Code:** `Services/TorrentCleanupService.cs`, `Services/TorrentBlacklistService.cs`
- **DB:** `TorrentBlacklist`

### 10.26 Media Metadata Sync
- **What:** Bulk TMDB refresh for ongoing shows or entire library
- **Code:** `Services/MediaMetadataSyncService.cs`
- **DB:** All tracked tables

### 10.27 Library Link Event Hub
- **What:** Pub/sub for hardlink create/remove events (internal coordination)
- **Code:** `Services/Events/LibraryLinkEventHub.cs`
- **DB:** None

---

## Appendix A — Pack Link Matching Heuristics

Pack linking maps files inside a completed season-pack torrent to TMDB episodes. Two modes: **Inspect** (inventory preview) and **Link** (strict TMDB match required).

**Primary classes:** `PackTorrentInventoryAnalyzer`, `PackSeasonFileGrouper`, `PackEpisodePatternInferrer`, `PackEpisodeResolver`, `PackFolderTreeAnalyzer`, `PackSpecialBucketDetector`

### Pipeline overview

1. **Folder tree analysis** — Detect season folders, extras/specials/movies folders, path season hints
2. **Season grouping** — Group files by path hint or parsed Sxx; ungrouped → flat group (key `0`)
3. **Flat promotion** — Infer filename prefix/suffix pattern; assign flat files to seasons via `Sxx` in prefix or pack owner season fallback
4. **Episode resolution per season** — Pairwise stem diff voting, compact `S01E05` suffix, standard `SxxExx` regex; validate against TMDB episode count
5. **Specials** — Bucket by folder layout; map to TMDB specials (S00) via pattern inferrer or Gemini (`SpecialMappingOrchestrator`)
6. **Classification** — Each file → RegularEpisode, MatchedSpecial, UnmatchedExtra, Movie, or Skipped

### Exclusions (never grouped as regular episodes)

- Under `movies/` or `films/` folder, or filename contains `"movie"`
- Under `extras/` or `extra/` folder
- Parsed as extra content (NCED/NCOP etc.)
- Special-bucket candidates (`PackSpecialBucketDetector`)

### Season resolution priority

1. Path season hint from folder tree
2. Owning season file group
3. Parsed season from filename

### Link-mode strict rules

- Season must be in pack coverage set
- Episode number must resolve and exist in TMDB tracked episodes
- Unmatched files → `Skipped` with reason

### Warnings

- Multi-season pack (Inspect mode)
- Movie files detected in pack

---

## 11. Database Tables Reference

| Table | Primary use |
|-------|-------------|
| `SourceItems` | Scanned/imported media files, link state, symlink paths |
| `SeriesMappings` | Parsed title → TMDB mapping cache |
| `TrackedShows` | TV show tracking, auto-track config, preferences |
| `TrackedSeasons` | Season management, pack mode, pack torrent state |
| `TrackedEpisodes` | Episode availability, torrent/candidate state, personal `UserRating`/`Thought` |
| `TrackedMovies` | Movie tracking, torrent/candidate state |
| `FetchJobs` | Background fetch job tracking (legacy rows purged on init) |
| `TorrentCartOrders` | Cart acquisition orders |
| `TorrentCartOrderCandidates` | Ranked candidates per order |
| `TorrentBlacklist` | Rejected torrent listings/hashes per show |

Full column schemas: `Services/DatabaseService.cs` (lines 63–3042 region).

---

## 12. Dialogs Summary

| Dialog | Trigger | Code |
|--------|---------|------|
| SetAutoTrackDialog | Library → Set Auto-Track | `Views/SetAutoTrackDialog.xaml` |
| EpisodeOrganizationDialog | Library → Change episode org | `Views/EpisodeOrganizationDialog.xaml` |
| EnginePickerDialog | Recipe → Choose engines | `Views/EnginePickerDialog.xaml` |
| TorrentAddDiskDialog | Torrent → Add to qBittorrent | `Views/TorrentAddDiskDialog.xaml` |
| PackLinkReviewWindow | Library → AI Link pack | `Views/PackLinkReviewWindow.xaml` |
| PackLinkProgressWindow | During AI pack linking | `Views/PackLinkProgressWindow.xaml` |
| ConsoleLogWindow | Status bar terminal button | `Views/ConsoleLogWindow.xaml` |
| WebViewerWindow | qBittorrent/Jellyfin pills | `Views/WebViewerWindow.xaml` |
