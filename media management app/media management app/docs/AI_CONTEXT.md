# Media Manager — AI Context Reference

Structured, machine-readable reference for AI assistants working on this codebase.

```yaml
meta:
  app_name: Media Manager
  platform: Windows Desktop
  framework: WPF .NET 8
  language: C#
  database: SQLite
  branch: auto-torrent  # canonical dev branch; origin/main is ~76 commits behind
  commit: 177cd5bd857adb21490e207bf4ee3f564542e080
  commit_message: Sprint 4 INavigationAware workspace refresh hooks (E3)
  root_namespace: media_management_app
  project_file: media management app.csproj
  core_library: MediaManager.Core/MediaManager.Core.csproj
  test_project: MediaManager.Core.Tests/MediaManager.Core.Tests.csproj
  default_state_folder: D:\MediaManagerState
  default_db: "{StateFolder}/media-manager.db"
  default_settings: "{StateFolder}/settings.json"
  initiative_status: "E1+E2+E3 done (Sprints 0–4). E4 Sprints 5–10 frozen/cancelled. Poster flash surgical fix shipped. Do not resume Sprint 5."
  tag: four-pillars-sprint-04
  workflow_doc: docs/planning/06-ai-execution-guide.md#20-mandatory-pre-sprint-workflow-git--plan-mode
  sprint_plan_folder: docs/planning/sprint-plans/
  settings_audit_folder: docs/settings-modernization/
  before_coding: "git check on auto-torrent; Plan Mode local plan for each new sprint"
  when_touching_code: >
    After any code change in a sprint/session, update progress in the relevant docs in the same change set:
    AI_CONTEXT.md (structure/DI/navigation/policy), 05-sprint-timeline.md (status/session notes/checklists),
    sprint-plans/sprint-NN-local-plan.md, and feature docs (FEATURES/STATE_FOLDER) when behavior changes.
    Do not leave planning docs describing cancelled policies or stale DoD.
  known_debt:
    - library_reconcile_poster: "fixed — Reconciled/PackReconciled now RefreshSelectedDetailAfterReconcile (keep SelectedPosterImage; RebuildSelectedShowDetail / RebuildSelectedMovieDetail + cart). See docs/planning/sprint-plans/poster-flash-surgical-fix.md"
    - settings_ui_json_mismatch: "Settings UI tabs != JSON/Apply ownership — audit docs/settings-modernization/; 7-VM split cancelled with E4, do not resume Sprint 5"
    - settings_live_apply: "RefreshLibraryRootPreview and OnWarpExecutablePathChanged mutate ISettingsService.Current without Save"
    - settings_multi_writer: "UiSettings in settings.json written by Library/Torrent/News VMs — whole-file Save last-writer-wins"
```

---

## Workspaces

