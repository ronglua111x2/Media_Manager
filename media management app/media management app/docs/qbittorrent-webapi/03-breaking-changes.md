# 03 — Breaking changes vs this app

Layered: **5.2 auth/add** (current outage) vs **5.0 names** (after reconnect) vs **unused** upstream changes.

## Blocks connection on 5.2+ (must fix first)

### Login body `"Ok."`

[`LoginAsync`](../../Services/QbittorrentClient.cs) requires `IsSuccessStatusCode` **and** trimmed body equal to `"Ok."` (case-insensitive).

On 5.2, success is **`204` + empty**. Empty ≠ `"Ok."` → `InvalidOperationException` (“qBittorrent login failed…”) → probe **AuthFailed**.

Wrong password is **401**. The app still shows the same login-failed message, so it is easy to blame credentials.

### Always login, even with localhost bypass

Probe may get **200** from `GET app/version` on loopback without cookies. It still calls `LoginAsync(force: true)` and fails the body check. Browser WebUI working without a prompt does **not** mean the client will connect.

### API key unused

qBittorrent 5.2 API keys cannot call `auth/login`. The app only posts login. Generating a key in Options → WebUI does nothing until the client sends `Authorization: Bearer`.

### Cookie name

`QBT_SID_{port}` vs `SID` is **not** the current failure. `CookieContainer` stores whatever `Set-Cookie` returns. Worth knowing for any **manual** SID parsing (this client does none).

## After login is 5.2-compatible — likely OK

| Area | Why it likely works |
|------|---------------------|
| `GET` version / info / files / categories / plugins / search results | JSON + `EnsureSuccessStatusCode`; 200 still used for bodies with data |
| `search/start` | JSON `{ id }` |
| `search/stop`, `search/delete` | Already allow 404; 204 is 2xx |
| `createCategory`, `setLocation`, `setCategory`, `addTags` | Fail only if **not** 2xx **or** body `"Fails."`; empty 204 is fine |
| `torrents/delete` | 2xx only |
| Pause | **`stop` first**, then `pause` — correct for 5.x (methods currently unused by UI) |
| Resume | **`start` only** — correct for 5.x; would 404 on 4.x |

`.NET` `IsSuccessStatusCode` includes **204** and **202**.

## After login — still worth handling

### `POST torrents/add` response

5.1: `"Ok."` / `"Fails."`.  
5.2: JSON counts + ids; **202** pending; **409** all failed.

Current check: 2xx and body not `"Fails."`.

- Success JSON on **200** — **not** `"Fails."` → treated as OK; hash still comes from **poll**.
- **202** — 2xx → treated as OK; poll still matches magnets.
- **409** — not 2xx → throw (reasonable).
- Mixed JSON (`failure_count` > 0 but HTTP 200) — may look like success until poll times out.

So add is **not** blocked the same way as login, but the client does not use `added_torrent_ids` and does not interpret 5.2 error JSON.

### Torrent states (from **5.0**, not 5.2)

[`QbittorrentTorrentStateNormalizer`](../../Common/QbittorrentTorrentStateNormalizer.cs) maps `pausedDL` → Paused, `pausedUP` → Uploading. Unknown states (including **`stoppedDL`**) fall through to **Downloading**.

Effect: paused/stopped torrents can show as downloading in reconcile/UI. Hunt add uses `Paused = false`, so this is labeling, not add-blocking.

### Add form `paused` vs `stopped` (from **5.0**)

Client sends `paused` only. Gate/automation set `Paused = false`. Pause-on-add would be the risky path; today it is unused.

### Resume on **4.x**

`ResumeTorrentsAsync` has no `/resume` fallback. Fine while the machine runs 5.x.

## Unused by this app (do not block upgrade)

From 5.2 changelog, not called here:

- `sync/maindata` field changes, `use_subcategories`
- `torrents/setShareLimits` + `shareLimitAction`
- `app/setPreferences` ratio/seeding pairing rules
- `clientdata/load|store`, `app/processInfo`, metadata fetch/parse endpoints
- Basic auth (optional alternative to cookie login)

## Probe / restart interaction

If login is “fixed” but password is wrong: probe stays **AuthFailed**, restart **does not** run. That remains correct.

If login is still `"Ok."`-strict on 5.2: probe is **AuthFailed** even with a good password — same skip. Looks like “restart didn’t help.”

## Summary

| Severity | Change | App impact |
|----------|--------|------------|
| **P0** | Login `204` / empty vs `"Ok."` | No connect, no hunt, no add |
| P1 | Add JSON / 202 / 409 | Usually works after P0; better errors and optional ids |
| P1 | `stoppedDL` / `stoppedUP` | Wrong normalized label |
| P2 | API key Bearer | Not required to reconnect; better long-term auth |
| — | Cookie name, 204 on other POSTs | Already compatible if login is |
| — | 5.2 features not called | None |
