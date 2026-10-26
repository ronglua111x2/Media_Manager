# Auto-Track interrupted orders: local plan

**Branch:** `fix/autotrack-interrupted-orders` (cut from `main` @ `dbc95c8`)
**Date:** 2026-10-26
**Status:** Implemented. **Not yet verified in a real crash or power-loss test.** Merged to `main` via PR #8 with label `release:skip`, so `auto-release.yml` creates **no GitHub Release**. Used from a local publish; migration 008 applied cleanly on the live DB (2026-10-26). Release once the manual test below passes.
**Not a sprint:** one-PR bugfix.

## Git baseline

- [x] `git fetch origin`, branch cut from up-to-date `origin/main` (`dbc95c8`, includes PR #7)
- [x] Working tree clean before starting
- [x] PR #7 code (rolling anchor cycle + WARP recovery) not touched

## Bug (confirmed in code, not reproduced live)

1. The hunt's `catch (OperationCanceledException) { throw; }` never rolls back order status. Orders left in `Searching` / `Approved` count as blocking in `TorrentCartService.TryGetAutoTrackHuntBlockingEpisodeOrder`. Only `CandidatesFound` is resumed, so the episode is skipped by every hunt until **Reset week**.
2. The add gate adds the torrent **running** (AUD-001) and validates for up to about 90 s. The hash used to reach the DB only **after** validation. Killing the app in that window left an unvalidated running torrent that the DB did not know about. Reconcile could later adopt it by name (`AdoptManualTorrentsAsync`) without validation.

Two kinds of stop:

| Stop | Does cleanup code run? |
| ---- | --------------------- |
| Graceful (close app → `_shutdown.Cancel()`) | Yes |
| Hard (power loss, BSOD, kill, crash) | **No** |

So the fix is mainly **startup recovery from persisted DB state**. Rollback on cancel is best effort.

## Scope IN

- `TorrentCartOrders.PendingValidationHash` (migration `008_torrentcart_pending_validation_hash` plus the `EnsureColumn` safety net).
- `ITorrentAddGateService.AddPausedValidateAndResumeAsync(..., onTorrentAdded)`, called with the hash before validation. Also `TryGetExistingByHashAsync`.
- `AutoTrackService.AddOrderToClientAsync` saves the pending hash in the callback. On success it promotes the hash to `TorrentHash`. On a verified reject or a generic error it clears it. On unverified cleanup it keeps it.
- `TorrentReconciliationService` seeds `usedHashes` with all pending hashes, so they are never adopted. It also skips orders that have a pending hash in `SyncOrdersFromTrackedItems`.
- `RecoverInterruptedOrdersOnceAsync` runs once per session, before the hunt queue and before background reconcile. The decision rules live in `Services/InterruptedOrderRecovery.cs`; see `docs/FEATURES.md` §10.3.
- `TorrentCartService.ReleaseInterruptedAutoTrackOrder(orderId, keepCandidates, detail)`.
- On graceful cancel during search: `Searching` → `Draft`.

## Scope OUT

- Torrents with no trace in the DB (the few-ms window between qBittorrent add and the callback, HTTP listings only). They are not deleted automatically.
- Reset week (`ReleaseAutoTrackHuntBlocks`) does not clear `PendingValidationHash`, so an orphaned unvalidated torrent stays non-adoptable until the next add for that order overwrites the hash. It is not deleted.
- Generic add-gate errors that are not rejections (API error mid-validation) still clear the pending hash, as before. Possible follow-up.
- No GitHub Release, no merge to `main`.

## Definition of Done

- [x] Merge-gate build green (`dotnet build -c Release -p:Platform=x64`)
- [x] Core.Tests green (248)
- [x] App.Tests green (93 after merging main @ 779bee7; +29 in `Services/InterruptedOrderRecoveryTests.cs`, `Services/TorrentAddGatePendingHashTests.cs`)
- [x] `docs/FEATURES.md` §10.3/§10.4/§10.6, `docs/AI_CONTEXT.md`, `docs/BUILD.md`, `docs/STATE_FOLDER.md` updated
- [x] PR #8 with `release:skip`, merged without a release
- [x] Live DB: `SchemaMigrations` has `008_torrentcart_pending_validation_hash`; app 1.0.5-alpha starts with no errors
- [x] Local publish to the install folder (exe backup kept)
- [ ] Manual test (user, later): start a hunt, `taskkill /F` the app during "Add with validation", restart. The log should show `Interrupted Auto-Track order ... RevalidateExisting` and `Recovered N/M`. The torrent should be re-validated and attached, or the order released. The episode should be hunted again without Reset week. Repeat with a kill during "Searching".

## Touch list

| File | Change |
| ---- | ------ |
| `MediaManager.Core/Migrations/008_torrentcart_pending_validation_hash.sql` | new column |
| `Services/DatabaseService.cs` | select/insert/upsert/read + `EnsureColumn` |
| `Models/TorrentCartOrder.cs` | `PendingValidationHash` |
| `Services/ITorrentAddGateService.cs` | `onTorrentAdded`, `TryGetExistingByHashAsync` |
| `Services/InterruptedOrderRecovery.cs` | new pure rules |
| `Services/AutoTrackService.cs` | recovery, pending hash persist/clear, cancel rollback |
| `Services/TorrentCartService.cs` | `ReleaseInterruptedAutoTrackOrder` |
| `Services/TorrentReconciliationService.cs` | never adopt pending hashes |
| `MediaManager.Core.Tests/Migrations/MigrationRunnerTests.cs` | expect 008 |
| `MediaManager.App.Tests/Services/*` | new tests |

## Test gate

```bash
dotnet build "media management app/media management app/media management app.csproj" -c Release -p:Platform=x64 -v minimal
dotnet test "media management app/media management app/MediaManager.Core.Tests/MediaManager.Core.Tests.csproj" -c Release -p:Platform=x64
dotnet test "media management app/media management app/MediaManager.App.Tests/MediaManager.App.Tests.csproj" -c Release -p:Platform=x64
```

## Risks and rollback

- Recovery runs before the first hunt and can take up to about 90 s per interrupted order with a live torrent (re-validation).
- Rollback: restore the `.bak-*` exe in the install folder (or `scripts/install-release.ps1 -Rollback`). The extra DB column is harmless to older builds.
