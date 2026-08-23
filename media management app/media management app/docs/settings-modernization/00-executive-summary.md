# Executive Summary — Settings Audit

**Date:** Aug 2026  
**Session type:** Read-only code audit (Sprint 5 then paused; **E4 later frozen — Sprint 5/6 not resuming**)

## User observation (confirmed)

> ViewModel sections do not match UI sections, and this mismatch repeats everywhere.

This is **correct**. The 7 Settings tabs are a **navigation convenience**, not an ownership boundary in code or JSON.

## The core problem (one sentence)

**One monolithic ViewModel mirrors one monolithic JSON file, but UI tabs and `Apply*` methods slice that blob differently — so any section split without redesign will silently break bindings or clobber unrelated values on Save.**

## Top 10 findings

| # | Finding | Risk if ignored in Sprint 5/6 |
|---|---------|------------------------------|
| 1 | **Jellyfin** is split across **Integrations** (URL, API key, viewer) and **Auto-Track** (refresh, WARP hold, log tail) but stored in **`AutoTrack.Jellyfin`** | Moving Integrations first leaves Jellyfin half on host; Save order matters |
| 2 | **qBittorrent** UI is under **Integrations**; download folders under **Torrent Storage** — both use **`ApplyAutoTorrentSettings()`** on one `AutoTorrent` object | Cannot extract Integrations without splitting Apply or duplicating state |
| 3 | **WARP** UI is under **Integrations**; JSON root is **`Warp`**; runtime consumer is **Auto-Track hunt** | Wrong tab ownership; WARP path changes live-apply to disk model without Save |
| 4 | **`Save()` always applies all 7 sections** regardless of active tab | Unedited sections still written; no dirty tracking (Sprint 6) |
| 5 | **`UiSettings`** is edited by **4 different ViewModels** (Settings, Library, Torrent, News) | Last `Save()` wins for entire file; Settings split does not isolate UI state |
| 6 | **`RefreshLibraryRootPreview()` mutates `Current.DefaultLibraryFolderName`** while typing | In-memory model diverges from disk before Save |
| 7 | **`OnWarpExecutablePathChanged` calls `ApplyWarpSettings()`** on every keystroke | Silent in-memory persistence side effect |
| 8 | **Test buttons call `Apply*` without Save** — sometimes **`ApplyAutoTorrentSettings`** (includes Torrent Storage fields user did not touch) | Test qBit can overwrite unsaved download-folder edits in memory |
| 9 | **Orphan JSON sections** have no Settings UI: `TorrentValidation`, `DriveLibraryRoots`, advanced `AutoTorrent` search tuning | Split docs must not assume "everything is in Settings VM" |
| 10 | **`SystemSettingsViewModel`** is the **workspace** type; name collides with **System** settings tab | Naming trap for `{Binding System.*}` vs `SystemSettingsViewModel` |

## What works today (do not break accidentally)

- Single `settings.json` + atomic tmp write in `SettingsService.Save()`
- Normalization/migration in `EnsureDefaults()` on every load/save (legacy AutoTrack, AutoTorrent categories, Backup MachineId)
- Explicit global Save button — user expects one file, one click
- Backup Connect/BackupNow intentionally partial-save before OAuth (documented behavior)

## Recommended sequence (historical — not a resume trigger)

User chose **not** to pick a full modernization track. Settings 7-VM split is **cancelled**. Do not start this sequence. Optional Sprint 4.5 hygiene is a separate, optional later choice — not Sprint 5.

1. **Decide modernization track** — see [06-modernization-options.md](./06-modernization-options.md) (never chosen).
2. **Fix ownership map** — realign UI tabs with JSON roots OR accept cross-tab aggregates and document shared Apply facades ([05-split-boundaries-recommendation.md](./05-split-boundaries-recommendation.md)).
3. **Stop silent in-memory writes** — preview paths and WARP path should not mutate `Current` until Save (small pre-split hygiene).
4. **Introduce section Apply interfaces** — even before sub-VMs: `ISystemSettingsSection.ApplyTo(AppSettings)` etc.
5. ~~Resume Sprint 5 with System + Backup only~~ **Cancelled.**

## Sprint status

| Sprint | Status | Notes |
|--------|--------|-------|
| Sprint 5 (Settings split 1/2) | **Frozen / cancelled** | User did not pick a modernization track; 7-VM split cancelled with E4 |
| Sprint 6 (Settings split 2/2 + dirty) | **Frozen / cancelled** | Depends on Sprint 5 — will not run |

## Files to read first in codebase

```
Services/SettingsService.cs          — load/save/normalize
Models/AppSettings.cs                — JSON root
ViewModels/SettingsViewModel.cs      — all Apply*, Save, LoadFromSettings
Views/SettingsView.xaml              — 7 panels, binding paths
App.xaml.cs                          — startup Gemini normalize, DI
```
