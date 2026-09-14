# 02 — Phase 2: host scanner

**Status:** Implemented.

[`HostScanService`](../../Services/HostScanService.cs) + [`HostSoftwareCatalog`](../../Common/HostSoftwareCatalog.cs) fill [`FirstRunWindow`](../../Views/FirstRunWindow.xaml). Phase 3 uses the same window as the setup **gate** (`SetupCompleted`); restore can reuse it later.

Live launches **without** `--force-first-run` do not show the window when `SetupCompleted` is true or missing (legacy).

## UI

- Title: **Host setup**
- Scan cards: title, one next-step line, one status pill (OK / Not configured / Warn / Error)
- **Paths** list: text + Browse (state folder read-only when `--state-folder` is pinned)
- Buttons: **Rescan**, **Continue** (primary). **Close** only on forced review after setup is done
- Continue writes paths into the current/pinned `settings.json` and sets `SetupCompleted = true`, then restarts
- Gated X quits without completing setup

Empty `--state-folder` test: TMDB, Gemini, Drive **usable = Not configured**. qBittorrent may still show installed/running from this PC; WebUI stays Not configured until credentials exist in the *test* settings.

Optional WARP/Jellyfin/Gemini/Drive never block Continue.

## Probe order (per app)

1. Session Browse path
2. Catalog default path
3. Windows uninstall registry / `FileVersionInfo` on the exe
4. Process name
5. Localhost TCP port (optional)

Reuse existing Test APIs. Do not add a second HTTP stack. Do not start qBittorrent.

| Integration | Installed / running | Usable |
|-------------|---------------------|--------|
| qBittorrent | exe + process `qbittorrent` + WebUI port | [`QbittorrentClient.TestConnectionAsync`](../../Services/QbittorrentClient.cs) only if creds exist |
| WARP | `File.Exists` + `warp-cli --version` | CLI present |
| Jellyfin | optional process / port 8096 | [`JellyfinClient.TestConnectionAsync`](../../Services/JellyfinClient.cs) only if API key exists |
| WebView2 | `GetAvailableBrowserVersionString` | runtime present |
| TMDB / Gemini / Drive | not installed apps | token / silent Drive connect if secrets exist |

Log: one `Host scan:` line per row (`LogTarget.All`).

## Version catalog (`HostSoftwareCatalog`)

| Component | Required / supported | How to read |
|-----------|----------------------|-------------|
| Windows | 10 1809+ (`10.0.17763`) | `Environment.OSVersion` |
| This app | self-contained `win-x64` | `FileVersionInfo` on the exe |
| WebView2 | Evergreen present | runtime string — no pin to a build |
| qBittorrent | **5.1 and 5.2+** | exe version; WebUI version after login. Below 5.1 = warn. See [qbittorrent-webapi](../qbittorrent-webapi/). |
| WARP | any `warp-cli --version` | browsable path |
| Jellyfin | no local min version | `System/Info` after API key; remote is OK |
| TMDB | API v3 bearer | `GET /3/authentication` |

## Default probe paths (overridable)

- qBittorrent: `C:\Program Files\qBittorrent\qbittorrent.exe`
- WARP: `C:\Program Files\Cloudflare\Cloudflare WARP\warp-cli.exe`
- Jellyfin: `http://127.0.0.1:8096`
- qBittorrent WebUI: `http://127.0.0.1:8080` (WebUI is off by default in qBit)

## Code

- [`Services/IHostScanService.cs`](../../Services/IHostScanService.cs)
- [`ViewModels/FirstRunViewModel.cs`](../../ViewModels/FirstRunViewModel.cs)
- [`App.OnStartup`](../../App.xaml.cs) shows the window gated (no MainWindow) or as a forced review overlay when `ForceFirstRun` and setup is already complete