```yaml
workspaces:
  - kind: News
    enum_value: 7
    view: Views/NewsView.xaml
    viewmodel: ViewModels/NewsViewModel.cs
    default_startup: true
  - kind: AutoTrack
    enum_value: 0
    view: Views/AutoTrackView.xaml
    viewmodel: ViewModels/AutoTrackViewModel.cs
  - kind: FindAdd
    enum_value: 1
    view: Views/FindAddView.xaml
    viewmodel: ViewModels/FindAddViewModel.cs
  - kind: Library
    enum_value: 2
    view: Views/LibraryView.xaml
    viewmodel: ViewModels/LibraryViewModel.cs
  - kind: Stats
    enum_value: 8
    view: Views/StatsView.xaml
    viewmodel: ViewModels/StatsViewModel.cs
  - kind: Torrent
    enum_value: 3
    view: Views/TorrentWorkspaceView.xaml
    viewmodel: ViewModels/TorrentWorkspaceViewModel.cs
  - kind: Recipe
    enum_value: 5
    view: Views/RecipeWorkspaceView.xaml
    viewmodel: ViewModels/RecipeWorkspaceViewModel.cs
  - kind: SystemSettings
    enum_value: 6
    view: Views/SystemSettingsView.xaml
    viewmodel: ViewModels/SystemSettingsViewModel.cs
shell:
  main_window: MainWindow.xaml
  main_viewmodel: ViewModels/MainViewModel.cs
  view_templates: Resources/ViewTemplates.xaml
  lucide_icons: docs/LUCIDE_ICONS.md
  lucide_package: MahApps.Metro.IconPacks.Lucide 6.2.1
  lucide_kind_rule: IconKind/Kind must match PackIconLucideKind exactly; unknown names render blank
  nested_scroll: Views/NestedScrollViewer.cs — vertical wheel on nested horizontal ScrollViewer goes to page; Shift+wheel pans
  navigation_command: NavigateCommand
navigation:
  contract: ViewModels/INavigationAware.cs
  base: ViewModels/ViewModelBase.cs (virtual OnNavigatedTo / OnNavigatedFrom)
  wiring: MainViewModel.NavigateTo — OnNavigatedFrom(previous) then swap then OnNavigatedTo(next); skip if same workspace
  lifetime: all eight workspace VMs remain DI singletons (Phase 5 transient reserved)
  refresh_policy:
    News: OnNavigatedTo UpdateDashboard if last load older than 2 minutes
    AutoTrack: OnNavigatedTo RefreshDashboard
    FindAdd: OnNavigatedTo RefreshExistingMedia + refresh SearchResults IsAlreadyAdded flags
    Library: OnNavigatedTo clear detail-load cache, RefreshLibrary, reload selected detail
    Stats: OnNavigatedTo rebuild personal rating overview (no TTL)
    Torrent: OnNavigatedTo RefreshWorkspace; OnNavigatedFrom no-op — cart/search keep running while on other tabs (user Stop only)
    Recipe: OnNavigatedTo ReloadRecipes if RecipesChanged while away; OnNavigatedFrom mark inactive
    Settings: OnNavigatedTo settingsService.Load + LoadFromSettings (dirty-tracking Sprint 6)
  long_ops_policy: >
    Workspace long-running UI ops (Torrent cart/search via _operationCts) are NOT cancelled on navigate away.
    Personal single-user app: multitask across tabs is preferred over cancel-for-safety.
    Explicit Stop on Torrent workspace remains the cancel path. FindAdd TMDB search likewise continues if user leaves mid-search.
```

---

## Database Entities

```yaml
tables:
  SourceItems:
    purpose: Scanned/imported media files from source folders; unmatched pack extras (IsOrphanPackSpecial) are linked files not in TMDB order and cannot be rated
    key_columns: [FilePath, MediaKind, State, LinkedPath, SymlinkPath, AutoTorrentTorrentHash]
    enums: [MediaKind, ParserPattern, ItemState, AutoTorrentLinkKind]
  SeriesMappings:
    purpose: Parsed title to TMDB provider mapping cache
    unique: [NormalizedParsedTitle, ParserPattern]
  TrackedShows:
    purpose: TMDB TV series tracking and auto-track config
    key_columns: [TmdbId, RecipeId, PackRecipeId, AutoTrackFromSeason, AutoTrackFromEpisode]
  TrackedSeasons:
    purpose: Per-season pack/episode mode and pack torrent state
    fk: ShowId -> TrackedShows CASCADE
    unique: [ShowId, SeasonNumber]
  TrackedEpisodes:
    purpose: Episode availability, torrent/candidate state, personal UserRating/Thought
    fk: ShowId -> TrackedShows CASCADE
    unique: [ShowId, SeasonNumber, EpisodeNumber]
    notes: SeasonNumber 0 is TMDB extras/specials/OVAs (rateable). Unmatched pack extras are SourceItems, not rows here
  TrackedMovies:
    purpose: TMDB movie tracking
    key_columns: [TmdbId, RecipeId, TorrentHash]
  FetchJobs:
    purpose: Legacy fetch job table (schema kept; rows purged once in migration 002)
    purge: MediaManager.Core/Migrations/002_fetchjobs_legacy_purge.sql
  TorrentCartOrders:
    purpose: Torrent acquisition cart orders
    key_columns: [TargetKind, MediaId, Status, Source]
    enums: [TorrentOrderStatus, TorrentOrderSource]
  TorrentCartOrderCandidates:
    purpose: Ranked search candidates per cart order
    fk: OrderId -> TorrentCartOrders
  TorrentBlacklist:
    purpose: Rejected torrent URLs/hashes per show
    key_columns: [ListingUrl, InfoHash, ShowId, IsActive]

migration_strategy:
  type: numbered_sql_scripts
  version_table: SchemaMigrations
  version_table_columns: [Id, Name, AppliedUtc]
  runner: MediaManager.Core/Migrations/MigrationRunner.cs
  scripts: MediaManager.Core/Migrations/*.sql
  applied:
    - 001_baseline
    - 002_fetchjobs_legacy_purge
    - 003_torrentblacklist_rebuild
    - 004_episode_rating_thought
  init_file: Services/DatabaseService.cs
  methods: [MigrationRunner.ApplyPendingMigrations, CREATE TABLE IF NOT EXISTS, EnsureColumn]
  failure_policy: block_startup
  failure_recovery: restore media-manager.db from Google Drive backup or CreateSafeSnapshot()
  ensure_column_chain: retained as transition safety net until later sprint
  code_migrations:
    - 003_torrentblacklist_rebuild: MediaManager.Core/Migrations/TorrentBlacklistRebuildMigration.cs
```

