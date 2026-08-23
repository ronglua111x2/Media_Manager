# settings.json Schema & Ownership

**File:** `{StateFolder}/settings.json` (default `D:\MediaManagerState\settings.json`)  
**Root type:** [`Models/AppSettings.cs`](../../Models/AppSettings.cs)

## Tree overview

```
AppSettings
├── StateFolder                    ← System tab
├── SourceFolders[]                ← Library tab
├── DefaultLibraryFolderName       ← Library tab
├── LibraryRootMode                ← forced AutoPerDrive (no UI)
├── DriveLibraryRoots{}            ← NO Settings UI (LibraryPathResolver)
├── OutputLibraryFolder            ← legacy, HardlinkService only
├── TmdbReadAccessToken            ← Integrations tab
├── AutoTorrent                    ← Integrations (qBit) + Torrent Storage
├── Warp                           ← Integrations UI, Auto-Track consumer
├── AutoTrack                      ← Auto-Track tab + Integrations (Jellyfin creds)
├── Logs                           ← System tab
├── Startup                        ← System tab
├── Ui                             ← System (theme) + Library/Torrent/News VMs
├── Notifications                  ← Notifications tab
├── Symlink                        ← Library tab
├── Gemini                         ← Integrations tab
├── Backup                         ← Backup tab (+ BackupService runtime fields)
└── TorrentValidation              ← NO Settings UI (Core, torrent safety)
```

## Section-by-section ownership matrix

Legend: **UI** = Settings page tab · **VM** = which code edits `Current` · **Save path** = how it reaches disk

### System tab ↔ mixed roots

| JSON path | UI tab | Written by | Loaded in Settings VM |
|-----------|--------|------------|------------------------|
| `StateFolder` | System | `Save()` direct | Yes |
| `Logs.*` | System | `ApplyLogSettings()` | Yes |
| `Startup.*` | System | `ApplyStartupSettings()` | Yes |
| `Ui.Theme` | System | `ApplyUiSettings()` | Yes (`SelectedTheme`) |

### Library tab

| JSON path | UI tab | Written by | Notes |
|-----------|--------|------------|-------|
| `SourceFolders[]` | Library | `Save()` direct | |
| `DefaultLibraryFolderName` | Library | `Save()` + **preview mutates Current** | Preview side effect |
| `Symlink.*` | Library | `ApplySymlinkSettings()` | |
| `LibraryRootMode` | — | Always `AutoPerDrive` on save | No UI |
| `DriveLibraryRoots` | — | Never from Settings | Per-drive overrides |

### Auto-Track tab ↔ `AutoTrack` + partial Jellyfin

| JSON path | UI tab | Written by |
|-----------|--------|------------|
| `AutoTrack.Enabled` | Auto-Track | `ApplyAutoTrackSettings()` |
| `AutoTrack.EnforceGlobalWeeklySchedule` | Auto-Track | same |
| `AutoTrack.AnchorDayOfWeek`, `AnchorTimeLocal` | Auto-Track | same |
| `AutoTrack.TmdbCheckIntervalMinutes` | Auto-Track | same |
| `AutoTrack.TorrentHuntIntervalMinutes` | **NO UI** | same (legacy IntervalHours migrated) |
| `AutoTrack.HuntMinHoursAfterAirDate` | Auto-Track | same |
| `AutoTrack.ReconcileIntervalMinutes` | Auto-Track | same |
| `AutoTrack.MaxTmdbRefreshesPerDay` | Auto-Track | same |
| `AutoTrack.Quality.*` | Auto-Track | same |
| `AutoTrack.Search.*` | Auto-Track (partial) | same — `MaxParallelWorkersPerShow` **no UI** |
| `AutoTrack.Jellyfin.Enabled` | Auto-Track | same |
| `AutoTrack.Jellyfin.WarpHoldSecondsAfterNotify` | Auto-Track | same |
| `AutoTrack.Jellyfin.EnableLogEarlyDisconnect` | Auto-Track | same |
| `AutoTrack.Jellyfin.LogPath` | Auto-Track | same |
| `AutoTrack.Jellyfin.LogQuietSecondsAfterRefresh` | Auto-Track | same |
| `AutoTrack.Jellyfin.BaseUrl` | **Integrations** | same |
| `AutoTrack.Jellyfin.ApiKey` | **Integrations** | same |
| `AutoTrack.Jellyfin.ConfirmCloseViewer` | **Integrations** | same |
| `AutoTrack.Jellyfin.AutoCloseViewerOnBackground` | **Integrations** | same |
| `AutoTrack.LastRunUtc`, `LastRunSummary` | — | `AutoTrackService` |
| `AutoTrack.DailyBudget` | — | `AutoTrackService` |

### Integrations tab ↔ 4 JSON roots

