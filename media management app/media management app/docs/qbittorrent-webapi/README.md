# qBittorrent WebAPI — 5.1 vs 5.2 compatibility

**Status:** Client upgrade implemented (5.1 + 5.2 login/add, optional API key). These files remain the architecture record.  
**Trigger:** After updating qBittorrent to **5.2+** (settings kept), Media Manager could not connect even though WebUI opened on localhost without a login prompt.

This folder is the **record** of how qBittorrent WebAPI changed, what Media Manager calls, and an upgrade outline. Implementation lives in `QbittorrentClient` (5.2 login/add + optional Bearer API key).

## Why this exists

[`Services/QbittorrentClient.cs`](../../Services/QbittorrentClient.cs) was written for the **≤ 5.1** cookie-login style (`200` + body `"Ok."` / `"Fails."`). qBittorrent **5.2** (WebAPI **2.14+**) keeps the same `/api/v2/...` tree but changes **login status codes**, **empty-body responses**, **`torrents/add` JSON**, and adds optional **API key** auth.

Downgrade to 5.1.x restores the old contract. Staying on 5.2+ requires a client upgrade.

## Documents (read in order)

| # | File | Contents |
|---|------|----------|
| 0 | [00-executive-summary.md](./00-executive-summary.md) | Why connection fails; 5.2 is a real HTTP/auth upgrade, not a restyle |
| 1 | [01-api-architecture-5.1-vs-5.2.md](./01-api-architecture-5.1-vs-5.2.md) | Two architectures: RPC-over-HTTP vs status codes + JSON add + Bearer |
| 2 | [02-app-endpoint-inventory.md](./02-app-endpoint-inventory.md) | Every `/api/v2` call in `QbittorrentClient` and who consumes it |
| 3 | [03-breaking-changes.md](./03-breaking-changes.md) | What blocks today vs what is leftover from 5.0 vs unused |
| 4 | [04-upgrade-plan.md](./04-upgrade-plan.md) | **Outline only** — phases for a later Plan Mode local plan |

## Related

- Client: [`Services/QbittorrentClient.cs`](../../Services/QbittorrentClient.cs), [`Services/IQbittorrentClient.cs`](../../Services/IQbittorrentClient.cs)
- Settings: [`MediaManager.Core/Models/AutoTorrentSettings.cs`](../../MediaManager.Core/Models/AutoTorrentSettings.cs)
- Probe / restart: [`Models/QbittorrentWebUiProbeResult.cs`](../../Models/QbittorrentWebUiProbeResult.cs), [`Services/QbittorrentProcessRestartService.cs`](../../Services/QbittorrentProcessRestartService.cs)
- States: [`Common/QbittorrentTorrentStateNormalizer.cs`](../../Common/QbittorrentTorrentStateNormalizer.cs)
- Upstream: [WebAPI Changelog (5.2.0)](https://github.com/qbittorrent/qBittorrent/blob/release-5.2.0/WebAPI_Changelog.md), [API key wiki (≥ 5.2.0)](https://github.com/qbittorrent/qBittorrent/wiki/API-Key-Authentication-(%E2%89%A5v5.2.0))

## Out of scope (this folder)

- No live curl verification in these files
- No Sprint timeline change
