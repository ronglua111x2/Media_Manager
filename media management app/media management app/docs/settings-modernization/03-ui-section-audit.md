# UI Section Audit — 7 Tabs vs ViewModel Reality

**Primary files:**
- [`Views/SettingsView.xaml`](../../Views/SettingsView.xaml) (~2142 lines)
- [`ViewModels/SettingsViewModel.cs`](../../ViewModels/SettingsViewModel.cs) (~2084 lines)

## Tab visibility mechanism

All seven panels are sibling `ScrollViewer`s in one `Grid`. Visibility toggled by styles with `DataTrigger` on `SelectedSettingsSection` (lines 226–280). **All panels stay in visual tree** — bindings remain active even when collapsed.

Tab order in selector (lines 324–344): System → Library → Auto-Track → Integrations → Torrent Storage → Notifications → Backup.

## Global chrome (host-only, not a tab)

| Binding | Purpose |
|---------|---------|
| `SelectedSettingsSection` | Tab switch |
| `StatusMessage` | Header + save bar feedback |
| `SaveCommand` | Global persist |

These must stay on a **host/coordinator** VM in any split.

---

## 1. System (lines 351–526)

**Clean boundary:** Yes — maps to `StateFolder`, `Logs`, `Startup`, `Ui.Theme`.

| Property | Command |
|----------|---------|
| `StateFolder` | `BrowseStateFolderCommand` |
| `LogMaxLinesPerFile`, `LogCleanupRetentionDays`, `LogAutoCloseConsoleOnBackground` | — |
| `RunAtStartup`, `StartMinimized`, `CloseToTray` | — |
| `SelectedTheme`, `ThemeOptions` | — |

**Post-save side effects (host):** Windows startup registry, `ThemeService.Apply`, tray init.

---

## 2. Library (lines 829–988)

**Clean boundary:** Mostly — `SourceFolders`, `Symlink`, library folder name.

| Property | Command |
|----------|---------|
| `SourceFolders`, `SelectedSourceFolder` | Add/Remove source folder |
| `DefaultLibraryFolderName`, `LibraryRootPreview` | — |
| `SymlinkEnabled`, `SymlinkSyncOnStartup`, `SymlinkUnifiedRoot` | Browse symlink root |
| `SymlinkPreviewShowsPath`, `SymlinkPreviewMoviesPath` | — |
| `AdministratorStatusLabel` | — (derived) |
| — | `SyncSymlinksNowCommand` (runtime action, not settings) |

**Bug-class behavior:** `RefreshLibraryRootPreview()` writes `Current.DefaultLibraryFolderName` while user types.

---

## 3. Auto-Track (lines 529–826)

**Split boundary:** No — shares `AutoTrack.Jellyfin` with Integrations; consumes `Warp` from Integrations UI.

**Bound here (behavior / schedule / quality):**

- All `AutoTrack*` schedule, quality, search limits
- Jellyfin **refresh** sub-card: `AutoTrackJellyfinRefreshEnabled`, WARP hold, log early-disconnect, log path, quiet seconds

**UI explicitly says:** Jellyfin URL/API key are under Integrations (line ~730 hint).

**Commands:**

- `BrowseJellyfinLogPathCommand`, `BrowseJellyfinLogFileCommand`
- `TestJellyfinLogPathCommand` → calls **`ApplyAutoTrackSettings()`** first
- `FlushJellyfinRefreshQueueCommand` → same

**Orphan VM properties (loaded/saved, no XAML):**

- `AutoTrackTorrentHuntIntervalMinutes`
- `AutoTrackMaxParallelWorkersPerShow`

---

## 4. Integrations (lines 991–1607) — **highest mismatch**

### Cards vs true domain

