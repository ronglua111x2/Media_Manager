# Poster flash — surgical fix (local plan)

**Branch:** `auto-torrent`  
**Date:** Aug 2026  
**Status:** Plan only — **do not implement in the freeze session.** Next Agent session implements this file.  
**Not a sprint:** E4 Library catalog/detail split (Sprint 8) is frozen. This is a one-PR bugfix.  
**Source:** Sprint 4 known debt in [05-sprint-timeline.md](../05-sprint-timeline.md); `docs/AI_CONTEXT.md` `library_reconcile_poster`

## Git baseline (implementer)

- [ ] `git status -sb` on `auto-torrent`
- [ ] Working tree otherwise clean or unrelated dirt left unstaged

## Bug (confirmed in code)

Auto-Track / torrent **`ITorrentReconciliationService.Reconciled`** and **`IPackLinkCoordinatorService.PackReconciled`** both call **`LibraryViewModel.RunReloadSelectedDetailOnUiThread`**, which runs **`ReloadSelectedDetailAsync` → `LoadSelectedMediaAsync(SelectedMediaCard)`**.

`LoadSelectedMediaAsync` **always** sets `SelectedPosterImage = null`, then rebuilds `SelectedShow` / `SelectedMovie`, then `await IPosterImageService.LoadAsync(...)`. While the user is already on Library with a title selected, `Views/LibraryView.xaml` shows the **No cover** placeholder until the async load completes (or stays blank until navigate away/back).

Pending-queue-only reconcile in `AutoTrackService.GetPendingAutoTrackReconcileShowIds` + `TorrentReconciliationScope.ForMedia` **reduces frequency**. It does **not** fix the Library handler.

Navigate-away (`OnNavigatedTo` → `ReloadSelectedDetailAsync`) is **not** the fix.

## Exact types / files

| Piece | Location |
| ----- | -------- |
| Workspace VM | `ViewModels/LibraryViewModel.cs` |
| Detail XAML | `Views/LibraryView.xaml` — overlay `TextBlock` **No cover**; `Image Source="{Binding SelectedPosterImage}"` |
| Poster property | `[ObservableProperty] selectedPosterImage` → `SelectedPosterImage` (`ImageSource?`) |
| Full detail load | `LoadSelectedMediaAsync(LibraryMediaCardViewModel? card)` — **clears poster** |
| Reconcile marshal | `RunReloadSelectedDetailOnUiThread()` → `ReloadSelectedDetailAsync()` |
| Event wiring (ctor) | `_torrentReconciliationService.Reconciled += (_, _) => RunReloadSelectedDetailOnUiThread();` |
| | `packLinkCoordinatorService.PackReconciled += (_, _) => RunReloadSelectedDetailOnUiThread();` |
| Show rebuild (no poster) | `RebuildSelectedShowDetail()` — already rebuilds seasons/episodes from DB |
| Cart/link commands | `RefreshCartStateOnSelectedDetail()` — cart flags + `NotifyCanExecuteChanged`; **does not touch poster** |
| Movie builder | `BuildMovieDetail(TrackedMovie, IReadOnlyList<SourceItem>)` — no `RebuildSelectedMovieDetail` yet |
| Poster IO | `Services/PosterImageService.cs` — `IPosterImageService.LoadAsync(posterPath, mediaKind, tmdbId)` |
| Episode reconcile | `Services/TorrentReconciliationService.cs` — `event EventHandler? Reconciled` (no media id in args) |
| Pack reconcile | `Services/IPackLinkCoordinatorService.cs` — `event EventHandler<PackReconcileResult>? PackReconciled` |
| Pack payload | `Models/PackReconcileResult.cs` — `Ran` / `Skipped` / `Inventory` / `LinkResult`; **no show id** |
| Selection change | `OnSelectedMediaCardChanged` → `LoadSelectedMediaAsync` (keep full reload + poster clear) |
| Tab return | `OnNavigatedTo` → `ReloadSelectedDetailAsync` (keep; not the bug path) |
| Tray/background | `OnAppModeChanged` / `ReleasePosterMemory` / `ReloadPosterOnForegroundAsync` — **do not change** |

`ITorrentReconciliationService.Reconciled` is `EventHandler?` (empty args). You cannot skip by “different media than selected” without changing the event. Do **not** expand that contract in this PR unless a one-line payload is trivial; default is refresh **currently selected** detail only.

## Scope IN / OUT

| In | Out |
| -- | --- |
| Library **scoped** detail refresh after reconcile | `LibraryDetailViewModel` / catalog-detail split |
| Keep `SelectedPosterImage` bitmap | AutoTrack service split |
| Refresh link / availability / cart / episode-season rows | Settings split / Sprint 4.5 hygiene |
| Same path for `Reconciled` and `PackReconciled` | New unit tests unless a tiny pure helper is extracted |
| | Changing `OnNavigatedTo` as the “fix” |

## Proposed approach

Keep poster; rebuild **non-poster** detail only. Prefer existing helpers over a second full load.

