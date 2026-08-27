# 02 — App endpoint inventory

All WebAPI usage is in [`Services/QbittorrentClient.cs`](../../Services/QbittorrentClient.cs) via [`IQbittorrentClient`](../../Services/IQbittorrentClient.cs). Settings: `AutoTorrent.QbittorrentWebUiUrl`, `Username`, `Password` only ([`AutoTorrentSettings`](../../MediaManager.Core/Models/AutoTorrentSettings.cs)). There is **no** qBittorrent API key field.

Success rules below are what the **current** code does, not what 5.2 returns.

## Auth and probe

| Method | Path | Success rule today | Callers |
|--------|------|--------------------|---------|
| `LoginAsync` | `POST api/v2/auth/login` | 2xx **and** body `"Ok."` | All session methods; `ProbeWebUiAsync(force: true)` |
| `ProbeWebUiAsync` | `GET api/v2/app/version` (pre-check + after login) | After forced login: 401/403 → AuthFailed; other non-2xx → AuthFailed; 2xx → Ok + version string | `TestConnectionAsync`, hunt preflight |
| `TestConnectionAsync` | (probe) | Throws if probe not Ok | [`SettingsViewModel.TestQbittorrentConnection`](../../ViewModels/SettingsViewModel.cs), [`DeviceStatusService`](../../Services/DeviceStatusService.cs) |

Retry: `GetWithAuthRetryAsync` / `PostFormWithAuthRetryAsync` — on 401/403, logout and `LoginAsync(force: true)` once.

Session cache key: `{WebUiUrl}|{Username}|{Password}`. Cookies: `CookieContainer` on the shared `HttpClient`.

**Process restart** ([`QbittorrentProcessRestartService`](../../Services/QbittorrentProcessRestartService.cs)): restart only if probe is **Unreachable**. **AuthFailed** and **InvalidUrl** skip restart.

Embedded WebUI ([`QbittorrentViewerService`](../../Services/QbittorrentViewerService.cs)) opens the URL in WebView2. It does **not** use this HTTP client.

## Search

| Method | Path | Success rule today | Callers |
|--------|------|--------------------|---------|
| `StartSearchAsync` | `POST search/start` | Auth retry; 409 → `QbittorrentSearchCapacityException`; else `EnsureSuccessStatusCode`; JSON `id` | `SearchAsync`, [`FetchJobService`](../../Services/FetchJobService.cs), [`ShowSearchSnapshotService`](../../Services/ShowSearchSnapshotService.cs), [`AutomationFlowService`](../../Services/AutomationFlowService.cs) |
| `GetSearchResultsAsync` | `GET search/results` | Auth retry + `EnsureSuccessStatusCode`; JSON `results` / `status` / `total` | Same |
| `StopSearchAsync` | `POST search/stop` | Warn unless 2xx or 404 | Same (cleanup) |
| `DeleteSearchAsync` | `POST search/delete` | Warn unless 2xx or 404 | Same (cleanup) |
| `GetSearchPluginsAsync` | `GET search/plugins` | Login + auth retry + `EnsureSuccessStatusCode` | [`QbittorrentSearchPluginService`](../../Services/QbittorrentSearchPluginService.cs) (engine picker) |
| `PostSearchDownloadTorrentAsync` | `POST search/downloadTorrent` | 2xx and body not `"Fails."` | Inside `AddTorrentAsync` when plugin name is set |

`SearchAsync` wraps start → poll results → stop/delete.

## Torrents — read

| Method | Path | Success rule today | Callers |
|--------|------|--------------------|---------|
| `GetTorrentsAsync` | `GET torrents/info` | Login + `EnsureSuccessStatusCode`; JSON array | Add verify, reconcile, link, cleanup, pack coordinator |
| `GetTorrentFilesAsync` | `GET torrents/files?hash=` | Login + `EnsureSuccessStatusCode` | Content validation, add gate, pack link, auto-link |
| `GetCategoriesAsync` | `GET torrents/categories` | `EnsureSuccessStatusCode` | Before `createCategory` |

## Torrents — mutate

| Method | Path | Success rule today | Callers |
|--------|------|--------------------|---------|
| `PostAddTorrentAsync` | `POST torrents/add` (multipart) | 2xx and body not `"Fails."` | `AddTorrentAsync` |
| `TryPrepareCategoryAsync` | `POST torrents/createCategory` | 2xx and body not `"Fails."` | `AddTorrentAsync` |
| `ApplyTorrentPostAddSettingsAsync` | `POST torrents/setLocation`, `setCategory`, `addTags` | Warn if not 2xx or `"Fails."` | After plugin download add |
| `DeleteTorrentsAsync` | `POST torrents/delete` | Warn if not 2xx | [`TorrentCleanupService`](../../Services/ITorrentCleanupService.cs) |
| `PauseTorrentsAsync` | `POST torrents/stop`, then `torrents/pause` | Stop 2xx wins; else pause must 2xx | **No app callers today** (interface only) |
| `ResumeTorrentsAsync` | `POST torrents/start` | Must 2xx; **no** `/resume` fallback | **No app callers today** (interface only) |

Add form fields: `urls` or `torrents` file, `savepath`, `category`, `tags`, **`paused`**. Default from add gate / automation: `Paused = false`.

After add, the client **polls** `torrents/info` for a new hash (`AddVerifyTimeoutSeconds = 20`). It does not parse add-response ids.

## Non-API HTTP

`ResolveAddSourcesAsync` / `TryDownloadTorrentPayloadAsync` GET candidate URLs (magnets, HTML, `.torrent` bytes) with the **same** `HttpClient` (cookies may go to tracker sites). Not qBittorrent WebAPI.

## Consumers (by feature)

| Feature | Services |
|---------|----------|
| Settings test + status pill | `SettingsViewModel`, `DeviceStatusService` |
| Hunt preflight / restart | `AutoTrackService` → `QbittorrentProcessRestartService` |
| Hunt search | `FetchJobService`, `ShowSearchSnapshotService`, `AutomationFlowService` |
| Add + validate | `TorrentAddGateService` → `AddTorrentAsync`; files via `QbittorrentTorrentContentValidationService` |
| Cart / recipe add | `TorrentWorkspaceViewModel`, `AutomationFlowService` |
| Reconcile / link | `TorrentReconciliationService`, `AutoTorrentLinkService`, `PackLinkCoordinatorService` |
| Engine list | `QbittorrentSearchPluginService` |

## Probe result model

[`QbittorrentWebUiProbeResult`](../../Models/QbittorrentWebUiProbeResult.cs): `Ok`, `Unreachable`, `AuthFailed`, `InvalidUrl`. Login `"Ok."` failure is mapped to **AuthFailed**.
