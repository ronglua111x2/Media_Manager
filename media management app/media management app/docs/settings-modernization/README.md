# Settings Modernization — Audit & Planning

**Status:** Historical audit. **Sprint 5/6 not resuming.** User chose **not** to pick a full modernization track. Settings 7-VM split is **cancelled** with E4. Do not start Settings VM extraction. Optional Sprint 4.5 hygiene is out of scope here.

## Why this exists

The app started as a small WPF shell and grew into a multi-workspace media manager. Settings accumulated in one place:

- **~2,084 lines** — `ViewModels/SettingsViewModel.cs`
- **~2,142 lines** — `Views/SettingsView.xaml`
- **7 UI tabs** bound to **one** ViewModel and **one** `settings.json`

The original [Sprint 5 plan](../planning/sprint-plans/sprint-05-local-plan.md) assumed clean section boundaries. Code review shows **UI tabs, ViewModel properties, and JSON roots do not align** — which is why a naive sub-VM split is high-risk.

This folder remains the **read-only record** of how settings work (structure, save model, ownership). It is **not** a green light to resume E4 splits.

## Documents (read in order)

| # | File | Contents |
|---|------|----------|
| 0 | [00-executive-summary.md](./00-executive-summary.md) | Top findings, freeze status, historical next-step ideas |
| 1 | [01-current-architecture.md](./01-current-architecture.md) | Data flow: disk → `SettingsService` → VM → UI |
| 2 | [02-settings-json-schema.md](./02-settings-json-schema.md) | `AppSettings` tree, who reads/writes each section |
| 3 | [03-ui-section-audit.md](./03-ui-section-audit.md) | 7 tabs: bindings, commands, misplaced fields |
| 4 | [04-save-load-apply-problems.md](./04-save-load-apply-problems.md) | Apply* coupling, live mutations, dual writers |
| 5 | [05-split-boundaries-recommendation.md](./05-split-boundaries-recommendation.md) | Where to cut VMs **after** modernization |
| 6 | [06-modernization-options.md](./06-modernization-options.md) | Options A–D for a cleaner settings mechanism |
| 7 | [07-pre-sprint-checklist.md](./07-pre-sprint-checklist.md) | Historical gate — **not** a resume trigger |

## Related planning docs

- [03-split-large-viewmodels-services.md](../planning/03-split-large-viewmodels-services.md) — original split design (Option B sub-VMs)
- [05-sprint-timeline.md](../planning/05-sprint-timeline.md) — Sprint 5–6 **frozen**; E4 cancelled
- [06-ai-execution-guide.md](../planning/06-ai-execution-guide.md) — execution workflow

## Out of scope (this audit)

- No runtime/code changes
- No `settings.json` schema migration yet
- No Sprint 5 implementation
