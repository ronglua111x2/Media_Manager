# Split Boundaries — Recommendation (post-modernization)

**Goal:** Define VM boundaries that match **persistence and Apply** — not just XAML `#region` comments.

## Principle

> **One section VM owns one `Apply*` slice of `AppSettings` and all UI that edits that slice.**

If UI stays in a different tab for UX reasons, the **VM still follows JSON/domain**, not the tab label.

---

## Recommended section map (7 → 7, realigned)

| Section VM | Host property | JSON roots | UI location (after realign) |
|------------|---------------|------------|----------------------------|
| `SystemSettingsSectionViewModel` | `SystemSection` | `StateFolder`, `Logs`, `Startup`, `Ui.Theme` | System tab (unchanged) |
| `LibrarySettingsSectionViewModel` | `LibrarySection` | `SourceFolders`, `DefaultLibraryFolderName`, `Symlink` | Library tab (unchanged) |
| `AutoTrackSettingsSectionViewModel` | `AutoTrackSection` | `AutoTrack` (full), `Warp` | Auto-Track tab + **move WARP + Jellyfin creds here from Integrations** |
| `IntegrationsSettingsSectionViewModel` | `IntegrationsSection` | `TmdbReadAccessToken`, `Gemini` | Integrations tab (**TMDB + Gemini only**) |
| `TorrentSettingsSectionViewModel` | `TorrentSection` | `AutoTorrent` (full) | Torrent Storage tab + **move qBittorrent card here from Integrations** |
| `NotificationsSettingsSectionViewModel` | `NotificationsSection` | `Notifications` | Notifications tab (unchanged) |
| `BackupSettingsSectionViewModel` | `BackupSection` | `Backup` | Backup tab (unchanged) |

### Why merge WARP into Auto-Track VM

- `Warp.AutoRecoverOnSsl` — Auto-Track TMDB only (documented in model)
- Hunt gate reads WARP before fetch
- Status pill disconnect confirm is Auto-Track pipeline lease

WARP is not a general "integration" like TMDB — it's **Auto-Track infrastructure**.

### Why merge full Jellyfin into Auto-Track VM

Single object `AutoTrack.Jellyfin` — connection + refresh + viewer flags. Splitting across VMs guarantees Save order bugs unless coordinator merges partial applies.

### Why merge full AutoTorrent into Torrent VM

Single object `AutoTorrent` — Web UI + download folders + process restart. qBittorrent card in Integrations tab is historical placement.

---

## Alternative: keep current UI layout, facade VMs

If moving XAML cards is too disruptive for users:

| Facade VM | Owns Apply for | Binds in UI tab |
|-----------|----------------|-----------------|
| `IntegrationsFacade` | TMDB, Gemini, **partial** AutoTorrent (connection only), **partial** Jellyfin (creds), Warp | Integrations |
| `TorrentFacade` | AutoTorrent storage fields | Torrent Storage |
| `AutoTrackFacade` | AutoTrack minus Jellyfin creds + Jellyfin refresh | Auto-Track |

Host `Save()` calls facades in **fixed order** with **merge rules** into shared JSON objects.

**Downside:** More complex than realigning UI; every Save is a merge conflict waiting to happen.

**Upside:** No visual tab changes for users.

---

## Host coordinator responsibilities (unchanged by split)

`SettingsViewModel` (or renamed `SettingsWorkspaceViewModel`) keeps:

- `SelectedSettingsSection`
- `StatusMessage` + `ReportStatus` callback to sections
- `SaveCommand` orchestration
- `LoadFromSettings()` / `OnNavigatedTo()`
- `OnSelectedSettingsSectionChanged` delegation
- Post-save: startup registry, theme, DB init, tray

Target size after full split: **~250–400 lines** (Sprint 6 DoD).

---

## Workspace type naming (fix in Sprint 5 resume)

| Today | Rename to (suggested) |
|-------|----------------------|
| `SystemSettingsViewModel` | `SettingsWorkspaceViewModel` |
| `SystemSettingsView` | `SettingsWorkspaceView` (optional) |

Keep `SystemSettingsSectionViewModel` for System **tab** only.

DI / ViewTemplates must update if renamed — defer until modernization plan approved.

---

## Sprint 5 revised scope (when resumed)

**Phase A — safe extracts (no Integrations):**

1. `SystemSettingsSectionViewModel` + panel
2. `BackupSettingsSectionViewModel` + panel
3. Host wiring + DI

**Phase B — UI realign or facades (required before Integrations extract):**

4. Move qBittorrent + Jellyfin connection + WARP XAML OR implement facades
5. Split `ApplyAutoTorrentSettings` / `ApplyAutoTrackSettings`

**Phase C — remaining sections (old Sprint 6):**

6. Library, Notifications, Auto-Track, Torrent section VMs
7. Dirty tracking (three-state draft model)

---

## Properties that stay on host (not section VMs)

| Property | Reason |
|----------|--------|
| `SelectedSettingsSection` | Navigation |
| `StatusMessage` | Shared chrome |
| `LibraryRootPreview` | Could move to Library section — OK either way |
| Clear*Candidates commands | Database ops, could move to Torrent section |

---

## Interface sketch (for modernization sprint)

```csharp
public interface ISettingsSectionViewModel
{
    void LoadFrom(AppSettings current);
    void ApplyTo(AppSettings current);
    void OnSectionActivated(); // optional: Integrations/Backup refresh hooks
}

public interface ISettingsWorkspaceHost
{
    void ReportStatus(string message);
    AppSettings GetCurrentSnapshot(); // read-only for tests
}
```

Section VMs do **not** call `_settingsService.Save()` except Backup OAuth flows — host decides when to persist.

---

## Do NOT split yet (remain internal helpers)

- Token masking (`MaskSecret`, masked properties)
- `BrowseFolder` / file dialogs — shared static or `ISettingsDialogService`
- Gemini catalog reload helpers — stay with Integrations section

---

## Cross-reference: original Sprint 5 plan mistake

Original prompt assumed `{Binding Integrations.TmdbToken}` style paths for **Integrations panel including qBit/Jellyfin/WARP**.

This audit shows **Integrations panel ≠ Integrations domain**. Revised binding prefixes should follow **section VM names above**, not current XAML regions.
