# Network package 2 — hosts, clients, failure classifier, `api.tmdb.org`, Jellyfin — local plan

**Branch:** `fix/network-package-1` (stacked on package 1, not pushed)  
**Date:** 2026-11-08  
**Status:** Implemented — **live test outstanding, together with package 1's**  
**Source:** whole-app network audit (branch `claude/zen-antonelli-410a4f`, `docs/network-audit/`): NET-006, NET-010 to NET-016, decision 1

## Scope IN / OUT

| In | Out (kept deliberately light) |
| -- | --- |
| `NetworkFailureClassifier`, one interpreter of network errors; wider WARP fallback, narrower false positives | Retry handler chain, `TmdbRouteCoordinator` |
| `NetworkEndpoints`, `TmdbApiHost` (default `api.tmdb.org`); WARP retries on `api.themoviedb.org` through the tunnel | Capability leases, `IWarpCliRunner` |
| `NetworkClients`: per-destination timeouts and pools; `net.fail` log line | DPAPI, persistent Jellyfin queue, status poll guard |
| Per-cycle recovery breaker (2 shows) | Manual-search tunnel |
| Jellyfin: notify unless a tunnel can help; 401/403 drops paths; HTTP timeouts no longer read as user cancellation | |

## Definition of Done

- [x] Classifier with table-driven tests; `SslTlsErrorDetector` removed
- [x] Hosts in `NetworkEndpoints`; token tests follow `TmdbApiHost`; Jellyfin pre-probe keeps the legacy host
- [x] `NetworkClients` wired for TMDB, shared, Jellyfin, qBittorrent, images
- [x] `net.fail` line; WARP mode cached so logging never spawns a process
- [x] Breaker; Jellyfin notify policy
- [x] App.Tests 234 passed (+74 over package 1), Core.Tests 248
- [x] Docs: FEATURES §10.10/§10.21, AI_CONTEXT, BUILD
- [ ] Manual test on the live install (user)

## Test gate

See [BUILD.md](../../BUILD.md).

Manual checks, WARP off:

1. Library refresh and Find/Add work; no `api.themoviedb.org` errors in the log.
2. Run Now with a due show: `TMDB refresh ok` **without** WARP connecting.
3. Jellyfin notify: legacy-host pre-probe is blocked → WARP → notify + hold.
4. Set `TmdbApiHost` to a broken host: recovery connects WARP and succeeds through the legacy host; the log has `net.fail` with address, class and WARP state.
5. Then run package 1's outstanding checks.

## Risks / rollback

- `api.tmdb.org` is undocumented and FPT may block it; the tunnel + legacy host fallback and the setting cover that.
- New timeouts apply to every call; values are generous for now, and `net.fail` gives real numbers for tightening.
- The WARP trigger set changed; pinned by the table tests.
- qBittorrent connect timeout is 5 s rather than the 3 s in the plan, because that client also resolves external torrent URLs.
- Rollback: revert the PR. No schema change; older builds ignore `TmdbApiHost`.