---

## Service Registry (DI Singletons)

```yaml
bootstrap: App.xaml.cs::ConfigureServices

core:
  - ISettingsService -> SettingsService
  - IDatabaseService -> DatabaseService
  - IAppLogger -> AppLogger
  - ICrashLogService -> CrashLogService
  - IThemeService -> ThemeService
  - IOperationProgressService -> OperationProgressService
  - IAppLifecycleService -> AppLifecycleService
  - ITrayIconService -> TrayIconService
  - IWindowsNotificationService -> WindowsNotificationService
  - ILogCleanupService -> LogCleanupService
  - IDeviceStatusService -> DeviceStatusService

library:
  - IScannerService -> ScannerService
  - IParserService -> ParserService
  - ILibraryPathResolver -> LibraryPathResolver
  - IHardlinkService -> HardlinkService
  - ISymlinkService -> SymlinkService
  - ISymlinkSyncService -> SymlinkSyncService
  - ISymlinkCoordinatorService -> SymlinkCoordinatorService
  - INfoWriterService -> NfoWriterService
  - ISourceReconciliationService -> SourceReconciliationService
  - ILibraryManagementService -> LibraryManagementService
  - IMediaImportService -> MediaImportService
  - IMediaCardCatalogService -> MediaCardCatalogService
  - ILibraryLinkEventHub -> LibraryLinkEventHub

tmdb:
  - IMetadataProvider -> TmdbMetadataProvider
  - ITmdbShowCatalogService -> TmdbMetadataProvider
  - ITmdbMovieCatalogService -> TmdbMetadataProvider
  - ITrackedShowService -> TrackedShowService
  - ITrackedMovieService -> TrackedMovieService
  - IMediaMetadataSyncService -> MediaMetadataSyncService
  - IPosterImageService -> PosterImageService

torrent:
  - IQbittorrentClient -> QbittorrentClient
  - IQbittorrentSearchPluginService -> QbittorrentSearchPluginService
  - IQbittorrentProcessRestartService -> QbittorrentProcessRestartService
  - IRecipeService -> RecipeService
  - ISearchPlanBuilder -> SearchPlanBuilder
  - ISearchTitleResolver -> SearchTitleResolver
  - ICandidateEvaluationService -> CandidateEvaluationService
  - IFetchJobService -> FetchJobService
  - ShowSearchSnapshotService -> ShowSearchSnapshotService
  - ITorrentCartService -> TorrentCartService
  - ITorrentAddGateService -> TorrentAddGateService
  - ITorrentContentValidationService -> TorrentContentValidationService
  - ITorrentCleanupService -> TorrentCleanupService
  - ITorrentBlacklistService -> TorrentBlacklistService
  - ITorrentReconciliationService -> TorrentReconciliationService
  - ITorrentAddDiskAssignmentService -> TorrentAddDiskAssignmentService
  - IDownloadFolderCatalogService -> DownloadFolderCatalogService
  - IAutomationFlowService -> AutomationFlowService
  - IAutoTorrentLinkService -> AutoTorrentLinkService
  - IPackLinkCoordinatorService -> PackLinkCoordinatorService

autotrack:
  - IAutoTrackService -> AutoTrackService
  - AutoTrackCandidatePolicyService -> AutoTrackCandidatePolicyService
  - IAutoTrackSchedulerService -> AutoTrackSchedulerService

integrations:
  - IJellyfinClient -> JellyfinClient
  - IJellyfinLibraryRefreshService -> JellyfinLibraryRefreshService
  - IJellyfinViewerService -> JellyfinViewerService
  - IQbittorrentViewerService -> QbittorrentViewerService
  - IWarpCliService -> WarpCliService
  - IGeminiApiClient -> GeminiApiClient
  - IGeminiModelCatalogService -> GeminiModelCatalogService
  - IGeminiLinkConfirmationService -> GeminiLinkConfirmationService
  - ISpecialMappingOrchestrator -> SpecialMappingOrchestrator

backup:
  - IGoogleDriveClient -> GoogleDriveClient
  - IBackupService -> BackupService
  - IBackupSchedulerService -> BackupSchedulerService

ui_services:
  - IConsoleWindowService -> ConsoleWindowService
  - IWindowsStartupService -> WindowsStartupService
```

