# Feature note: Find quick-add route and per-item Jellyfin refresh

Status: implemented 2026-11-24 (see "As built" at the end). Written 2026-11-24 as an idea note; the proposal sections below are kept as written.

Goal: make the path **Find → add to library → add to cart → search torrent → link → visible in Jellyfin** feel like one flow instead of four tab hops.

---

## 1. Quick-add route in Find

### Today

`ViewModels/FindAddViewModel.cs` `AddSelectedToLibraryAsync` (around line 258):

1. Shows `AddMediaLockDialog`.
2. Movie: `_trackedMovieService.AddMovieAsync` + `UpdateRecipe`. Show: optional `EpisodeOrganizationDialog`, `_trackedShowService.AddShowAsync`, `UpdateRecipe` and `UpdatePackRecipe`.
3. Sets status "Added ... to library". The user then has to open Library, find the item, add it to the cart (`TorrentCartService.AddMovieOrder` / `AddEpisodeOrder` / `AddSeasonPackOrder`), open the Cart and start the search.

### Proposal

Add a second button next to **Add to library**: **Add + search** (split button or a secondary button, same `CanExecute` as add).

Flow:

1. Run the existing add logic. Refactor it into a private method that returns the new `TrackedMovie` / `TrackedShow` (or null on cancel), so both buttons share it. Do not duplicate the dialogs.
2. Queue cart orders:
   - **Movie:** `AddMovieOrder(movie.Id, title)`.
   - **Show:** small picker (inline popup or the lock dialog extended with a choice):
     - *Season packs for all aired seasons* (default for ended shows) → `AddSeasonPackOrder(showId, season)` per season.
     - *Latest season only* → one season pack.
     - *Missing aired episodes* → `AddEpisodeOrder` per aired episode (cap the count; above the cap, fall back to packs and say so).
     - *Library only* → stop here (same as today).
   - Skip specials (season 0) unless picked.
3. Start the cart search for just those orders (reuse whatever the Cart "Search" command calls; do not search the whole cart).
4. Feedback: status line in Find "Added + N orders queued, searching..." with a link/button **Open cart** that navigates to the Cart tab focused on the new orders. Do not auto-switch tabs; the user may keep browsing Find.

### Rules and edge cases

- Recipe: use `SelectedRecipeId` (movie / episode) and `GetDefaultRecipeId(MediaKind.TvSeasonPack)` exactly as the add step already does, so the search uses the same recipe the item was saved with.
- Already in library (`IsAlreadyAdded`): the button becomes **Search now** and only does steps 2–4.
- Cancel in any dialog: nothing is queued. If the add succeeded but queueing fails, keep the library item and show the error (no rollback).
- Metadata for a new show may still be loading (episodes not in DB yet). Await the add, then read episodes; if empty, queue season packs from `SeasonCount` instead.
- Avoid duplicate orders: check the cart service for an existing order for the same movie/episode/season before adding.
- Respect the cart's concurrency limits; quick-add must not bypass the search queue or the torrent intake checks (pre-add metadata check, hash blacklist, reset hashes).
- Log one line per quick-add: item kind, orders queued, recipe ids.

### Tests

- Shared add method returns null on cancel → no cart calls.
- Movie → exactly one `AddMovieOrder`.
- Show with each picker option → expected order set; duplicate order not re-added.
- Already-added item → no add call, orders queued.

---

## 2. Per-item "Refresh in Jellyfin" after a link

### Today

- `Services/JellyfinLibraryRefreshService.cs` already has:
  - `EnqueueFromSourceItem(SourceItem item, TrackedShow? show = null)` (line 49)
  - `EnqueuePaths(IEnumerable<string> symlinkPaths)` (line 98)
  - `FlushAsync()` and `TestConnectionAsync()`
- `Services/Symlink/SymlinkCoordinatorService.cs` already enqueues automatically on link/move/delete (lines 276, 392, 484, 597) and flushes (493).
- The only manual trigger is in Settings (`SettingsViewModel` around line 861), which flushes whatever is queued. There is no per-item button.

