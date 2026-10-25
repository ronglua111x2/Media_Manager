# Auto-Track anchor cycle + WARP recovery — local plan

**Branch:** `fix/auto-track-anchor-cycle-warp` from `main` @ `f72a7a9`  
**Date:** 2026-10-25  
**Status:** ✅ Implemented — live-tested 2026-10-25  
**Not a sprint:** one-PR bugfix. Investigation: [debug/auto-track/a-certain-dark-item-missed-anchor-2026-10-25.md](../../debug/auto-track/a-certain-dark-item-missed-anchor-2026-10-25.md)

## Git baseline

- [x] `git status -sb` clean on `main`
- [x] Branch cut from up-to-date `main`

## Problems

| Id | Problem | Evidence |
| -- | ------- | -------- |
| Gap 1 | A missed anchor is never caught up once the calendar week rolls over (week starts Sunday) | A Certain Dark Item: anchor Sat 2026-10-24 06:00 while the app was closed; Sunday starts a new week → next refresh 2026-10-31 |
| Gap 2 | Window is "anchor → Saturday 23:59", not 7 days | Saturday anchor = ~18 h window |
| Gap 3 | Torrent hunt shares the same show-level gate | `BuildHuntQueue` skips the show until this week's anchor |
| W1 | WARP self-recover only matches SSL/TLS messages | ~305× `actively refused (api.themoviedb.org:443)` vs 2× SSL in logs; recover never ran |
| W5 | WARP lease taken over a tunnel that was already up still disconnects on release | 2026-10-25 21:39:53 disconnect after `tunnel already up` |

## Scope IN / OUT

| In | Out |
| -- | --- |
| Rolling anchor cycle (most recent anchor ≤ now) for TMDB eligibility, Dormant reset, state machine week key, card status | Gap 4 (TMDB lag / air date changes) — deferred, needs discussion |
| Episode-level hunt gate: hunt pending episodes with `AirDate <= cycleAnchor` | W2 retry policy (by design) |
| `TmdbNetworkBlockDetector` for Auto-Track TMDB refresh only + per-cycle WARP failure guard | W4 WARP recovery outside Auto-Track (by design) |
| WARP tunnel ownership: release does not disconnect a tunnel the app did not start | New settings / Settings UI fields |
| Settings hint text for `Warp.AutoRecoverOnSsl` | Renaming the JSON key |

## Definition of Done

- [x] `AutoTrackWeekAnchor.GetCurrentCycleAnchor` / `GetNextCycleAnchor`; `IsPastAnchorThisWeek` / `GetAnchorDateTimeThisWeek` removed
- [x] `AutoTrackTmdbEligibility.HasSatisfiedCurrentCycle` + `FilterToCycle`
- [x] `AutoTrackService` uses cycle anchor in anchor check log, state machine, hunt queue; WARP recover via detector with per-cycle guard
- [x] `WarpCliService` tunnel ownership
- [x] App.Tests: anchor, eligibility, detector tests (53 passed; Core 248)
- [x] Docs: FEATURES §3.7/§10, AI_CONTEXT, BUILD test counts, debug note
- [x] Manual test on the live install (user, 2026-10-25): A Certain Dark Item refreshed via WARP recover (DNS ready → attempt 3/3 ok), S01E03 hunted and added

## Files to touch

- `Services/AutoTrackWeekAnchor.cs`, `Services/AutoTrackTmdbEligibility.cs`, `Services/AutoTrackService.cs`
- `ViewModels/AutoTrackShowCardViewModel.cs`
- `Common/TmdbNetworkBlockDetector.cs` (new)
- `Common/TmdbDnsProbe.cs` (new — added after the first live test, see debug note "Live test 1")
- `Services/WarpCliService.cs`
- `Views/SettingsView.xaml` (WARP hint text)
- `MediaManager.App.Tests/Services/*Tests.cs` (new)

## Test gate

See [BUILD.md](../../BUILD.md): x64 MSBuild, Core.Tests (248), App.Tests (31 + new).

Manual (live state, WARP off at start):

1. ~5 min after start: `Anchor check 'A Certain Dark Item (2026)': cycleAnchor=2026-10-24 06:00 … satisfiedCycle=False`.
2. `actively refused` → `TMDB network error (ConnectionRefused)… Attempting WARP self-recover` → `TMDB refresh ok`.
3. From Far Away skipped with "current cycle" until 2026-10-27 08:30.
4. WARP connected by the user before Run Now stays connected after the cycle.
5. Network fully down + Run Now: only one WARP connect timeout per cycle.

## Risks / rollback

- First run after upgrade refreshes every show whose last refresh is older than its cycle anchor (4 auto-tracked shows, cap 10/day).
- Hunt may run on every discovery cycle for the whole 7-day cycle (previously anchor → Saturday only).
- Broader detector may connect WARP when TMDB is down for other reasons; guarded to one attempt per cycle.
- Rollback: revert the PR; no schema or settings changes.
