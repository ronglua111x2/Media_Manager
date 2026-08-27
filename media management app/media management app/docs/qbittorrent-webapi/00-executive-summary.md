# 00 — Executive summary

**Status:** Docs only. Client still implements the **5.1 login contract**.

## Symptom

After updating qBittorrent to **5.2+** without wiping settings:

- qBittorrent **WebUI** at `http://localhost:8080` opens normally.
- **Bypass authentication for clients on localhost** is on, so the browser often never shows a login form.
- Media Manager **Test Connection** / status pill still fails, typically as login/auth failure.
- Changing the WebUI password to match Settings does **not** fix it.
- Generating an API key in qBittorrent does **not** fix it — the app never sends `Authorization: Bearer`.

This is expected with the current client, not a mis-typed password.

## Root cause (one gate)

Every useful call goes through `LoginAsync` in [`QbittorrentClient.cs`](../../Services/QbittorrentClient.cs):

```text
POST api/v2/auth/login
then require: HTTP success AND body == "Ok."
```

| qBittorrent | Successful login | Failed login |
|-------------|------------------|--------------|
| **≤ 5.1** | `200` + `"Ok."` | Often `200` + `"Fails."` |
| **5.2+** | **`204` + empty body** | **`401`** |

`204` is a success status (`IsSuccessStatusCode` is true), but the extra `"Ok."` check treats an empty body as failure. Localhost bypass does not help: the app **always** posts `/auth/login` and does not skip it for loopback.

Cookie rename (`SID` → `QBT_SID_{port}`) is **not** the blocker. `HttpClient` uses `CookieContainer` and does not hardcode `SID`.

## Is 5.2 “better” than 5.1?

**Yes, for a client that trusts HTTP.** 5.2 is not a restyle of the same `"Ok."` RPC:

- Login uses **204 / 401** instead of always-200 plus a magic string.
- `torrents/add` returns **JSON counts and torrent ids**, plus **202** (pending) and **409** (all failed).
- Optional **stateless API key** is the right auth for a desktop tool (no cookie round-trip, no CSRF on API).

The hunt/search JSON endpoints (`search/start`, `torrents/info`, plugins, …) are largely unchanged. The break is concentrated in **auth** and **add response shape**. See [01-api-architecture-5.1-vs-5.2.md](./01-api-architecture-5.1-vs-5.2.md).

## Impact on Media Manager today

```text
Test Connection / DeviceStatus / Hunt preflight
        → ProbeWebUiAsync
        → LoginAsync(force: true)     ← FAILS on 5.2+
        → never reaches search / add / info
```

Process restart ([`QbittorrentProcessRestartService`](../../Services/QbittorrentProcessRestartService.cs)) only runs when the WebUI is **Unreachable**. **AuthFailed** is skipped on purpose. Restarting qBittorrent will not unstick this.

After login is compatible, most remaining endpoints are likely usable. Remaining follow-ups: add JSON (and 409), torrent **state names** from 5.0 (`stoppedDL` vs `pausedDL`), optional API key. Details: [03-breaking-changes.md](./03-breaking-changes.md).

## What this folder is not

- Not a green light to patch `QbittorrentClient` from these files alone.
- Not a substitute for a **Plan Mode local plan** after a full code read ([04-upgrade-plan.md](./04-upgrade-plan.md)).

## Workaround until the client is upgraded

- Run **qBittorrent 5.1.x** (login still returns `"Ok."`), **or**
- Wait for the client upgrade (outline in doc 04).
