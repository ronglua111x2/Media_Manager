# Jellyfin refresh WARP recover + retry backoff — local plan

**Branch:** `fix/jellyfin-warp-recover-backoff` from `main` @ `dbc95c8`  
**Date:** 2026-10-26  
**Status:** ✅ Implemented — live-tested 2026-10-26  
**Not a sprint:** one-PR bugfix, follow-up to [auto-track-anchor-cycle-warp-fix.md](./auto-track-anchor-cycle-warp-fix.md)

## Problem

After reconcile links an episode, `JellyfinLibraryRefreshService` probes TMDB before notifying Jellyfin. The pre-probe (`EnsureTmdbReachableWithWarpAsync`) only connected WARP on SSL/TLS errors (`SslTlsErrorDetector`). The real block is local DNS answering `api.themoviedb.org` with loopback, which surfaces as `actively refused`, so:

- every flush failed and was re-queued with a fixed 3 s debounce, looping forever (2026-10-20: 1,122 retries in ~1.5 h; 2026-10-26 07:09: every ~5 s until the app closed);
- the post-notify WARP hold only ran while an Auto-Track hunt lease was active, never for reconcile;
- Jellyfin's own TMDb plugin failed too (`Error in "TheMovieDb" … actively refused`, Jellyfin log 2026-10-26 07:10:50), so S01E03 was indexed without metadata.

## Scope IN / OUT

| In | Out |
| -- | --- |
| J1 `Services/TmdbWarpRecovery.cs`: shared connect WARP → wait DNS → spaced retry, used by Auto-Track TMDB refresh and the Jellyfin pre-probe | J5 persisting the Jellyfin path queue across restarts |
| J2 Jellyfin pre-probe uses `TmdbNetworkBlockDetector`; post-notify hold also runs when the pre-probe needed WARP | Re-fetching metadata already missing in Jellyfin (manual Refresh metadata) |
| J3 `Services/JellyfinRetryBackoff.cs`: 3 s → 30 s → 2 min → 5 min; WARP connect failure → 5 min; reset on new paths / success | WARP recovery outside Auto-Track (Library TMDB, Find/Add) |
| J4 App.Tests + docs | |

## Definition of Done

- [x] `TmdbWarpRecovery` + `TmdbWarpConnectFailedException`; `AutoTrackService` refactored onto it
- [x] Jellyfin pre-probe via detector + shared recovery; hold when recovered with WARP
- [x] Retry backoff with log "next retry in …"
- [x] App.Tests 64 passed (+11), Core.Tests 248
- [x] Docs: FEATURES §10.2/§10.10, AI_CONTEXT, BUILD
- [x] Manual test on the live install (user, 2026-10-26 08:39): Sync Symlinks Now → pre-probe `actively refused` → WARP connected → DNS ready → pre-probe OK → notify 204 → log-tail hold → early WARP disconnect at 08:40:26; no `Error in "TheMovieDb"` in the Jellyfin log after the refresh

## Test gate

[BUILD.md](../../BUILD.md): x64 build, Core.Tests 248, App.Tests 64.

Manual (WARP off, Jellyfin running): link a new episode (or Settings → Flush Jellyfin queue with a pending path). Expect `TMDB network error (ConnectionError) for Jellyfin refresh pre-probe` → `WARP connected` → `TMDB DNS ready` → `TMDB pre-probe OK after WARP` → path notify → `Jellyfin log early disconnect enabled` / hold → WARP disconnected. Jellyfin log: episode metadata fetched without `Error in "TheMovieDb"`. With WARP unusable: retries at 3 s / 30 s / 2 min / 5 min, not every 5 s.

## Risks / rollback

- A flush that needs WARP now holds it for up to `WarpHoldSecondsAfterNotify` (×2 hard cap) after notify.
- Retry is slower after repeated failures (max 5 min); new paths reset it.
- Rollback: revert the PR; no schema or settings changes.
