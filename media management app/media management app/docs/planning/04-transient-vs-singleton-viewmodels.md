# Planning: Transient vs Singleton ViewModels

**Priority:** High  
**Source:** [IMPROVEMENTS.md](../IMPROVEMENTS.md) § Priority: High #4  
**Status:** Option A implemented (Sprint 4) — singleton VMs + `INavigationAware` hooks; Phase 5 transient VMs still reserved

---

## Problem statement

All **seven workspace ViewModels** are registered as **DI singletons** and kept alive for the entire app session. `MainViewModel` switches workspaces by pointing `CurrentView` at the same instance created at startup — there is **no `OnNavigatedTo` refresh** and no disposal when leaving a workspace.

After long sessions or background automation (Auto-Track, reconciliation, cart updates), users may see **stale lists, selection, or in-memory caches** until a manual refresh. Unused workspaces also retain **poster images, search results, and detail panes** in memory.

---

## Why it happened

WPF + DI tutorials commonly register ViewModels as singletons to:

- Preserve navigation state “for free”
- Avoid re-subscribing to events on every visit
- Simplify constructor injection in `MainViewModel`

Media Manager leaned into this: Library and Torrent workspaces **intentionally persist** sort/filter/selection via `settings.json` (`UiSettings`). That worked for UX (return to Library with same show selected) but conflated **persisted preferences** with **live data freshness**.

Background services update the database while the user is on another tab; singleton VMs only refresh if they subscribed to the relevant event — and not all paths emit events the UI listens for.

---

## Current behavior

### DI registration (all singleton)

```203:210:App.xaml.cs
        services.AddSingleton<AutoTrackViewModel>();
        services.AddSingleton<NewsViewModel>();
        services.AddSingleton<FindAddViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<TorrentWorkspaceViewModel>();
        services.AddSingleton<RecipeWorkspaceViewModel>();
        services.AddSingleton<SystemSettingsViewModel>();
        services.AddSingleton<MainViewModel>();
```

No `AddTransient` ViewModels exist. Dialog VMs (`PackLinkReviewViewModel`, etc.) are created ad hoc when opening windows.

### Navigation — swap reference, no lifecycle hooks

```47:56:ViewModels/MainViewModel.cs
        _workspaceMap = new Dictionary<AppWorkspaceKind, ViewModelBase>
        {
            [AppWorkspaceKind.AutoTrack] = autoTrackViewModel,
            [AppWorkspaceKind.News] = newsViewModel,
            [AppWorkspaceKind.FindAdd] = findAddViewModel,
            [AppWorkspaceKind.Library] = libraryViewModel,
            [AppWorkspaceKind.Torrent] = torrentWorkspaceViewModel,
            [AppWorkspaceKind.Recipe] = recipeWorkspaceViewModel,
            [AppWorkspaceKind.SystemSettings] = systemSettingsViewModel
        };
```

```452:461:ViewModels/MainViewModel.cs
    private void NavigateTo(AppWorkspaceKind workspace)
    {
        SelectedWorkspace = workspace;
        CurrentView = _workspaceMap[workspace];
        SelectedWorkspaceLabel = NavigationItems.First(item => item.Kind == workspace).Label;
        // ... update nav selection ...
    }
```

There is **no** `IWorkspaceNavigable`, `OnNavigatedTo`, or `OnNavigatedFrom` anywhere in the codebase.

### What each singleton retains

| ViewModel | Lines | Session state kept in memory |
|-----------|-------|------------------------------|
| `NewsViewModel` | ~small | Weekly episode cards |
| `AutoTrackViewModel` | ~239 | Run status, show cards |
| `FindAddViewModel` | ~736 | TMDB search results, **`_posterImageCache`**, `_allMediaCards` |
| `LibraryViewModel` | ~2,381 | Full media grid, detail VM tree, **`SelectedPosterImage`**, import state |
| `TorrentWorkspaceViewModel` | ~1,965 | Media grid, cart UI, fetch candidates, `_operationCts` |
| `RecipeWorkspaceViewModel` | medium | Open recipe editor state |
| `SystemSettingsViewModel` | ~2,076 | All settings fields (may be stale vs disk if edited externally) |

