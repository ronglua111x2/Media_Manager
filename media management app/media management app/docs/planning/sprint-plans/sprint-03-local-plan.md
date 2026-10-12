# Sprint 03 — Local plan / completion record

**Branch:** `auto-torrent`  
**Date:** Aug 2026  
**Source:** [05-sprint-timeline.md § Sprint 3](../05-sprint-timeline.md#sprint-3--critical-path-test-expansion-w6w7-12-h)  
**Cursor plan:** Sprint 3 Critical Paths

## Git baseline

- [x] Work on canonical branch `auto-torrent`
- [x] Tag `four-pillars-sprint-03` after human checklist + commit

## Scope IN / OUT

| In | Out |
| -- | --- |
| Move to Core: evaluation, search, pack, validation (file-list) | FetchJob / AutoTrack integration tests |
| Builders + golden fixtures; ≥40 Core.Tests | UI tests / CI coverage threshold |
| Migration **003** TorrentBlacklist rebuild | Navigation hooks |

## Definition of Done

- [x] ≥40 unit tests total in Core.Tests (80)
- [x] Advisory ≥80% line coverage on parser + evaluation
- [x] No WPF reference from test project
- [x] Manual: cart + pack link (Auto-Track hunt deferred)
- [x] Production DB: `003_torrentblacklist_rebuild` applied (`D:\MediaManagerState`)
- [x] Commit + tag `four-pillars-sprint-03`

## Files touched

- `MediaManager.Core` models/services + migration 003
- `QbittorrentTorrentContentValidationService` App wrapper
- `MediaManager.Core.Tests` (Evaluation/Search/Pack/Validation/Fixtures)
- Docs: `AI_CONTEXT.md`, `STATE_FOLDER.md`, `05`, this plan

## Test gate

```bash
dotnet test MediaManager.Core.Tests/MediaManager.Core.Tests.csproj -c Release
dotnet build "media management app.csproj" -c Release -p:Platform=x64
```

Result (Aug 2026): **80 tests passed**; Release x64 **0 errors**.

## What you do next

Sprint 3 closed. Start Sprint 4 with Plan Mode + `sprint-04-local-plan.md` per 06 §2.0. Optional carry-forward: Auto-Track hunt smoke.
