# Sprint 04 — Local plan / completion record

**Branch:** `auto-torrent`  
**Date:** Aug 2026  
**Source:** [05-sprint-timeline.md § Sprint 4](../05-sprint-timeline.md#sprint-4--navigation-refresh-hooks-e3-w8w9-12-h)  
**Cursor plan:** Sprint 4 Navigation Hooks

## Git baseline

- [x] On canonical branch `auto-torrent`
- [x] Working tree reviewed before Agent Mode
- [x] Tag `four-pillars-sprint-04` after human checklist + commit

## Scope IN / OUT

| In | Out |
| -- | --- |
| `INavigationAware` on `ViewModelBase` | Transient VMs (Phase 5) |
| `MainViewModel.NavigateTo` invokes hooks | Splitting large VMs |
| Per-workspace refresh policy (04) | Full `IWorkspaceRefreshService` |
| Torrent cart/search **continue** off-tab (explicit Stop only) | Cancel `_operationCts` on leave |
| FindAdd: `RefreshExistingMedia()` on navigate | Settings dirty-tracking (Sprint 6) |

## Definition of Done

- [x] All seven workspace VMs implement refresh policy (documented in `AI_CONTEXT.md`)
- [x] Repro scripts 1–5 pass (human; checkboxes in 05) — scripts 1–6 all passed
- [x] No new singleton memory leaks from duplicate event subscriptions
- [x] Torrent (and similar UI long-ops) do **not** cancel solely because the user changed workspace
- [x] `dotnet test` + Release x64 green (80 tests)
- [x] Commit + tag `four-pillars-sprint-04`

## Refresh policy (shipped)

| Workspace | OnNavigatedTo | OnNavigatedFrom |
| --------- | ------------- | --------------- |
| News | `UpdateDashboard` if cache older than 2 min | no-op |
| Auto-Track | `RefreshDashboard` | no-op |
| FindAdd | `RefreshExistingMedia` + search `IsAlreadyAdded` flags | no-op |
| Library | Clear detail-load cache; `RefreshLibrary`; reload detail | no-op |
| Torrent | `RefreshWorkspace` | **no-op** — cart/search keep running |
| Recipe | `ReloadRecipes` if `RecipesChanged` while away | mark inactive |
| Settings | `Load()` + `LoadFromSettings()` (no dirty prompt) | no-op |

## Carry-forward

- Library poster **No cover** on `Reconciled` while detail is open → Sprint 8 (scoped detail refresh / detail VM ownership).

## What you do next

Sprint 4 closed. Start Sprint 5 with Plan Mode + `sprint-05-local-plan.md` per 06 §2.0. Optional: progress-review chat (§2.1a) before S5.
