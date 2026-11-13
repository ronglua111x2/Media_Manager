# Network package 1 — token/race, `warp-cli` deadlines, Hunt tunnel — local plan

**Branch:** `fix/network-package-1` from `main` @ `ff2caa3`  
**Date:** 2026-11-08  
**Status:** Implemented; user OK on the installed build 2026-11-08 — Hunt tunnel in DNS-only mode not seen in the log yet  
**Not a sprint:** one-PR bugfix package. Source: whole-app network audit (branch `claude/zen-antonelli-410a4f`, `docs/network-audit/`, findings NET-001 to NET-004, NET-025)

## Problems

| Id | Problem |
| -- | ------- |
| NET-001 | The TMDB Bearer token was set on the shared `HttpClient` defaults, so Gemini requests carried it too |
| NET-002 | Rewriting `DefaultRequestHeaders` on every TMDB call races with concurrent sends and can break the shared client for the process lifetime |
| NET-025 | `BaseAddress` was set on the shared client |
| NET-003 | `warp-cli` had no deadline while holding the global gate; one hung call stalls Auto-Track, Jellyfin and the status pill |
| NET-004 | Hunts did not get a tunnel in DNS-only modes, though many torrent sites block this network (DNS-only: 1–2 of 10 engines returned rows; tunnel: 3–5) |

## Scope IN / OUT

| In | Out |
| -- | --- |
| TMDB headers per request; no `BaseAddress` or default headers on the singleton | Per-destination clients, host table, diagnostics (package 2) |
| `WarpCliProcess`: deadline 20 s `connect`, 10 s others; kill process tree | Capability leases, `IWarpCliRunner` |
| `Warp.ForceTunnelForHunt` (Settings checkbox, default on); no tunnel aborts the hunt | Manual searches (user decision) |
| | Classifier and wider fallback, `api.tmdb.org` (package 2) |

## Definition of Done

- [x] `TmdbMetadataProvider`: `CreateGetRequest`, `ConfigureHeaders` and `BaseAddress` removed (after merging main 2026-11-13: main had already moved the headers into `GetAsyncWithRetryAsync` for native search, NS-30; that version is kept, with the URI built from `NetworkEndpoints`, and `CreateGetRequest` is gone)
- [x] `WarpCliProcess` used by `WarpCliService`; mode restored when a connect fails on an exception
- [x] `WarpModePolicy.LeaseNeedsTunnel`, `_tunnelLeases`, `WarpLeaseResult.TunnelActive`, hunt preflight check, Settings checkbox
- [x] App.Tests 160 passed (+22), Core.Tests 248
- [x] Docs: FEATURES §10.3/§10.21, AI_CONTEXT, BUILD
- [ ] Manual test on the live install (user) — deferred on 2026-11-08 while package 2 proceeds; run the checks below before relying on this package

## Test gate

See [BUILD.md](../../BUILD.md).

Manual checks:

1. **WARP mode `warp`:** Run Now with a hunt; no mode switch; TMDB refresh still works.
2. **DNS-only, option on:** log shows `WARP mode doh → warp+doh for Auto-Track hunt`, more engines return rows, then `restored`; the marker is gone.
3. **DNS-only, option off:** the hunt keeps `doh`.
4. **Library refresh and Find/Add** still work without `BaseAddress`.

A hung `warp-cli` cannot be reproduced on the machine; that path is unit-tested only.

## Merge with main (2026-11-13)

- **Hunt slot (GOV-04):** main lets a scheduled hunt run once per hunt interval and stamps show rotation before the preflight. A hunt that `ForceTunnelForHunt` aborts (no tunnel) searched nothing, so `AutoTrackHuntSlot` gives the slot back and rotation is stamped only after the preflight passes. The next 30-minute cycle retries, as the Settings hint says.
- **WARP leases:** native search adds no WARP lease of its own; it runs inside the hunt's `Hunt` lease (background lane). `Hunt` and `TmdbSslRecover` share `_tunnelLeases`, so releasing one never restores the user's mode while the other still needs the tunnel.
- **Native search HTTP:** `NativeSearchService` builds its own `HttpClient` for `GovernedIndexerHttp` (no client timeout, per-request deadlines), so `NetworkClients` does not touch it. Its IMDb lookup goes through `TmdbMetadataProvider` and so uses `api.tmdb.org`, the per-request token and the classifier; its own deadline stays an `OperationCanceledException` and does not trigger the WARP hint.

## Risks / rollback

- DNS-only with the option on: machine traffic, including qBittorrent, uses WARP for about 2 minutes per hunt.
- If reading the WARP mode keeps failing, hunts stop until it recovers; a warning is logged and the option can be unticked.
- The TMDB token may have reached Google if Gemini was called earlier; rotating it is the user's call.
- Rollback: revert the PR. No schema change; older builds ignore the new JSON key.