| UI card | VM property prefix | Actual JSON home | Should live in tab |
|---------|-------------------|------------------|-------------------|
| TMDB | `Tmdb*` | `TmdbReadAccessToken` | Integrations ✓ |
| Gemini AI | `Gemini*` | `Gemini` | Integrations ✓ |
| qBittorrent Web UI | `Qbittorrent*` | `AutoTorrent` | **Torrent Storage** |
| Jellyfin connection | `AutoTrackJellyfinBaseUrl`, `AutoTrackJellyfinApiKey`, viewer flags | `AutoTrack.Jellyfin` | **Auto-Track** (or dedicated Jellyfin) |
| Cloudflare WARP | `Warp*` | `Warp` | **Auto-Track** (hunt/SSL) |

### Test commands and their Apply side effects

| Command | Apply called | Also mutates |
|---------|--------------|--------------|
| `TestTmdbTokenCommand` | none (reads VM token only) | — |
| `TestGeminiCommand` | `ApplyGeminiSettings()` | Gemini |
| `RefreshGeminiModelsCommand` | `ApplyGeminiSettings()` | Gemini |
| `TestQbittorrentConnectionCommand` | **`ApplyAutoTorrentSettings()`** | **Entire AutoTorrent** incl. download folders |
| `TestJellyfinConnectionCommand` | **`ApplyAutoTrackSettings()`** | **Entire AutoTrack** incl. Jellyfin behavior fields |
| `TestWarpConnectionCommand` | `ApplyWarpSettings()` | Warp |

This is why Sprint 5 "Integrations sub-VM" cannot be a simple property move.

---

## 5. Torrent Storage (lines 1610–1749)

**Clean for storage fields:** download folder list, categories, auto-link.

| Property | Command |
|----------|---------|
| `AutoTorrentDownloadFolder` | Browse |
| `AutoTorrentDownloadFolders`, `SelectedAutoTorrentDownloadFolder` | Add/Remove |
| `AutoTorrentTvShowCategoryName`, `AutoTorrentMovieCategoryName` | — |
| `AutoLinkCompletedDownloads` | — |
| — | Clear*Candidates commands (**database**, not settings) |

**Coupling:** qBittorrent connection UI is **not here** but shares `ApplyAutoTorrentSettings()`.

---

## 6. Notifications (lines 1752–1925)

**Clean boundary:** `Notifications.EnabledByKind` + test UI (session-only strings).

Commands: test send, browse test images, brand logo presets.

---

## 7. Backup (lines 1928–2112)

**Clean boundary:** `Backup` root + Google Drive integration.

**Special save behavior:**

- `ConnectGoogleDriveCommand` → `ApplyBackupSettings()` + **`Save()`** (partial persist before OAuth)
- `BackupNowCommand` → same

These save the **whole** settings file, not just Backup section.

---

## Command inventory by logical owner (not UI tab)

Use this when splitting — **commands follow JSON/domain**, not XAML region:

| Logical section | Command count | Notes |
|-----------------|---------------|-------|
| Host | 1 | `SaveCommand` |
| System | 1 | Browse state folder |
| Library | 4 | incl. SyncSymlinksNow (runtime) |
| Auto-Track + Jellyfin behavior + WARP | 8 | Jellyfin log/flush + WARP browse/test |
| Integrations (TMDB+Gemini only) | 11 | Gemini chain management |
| Torrent (qBit + storage) | 6 | qBit tests + folder browse + DB clears |
| Notifications | 5 | |
| Backup | 5 | |

---

## Section-switch side effects

`OnSelectedSettingsSectionChanged`:

| Tab entered | Action |
|-------------|--------|
| Integrations | Reload Gemini catalog from disk; refresh WARP CLI status |
| Backup | Refresh Drive connection; async load backup history |

Split VMs must preserve these hooks on the host or delegate to section `OnActivated()`.

---

## XAML extraction note (Sprint 5 original plan)

Planned panels: `SystemSettingsPanel`, `IntegrationsSettingsPanel`, `BackupSettingsPanel`.

**Risk:** Copying XAML regions without fixing binding prefixes (`Integrations.QbittorrentWebUiUrl`) leaves **wrong mental model** — qBit would still look like "Integrations" in code but bind under Integrations path.

**Recommendation:** Either move qBit/Jellyfin/WARP blocks in XAML when modernizing, or name facades honestly (`TorrentIntegrationPanel`, `AutoTrackIntegrationPanel`).