1. **Stop using `LoadSelectedMediaAsync` on reconcile.** Change ctor handlers to a new UI-thread method, e.g. `RunRefreshSelectedDetailAfterReconcileOnUiThread` → sync `RefreshSelectedDetailAfterReconcile()` (or keep dispatcher hop, drop the async poster load).
2. **If `SelectedMediaCard` is null**, return. No selection → nothing to flash.
3. **Capture identity first:** `id = SelectedMediaCard.Id`, `kind = SelectedMediaCard.MediaKind`. If selection changes before apply, abort (see edges).
4. **Show:** call existing `RebuildSelectedShowDetail()` (already leaves `SelectedPosterImage` alone), then `RefreshCartStateOnSelectedDetail()`.
5. **Movie:** add a small `RebuildSelectedMovieDetail()` mirroring the show helper: `BuildMovieDetail` + watch/rating UI from DB, **do not** set `SelectedPosterImage = null`, **do not** call `LoadAsync`. Then `RefreshCartStateOnSelectedDetail()`.
6. **Do not** null `SelectedShow`/`SelectedMovie` before rebuild if that would blank the whole pane; replace in place like `RebuildSelectedShowDetail` already does.
7. Leave `LoadSelectedMediaAsync` for **selection change** and **`OnNavigatedTo`**. Those paths may still clear poster (correct when the card changed).

Optional hardening (only if cheap): generation counter around any remaining `await`; ignore stale completions. Prefer staying **synchronous** on the reconcile path so this is unnecessary.

Do **not** extract a detail VM. Do **not** change Auto-Track pending-queue policy as the fix.

## Edge cases

| Case | Expected |
| ---- | -------- |
| Selection changes during reconcile | Capture id/kind at start of UI work; if `SelectedMediaCard` no longer matches, **do not** apply rebuild. Full `LoadSelectedMediaAsync` from `OnSelectedMediaCardChanged` owns the new card (including poster). |
| Reconcile for a **different** title than selected | Events have **no media id**. Refresh **selected** detail only (availability/cart may still have changed). Do not clear poster. Do not reload the whole grid unless already required elsewhere. |
| Pack vs episode reconcile | Same scoped refresh. Pack updates season pack link/inventory; episode updates episode rows. `RebuildSelectedShowDetail` rereads DB/source items for both. |
| User **not** on Library tab | Singleton VM still receives events. Scoped refresh is fine (no visible flash). Do not depend on `OnNavigatedTo`. Optional: skip work if Library is not current workspace — **not required** for the bug. |
| No poster on disk yet | Leave `SelectedPosterImage` null; **No cover** overlay stays. Do **not** `LoadAsync` on reconcile. |
| Poster was showing; reconcile completes | Bitmap stays; episode/pack/cart/link text updates. |
| Background/tray mode | Existing `ReleasePosterMemory` / `ReloadPosterOnForegroundAsync` unchanged. |
| `PackReconcileResult.Skipped` | Still cheap to refresh selected show; or no-op if `Skipped && !Ran` — either is OK if poster is never cleared. |

## Manual test checklist

Use a library item that **already has a cover** on disk. Stay on Library with detail open.

- [ ] Reconcile (Auto-Track or torrent) while Library detail is open — poster does **not** flash **No cover**
- [ ] Same for a **season pack** reconcile (`PackReconciled`) with detail open — no cover flash
- [ ] After reconcile, episode/pack **availability / link / cart** state updates without changing tabs
- [ ] Select a **different** title, then reconcile — new title’s poster still loads via `LoadSelectedMediaAsync`; no stale detail from the previous title
- [ ] Reconcile while a **different** show is selected than the one that finished — selected poster stays; no crash
- [ ] Reconcile while **not** on Library, then open Library — detail/poster sane (navigate-to full load still OK)
- [ ] Title with **no** poster file — still **No cover**; no crash
- [ ] Movie detail open during reconcile — poster stays; cart/link flags update
- [ ] Sprint 4 Library navigate script still OK (leave Library, return, selection restored)
- [ ] Cart add/remove on detail still updates without touching poster (`CartChanged` path unchanged)

## Test / build gate

No new unit tests required unless a tiny pure helper is extracted.

```bash
APP_ROOT="d:/VScode/Misc/Media_Manager/media management app/media management app"
dotnet test "$APP_ROOT/MediaManager.Core.Tests/MediaManager.Core.Tests.csproj" -c Release -v normal
# MSBuild x64 Release (see docs/BUILD.md); Git Bash: MSYS_NO_PATHCONV=1 for /p:
```

## Risk / rollback

**Risk:** Detail stale after reconcile (link/availability not updated) if rebuild misses a field that `LoadSelectedMediaAsync` set. **Mitigation:** manual checklist; reuse `RebuildSelectedShowDetail` + `BuildMovieDetail` + `RefreshCartStateOnSelectedDetail`.  
**Rollback:** one small PR — restore ctor handlers to `RunReloadSelectedDetailOnUiThread`.  
**Do not** fold this into a Library or AutoTrack split.

## Definition of Done

- [ ] Reconcile/pack-reconcile while Library detail is open: poster does not flash **No cover**
- [ ] Link/availability/cart/episode state still refreshes
- [ ] No `LibraryDetailViewModel` extract
- [ ] `dotnet test` + MSBuild x64 Release green
- [ ] `AI_CONTEXT.md` `library_reconcile_poster` note updated when the fix ships
