# Planning Documents

Pre-implementation planning for **High priority** technical debt identified in [IMPROVEMENTS.md](../IMPROVEMENTS.md). **Sprints 0–4 complete** on **`auto-torrent`**. **Sprints 5–10 (E4) frozen/cancelled.** Do not resume Sprint 5. Next code work: [poster-flash-surgical-fix.md](./sprint-plans/poster-flash-surgical-fix.md).

**Branch context:** `auto-torrent` (Sprint 1 tagged `four-pillars-sprint-01`; Sprint 2 tagged `four-pillars-sprint-02`)

**Before every new sprint:** [06 §2.0 — git check + Plan Mode local plan](./06-ai-execution-guide.md#20-mandatory-pre-sprint-workflow-git--plan-mode)

---

## Start here

**[00-integrated-roadmap.md](./00-integrated-roadmap.md)** — How the four High-priority items connect, a phased timeline (not big-bang), and whether each item is an industry / desktop-app standard.

**[05-sprint-timeline.md](./05-sprint-timeline.md)** — Locked decisions, sprint-by-sprint goals (~22 weeks part-time), test gates, migration milestones, and unit test matrix. **Start here when scheduling work.**

**[06-ai-execution-guide.md](./06-ai-execution-guide.md)** — **How to execute** the sprint plan with AI: **git check before code**, **Plan Mode → local plan doc** before Agent Mode, compressed calendar, prompt templates, pre-merge checklist, and per-sprint playbook.

**[sprint-plans/](./sprint-plans/)** — Per-sprint local plans created in Plan Mode (template in README).

---

## High Priority Items

| # | Topic | Document | Summary |
|---|--------|----------|---------|
| 1 | Database migration versioning | [01-database-migration-versioning.md](./01-database-migration-versioning.md) | **Sprint 2 implemented:** `SchemaMigrations` + `MigrationRunner`; FetchJobs purge is one-time migration 002. EnsureColumn chain still a transition safety net. |
| 2 | Unit tests for critical paths | [02-unit-tests-critical-paths.md](./02-unit-tests-critical-paths.md) | No test project exists; torrent parsing, scoring, search plans, pack mapping, and content validation are untested regex/logic-heavy code on the Auto-Track and cart pipeline. Planning covers xUnit setup, fixture strategy, and Core extraction tradeoffs. |
| 3 | Split large ViewModels/services | [03-split-large-viewmodels-services.md](./03-split-large-viewmodels-services.md) | `SettingsViewModel` (~2k lines), `LibraryViewModel` (~2.4k), and `AutoTrackService` (~1.5k) combine many features in single types; settings UI already has 7 sections but one VM. Planning maps split candidates and partial vs sub-VM vs service extraction options. |
| 4 | Transient vs singleton ViewModels | [04-transient-vs-singleton-viewmodels.md](./04-transient-vs-singleton-viewmodels.md) | All seven workspace VMs are DI singletons; navigation swaps `CurrentView` with no refresh hooks, so long sessions can show stale grids while retaining poster/search memory. Planning compares navigation-aware refresh vs transient recreation vs hybrid coordinators. |
| 5 | Sprint timeline (execution schedule) | [05-sprint-timeline.md](./05-sprint-timeline.md) | Turns the integrated roadmap into 11 sprints (Sprint 0–10): Core extraction, migration runner, test matrix, navigation hooks, then Settings → AutoTrack → Library → DB/Torrent splits with per-sprint DoD and checklists. |
| 6 | AI execution guide | [06-ai-execution-guide.md](./06-ai-execution-guide.md) | Practical playbook for AI-assisted delivery: mindset shift vs 22-week estimate, session workflow, copy-paste prompts per sprint, branch strategy, risks, and compressed timeline. |

---

## Suggested order of work

See **[00-integrated-roadmap.md](./00-integrated-roadmap.md)** for the dependency map and **[05-sprint-timeline.md](./05-sprint-timeline.md)** for week-by-week execution.

Short version:

1. **Foundation (done):** tests + migrations (Sprints 1–3)
2. **Navigation hooks (done):** [04](./04-transient-vs-singleton-viewmodels.md) Option A — Sprint 4
3. **Splits ([03](./03-split-large-viewmodels-services.md)) — FROZEN.** Do not start Settings → AutoTrack → Library.
4. **Next code:** surgical Library poster fix — [poster-flash-surgical-fix.md](./sprint-plans/poster-flash-surgical-fix.md)
5. **Transient VMs** (optional) — still reserved; not a reason to resume E4

Initiative is **done at E1+E2+E3.** You do **not** implement remaining splits.

---

## Related documentation

| Doc | Relevance |
|-----|-----------|
| [IMPROVEMENTS.md](../IMPROVEMENTS.md) | Source evaluation and full suggestion list (Medium/Lower items not planned here) |
| [FEATURES.md](../FEATURES.md) | Feature catalog, DB tables, pack linking appendix |
| [AI_CONTEXT.md](../AI_CONTEXT.md) | Machine-readable modules, DI registry, migration notes |
| [STATE_FOLDER.md](../STATE_FOLDER.md) | `media-manager.db`, FetchJobs purge, OAuth paths |
| [APP_OVERVIEW.md](../APP_OVERVIEW.md) | Architecture and data model overview |

---

## Out of scope (this folder)

Medium and Lower priority items from IMPROVEMENTS (unified error UX, recipe validation on save, CI/CD, etc.) are not covered here unless promoted to High priority.