---

## Key Flows

### Auto-Track Pipeline

```yaml
flow: autotrack
scheduler: Services/AutoTrackSchedulerService.cs
executor: Services/AutoTrackService.cs
phases:
  - name: tmdb_discovery
    method: RunTmdbDiscoveryAsync
    lock: _discoveryLock
    actions: [refresh shows, detect new episodes, enforce daily budget, weekly anchor]
  - name: torrent_hunt
    method: RunTorrentHuntAsync
    lock: _huntLock
    actions: [search qbit, score candidates, create cart orders, add torrents via gate]
    dependencies: [IWarpCliService, IQbittorrentProcessRestartService, ITorrentAddGateService]
  - name: background_reconcile
    method: RunBackgroundReconcileAsync
    lock: _reconcileLock
    actions: [sync torrent state, hardlink, pack link, release hunt blocks]
triggers:
  - timer intervals from AutoTrackSettings
  - manual Run Now from AutoTrackViewModel
  - RequestReconcileAfterAdds after successful torrent adds
events:
  - AutoTrackSchedulerService.RunCompleted -> tray notification, News refresh
tmdb_eligibility:
  service: Services/AutoTrackTmdbEligibility.cs
  should_refresh: [is_auto_tracked, past_anchor, not_satisfied_this_week, not_finished_complete, not_fully_caught_up_finished]
  pending_hunt: [at_or_after_checkpoint, air_date_plus_delay, missing, no_hash, no_active_cart]
  tmdb_states: [Active=0, DormantCaughtUp=1, FinishedComplete=2]
  weekly_air_day: Services/ShowWeeklyAirDay.cs
    method: strict_plurality_of_last_12_episodes_utc_plus_7
  anchor: Services/AutoTrackWeekAnchor.cs
    week_starts: Sunday
```

### Torrent Cart Acquisition

```yaml
flow: torrent_cart
entry_points:
  - LibraryViewModel (AddEpisodeToCart, AddSeasonPackToCart, AddMovieToCart)
  - AutoTrackService (auto-created orders)
  - TorrentWorkspaceViewModel (RunCart, AddCart)
services:
  search: FetchJobService, ShowSearchSnapshotService
  evaluate: CandidateEvaluationService, CandidateMatcher
  persist: TorrentCartService
  add: TorrentAddGateService -> QbittorrentClient
  reconcile: TorrentReconciliationService
order_status_enum: TorrentOrderStatus
  values: [Draft, Searching, CandidateSelected, Adding, Downloading, Completed, Failed, Canceled, ...]
```

### Torrent Add Gate (Safety)

