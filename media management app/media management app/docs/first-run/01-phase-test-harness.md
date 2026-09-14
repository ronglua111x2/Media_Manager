# 01 — Phase 1: no-VM test harness

**Status:** Implemented.

Lets this PC pretend to be a new install **without renaming** `D:\MediaManagerState` and without a VM.

## CLI

Parsed by [`AppLaunchOptions.TryParse`](../../Common/AppLaunchOptions.cs) **before** `SettingsService.Load()`.

| Flag | Effect |
|------|--------|
| `--state-folder <path>` | Bootstrap and **pin** the state folder for the whole process. Never create or read `D:\MediaManagerState` this session. **Does not read or write** `%LocalAppData%\MediaManager\state-path.txt` (must not point the live app at the test folder). `settings.json` `StateFolder` inside a copied file is ignored. |
| `--force-first-run` | Show [`FirstRunWindow`](../../Views/FirstRunWindow.xaml). On a new/`SetupCompleted == false` folder this is the **gate**. After Continue, the flag reopens a forced review (Close returns to the shell). |
| `--enable-background` | With `--state-folder`, start Auto-Track, Backup, symlink, poster warmup like production. **Default is off.** |

Unknown args (for example toast activation) are ignored. Missing path after `--state-folder` shows a dialog and exits.

Also accepted: `--state-folder=C:\path`.

Safe test mode = `--state-folder` is set and `--enable-background` is **not** set.

## Settings pin

[`ISettingsService.Load(string? bootstrapStateFolder)`](../../Services/ISettingsService.cs):