So the automatic path exists. The button is for when Jellyfin did not pick the item up (debounce missed, Jellyfin was down, item linked by Reconcile/adopt, user fixed a name by hand).

### Proposal

A command **Refresh in Jellyfin** that, for one library item:

1. Collects the item's current symlink paths (movie: its link file; show: the episode link, or all link paths of a season/show).
2. Calls `EnqueuePaths(paths)` then `FlushAsync()` right away (no debounce wait).
3. Shows the result: "Jellyfin refresh sent for N path(s)" or the error from the service (not configured / unreachable).

Add an `IJellyfinLibraryRefreshService.RefreshNowAsync(IEnumerable<string> paths, CancellationToken)` that does enqueue + flush and returns a small result (paths sent, ok/failed), so view models do not call two methods.

### Where to put it (recommended order)

1. **Library tab, item context menu and detail pane** (main place). Movie row and episode row: *Refresh in Jellyfin*. Season header and show header: same, for all linked episodes under it. Enabled only when the item has at least one symlink and Jellyfin is configured. This covers every way an item gets linked (cart, Reconcile, adopt, manual).
2. **Cart order row, after the order reaches Linked/Done**: a small refresh icon beside the status. Same command, paths from that order's links. Useful right after a download finishes.
3. **Toast/status after Reconcile or adopt** that linked items: "N items linked · Refresh in Jellyfin" button that sends all of them in one flush.

Not in Find (nothing is linked yet at that point) and not as a new Settings button (Settings already has the global flush).

### Rules and edge cases

- Only send paths that exist on disk; skip and count missing ones.
- Throttle: disable the button for a few seconds after a click; the service should also merge with any pending debounce batch instead of sending twice.
- If Jellyfin is not configured, hide the menu item (or show it disabled with a tooltip), do not throw.
- Log path count and outcome, not full paths at Info level (paths contain titles).

### Tests

- `RefreshNowAsync` with an empty list → no HTTP call.
- Paths that do not exist are filtered out.
- Not configured → returns a "not configured" result, no exception.
- Library command enable/disable follows "has symlink" and "Jellyfin configured".

---

## Order of work

1. Part 2 first (small, self-contained): `RefreshNowAsync` + Library context menu.
2. Part 1: refactor the add method, then the **Add + search** button, then the show picker.
3. Then the cart-row icon and the Reconcile toast.

---

## As built (2026-11-24)

### 0. Add-to-library confirm dialog is off

- `FindAddViewModel.ConfirmBeforeAdd` (`public static readonly bool`, `false`). When on, the shared add step shows `AddMediaLockDialog` first. The dialog class and XAML are unchanged.
- `EpisodeOrganizationDialog` is still shown for shows with TMDB episode groups (it is a real choice).
- Dialogs go through `IFindAddDialogs` (`WpfFindAddDialogs` in the app), so the flow is unit-tested without windows.

### 2. Per-item "Refresh in Jellyfin"

Service (`Services/JellyfinLibraryRefreshService.cs`, result types in `Services/JellyfinRefreshNowResult.cs`):

- `IJellyfinLibraryRefreshService.IsConfigured` and `RefreshNowAsync(paths, ct)` → `JellyfinRefreshNowResult(Outcome, SentCount, SkippedCount, Error)`; outcomes `Sent`, `NothingToSend`, `NotConfigured`, `Failed`.
- Paths are normalized and de-duplicated; paths missing on disk are skipped and counted.
- Not configured, or nothing left to send: no HTTP call, no throw.
- The pending debounce batch is cancelled and merged into the same notify, so nothing goes out twice.
- It returns as soon as Jellyfin accepted or rejected the notify. The post-notify WARP hold keeps running in the background, as for the debounced flush. A transient failure keeps the paths queued for the normal backoff retry and returns `Failed`.
- Logs counts only (no paths at Info).
- `JellyfinRefreshTargets.ForMovie` / `ForShow(tmdbId, items, season?, episode?)` pick the linked, accepted source items' symlink paths for a title.