```yaml
flow: torrent_add_gate
service: Services/ITorrentAddGateService.cs (TorrentAddGateService)
commit_631c3d7: "remove add paused and added polling validation after torrent add"
steps:
  1: Blacklist check (listing URL)
  2: Add torrent RUNNING (Paused=false) via QbittorrentClient
  3: Infohash blacklist check -> delete if matched
  4: If EnableContentValidation=false -> return immediately
  5: Poll GetTorrentFilesAsync every 1s until files or timeout
  6: Empty file list -> delete, throw (no blacklist)
  7: Validate via TorrentContentValidationService (isPack inferred from order)
  8a: Return live torrent if valid (download continues)
  8b: Blacklist + delete if malicious (MaliciousTorrentException)
config: Models/TorrentValidationConfig.cs
  ValidationTimeoutSeconds: default 90 (code), clamp 5-120
  note: settings.json may still show 30 until user saves settings
```

### Library Linking

```yaml
flow: library_link
service: Services/AutoTorrentLinkService.cs
types:
  episode: LinkEpisodeAsync -> HardlinkService -> NfoWriterService
  season_pack: LinkSeasonPackAsync -> SpecialMappingOrchestrator (optional Gemini)
  movie: LinkMovieAsync
  show_bulk: LinkShowAsync
symlink: SymlinkSyncService (via SymlinkCoordinatorService)
jellyfin: JellyfinLibraryRefreshService.EnqueueFromSourceItem
events: LibraryLinkEventHub.PublishHardlinkCreated
```

### Backup

```yaml
flow: backup
service: Services/Backup/BackupService.cs
scheduler: Services/Backup/BackupSchedulerService.cs
triggers: [Daily, EventDriven, Manual]
contents:
  - database snapshot (DatabaseService.CreateSafeSnapshot)
  - settings.json (secrets redacted via BackupSettingsRedactor)
  - recipes folder
  - manifest with checksums
destination: Google Drive (IGoogleDriveClient)
oauth:
  credentials: "{StateFolder}/GoogleDrive/credentials.json or Backup.CredentialsFilePath"
  token_store: "{StateFolder}/GoogleDrive/token/"
  scope: DriveService.Scope.DriveFile
  user_id: media-manager
  doc: docs/STATE_FOLDER.md#google-drive-oauth
```

---

## Settings Schema

```yaml
settings_file: settings.json
root_model: Models/AppSettings.cs
loader: Services/SettingsService.cs

sections:
  StateFolder: string
  AutoTorrent: Models/AutoTorrentSettings
  Warp: Models/WarpSettings
  AutoTrack: Models/AutoTrackSettings
  Logs: Models/LogSettings
  Startup: Models/AppStartupSettings
  Ui: Models/UiSettings
  Notifications: Models/NotificationSettings
  SourceFolders: list<string>
  LibraryRootMode: LibraryRootMode
  DefaultLibraryFolderName: string
  DriveLibraryRoots: dict
  Symlink: Models/SymlinkSettings
  TmdbReadAccessToken: string
  Gemini: Models/GeminiSettings
  Backup: Models/BackupSettings
  TorrentValidation: Models/TorrentValidationConfig

settings_sections_ui:
  enum: SettingsSection
  values: [System, Library, AutoTrack, Integrations, TorrentStorage, Notifications, Backup]
```

---

## Enums (Critical)

```yaml
enums_file: Common/AppEnums.cs
key_enums:
  MediaKind: [Unknown, TvEpisode, Movie, TvSeasonPack]
  TorrentOrderStatus: [Draft, Searching, CandidateSelected, Adding, Downloading, Completed, Failed, Canceled, ...]
  TorrentOrderSource: [Manual, AutoTrack]
  UserWatchStatus: [None, Watching, Completed, OnHold, Dropped, PlanToWatch]
  SeasonManagementMode: [Episode, Pack]
  EpisodeAvailability: [Missing, Available]
  ShowSeriesStatus: [Unknown, Ongoing, Finished]
  AutoTorrentLinkKind: [Episode, SeasonPack, Movie]
  AppWorkspaceKind: [AutoTrack=0, FindAdd=1, Library=2, Torrent=3, Recipe=5, SystemSettings=6, News=7, Stats=8]
  NotificationKind: see Common/NotificationCatalog.cs
  AppTheme: [Light, Dark]
```

---

