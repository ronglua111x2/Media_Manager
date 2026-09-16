# AUD-002 Cleanup Failure

## AUD-001 trade-off (not fixed)

Gated adds still send `Paused = false` and wait for the qBittorrent file list while the torrent is running (default 90 seconds, cap 120). Pause-on-add is out of scope because it previously caused add regressions (`search/downloadTorrent` cannot carry pause/stop).

A rejected payload can therefore start downloading before validation. AUD-002 is the remaining control: failed cleanup must not be reported as a successful malware reject.

## Call chain

```
Cart UI / Auto-Track
  -> ITorrentAddGateService.AddPausedValidateAndResumeAsync
      -> IQbittorrentClient.AddTorrentAsync (Paused=false)
      -> WaitForTorrentFilesAsync
      -> ValidateFilesAsync / infohash blacklist
      -> ITorrentCleanupService.DeleteTorrentAsync
          -> IQbittorrentClient.DeleteTorrentsAsync
          -> verify torrent is gone
```

Reachable add callers:

- `TorrentWorkspaceViewModel.AddOrderToClientAsync` (batch add and retry)
- `AutoTrackService.AddOrderToClientAsync` (hunt candidate retries)

`AutomationFlowService.RunNowAsync` still bypasses the gate and has no production caller.

## Before

- `DeleteTorrentsAsync` logged and swallowed non-2xx and transport errors.
- `DeleteTorrentAsync` returned `false` when the torrent was still listed.
- The gate ignored that Boolean, blacklisted, and threw `MaliciousTorrentException`.
- Cart and Auto-Track treated the candidate as cleaned up and continued.

## After

- Delete API failures throw.
- Cleanup retries deletion up to three times with a short delay, using a cleanup timeout that is not tied to user cancellation.
- Confirmed removal plus blacklist still throws `MaliciousTorrentException`.
- Unverified removal best-effort pauses/stops the torrent, then throws `TorrentCleanupFailedException` with the hash.
- Auto-Track and the current cart batch halt. They do not try the next candidate or order.

## Residual risk

- AUD-001 download window remains accepted.
- Pause after failed delete is best-effort; if qBittorrent is unreachable, the torrent may keep running until the user inspects it.
- Isolated live qBittorrent delete-failure injection was not run and must not use the live state folder.

## Verification

- `MediaManager.App.Tests`: 9 passed (Release/x64)
- `MediaManager.Core.Tests`: 207 passed (Release/x64)
- 64-bit MSBuild Release/x64 of `media management app.csproj`: succeeded