1. Resolve the bootstrap path to full path and pin it.
2. Create that folder; read or create `settings.json` **there**.
3. After deserialize, force `Current.StateFolder` back to the pin so a live copy cannot redirect to `D:\` (or to the LocalAppData default).
4. `Save()` writes the pin, not `AppConstants.DefaultStateFolder`, and **does not** update the pointer file.

A second `Load()` from Settings navigate keeps the pin.

## Background skip (safe test mode)

[`App.OnStartup`](../../App.xaml.cs) does **not** start:

- `IAutoTrackSchedulerService`
- `IBackupSchedulerService`
- `ISymlinkCoordinatorService`
- poster warmup

Log line: `Safe test mode: Auto-Track, Backup, symlink sync, and poster warmup were not started.`

Still runs: settings, DB migrate, log cleanup, notifications. Gated first-run does **not** show MainWindow until Continue. Mutex is unchanged — **quit the live app first**.

## Script

Creates the folder if missing and launches Release x64 with `--state-folder` and `--force-first-run`. Refuses to start if Media Manager is already running.

**Command Prompt (cmd.exe)** — you are on `C:\`, so you must `cd /d` to switch drive. Run **two** lines; do not glue `cd` and the script:

```bat
cd /d "d:\VScode\Misc\Media_Manager\media management app\media management app"
scripts\run-test-state.cmd
```

Optional test folder: `scripts\run-test-state.cmd D:\MediaManagerState_test`

Restore apply (empty target, not live `D:\`):

```bat
scripts\run-test-state.cmd D:\MediaManagerState_restore_test
```

Fixture zip from a **copy** of live state (not `D:\MediaManagerState` while the app is running):

```bat
scripts\make-test-restore-zip.cmd D:\MediaManagerState_LIVE_COPY
```

Default output: `D:\MediaManagerRestoreFixture\from-copy.zip`. A Drive history zip (secrets already redacted) can be copied next to that as `backup.zip` for the no-secrets prompt path.

**Git Bash** only:

```bash
cd "/d/VScode/Misc/Media_Manager/media management app/media management app"
./scripts/run-test-state.sh
```

`.sh` does not run in cmd.exe. The error `The directory name is invalid` happens if `cd` and `./scripts/...` are typed as one path.

## No-VM test procedure (manual)

Do **not** rename or swap `D:\MediaManagerState`.

### Once

1. Copy `D:\MediaManagerState` → `D:\MediaManagerState_LIVE_COPY` and do not use that copy as the running folder.

### Empty first-run (every time you test harness)

1. **Quit** Media Manager (tray too).
2. `mkdir` `D:\MediaManagerState_test` if needed (or let the script create it).
3. Launch (script or):

   `"…\bin\x64\Release\net8.0-windows10.0.17763.0\win-x64\media management app.exe" --state-folder "D:\MediaManagerState_test" --force-first-run`

4. Confirm:
   - **Host setup** wizard is the first surface (Start → Fresh → pinned State folder with no Browse; then Folders, Windows, WARP, qBit, TMDB). Rail groups: Start, This PC, Apps, Library
   - Folders Next stays off until both paths exist. qBit/TMDB can Skip without Test. Empty token is not an error until Test.
   - Console/system log lines start with `Host scan:` (scan is not a page)
   - `D:\MediaManagerState_test\settings.json` and `media-manager.db` exist
   - logs mention `--state-folder`, safe test mode, setup not completed / gated, and `Showing first-run scan window`
   - **no** new writes under `D:\MediaManagerState`
   - `%LocalAppData%\MediaManager\state-path.txt` is **unchanged** (pin must not rewrite the pointer)
5. Walk This PC must-steps, Skip Apps if needed (or Test when the network allows), **Finish** (expect a restart into the test folder, without `--force-first-run`). Settings → State Folder still `D:\MediaManagerState_test`. Run the script again to reopen Host setup as a forced review; Close returns to the shell.
6. Close the test app.
7. Launch **with no args** and confirm the live library still opens — no Host setup window (legacy `settings.json` without `SetupCompleted` counts as already set up).

### Local Restore apply (pinned test)

Quit the live app first. Never set `--state-folder` to `D:\MediaManagerState`.

1. **Target:** empty folder `D:\MediaManagerState_restore_test` (or wipe `D:\MediaManagerState_test` settings/db).
2. **Source without secrets:** copy one Drive history zip from the Settings list (e.g. `2026-09-14_*.zip`) to `D:\MediaManagerRestoreFixture\backup.zip`. Prefill paths/URLs; secrets prompt does **not** appear.
3. **Source with secrets (so you can Test TMDB):** a **copy** of live state, not the live folder. If you already have `D:\MediaManagerState_LIVE_COPY`, Restore **Folder** that path, or `scripts\make-test-restore-zip.cmd D:\MediaManagerState_LIVE_COPY`.
4. Launch: `scripts\run-test-state.cmd D:\MediaManagerState_restore_test`.
5. Start → I have a backup → (pinned) state folder Next → Restore Browse the fixture → Next applies (busy + footer). State folder is read-only; Restore Browse is the **source**.
6. Confirm `media-manager.db` and `Recipes\` landed in the test folder, `restored-settings.review.json` exists, and live `D:\MediaManagerState` was not written. Add titles is skipped. WARP Test holds a lease; TMDB Test can run if you accepted secrets from a copied state zip/folder.

### Optional cloned UI (still safe-test)

Copy `LIVE_COPY` into `D:\MediaManagerState_test` then launch with `--state-folder` **without** `--enable-background`. Expect Library to look populated; Auto-Track/Backup/symlink must not start. Pin still prevents `StateFolder` in the copied JSON from sending IO back to `D:\`. Missing `SetupCompleted` on a copied live file is treated as already set up — use `--force-first-run` to see Host setup.

### Do not

- Run two instances (mutex).
- Pass `--enable-background` against a cloned live `settings.json` unless you intend real qBit/Drive/symlink side effects.
- Treat a no-arg live launch as first-run — existing live `settings.json` is not gated.

## Verify in logs

Settings bootstrap (`settingslog`): `InitialStateFolder` is the test path.

System log: `State folder bootstrap source=cli-pin`; pinned `--state-folder` line (pointer not read/written); `--force-first-run` / showing scan window; `Host scan:` rows; safe test mode line.
