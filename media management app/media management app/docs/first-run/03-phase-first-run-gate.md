# 03 — Phase 3: first-run gate

**Status:** Implemented.

[`Startup.SetupCompleted`](../../Models/AppStartupSettings.cs) (`bool?`) gates News. [`FirstRunWindow`](../../Views/FirstRunWindow.xaml) is the only surface until Finish on an incomplete folder (Phase 4 sequential wizard). `--force-first-run` still reopens the window after setup is done (overlay on MainWindow).

Do not use “`D:\` exists” as the signal.

## Load rules

[`SettingsService.Load`](../../Services/SettingsService.cs):

| `settings.json` | `SetupCompleted` | Result |
|-----------------|------------------|--------|
| Missing (created this launch) | written `false` | Gated first-run. `CreatedNewSettingsThisLoad` is true. |
| Exists, property missing/null | in-memory `true` (legacy) | Live library. **No Save** on load (avoids mtime jumps on `D:\MediaManagerState`). Persists on the next normal Save. |
| Exists, `false` / `true` | honored | Gated or shell. |

A plain `bool` defaulting to false would block every existing install. Null vs false is why the property is `bool?`.

## When the window shows

Show Host setup when **any**:

- settings file was just created
- `SetupCompleted != true`
- `--force-first-run`

**Gated** (`SetupCompleted != true`): do not show MainWindow. X / Alt+F4 confirms and **quits** without setting the flag. Continue writes paths + `SetupCompleted = true`, then **restarts** (keeps `--state-folder` / `--enable-background`, drops `--force-first-run`). Mutex is released before the new process starts.

**Forced review** (already complete + `--force-first-run`): MainWindow is shown; Close hides Host setup only.

## Background jobs

Start Auto-Track / Backup / symlink / poster warmup only when `!SafeTestMode && SetupCompleted == true`. Incomplete setup skips that list even with no `--state-folder`. `--enable-background` cannot start jobs until setup is completed.

## UI

Slim scan cards: title + one next-step line + one pill (OK / Not configured / Warn / Error). Browse on a card only if qBit/WARP is missing. Compact **Paths** list (text + Browse) for state folder (read-only when CLI-pinned), qBittorrent exe, WARP CLI, download folders, source folders, Jellyfin URL, symlink root. Continue persists those into the **current / pinned** folder — never live `D:\` during `run-test-state`.

Optional WARP / Jellyfin / Gemini / Drive never block Continue. TMDB/qBit credential tests and Continue-until-green are Phase 4.

One `Host scan:` log line per row still has the full probe detail.

## Shell

Theme already applied. Mutex unchanged. Laptops with no `D:` still fail if `Load()` cannot create the default state folder (not delayed this phase).