## External APIs

```yaml
integrations:
  qbittorrent:
    client: Services/QbittorrentClient.cs
    config: AutoTorrentSettings (WebUiUrl, credentials, ApiKey, DownloadFolders)
    features: [search, add, pause, resume, delete, file list, plugins]
    webapi_docs: docs/qbittorrent-webapi/  # 5.1 vs 5.2; client supports 5.2 login/add + optional API key
  jellyfin:
    client: Services/JellyfinClient.cs
    config: JellyfinRefreshSettings (BaseUrl, ApiKey)
    features: [connection test, path notify, scheduled tasks]
  tmdb:
    provider: Services/TmdbMetadataProvider.cs
    config: AppSettings.TmdbReadAccessToken
  gemini:
    client: Services/Gemini/GeminiApiClient.cs
    config: GeminiSettings
    use_case: special episode mapping in season packs
  warp:
    service: Services/WarpCliService.cs
    config: WarpSettings
    use_case: SSL recovery during torrent hunt
  google_drive:
    client: Services/Backup/GoogleDriveClient.cs
    config: BackupSettings
    packages: [Google.Apis.Drive.v3, Google.Apis.Auth]
```

---

## File Index (High-Value)

```yaml
entry_points:
  - App.xaml.cs
  - MainWindow.xaml
  - MainWindow.xaml.cs

orchestration:
  - ViewModels/MainViewModel.cs
  - ViewModels/LibraryViewModel.cs
  - ViewModels/TorrentWorkspaceViewModel.cs
  - ViewModels/AutoTrackViewModel.cs
  - ViewModels/SettingsViewModel.cs
  - Services/AutoTrackService.cs
  - Services/AutoTrackSchedulerService.cs
  - Services/TorrentCartService.cs
  - Services/FetchJobService.cs

data:
  - Services/DatabaseService.cs
  - Services/IDatabaseService.cs
  - Models/AppSettings.cs

safety:
  - Services/TorrentAddGateService.cs
  - Services/TorrentContentValidationService.cs
  - Services/TorrentBlacklistService.cs
  - Common/MaliciousTorrentException.cs

linking:
  - Services/AutoTorrentLinkService.cs
  - Services/HardlinkService.cs
  - Services/Symlink/SymlinkCoordinatorService.cs
  - Services/PackLinkCoordinatorService.cs
  - Services/SpecialMappingOrchestrator.cs (Services/Gemini/)

recipes:
  - Services/RecipeService.cs
  - Services/SearchPlanBuilder.cs
  - Services/CandidateEvaluationService.cs
  - Services/QbittorrentSearchPluginService.cs

ui_templates:
  - Resources/ViewTemplates.xaml
  - Resources/AppStyles.xaml
  - Resources/WorkspaceSharedTemplates.xaml
  - docs/LUCIDE_ICONS.md
  - Views/NestedScrollViewer.cs
```

---

## Startup Sequence

```yaml
startup_order:
  1: Single instance mutex check
  2: DI container build (ConfigureServices)
  3: SettingsService.Load()
  4: Gemini model catalog reload + normalize
  5: ThemeService.Apply()
  6: DatabaseService.Initialize(stateFolder)
    side_effect: MigrationRunner applies pending SchemaMigrations (001_baseline, 002_fetchjobs_legacy_purge, 003_torrentblacklist_rebuild, 004_episode_rating_thought once)
    on_failure: DatabaseMigrationException + dialog; App.OnStartup Shutdown()
  7: LogCleanupService.Start()
  8: SymlinkCoordinatorService.Start()
  9: AutoTrackSchedulerService.Start()
  10: BackupSchedulerService.Start()
  11: WindowsNotificationService.Initialize()
  12: Poster cache warmup (background task)
  13: MainWindow show (normal, minimized, or toast-activated)
  14: TrayIconService.Initialize (if configured)

shutdown_order:
  - AutoTrackSchedulerService.Dispose
  - BackupSchedulerService.Dispose
  - SymlinkCoordinatorService.Dispose
  - JellyfinLibraryRefreshService.Dispose
  - LogCleanupService.Dispose
  - TrayIconService.Dispose
  - Release single instance mutex
```

