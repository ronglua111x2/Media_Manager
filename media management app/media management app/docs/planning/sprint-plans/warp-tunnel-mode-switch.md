# WARP tunnel-mode switch for TMDB + no-recovery hint — local plan

**Branch:** `fix/warp-tunnel-mode-switch` from `main` @ `92737a7`  
**Date:** 2026-10-31  
**Status:** ✅ Implemented — live-tested 2026-10-31  
**Not a sprint:** one-PR bugfix, follow-up to [jellyfin-refresh-warp-backoff-fix.md](./jellyfin-refresh-warp-backoff-fix.md)

## Problem

2026-10-31 09:59 and 10:30 (`20261031_095448_814_systemlog.txt`): the A Certain Dark Item TMDB refresh failed. WARP connected and DNS returned real CloudFront IPs, yet 9/9 retries failed with `The SSL connection could not be established`.

Direct probes showed the network now **cuts TLS by SNI** for `api.themoviedb.org`:

| Probe | Result |
| ----- | ------ |
| Any TMDB IP + SNI `api.themoviedb.org` | curl exit 35 |
| Same IPs + another SNI | 200 |

The user's WARP mode is `doh` (DNS only), so traffic still goes direct.

Live test on the machine:

1. `warp-cli mode warp+doh` → `connect` (3.9 s)
2. `api.themoviedb.org` → HTTP 401 on the first try (TLS through)
3. `disconnect` → `mode doh` (back to the original state)

## Scope IN / OUT

| In | Out |
| -- | --- |
| Switch a DNS-only mode to `warp+doh` for leases that need TMDB, then restore the user's mode | Switching the mode for hunt / user / Settings test leases |
| Crash-safe restore marker + startup restore | Settings UI control for the new setting |
| Console hint (rate-limited) when TMDB is blocked outside Auto-Track recovery | WARP recovery for Library / Find/Add (still by design) |

## Definition of Done

- [x] `WarpModePolicy` (tunnel modes, JSON parse, restore decision, marker, mode-name guard)
- [x] `WarpLeaseReasonText.RequiresTunnel`; `WarpCliService` switch / restore / `GetOperationModeAsync` / `RestorePendingModeAsync`
- [x] Startup restore in `App.xaml.cs`; `Warp.SwitchModeForTmdb`; Settings hint text
- [x] `TmdbBlockAdvisor` + `TmdbWarpRecovery` scope (last retry outside scope); wired into `TmdbMetadataProvider`
- [x] App.Tests 131 passed (+38), Core.Tests 248
- [x] Docs: FEATURES §10.21 + §8.3, AI_CONTEXT, BUILD, STATE_FOLDER
- [x] Manual test on the live install (user, 2026-10-31): 10:55 Auto-Track TMDB recover `doh → warp+doh` → connected → `TMDB refresh ok` (A Certain Dark Item) → 10:57 `warp+doh → doh restored`; 11:03 Jellyfin hold same switch → 11:04 restored; marker removed

## Test gate

See [BUILD.md](../../BUILD.md).

Manual checks:

1. **Run Now with WARP in `doh`, disconnected.** Log shows `WARP mode doh → warp+doh` → `WARP connected` → `TMDB DNS ready` → `TMDB refresh ok` → `WARP mode warp+doh → doh restored`. Afterwards `warp-cli -j settings` reports `doh` and the marker is gone.
2. **User connects WARP in `doh` first, then Run Now.** The mode switches and comes back, and WARP stays connected.
3. **Kill the app mid-recover.** On restart the log shows `restoring doh`, and the marker is deleted.
4. **Library refresh with WARP off.** The console shows one warning to check the WARP mode (at most once per 10 min).

## Risks / rollback

- While in `warp+doh` and connected, all traffic (incl. qBittorrent) goes through WARP. This only lasts for TMDB recover / Jellyfin hold windows.
- A mode switch while a tunnel is already up makes WARP reconnect, so the pill and toasts may flicker.
- Rollback: revert the PR. If a marker is left behind, delete `{StateFolder}/warp-mode-restore.json` and set the WARP mode by hand.