| JSON path | Bound in Integrations UI | Apply method |
|-----------|--------------------------|--------------|
| `TmdbReadAccessToken` | TMDB card | `Save()` direct |
| `Gemini.*` | Gemini card | `ApplyGeminiSettings()` |
| `AutoTorrent.QbittorrentWebUiUrl`, `Username`, `Password` | qBittorrent card | `ApplyAutoTorrentSettings()` |
| `AutoTorrent.ConfirmCloseViewer`, `AutoCloseViewerOnBackground` | qBittorrent card | same |
| `AutoTorrent.ProcessRestart.*` | qBittorrent card | same |
| `AutoTrack.Jellyfin.BaseUrl`, `ApiKey`, viewer flags | Jellyfin card | `ApplyAutoTrackSettings()` |
| `Warp.*` | WARP card | `ApplyWarpSettings()` |

### Torrent Storage tab ↔ `AutoTorrent` (same object as qBit)

| JSON path | UI tab | Apply method |
|-----------|--------|--------------|
| `AutoTorrent.DownloadFolder` | Torrent Storage | `ApplyAutoTorrentSettings()` |
| `AutoTorrent.DownloadFolders[]` | Torrent Storage | same |
| `AutoTorrent.TvShowCategoryName` | Torrent Storage | same |
| `AutoTorrent.MovieCategoryName` | Torrent Storage | same |
| `AutoTorrent.AutoLinkCompletedDownloads` | Torrent Storage | same |

**Hidden `AutoTorrent` fields** (no Settings UI, used at runtime):

- `MaxCandidatesPerFetch`, `MaxParallelSearches`, snapshot timeouts, dedupe flags, `UseShowSnapshotSearch`, etc.
- Consumed via `RecipeRuntimeSettings` / torrent search pipeline

### Notifications tab

| JSON path | UI | Apply |
|-----------|-----|-------|
| `Notifications.EnabledByKind` | preference toggles | `ApplyNotificationSettings()` |

Test notification fields (`NotificationTestTitle`, etc.) are **session-only** — not in JSON.

### Backup tab

| JSON path | UI | Apply / other |
|-----------|-----|---------------|
| `Backup.Enabled`, schedule/throttle/retention | Backup | `ApplyBackupSettings()` |
| `Backup.CredentialsFilePath` | Backup | same (null = default path) |
| `Backup.MachineId` | display only | generated in `EnsureDefaults` |
| `Backup.Drive*FolderId`, `LatestFileId` | — | `BackupService` after upload |
| `Backup.LastBackupUtc`, `LastBackupSucceeded`, `LastBackupError` | label | `BackupService` |

### UiSettings — split brain

| JSON path | Settings UI | Other writers |
|-----------|---------------|---------------|
| `Ui.Theme` | System tab Save | `App.xaml.cs` read at startup |
| `Ui.Library*` (sort, filter, selection) | **None** | `LibraryViewModel.PersistLibraryUiState()` |
| `Ui.Torrent*` | **None** | `TorrentWorkspaceViewModel` |
| `Ui.NewsEpisodeSortMode`, `NewsTrackedShowViewMode` | **None** | `NewsViewModel` |

**Critical:** Any of these VMs calling `Save()` rewrites the **whole** file.

## Normalization & migration (SettingsService.EnsureDefaults)

Runs on **every** Load and Save:

| Area | Behavior |
|------|----------|
| `AutoTrack.IntervalHours` | Legacy → `TorrentHuntIntervalMinutes` if unset |
| `AutoTrack.LastTmdbRefreshDayKey` | Legacy → `DailyBudget` |
| `AutoTorrent.CategoryName` | Legacy → TV/Movie category names |
| `Backup.MachineId` | Generate GUID once if empty |
| `LibraryRootMode` | **Forced** to `AutoPerDrive` |
| Numeric fields | Clamped (logs, warp timeout, backup hours, process restart, etc.) |

Startup-only (App.xaml.cs): Gemini model + fallback normalize from catalog — **not saved** until user Save or another writer.

## Who else touches Current (outside Settings page)

| Component | Writes | Saves disk? |
|-----------|--------|-------------|
| `AutoTrackService` | `AutoTrack.DailyBudget`, last run metadata | Yes |
| `BackupService` | Drive folder IDs, last backup status | Yes |
| `LibraryViewModel` | `Ui.Library*` | Yes |
| `TorrentWorkspaceViewModel` | `Ui.Torrent*` | Yes |
| `NewsViewModel` | `Ui.News*` | Yes |
| `App.xaml.cs` | `Gemini.Model`, fallbacks | No (until next Save) |
| `SettingsViewModel` preview/WARP handlers | various | No (until Save) |

## Implication for modernization

A **section sub-VM** aligned to UI tabs is **not** the same as a section aligned to **JSON aggregates**. Choose one:

1. **Realign UI tabs** to JSON roots (move qBit UI to Torrent Storage, Jellyfin creds to Auto-Track, WARP to Auto-Track), **or**
2. **Keep UI layout** but introduce **aggregate facades** (`AutoTorrentSection`, `JellyfinSection` spanning tabs), **or**
3. **Split JSON** into multiple files (bigger migration — see [06-modernization-options.md](./06-modernization-options.md)).
