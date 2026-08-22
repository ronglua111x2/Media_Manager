# Planning: Split Large ViewModels and Services

**Priority:** High  
**Source:** [IMPROVEMENTS.md](../IMPROVEMENTS.md) § Priority: High #3  
**Status:** Planning only — no implementation yet

---

## Problem statement

Several core types far exceed comfortable maintenance size. Navigation, code review, and parallel work are hard when a single file owns dozens of commands, properties, and integration tests.

| Type | Lines | Role |
|------|-------|------|
| `LibraryViewModel` | ~2,381 | Library grid, detail pane, import, pack link, cart, TMDB sync |
| `SettingsViewModel` | ~2,076 | All 7 settings sections (via `SettingsView.xaml` ~2,142 lines) |
| `TorrentWorkspaceViewModel` | ~1,965 | Torrent search UI, cart run, recipe assignment (not in IMPROVEMENTS list but same debt) |
| `AutoTrackService` | ~1,564 | TMDB discovery, hunt, reconcile, notifications |
| `DatabaseService` | ~3,257 | Schema + all CRUD (overlaps with [01-database-migration-versioning.md](./01-database-migration-versioning.md)) |
| `FetchJobService` | ~1,473 | Search orchestration (related to Auto-Track/Torrent) |

IMPROVEMENTS explicitly names **`SettingsViewModel`**, **`LibraryViewModel`**, and **`AutoTrackService`**.

---

## Why it happened

Features were added ** vertically in the workspace that needed them**:

- Library needed pack AI link → commands landed in `LibraryViewModel`.
- Settings UI has 7 sections (`SettingsSection` enum) but **one ViewModel** binds all sections in `Views/SettingsView.xaml`.
- Auto-Track grew from “refresh TMDB” into full hunt + cart + reconcile + WARP recovery in one service.

MVVM with CommunityToolkit `[RelayCommand]` encourages co-locating UI actions with state — productive early on, heavy later. `SystemSettingsViewModel` is only a thin subclass:

```9:33:ViewModels/SystemSettingsViewModel.cs
public sealed class SystemSettingsViewModel : SettingsViewModel
{
    public SystemSettingsViewModel(...) : base(...) { }
}
```

No partial-class splits exist today (`SettingsViewModel` and `LibraryViewModel` are `partial` for source generators only, not file splits).

---

## Current behavior

### SettingsViewModel — monolithic settings brain

- **~40+ `[RelayCommand]`** methods (browse folders, test TMDB/Gemini/qBittorrent/Jellyfin/WARP, backup, symlink sync, Gemini fallback chain, notification tests, etc.).
- **~80+ `[ObservableProperty]`** fields spanning System, Library, Auto-Track, Integrations, Torrent Storage, Notifications, Backup.
- Constructor injects **18 dependencies** (settings, DB, qBittorrent, WARP, Gemini, Google Drive, backup, symlink, Jellyfin, …).
- UI section switch is **`SelectedSettingsSection`** only — no separate VMs:

```35:44:Common/AppEnums.cs
public enum SettingsSection
{
    System = 0,
    Library = 1,
    AutoTrack = 2,
    Integrations = 3,
    TorrentStorage = 4,
    Notifications = 5,
    Backup = 6
}
```

`Views/SettingsView.xaml` uses `DataTrigger` on `SelectedSettingsSection` to show/hide seven `ScrollViewer` panels — all bound to the same `SystemSettingsViewModel` instance.

### LibraryViewModel — monolithic library workspace

- **47 `[RelayCommand]`** — refresh, delete, cart add, pack link/unlink, import, TMDB refresh, watch status, rating, episode org, etc.
- **16 constructor dependencies** including cart, reconciliation, pack link, Gemini, import.
- Subscribes to **`CartChanged`**, **`Reconciled`**, **`PackReconciled`**, **`AppModeChanged`** for live updates.
- Persists UI state to `settings.json` (`LibrarySelectedMediaId`, sort, filters) — `RestoreLibraryUiState()` / `PersistLibraryUiState()`.
- Detail loading uses `_loadedDetailMediaId` to avoid redundant reloads when re-selecting same card.

Constructor + event wiring:

```49:100:ViewModels/LibraryViewModel.cs
    public LibraryViewModel(... 16 services ...)
    {
        _torrentCartService.CartChanged += (_, _) => { ... RefreshCartStateOnSelectedDetail(); };
        _torrentReconciliationService.Reconciled += (_, _) => RunReloadSelectedDetailOnUiThread();
        packLinkCoordinatorService.PackReconciled += (_, _) => RunReloadSelectedDetailOnUiThread();
        lifecycleService.AppModeChanged += OnAppModeChanged;
        RestoreLibraryUiState();
        RefreshLibrary();
    }
```

Already uses **child VMs** for rows/cards: `LibraryMediaCardViewModel`, `LibraryShowDetailViewModel`, `LibrarySeasonViewModel`, `LibraryEpisodeRowViewModel`, `LibraryMovieDetailViewModel`, `PackLinkReviewViewModel`.

### AutoTrackService — three-phase orchestrator

Public surface:

```68:122:Services/AutoTrackService.cs
    public bool IsRunning => IsTmdbDiscoveryRunning || IsTorrentHuntRunning || IsReconcileRunning;
    public async Task<AutoTrackRunResult> RunAsync(...)
    public async Task<AutoTrackRunResult> RunTmdbDiscoveryAsync(...)
    public async Task<AutoTrackRunResult> RunTorrentHuntAsync(...)
    public async Task<AutoTrackRunResult> RunBackgroundReconcileAsync(...)
```

Internal phases (private methods):

| Phase | Approx. responsibility |
|-------|------------------------|
| TMDB discovery | Weekly anchor, eligibility, TMDB refresh, SSL/WARP recovery |
| Torrent hunt | Preflight (WARP, qBittorrent), `FetchJobService`, cart orders, add gate |
| Reconcile | Completed downloads → auto-link via `IAutoTorrentLinkService` |

Uses three **`SemaphoreSlim`** locks (`_discoveryLock`, `_huntLock`, `_reconcileLock`) so phases can overlap policy-wise but not duplicate work.

`AutoTrackViewModel` (~239 lines) is relatively small — it delegates to `IAutoTrackService` and scheduler; **the service** is the maintenance burden.

---

## Impact

| Risk | Detail |
|------|--------|
| **Regression surface** | Any edit to `LibraryViewModel` can break cart, import, or pack link |
| **Review fatigue** | 2k-line diffs discourage thorough review |
| **Onboarding** | New contributors cannot find “where backup settings save” without search |
| **Testing** | UI logic intertwined with services — hard to unit test ([02-unit-tests-critical-paths.md](./02-unit-tests-critical-paths.md)) |
| **Merge conflicts** | Solo dev today; still painful when touching same file for unrelated features |
| **DI complexity** | Large constructors mask missing abstractions |

---

## Affected areas

### Settings split candidates

| Proposed sub-VM | SettingsSection | Key dependencies |
|-----------------|-----------------|------------------|
| `SystemSettingsSectionViewModel` | System | state folder, logging, startup, theme |
| `LibrarySettingsSectionViewModel` | Library | source folders, symlink, library paths |
| `AutoTrackSettingsSectionViewModel` | AutoTrack | auto-track schedule, quality defaults |
| `IntegrationsSettingsSectionViewModel` | Integrations | TMDB, Gemini, qBittorrent, Jellyfin, WARP |
| `TorrentStorageSettingsSectionViewModel` | TorrentStorage | download folders, candidate cleanup |
| `NotificationsSettingsSectionViewModel` | Notifications | notification prefs |
| `BackupSettingsSectionViewModel` | Backup | Google Drive, restore |

Files: `ViewModels/SettingsViewModel.cs`, `Views/SettingsView.xaml`, `ViewModels/SystemSettingsViewModel.cs`, `Common/AppEnums.cs`.

### Library split candidates

| Proposed piece | Responsibility |
|----------------|----------------|
| `LibraryCatalogViewModel` | Grid, sort, filter, search, card selection |
| `LibraryDetailViewModel` | Show/movie detail, seasons, episodes |
| `LibraryImportCoordinator` | External import flow (service or VM) |
| `LibraryPackLinkCommands` | Pack link/unlink/cleanup (may stay coordinated with `IPackLinkCoordinatorService`) |

Files: `ViewModels/LibraryViewModel.cs`, `Views/LibraryView.xaml`, child VMs in `ViewModels/Library*.cs`.