Where it is in the UI:

- **Library** (`ViewModels/LibraryViewModel.JellyfinRefresh.cs`):
  - right-click an episode row → *Refresh in Jellyfin*;
  - right-click a season header → *Refresh season in Jellyfin* (all linked episodes of the season);
  - detail pane header: a **Jellyfin** button (refresh icon) for the selected movie or the whole show, shown when the title has a link.
  - Enabled only when the item has a link on disk and Jellyfin is configured. Disabled while a request runs plus 3 s. The result goes to the status line.
- **Cart order row**: a small TV icon once the order is `Completed` and Jellyfin is configured. It sends that order's links (movie, the episode, or the pack's season).
- **After Reconcile / Import existing media** (Library): the link paths that are new since before the run are kept, and a **Refresh N new link(s) in Jellyfin** button appears next to the status line. One click sends them all in one notify.

Not added: a card-list context menu in Library (the card has no link state), anything in Find, and a new Settings button.

### 1. Find quick-add

- `AddSelectedToLibraryAsync` now calls the shared `AddToLibraryCoreAsync(result)`, which returns a `QuickAddTarget` (kind, id, title) or null on cancel.
- **Add + search** button next to **Add to Library** in both detail layouts. When the result is already in the library it reads **Search now** (no add, steps 2–4 only).
- Movie: one `AddMovieOrder`.
- Show: `QuickAddShowDialog` picker first (cancel = nothing added, nothing queued):
  - season packs for every aired season (default);
  - latest aired season only;
  - missing aired episodes (`AddEpisodeOrder`), capped at `FindQuickAddPlanner.MaxEpisodeOrders` (24); above the cap it falls back to packs for the seasons with missing episodes and says so;
  - library only (hidden for *Search now*).
- Order selection is `FindQuickAddPlanner.PlanShow` (pure): it skips season 0, episodes TMDB removed and episodes that have not aired. With no episodes in the DB yet it uses the result's `SeasonCount`.
- Orders already active in the cart (`TryGetActive*Order`) are skipped and counted; only new orders are searched.
- Search: `ICartSearchLauncher.QueueSearch` (implemented by `TorrentWorkspaceViewModel`). Run Cart's loop was split into `RunOrdersSearchAsync(kind, mediaId, orders, context)`, so Run Cart and quick-add share one search path.
  - A queued search waits until no cart operation is running (run, add, retry, reconcile) and then runs like Run Cart. Stop in the Cart stops it too.
  - It only searches and stores candidates. Adding torrents is still the Cart's Add step with all intake checks: pre-add metadata check, hash blacklist, reset hashes.
  - The search uses the recipes and cart overrides saved on the item, i.e. the ones the add step just set (`SelectedRecipeId`, `GetDefaultRecipeId(MediaKind.TvSeasonPack)`).
- Feedback: the Find status line says what was queued; an **Open cart** button opens the Cart tab on that title. Find does not switch tabs. When the search ends, the Find status shows its result.
- If the add worked but queueing failed, the library item stays and the error is shown.
- One log line per quick-add: kind, option, new orders, skipped, pack fallback, recipe ids.

### Tests

- `MediaManager.App.Tests/Services/JellyfinRefreshNowTests.cs`: empty list, missing paths skipped, only-missing, not configured, merge with the pending debounce batch (one notify, nothing sent twice), unreachable server, path targeting per movie / show / season / episode.
- `MediaManager.App.Tests/Services/FindQuickAddTests.cs`: planner per option, season 0 / unaired skipped, cap fallback, `SeasonCount` fallback. Flow: confirm dialog off, add without the confirm dialog, movie → exactly one order, picker cancel → nothing added or queued, pack already in cart not re-added, library only, *Search now* does not re-add, nothing new → no search.
- Not unit-tested: the WPF enable/disable bindings of the Library commands (manual test).

