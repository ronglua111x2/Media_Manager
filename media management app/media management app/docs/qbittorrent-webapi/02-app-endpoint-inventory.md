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
| `GetTorrentsAsync` | `GET torrents/info` | Login + `EnsureSuccessStatusCode`; JSON array | Add verify, pre-add existing-hash match (`TryGetExistingByListingUrlAsync`), reconcile, link, cleanup, pack coordinator |
| `GetTorrentFilesAsync` | `GET torrents/files?hash=` | Login + `EnsureSuccessStatusCode` | Content validation, add gate, pack link, auto-link |
| `GetCategoriesAsync` | `GET torrents/categories` | `EnsureSuccessStatusCode` | Before `createCategory` |
| `GetWebApiVersionAsync` | `GET app/webapiVersion` | 2xx, parsed by `QbittorrentWebApiCompatibility.ParseWebApiVersion`; cached 10 min; failure → 0.0 | Add gate (S0): picks the add strategy |
| `InspectBeforeAddAsync` → `FetchMetadataAsync` | `POST torrents/fetchMetadata` (`source=<magnet>`) | 202 = still fetching (poll 1 s), 200 = JSON with `info.files[]`; other status → no pre-add check | Add gate (S0), WebAPI ≥ 2.11.9 (qBittorrent 5.2+) |

## Torrents — mutate

| Method | Path | Success rule today | Callers |
|--------|------|--------------------|---------|
| `PostAddTorrentAsync` | `POST torrents/add` (multipart) | 2xx and body not `"Fails."` | `AddTorrentAsync` |
| `TryPrepareCategoryAsync` | `POST torrents/createCategory` | 2xx and body not `"Fails."` | `AddTorrentAsync` |
| `ApplyTorrentPostAddSettingsAsync` | `POST torrents/setLocation`, `setCategory`, `addTags` | Warn if not 2xx or `"Fails."` | After plugin download add; `ApplyManagedTorrentSettingsAsync` when attaching an existing hash |
| `DeleteTorrentsAsync` | `POST torrents/delete` | Must 2xx; throws on non-success and transport/auth failure | [`TorrentCleanupService`](../../Services/ITorrentCleanupService.cs) |
| `PauseTorrentsAsync` | `POST torrents/stop`, then `torrents/pause` | Stop 2xx wins; else pause must 2xx | [`TorrentCleanupService`](../../Services/ITorrentCleanupService.cs) fail-safe after unverified delete (AUD-002) |
| `ResumeTorrentsAsync` | `POST torrents/start`, then `torrents/resume` | Start 2xx wins; else resume must 2xx | Add gate (S0): starts a torrent it added stopped, after the check passed |
| `SetFilePrioritiesAsync` | `POST torrents/filePrio` (`hash`, `id=i|j|…`, `priority=0`) | Must 2xx | Add gate (S0): files outside the download allow-list are never downloaded |

Add form fields: `urls` or `torrents` file, `savepath`, `category`, `tags`, `paused` + `stopped`, and since S0:

- `stopCondition=MetadataReceived` when `AddTorrentRequest.StopAfterMetadata` (WebAPI ≥ 2.8.18, qBittorrent 4.5+): a magnet fetches its metadata, then stops.
- `filePriorities=1,0,…` when the metadata was fetched first (`torrents/fetchMetadata`); not allowed with an uploaded `.torrent`.
- An uploaded `.torrent` (`TorrentBytes`, the exact bytes that were checked) goes with `paused=true`/`stopped=true`.

The add strategy (`TorrentAddGateService`):

| qBittorrent | Magnet | `.torrent` over HTTP(S) | Search plugin |
|---|---|---|---|
| 5.2+ (WebAPI ≥ 2.11.9) | `fetchMetadata` → check → add with `filePriorities` (starts) | app downloads + parses → check → add same bytes stopped → `filePrio` → start | add with stop request; stopped as soon as it appears; check → `filePrio` → start |
| 4.5 – 5.1 | add with `stopCondition=MetadataReceived` → check → `filePrio` → start | as above | as above |
| < 4.5 | add running (pre-S0 behaviour) | as above | add running |

An add that ends before the check finished (cancel, exception) stops the torrent it added.

After add, the client **polls** `torrents/info` for a new hash (`AddVerifyTimeoutSeconds = 20`). It does not parse add-response ids.

## Non-API HTTP

`ResolveAddSourcesAsync` / `TryDownloadTorrentPayloadAsync` GET candidate URLs (magnets, HTML, `.torrent` bytes) with the **same** `HttpClient` (cookies may go to tracker sites). Not qBittorrent WebAPI.

## Consumers (by feature)

| Feature | Services |
|---------|----------|
| Settings test + status pill | `SettingsViewModel`, `DeviceStatusService` |
| Hunt preflight / restart | `AutoTrackService` → `QbittorrentProcessRestartService` |
| Hunt search | `FetchJobService`, `ShowSearchSnapshotService`, `AutomationFlowService` |
| Add + validate | `TorrentAddGateService` → `TryGetExistingByListingUrlAsync` / `AddTorrentAsync` / `ApplyManagedTorrentSettingsAsync`; files via `QbittorrentTorrentContentValidationService` |
| Cleanup / reject | `TorrentCleanupService` → `DeleteTorrentsAsync` (throws); fail-safe `PauseTorrentsAsync` |
| Cart / recipe add | `TorrentWorkspaceViewModel`, `AutomationFlowService` |
| Reconcile / link | `TorrentReconciliationService`, `AutoTorrentLinkService`, `PackLinkCoordinatorService` |
| Engine list | `QbittorrentSearchPluginService` |

## Probe result model

[`QbittorrentWebUiProbeResult`](../../Models/QbittorrentWebUiProbeResult.cs): `Ok`, `Unreachable`, `AuthFailed`, `InvalidUrl`. Login `"Ok."` failure is mapped to **AuthFailed**.
