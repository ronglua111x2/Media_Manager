# 04 — Phase 4: guided setup

**Status:** Implemented (env-first shell: Start / This PC / Apps / Library). Local Restore **applies** a zip or copied folder. WARP Test **holds a lease** until Finish so TMDB Test can use the tunnel.

Sequential wizard inside [`FirstRunWindow`](../../Views/FirstRunWindow.xaml). One step at a time, grouped rail. Host scan runs in the background and only writes `Host scan:` log lines; it is not a page. Scan `Not configured in this folder.` is not shown as the step subtitle. Empty TMDB/Gemini errors appear only after **Test**.

`--state-folder` still **shows** the state-folder step (path read-only, Browse off). Skip on Jellyfin also skips the symlink step and sets `Symlink.Enabled = false`. Restore **does not** call [`RestoreAsync(driveFileId)`](../../Services/Backup/IBackupService.cs). It calls [`ApplyLocalBackup`](../../Services/Backup/IBackupService.cs) (shared with Settings Drive restore after download).

## Step order

| # | Group | Step | Why | Next |
|---|-------|------|-----|------|
| 1 | Start | How you start | Fresh vs I have a backup | Must pick one (default Fresh) |
| 2 | This PC | State folder | Library database, settings, posters | Must: writable, not under the exe (pinned launch: already OK). **Before** Restore so apply has a target. |
| 3 | This PC | Backup location | Local zip or copied state folder | Only if Restore. Next **applies** (busy + footer). **Skip** = Fresh path (shows Add titles) |
| 4 | This PC | Folders | Downloads and library sources | Must: both exist. Prefill from backup when Restore applied. Use as source. [`GetPreviewRoots`](../../Services/LibraryPathResolver.cs) |
| 5 | This PC | Windows | Theme, run at startup, close to tray | Skip. Prefill from backup when Restore applied. |
| 6 | Apps | WARP | Helps TMDB/Jellyfin on some networks | Skip. **Test** `AcquireAsync(FirstRun)` and **holds** until Finish/Close (does not disconnect after Test). |
| 7 | Apps | qBittorrent | Add torrents and hunt | Skip. Test warns if no search plugins |
| 8 | Apps | TMDB | Search and track shows and movies | Skip. Test OK (`GET /3/authentication`). If `SendAsync` fails and WARP is available but not leased: one `AcquireAsync(TmdbSslRecover)` then retry; keep the lease. Network failure stays in the footer (no crash) |
| 9 | Apps | Jellyfin | Open in Jellyfin / library refresh | Skip |
| 10 | Apps | Symlink root | One folder of links for Jellyfin | Skip / Jellyfin skipped → symlink off |
| 11 | Apps | Gemini | Maps messy specials | Skip |
| 12 | Apps | Google Drive | Backup zip of this machine | Skip |
| 13 | Library | Add titles | TMDB search + add (default numbering, no org dialog) | Fresh path only (skipped after a successful Restore apply). Skip |
| 14 | Library | Auto-Track | Hunt on a schedule. Default **off** | Skip |

Must steps are **This PC** only (state folder + folders). Installed ≠ usable: seeing `qbittorrent.exe` does not mean Test succeeded.

Finish = Phase 3 Continue: `SetupCompleted = true`, `Save()` to the current/pinned folder, release first-run WARP leases, restart without `--force-first-run`.

If TMDB, qBittorrent, or other app steps were skipped (or never Tested), News shows a dismissible “Some setup was skipped — open Settings for TMDB, qBittorrent, and other apps.” (`Startup.SetupReminderDismissed`). Legacy JSON without that property does not show the banner.

Do **not** put on first-run: log retention, notification catalog, recipe editor, torrent timeouts, Gemini fallback chain, backup schedule. Default recipes still appear when the state folder is used.

## Restore apply

Target is `Current.StateFolder` (the pin when `--state-folder`). Pinned tests **cannot** apply into live `D:\MediaManagerState`.

Copies `media-manager.db` + `Recipes/` and writes `restored-settings.review.json`. Does **not** overwrite live `settings.json`. Prefills download/source folders, qBit URL, Jellyfin URL, symlink root, WARP path, theme, Windows startup. Secrets (TMDB token, Gemini key, qBit user/password/API key, Jellyfin API key) stay empty unless the source still has them **and** the user answers Yes to *This backup includes saved credentials. Use them in Host setup so you can Test now?* Drive zips are redacted — no prompt.

## Copy rules

- Do not silent-install qBit/WARP/Jellyfin.
- Do not read WebUI passwords off disk except the optional Restore secrets prompt.
- Google Drive restore on a new `MachineId` stays later.