### AutoTrackService split candidates

| Proposed service | Methods (conceptual) |
|------------------|----------------------|
| `AutoTrackTmdbDiscoveryService` | `RunTmdbDiscoveryCoreAsync`, eligibility, anchor |
| `AutoTrackHuntService` | `RunTorrentHuntCoreAsync`, preflight, fetch/add |
| `AutoTrackReconcileService` | `RunBackgroundReconcileCoreAsync`, link ready episodes |
| Keep façade | `AutoTrackService` implements `IAutoTrackService`, delegates |

Files: `Services/AutoTrackService.cs`, `Services/IAutoTrackService.cs`, `Services/AutoTrackSchedulerService.cs`, `Services/AutoTrackCandidatePolicyService.cs`.

---

## Constraints

- **Single settings save model** — `ISettingsService` + `settings.json`; splits must not fragment save/load (one `Save()` or coordinated section saves).
- **XAML bindings** — Split VMs need composite root (`SettingsViewModel` as host) or `DataTemplate` per section to avoid massive binding churn in one sprint.
- **Event subscriptions** — Library/Torrent VMs subscribe to singleton services; document lifetime when splitting.
- **No behavior change** — Refactor-only unless paired with tests.
- **WPF design-time** — Designer may need parameterless ctor or mocks for new VMs.

---

## Options for resolution

### Option A: Partial classes by region (file split only)

`SettingsViewModel.System.cs`, `SettingsViewModel.Integrations.cs`, etc. Same public type, multiple files.

| Pros | Cons |
|------|------|
| Minimal DI/XAML change | Still one giant object conceptually |
| Quick organization win | Does not reduce constructor size |
| Low risk | Review still shows “SettingsViewModel changed” |

### Option B: Section sub-ViewModels (IMPROVEMENTS suggestion)

Host VM exposes `IntegrationsSettingsViewModel Integrations { get; }`; XAML binds `{Binding Integrations.TmdbToken}`.

| Pros | Cons |
|------|------|
| Clear ownership per settings tab | XAML binding path updates across ~2k lines XAML |
| Smaller units for tests | Save/load orchestration needed |
| Matches UI structure | 7 new types + DI registration |

### Option C: Extract application services from VMs

Move logic from `LibraryViewModel` into `ILibraryWorkspaceService` / use cases; VM becomes thin.

| Pros | Cons |
|------|------|
| Best for testability | Larger initial refactor |
| Services reusable from Auto-Track | More interfaces |

For **AutoTrackService**, Option C maps naturally to **phase services** (Option B in service split table above).

---

## Suggested planning steps

1. **Prioritize by churn** — Which file changes most often? (Likely `LibraryViewModel` or `AutoTrackService`.)
2. **Add tests first** for logic you will move ([02-unit-tests-critical-paths.md](./02-unit-tests-critical-paths.md)).
3. **Settings: start with Integrations or Backup** — Isolated test buttons, clear boundaries.
4. **Introduce host pattern** — `SettingsViewModel` keeps `SelectedSettingsSection`, exposes child VMs, forwards `LoadFromSettings` / `SaveToSettings`.
5. **Library: extract catalog vs detail** — Detail pane is ~half the commands; use separate partial VM or nested object bound in detail `ContentControl`.
6. **AutoTrack: extract hunt service** — Hunt phase is most complex (WARP, fetch, gate); discovery/reconcile follow.
7. **DatabaseService** — Pair with migration split ([01-database-migration-versioning.md](./01-database-migration-versioning.md)), not this item alone.
8. **Document ownership** — Update `AI_CONTEXT.md` key files per new type.

---

## Open questions

1. **Split depth** — Partial files (A), sub-VMs (B), or service extraction (C)?  
2. **Settings save UX** — One global Save button vs. per-section save (today: mixed auto-save on change)?  
3. **TorrentWorkspaceViewModel** — Include in same initiative (~1,965 lines)?  
4. **FetchJobService** — Split with Auto-Track or separately?  
5. **XAML strategy** — Split `SettingsView.xaml` into user controls per section (`IntegrationsSettingsPanel.xaml`)?  
6. **Order of execution** — Settings first (less runtime risk) or AutoTrack service (higher business value)?