---

## MediaManager.Core (Sprint 1–3)

Pure torrent / recipe / pack / validation logic extracted to a **net8.0** class library so unit tests run without WPF.

```yaml
core:
  project: MediaManager.Core/MediaManager.Core.csproj
  tfm: net8.0
  tests: MediaManager.Core.Tests/MediaManager.Core.Tests.csproj
  test_stack: [xUnit, FluentAssertions, coverlet.collector]
  test_count: 80
  wpf_reference: media management app.csproj -> ProjectReference MediaManager.Core
  moved_types:
    - TorrentCandidateParser (+ TorrentCandidateParseResult)
    - TorrentReleaseKind (+ TorrentReleaseKindFlags)
    - TorrentQuality (Detect, GetRank, AllQualities, MatchesSelectedQuality)
    - MediaKind (enum; removed duplicate from Common/AppEnums.cs)
    - CandidateEvaluationService + ICandidateEvaluationService
    - SearchPlanBuilder + ISearchPlanBuilder
    - SearchTitleResolver + ISearchTitleResolver
    - PackSeasonFileGrouper, PackEpisodePatternInferrer, PackSpecialBucketDetector
    - TorrentContentValidationService (file-list); ITorrentContentValidationService
    - RecipeRuntimeSettings, Recipe/Tracked/validation models
  app_wrapper:
    - QbittorrentTorrentContentValidationService (ValidateAsync via qBit, then Core file-list)
  namespaces_unchanged: media_management_app.Services, media_management_app.Models, media_management_app.Common
  coverage_advisory:
    TorrentCandidateParser: "90.9% line"
    CandidateEvaluationService: "94.8% line"
  sprint_2:
    runner: MediaManager.Core/Migrations/MigrationRunner.cs
    tests: MediaManager.Core.Tests/Migrations/MigrationRunnerTests.cs
    migrations: [001_baseline, 002_fetchjobs_legacy_purge, 003_torrentblacklist_rebuild, 004_episode_rating_thought]
  sprint_3_note: "003 TorrentBlacklist rebuild extracted from DatabaseService (C# conditional migration)"
```

---

## Build

```yaml
build:
  tool: MSBuild
  platform: x64
  tfm: net8.0-windows10.0.17763.0
  project: media management app.csproj
  core_project: MediaManager.Core/MediaManager.Core.csproj
  test_project: MediaManager.Core.Tests/MediaManager.Core.Tests.csproj
  command: msbuild "media management app.csproj" /p:Platform=x64
  test_command: dotnet test MediaManager.Core.Tests/MediaManager.Core.Tests.csproj -c Release
  publish: single-file self-contained win-x64
  third_party: ThirdParty/Sonarr.Parser/MediaManager.Sonarr.Parser.csproj
```

---

## Known Gaps / Unclear Areas

```yaml
gaps:
  - pack_episode_resolver: Skip reasons in PackEpisodeResolver not fully documented
  - gemini_prompts: Special-mapping prompt text in Services/Gemini/ not extracted
resolved_docs:
  - recipe_json_schema: docs/RECIPE_SCHEMA.md
  - state_folder: docs/STATE_FOLDER.md
  - pack_link_heuristics: docs/FEATURES.md Appendix A
  - autotrack_eligibility: docs/FEATURES.md section 3.7
  - fetch_jobs_purge: docs/STATE_FOLDER.md (one-time migration 002)
  - google_drive_oauth: docs/STATE_FOLDER.md
  - commit_631c3d7: docs/FEATURES.md section 10.6, torrent_add_gate above
```

---

## Related Human Docs

- [APP_OVERVIEW.md](./APP_OVERVIEW.md) — narrative overview
- [FEATURES.md](./FEATURES.md) — exhaustive feature list
- [IMPROVEMENTS.md](./IMPROVEMENTS.md) — evaluation and suggestions
- [STATE_FOLDER.md](./STATE_FOLDER.md) — state folder, OAuth, SchemaMigrations / FetchJobs purge
- [RECIPE_SCHEMA.md](./RECIPE_SCHEMA.md) — recipe `.rcp` JSON schema
