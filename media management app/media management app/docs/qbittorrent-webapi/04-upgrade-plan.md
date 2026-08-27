# 04 — Upgrade outline

**This is not an implementation plan.** Do not patch from this file.

When upgrading the client, use **Plan Mode** to write a **local plan** (same idea as [`docs/planning/sprint-plans/`](../planning/sprint-plans/README.md)): read `QbittorrentClient` and callers end-to-end, then list exact diffs, tests, and verify steps. This outline only names **phases and constraints**.

## Constraints for the later local plan

- Keep **5.1 and 5.2** working (plaintext `"Ok."`/`"Fails."` and 204/401/JSON add).
- Do not restart qBittorrent on **AuthFailed** (existing probe rule).
- Search / info / plugins should stay JSON as they are unless the live 5.2 run proves otherwise.
- Settings: username/password remain valid; API key is **optional**.
- Build: MSBuild x64 per [`docs/BUILD.md`](../BUILD.md).

## Phases (suggested order)

### P0 — Login (unblocks everything)

Treat login success as **HTTP 2xx** and body **not** `"Fails."`. Do not require `"Ok."`. Map **401** (and existing 403) to auth failure. Dual-compat with 5.1 (`200` + `"Ok."`).

### P1 — Add response + 5.0 names

- `torrents/add`: accept JSON counts/ids and **202** / **409**; keep poll as fallback.
- Normalizer: map `stoppedDL` / `stoppedUP` like the old paused states.
- Add form: send **`paused` and `stopped`** if pause-on-add stays in the model.

### P2 — Optional API key

If `AutoTorrent.ApiKey` is set: `Authorization: Bearer`, skip `/auth/login`. Else keep cookie login. Settings Integrations field; session key must include the key. Do not send the key to `auth/login`.

### Verify (on the later local plan)

Live 5.2: Test Connection, status pill, search, add (.torrent and magnet), files list, hunt preflight. AuthFailed must not trigger process restart. Optional: 5.1.x smoke so dual-compat is real.

## Explicitly later / not this outline

- Mock qBittorrent HTTP server in unit tests (none today).
- Changing hunt/add product behavior (gate, recipes, categories).
- Rewriting the WebView2 viewer (browser auth ≠ API client).

## Next action

Plan Mode → local plan from this folder + current `QbittorrentClient.cs` → Agent Mode implements that local plan only.
