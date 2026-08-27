# 01 — API architecture: 5.1 vs 5.2

WebAPI path prefix is unchanged: `/api/v2/{group}/{method}`. Authentication and **empty / add** responses are what shifted.

## 5.1 (what the app was built for)

Style: **RPC over HTTP**. Status is often `200` either way; the client reads a **plaintext token**.

| Concern | 5.1 contract |
|---------|----------------|
| Session | Cookie after `POST /api/v2/auth/login` (historically `SID`) |
| Login OK | `200` + body `"Ok."` |
| Login fail | Often `200` + `"Fails."` (403 if IP banned) |
| Add torrent | `200` + `"Ok."` or `"Fails."` — **no infohash in the body** |
| Empty POST success | Often `200` with empty or `"Ok."` |
| Tool auth | Username/password only |

Media Manager encodes this in `LoginAsync` (`body.Equals("Ok.")`) and several POSTs that also treat `"Fails."` as failure.

Client must **poll** `GET /api/v2/torrents/info` after add (app: `WaitForAddedTorrentAsync`, 20s) because add does not return ids.

## 5.2 (WebAPI 2.14+)

Style: closer to **HTTP semantics** + structured add. Same URL tree; new status codes and one new auth mode.

| Concern | 5.2 contract |
|---------|----------------|
| Session cookie | Still cookies for login; name is `QBT_SID_{WebUI port}` (e.g. `QBT_SID_8080`) |
| Login OK | **`204 No Content`**, empty body |
| Login fail | **`401 Unauthorized`** |
| Add torrent | JSON: `success_count`, `pending_count`, `failure_count`, `added_torrent_ids` |
| Add pending (e.g. magnet) | **`202`** when `pending_count > 0` |
| Add all failed | **`409`** |
| Empty success | Many endpoints **`204`** when there is no body ([changelog 2.11.8](https://github.com/qbittorrent/qBittorrent/blob/release-5.2.0/WebAPI_Changelog.md)) |
| Tool auth | Optional **API key**: `Authorization: Bearer qbt_...` — **no** `/auth/login`, **no** WebUI static files |

API keys are 32 characters (`qbt_` + 28 alphanumerics), one key at a time, rotatable. Intended for third-party tools. Wiki: [API Key Authentication (≥ v5.2.0)](https://github.com/qbittorrent/qBittorrent/wiki/API-Key-Authentication-(%E2%89%A5v5.2.0)).

5.2 also added **HTTP Basic** for WebUI/API (credentials, still cookie-oriented). Media Manager does not use Basic today.

## What did *not* change (for this app)

These remain JSON GET/POST in the same shape the client already parses:

- `GET app/version`
- `GET torrents/info`, `GET torrents/files`, `GET torrents/categories`
- `POST search/start` → `{ "id": n }`, `GET search/results`, `POST search/stop|delete`, `GET search/plugins`

Unused by this app (ignore for upgrade unless a future feature needs them): `sync/maindata`, `app/setPreferences`, `torrents/setShareLimits`, etc.

## 5.0 leftovers (not 5.2-specific)

qBittorrent **5.0** already renamed pause/resume. The client is **partially** adapted:

| Area | 4.x | 5.0+ | App today |
|------|-----|------|-----------|
| Pause endpoint | `torrents/pause` | `torrents/stop` | Tries **`stop` then `pause`** |
| Resume endpoint | `torrents/resume` | `torrents/start` | **`start` only** (no `/resume` fallback) |
| List states | `pausedDL` / `pausedUP` | `stoppedDL` / `stoppedUP` | Normalizer maps **paused*** only |
| Add form | `paused` | `stopped` preferred | Sends **`paused` only** |

These do **not** explain the current disconnect. They matter **after** login works (UI labels, pause-on-add).

## Auth flows

```text
5.1 / current app
  POST auth/login (user+pass) → cookie → all other /api/v2 calls

5.2 cookie (same as 5.1, different success codes)
  POST auth/login → 204 + Set-Cookie: QBT_SID_8080=... → other calls

5.2 API key (not implemented)
  every /api/v2 call except auth/*  +  header Authorization: Bearer qbt_...
  skip login; cannot fetch WebUI HTML/CSS/JS with the key
```

Localhost bypass (`bypass_local_auth`) means qBittorrent may accept **unauthenticated** API from loopback. The app still logs in first, so a strict `"Ok."` check still fails on 5.2 even when `/app/version` would succeed without cookies.

## Why 5.2 is a better contract (for a rewritten client)

1. **Status codes** — 401 vs 204 vs 202 vs 409 can drive probe/restart without parsing `"Fails."`.
2. **Add JSON** — `added_torrent_ids` can skip or shorten hash polling for file adds; `pending_count` + 202 matches magnets/plugin downloads.
3. **API key** — one header, no session expiry/CSRF, matches how Jellyfin is already configured in this app.

5.1 was easier to write (`if body == "Ok."`). 5.2 is easier to get **right** once the client stops requiring that string.

## Upstream references

- Changelog WebAPI **2.11.8** — 204 when no body
- Changelog WebAPI **2.14.0** — login 401; add JSON + 202/409
- Changelog WebAPI **2.14.1** — API key rotate/delete endpoints
- Changelog WebAPI **2.15.0** — Basic auth