### Partial mitigation today

**Library** — persists selection/sort to settings; subscribes to cart/reconcile events; releases poster bitmaps on background:

```2346:2367:ViewModels/LibraryViewModel.cs
    private void OnAppModeChanged(object? sender, AppMode mode)
    {
        if (mode == AppMode.Background)
        {
            ReleasePosterMemory();
            return;
        }
        if (SelectedMediaCard is not null)
            _ = ReloadPosterOnForegroundAsync();
    }
```

**Torrent workspace** — similar `_pendingRestoreMediaId` / `RestoreTorrentUiState` pattern (mirrors Library).

**FindAdd** — `RefreshExistingMedia()` on construct only; tracked library changes while away do not auto-refresh.

**Services** — singleton services (`ITrackedShowService`, `ITorrentCartService`, …) correctly hold domain state; the issue is **VM-level caches and grids** not reloading when re-entering workspace.

### View templates

`Resources/ViewTemplates.xaml` maps each workspace VM type to a `DataTemplate`. Transient VMs would still work if `CurrentView` gets a new instance each navigate — templates unchanged.

---

## Impact

| Risk | Detail |
|------|--------|
| **Stale UI** | User returns to Library/Torrent after Auto-Track run; grid not updated until Refresh |
| **Memory** | Posters + full card lists for all workspaces for hours/days |
| **Wrong detail pane** | `_loadedDetailMediaId` optimization may skip reload when underlying DB changed |
| **Duplicate operations** | Torrent `_operationCts` may span navigations by design — cart/search continue off-tab; user Stop cancels |
| **Settings drift** | `SettingsViewModel.LoadFromSettings()` only at startup |
| **Confusing bugs** | Hard to reproduce “left tab open overnight” reports |

Not all stale behavior is bad — **restoring Library selection** from `settings.json` is desired ([LibraryViewModel.RestoreLibraryUiState](../ViewModels/LibraryViewModel.cs)).

---

## Affected areas

| Area | Files |
|------|-------|
| DI bootstrap | `App.xaml.cs` |
| Shell navigation | `ViewModels/MainViewModel.cs`, `MainWindow.xaml` |
| Workspace VMs | All seven singleton VMs + `Views/*View.xaml` |
| UI persistence | `Models/UiSettings` via `ISettingsService` |
| Background updaters | `AutoTrackSchedulerService`, `TorrentReconciliationService`, `ITorrentCartService.CartChanged` |
| Docs | `docs/AI_CONTEXT.md` workspaces section |

---

## Constraints

- **Preserve UX preferences** — Sort, filter, selected media ID must still restore when user wants continuity.
- **Do not recreate expensive subscriptions** naively — transient VMs must unsubscribe in `Dispose` or use weak events.
- **WPF DataContext** — `CurrentView` binding must notify change when VM instance replaced.
- **Background mode** — App minimizes to tray; VMs should not assume visible when services fire events.
- **Single window** — No multi-window navigation stack; simpler than region-based PRISM apps.
- **MainViewModel stays singleton** — Holds timer, device status, viewers.

---

## Options for resolution

### Option A: Keep singletons + `INavigationAware` refresh hooks

Add interface with `OnNavigatedTo()` / `OnNavigatedFrom()`; `MainViewModel.NavigateTo` calls them. Each VM decides: full `RefreshLibrary()` vs. light invalidation.

| Pros | Cons |
|------|------|
| Smallest DI change | Memory retention unchanged |
| Fixes stale data explicitly | Every VM must implement correctly |
| Keeps persisted UI state | Easy to forget a workspace |

### Option B: Transient workspace VMs

Register workspace VMs as transient; `MainViewModel` uses `IServiceProvider.GetRequiredService<LibraryViewModel>()` on each navigate (or factory). Persist preferences only via `UiSettings`, not VM fields.

