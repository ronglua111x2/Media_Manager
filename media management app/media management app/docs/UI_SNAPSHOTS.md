# UI snapshots (dev tooling)

One command renders every main screen to PNG at the 1280x720 design size, so UX reviews (and AI sessions) can see the UI without opening the app by hand.

```powershell
.\scripts\ui-snapshots.ps1
```

Output: `<repo root>\ui-snapshots\<yyyy-MM-dd_HHmm>_<shortsha>\` plus a mirror in `ui-snapshots\latest\`. The folder is git-ignored. **The images show real library titles: never commit, attach or upload them.** Hand-written notes such as `latest\clutter-inventory.md` survive the mirror.

## When to run

After any PR that changes UI: run the script and mention the snapshot folder in the PR report or hand-over. Compare against the previous run folder for before/after.

## What the script does

1. `dotnet build -c Debug`. The exe lands under `bin\...\net8.0-*`, the "dev build" path that `WindowsStartupService` never registers as the AutoStart task. The script refuses an exe anywhere else.
2. Refreshes a sandbox copy of the state folder (default `%LOCALAPPDATA%\MediaManagerUiSnapshot\state`). The live `<StateFolder>` is only read: an allow-list (`settings.json`, `media-manager.db` with `-wal`/`-shm`, Gemini/indexer JSON, `Recipes\`, `posters\`), opened with shared read access so the running app is never blocked. Logs, `GoogleDrive\` tokens, WebView2 data and old DB backups are not copied. The script refuses a sandbox path that overlaps the live folder, the LocalAppData default or the pointer target, and only wipes a folder that carries its `.ui-snapshot-sandbox` marker.
3. Starts the app with `--state-folder <sandbox> --ui-snapshots <run folder>`. The exe is `requireAdministrator`, so Windows may show a UAC prompt. The commit and settle time go through `snapshot-request.json` in the run folder, because an elevated child does not inherit the script's environment.
4. Mirrors the run folder to `latest\` and checks that it is git-ignored.

Options: `-SkipBuild`, `-NoRefresh` (reuse the sandbox), `-SettleMs 4000` (wait longer per screen), `-TimeoutSeconds`, `-SandboxState`, `-OutputRoot`, `-SourceState`.

## Snapshot mode in the app (`--ui-snapshots <folder>`)

Parsed by `AppLaunchOptions`. It requires `--state-folder` and refuses `--enable-background`. Startup (`App.OnStartup`):

- refuses the legacy live folder, the LocalAppData default and the pointer target (`UiSnapshotSandbox.GetStateFolderRefusal`), writing `error.txt` and exiting with code 3;
- takes its own mutex (`Local\MediaManager.UiSnapshot`), so it runs beside the live app;
- cuts off external services in the sandbox `settings.json` and saves it there (`UiSnapshotSandbox.CutOffExternalServices`): qBittorrent and Jellyfin point at an unreachable loopback port with no credentials, and WARP and the qBittorrent restart point at a missing exe. TMDB, Gemini and Drive credentials are cleared, and Auto-Track, Backup, symlink sync, autostart, tray and notifications are off. Saved list filters are cleared so lists show everything;
- skips the WARP mode restore, the unclean-shutdown report, toast registration and the first-run gate; safe test mode already skips the background jobs;
- shows `MainWindow` and runs `UiSnapshotRunner`.

`UiSnapshotRunner` visits each sidebar workspace, each System Settings section, the shell with the sidebar expanded and the Console window. It also captures Library with a show selected and Torrent with the fullest cart selected. Two "cart is running" states are staged in memory on that cart, with no database writes and no search or qBittorrent calls: Run Cart mid-search (earlier rows found candidates, row 3 searching, Stop active, shell progress pill) and Add mid-way to qBittorrent (status line "Adding 2/N", rows marked added). It also opens a few dialogs filled with placeholder data ("Show 01", "Movie 01"): add lock, episode organization, disk plan and the cart candidate list. Each screen gets time to load: a settle delay plus a wait while any `Is*Busy`/`IsLoading*` view-model flag is true. It is rendered with `RenderTargetBitmap` at 96 DPI, so one image pixel is one WPF unit whatever the display scale. It writes `NN-<name>.png` and `index.md` (date, commit, version, one line per image, failures), then exits: 0 when every capture succeeded, 1 when some failed, 2 on a startup error.

Status pills differ from the live app on purpose: qBittorrent, Jellyfin, WARP and Drive show offline or disabled.

## Adding a screen

Add a `CaptureStepAsync` call in `Services/UiSnapshot/UiSnapshotRunner.cs`. A new sidebar workspace is picked up automatically from `MainViewModel.NavigationItems`. A dialog needs a factory with placeholder data: never real titles.
