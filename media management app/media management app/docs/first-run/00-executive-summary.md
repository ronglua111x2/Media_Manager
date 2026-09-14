# 00 — First-run executive summary

**Status:** Design record. Phases 1–4 are live.

## Why this exists

On a machine that only has the published exe:

1. There is **no installer**. Self-contained `win-x64` publish is the distribution.
2. [`SettingsService.Load()`](../../Services/SettingsService.cs) used to create `D:\MediaManagerState` immediately. That is **legacy detect only** now: new PCs use `%LocalAppData%\MediaManager\State`; this PC keeps `D:\` via `%LocalAppData%\MediaManager\state-path.txt`.
3. [`App.OnStartup`](../../App.xaml.cs) started Auto-Track, Google Drive backup, symlink sync, and poster warmup **before** the main window. Empty or cloned data can hunt on real qBittorrent, upload a test DB over Drive `latest.zip`, or rewrite `C:\JellyfinLibrary`.
4. Settings has seven tabs and Test buttons, but nothing **blocks** entry until TMDB + qBittorrent + folders exist.

Docs-only onboarding is not enough: restore and first launch happen when the user is not reading `STATE_FOLDER.md`.

## Three check layers (do not collapse)

| Layer | Meaning | Example |
|-------|---------|---------|
| **Installed** | File, service, or registry hit on this PC | `qbittorrent.exe` exists; `warp-cli.exe` exists |
| **Running** | Process or port is live | `qbittorrent` process; TCP 8080 |
| **Usable** | This app can authenticate and call the API | WebUI login; Jellyfin API key; TMDB bearer |

Admin elevation (app.manifest `requireAdministrator`) helps process/service inspection. It does **not** replace passwords, API keys, or “WebUI enabled”.

## Phase map

| Phase | Goal | Code |
|-------|------|------|
| **1** | Point the process at a disposable state folder; skip background jobs; test on this PC without a VM | Shipped — [01](./01-phase-test-harness.md) |
| **2** | Scan host software, versions, browsable paths; **Host setup** window | Shipped — [02](./02-phase-host-scanner.md) |
| **3** | Block the shell until setup is done (`SetupCompleted` / `--force-first-run`) | Shipped — [03](./03-phase-first-run-gate.md) |
| **4** | Guided forms: must / skip, reuse existing Test commands | Shipped — [04](./04-phase-guided-setup.md) |

## What later phases still do

- Google Drive restore on a new PC (`MachineId` / credentials)
- Production no-arg launch is **not** “always `D:\`”: pointer → legacy `D:\MediaManagerState\settings.json` → `%LocalAppData%\MediaManager\State`. Live this-PC library remains on `D:\` via the pointer.
- One instance only (`Local\MediaManager.SingleInstance`) — close the live app before a test launch