| Pros | Cons |
|------|------|
| Fresh data each visit | Loses in-memory-only state (scroll position?) |
| Memory released on leave | Must re-subscribe events; dispose properly |
| Clear lifetime | Slightly slower first paint (reload grid) |

### Option C: Hybrid — singleton “coordinator” + transient “view state”

Singleton `LibraryWorkspaceCoordinator` loads data; short-lived or resettable `LibraryViewState` for bindings. Coordinator listens to domain events globally.

| Pros | Cons |
|------|------|
| Centralized refresh logic | New abstraction layer |
| Can refresh coordinator without recreating VM | More design upfront |

### Option D: Scoped lifetime (DI scope per navigation)

Create a scope per workspace visit; dispose scope on leave. Middle ground between A and B.

| Pros | Cons |
|------|------|
| Automatic dispose of scoped services | WPF DI scopes less common; needs `Microsoft.Extensions.DependencyInjection` scope wiring |
| | ViewModels in scope still need careful registration |

**Planning note:** Option A is lowest risk; Option B best for memory; hybrid C fits if splitting VMs ([03-split-large-viewmodels-services.md](./03-split-large-viewmodels-services.md)).

---

## Suggested planning steps

1. **Audit stale scenarios** — Manual test script: run Auto-Track, switch Library → News → Library; note what updates without Refresh.
2. **Define refresh policy per workspace** — e.g. Library: refresh catalog on navigate; preserve selection from settings. Torrent: refresh cart orders always.
3. **Introduce navigation interface** — `INavigationAware` on `ViewModelBase` optional implementation.
4. **Wire MainViewModel** — Call `OnNavigatedFrom` on old, `OnNavigatedTo` on new workspace.
5. **Event hub optional** — `IWorkspaceRefreshService`.Publish(`LibraryCatalogInvalidated`) for background updates while away.
6. **Memory pass** — Ensure `IDisposable` clears poster caches on dispose; Torrent `_operationCts` is **not** cancelled on navigate away (multitask); cancel only via explicit Stop / `BeginOperation` replacement.
7. **Evaluate transient pilot** — Try `FindAddViewModel` transient first (smallest blast radius, no detail persistence).
8. **Measure** — Working set before/after long session with 500+ library items.

### Suggested `OnNavigatedTo` behaviors (draft)

| Workspace | On navigate to |
|-----------|----------------|
| News | Reload weekly data if cache older than N minutes |
| Auto-Track | Refresh show cards + scheduler status |
| FindAdd | `RefreshExistingMedia()` |
| Library | `RefreshLibrary()` + restore selection from settings |
| Torrent | Reload media cards + cart list (`RefreshWorkspace`); leave mid-run allowed |
| Recipe | Reload recipe list if `RecipesChanged` fired while away |
| Settings | `LoadFromSettings()` |

---

## Open questions

1. **Primary goal** — Freshness, memory, or both? (Drives A vs B.)  
2. **Scroll/grid position** — Must persist within session when switching tabs?  
3. **Refresh cost** — Is full `RefreshLibrary()` acceptable on every Library visit (large libraries)?  
4. **Transient + split VMs** — Do navigation changes come before or after [03-split-large-viewmodels-services.md](./03-split-large-viewmodels-services.md)?  
5. **FindAdd poster cache** — Recreate or move cache to singleton `IPosterImageService` only?  
6. **Settings workspace** — Reload on every visit may wipe unsaved edits — need dirty tracking?

---

## Relationship to other High-priority items

| Item | Interaction |
|------|-------------|
| [03-split-large-viewmodels-services.md](./03-split-large-viewmodels-services.md) | Smaller VMs make transient recreation cheaper |
| [02-unit-tests-critical-paths.md](./02-unit-tests-critical-paths.md) | Navigation coordinator testable without WPF |
| [01-database-migration-versioning.md](./01-database-migration-versioning.md) | Independent |
