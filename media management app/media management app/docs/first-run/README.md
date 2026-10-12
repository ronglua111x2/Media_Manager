# First-run — host setup wizard

**Status:** Phases 1–4 implemented (test harness, host scan, setup gate, sequential wizard with local Restore apply).  
**Trigger:** `--state-folder` + `--force-first-run` (scripts), or a new/`SetupCompleted == false` settings file. Live launches with an existing `settings.json` that has no `SetupCompleted` key are treated as **already set up**.

Media Manager is a self-contained exe with no installer. Persistent data lives in a **state folder**. New installs default to `%LocalAppData%\MediaManager\State`; this PC’s live library stays at `D:\MediaManagerState` via `%LocalAppData%\MediaManager\state-path.txt`. First-run must not depend on `D:\` existing, must not start Auto-Track/Backup/symlink against empty or cloned data, and must let the user **browse every path**.

[`FirstRunWindow`](../../Views/FirstRunWindow.xaml) is a sequential wizard with a grouped rail (Start, This PC, Apps, Library). On a gated first-run, Finish restarts into the app; X quits. On `--force-first-run` after setup, Close returns to the shell.

## Documents (read in order)

| # | File | Contents |
|---|------|----------|
| 0 | [00-executive-summary.md](./00-executive-summary.md) | Why first-run; installed vs running vs usable; phase map |
| 1 | [01-phase-test-harness.md](./01-phase-test-harness.md) | **Phase 1 (shipped)** — CLI, pinned state folder, safe-test, no-VM procedure |
| 2 | [02-phase-host-scanner.md](./02-phase-host-scanner.md) | **Phase 2 (shipped)** — host scan, catalog, versions |
| 3 | [03-phase-first-run-gate.md](./03-phase-first-run-gate.md) | **Phase 3 (shipped)** — `SetupCompleted`, wizard gate, skip background jobs |
| 4 | [04-phase-guided-setup.md](./04-phase-guided-setup.md) | **Phase 4 (shipped)** — env-first shell; local Restore apply; WARP hold for TMDB Test |

When a later phase ships, change its **Status** line in that file and this README.

## Related

- Launch flags: [`Common/AppLaunchOptions.cs`](../../Common/AppLaunchOptions.cs)
- Scan: [`Services/HostScanService.cs`](../../Services/HostScanService.cs), [`Common/HostSoftwareCatalog.cs`](../../Common/HostSoftwareCatalog.cs)
- Window: [`Views/FirstRunWindow.xaml`](../../Views/FirstRunWindow.xaml)
- Startup: [`App.xaml.cs`](../../App.xaml.cs)
- Settings load: [`Services/SettingsService.cs`](../../Services/SettingsService.cs)
- State folder layout: [STATE_FOLDER.md](../STATE_FOLDER.md)
- qBittorrent 5.1 vs 5.2: [qbittorrent-webapi/](../qbittorrent-webapi/)
- Test scripts: [`scripts/run-test-state.cmd`](../../scripts/run-test-state.cmd) (cmd.exe) · [`scripts/run-test-state.sh`](../../scripts/run-test-state.sh) (Git Bash) · [`scripts/make-test-restore-zip.cmd`](../../scripts/make-test-restore-zip.cmd)

## Out of scope (this folder)

- Google Drive restore on a new `MachineId` (backup chicken-egg)
- Inno/MSIX installer
- VM / Windows Sandbox as the required test path
