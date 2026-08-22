# Integrated Roadmap — Four High-Priority Items

**Audience:** Human (owner/developer planning future work)  
**Related docs:** [01](./01-database-migration-versioning.md) · [02](./02-unit-tests-critical-paths.md) · [03](./03-split-large-viewmodels-services.md) · [04](./04-transient-vs-singleton-viewmodels.md)

---

## Your intuition is partly right

These four items **feel like one big refactor** because they share the same root cause: the app grew feature-by-feature without upfront structure for schema, testability, class size, or UI lifetime.

They are **connected**, but you do **not** need to ship all four as a single release. That would be high risk and hard to review.

What *is* true:


| Feeling                               | Reality                                                                                                          |
| ------------------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| “Fix one → must fix all four at once” | **No** — each item can be delivered in slices                                                                    |
| “They are intertwined”                | **Yes** — order and overlap matter (see dependency map below)                                                    |
| “Each one is a large change”          | **Yes** if done fully — but each doc describes **incremental** paths                                             |
| “Item 01 blocks everything”           | **Partly** — only the *full* migration framework blocks safe schema changes; quick wins on 01 do not block 02–04 |


The goal of this document is a **single timeline** that respects dependencies without a “big bang.”

---



## Dependency map (how the four connect)

```
                    ┌─────────────────────────────────────┐
                    │  ROOT CAUSE: organic growth         │
                    │  (no schema version, no tests,      │
                    │   features stacked in one class,    │
                    │   singleton VMs kept forever)       │
                    └─────────────────────────────────────┘
                                        │
          ┌─────────────────────────────┼─────────────────────────────┐
          ▼                             ▼                             ▼
   ┌──────────────┐            ┌──────────────┐            ┌──────────────┐
   │ 01 DB        │            │ 02 Tests     │            │ 04 VM        │
   │ Migration    │            │              │            │ Lifetime     │
   └──────┬───────┘            └──────┬───────┘            └──────┬───────┘
          │                           │                           │
          │  DatabaseService split    │  Safety net before        │  Smaller VMs
          │  overlaps with 03 ────────┼──► refactor fear ────────►│  cheaper to
          │                           │                           │  recreate (03→04)
          │                           ▼                           │
          │                    ┌──────────────┐                   │
          └───────────────────►│ 03 Split     │◄──────────────────┘
                               │ large VMs/   │
                               │ services     │
                               └──────────────┘
```



### Direct dependencies


| From        | To                                                                             | Relationship                                                  |
| ----------- | ------------------------------------------------------------------------------ | ------------------------------------------------------------- |
| **02 → 03** | Tests should exist **before** moving logic out of large VMs/services           | Without tests, splits are blind refactors                     |
| **02 → 01** | Migration tests need a test project                                            | Same bootstrap as 02                                          |
| **03 → 04** | Smaller VMs reduce cost of transient navigation or scoped state                | `LibraryViewModel` at 2.4k lines is expensive to recreate     |
| **01 → 03** | `DatabaseService` (~3.2k lines) is both migration debt **and** split candidate | Do migration runner first; split CRUD into repositories later |
| **04 → 03** | Navigation hooks (04 Option A) can ship **before** splits                      | Low risk; does not require 03                                 |
| **01 ⊥ 04** | Independent                                                                    | VM lifetime does not depend on schema versioning              |




### What “intertwined” does **not** mean

- You do **not** need a new migration system before adding the first parser unit test.
- You do **not** need to split `SettingsViewModel` before adding `OnNavigatedTo` to fix stale Library grids.
- You do **not** need transient VMs before having any tests (but transient VMs **without** tests are risky).

---



## Recommended timeline (phased, not big-bang)

Estimate assumes solo developer, part-time refactors alongside feature work. Adjust scale: **S** = small slice (1–3 days), **M** = medium (1–2 weeks), **L** = large (2–4 weeks).

### Phase 0 — Decisions (before code)

**Duration:** 1–2 sessions (no app changes)


| Decision           | Options                                            | Blocks         |
| ------------------ | -------------------------------------------------- | -------------- |
| Migration approach | 01 Option C (minimal) → A (numbered scripts) later | Phase 1b       |
| Test layout        | 02 Option A (WPF test ref) vs B (Core lib)         | Phase 1a scope |
| VM lifetime goal   | 04 Option A (hooks) first vs jump to transient     | Phase 3 vs 4   |
| Split depth        | 03 Option A (partial files) vs B (sub-VMs)         | Phase 4 effort |


**Exit criteria:** Written choices in this doc or a short `DECISIONS.md`; no need to implement everything.

---



### Phase 1 — Foundation & safety net (parallel tracks)

**Goal:** Stop the worst foot-gun; start tests without touching UI architecture.


| Track  | Work                                                                                                        | Size | Doc                                         |
| ------ | ----------------------------------------------------------------------------------------------------------- | ---- | ------------------------------------------- |
| **1a** | Create xUnit project; parser + validation file-list tests                                                   | M    | [02](./02-unit-tests-critical-paths.md)     |
| **1b** | 01 Option C: `SchemaVersion` table; **remove unconditional** `FetchJobs` **purge**; one-time migration flag | S–M  | [01](./01-database-migration-versioning.md) |


**Can run in parallel:** 1a and 1b touch different files; only overlap is “add migration test” at end of 1b.

**Do not start yet:** Splitting `LibraryViewModel`, transient VMs, full migration script extraction.

**Exit criteria:**

- `dotnet test` passes with ≥20 parser/evaluation tests
- App starts; existing DB upgrades; FetchJobs purge runs **once** not every startup

---



### Phase 2 — Migration hardening + test expansion

**Goal:** Safe schema changes forever; enough tests to refactor Auto-Track/cart logic.


| Work                                                                     | Size | Depends on    |
| ------------------------------------------------------------------------ | ---- | ------------- |
| Numbered migrations (01 Option A); baseline migration for current schema | M–L  | Phase 1b      |
| Migration tests (empty DB, legacy blacklist column fixture)              | S    | 1a + 1b       |
| Evaluation, search plan, pack inferrer tests                             | M    | 1a            |
| Optional: extract `MediaManager.Core` if WPF test friction hurts         | L    | 1a pain point |


**Exit criteria:**

- `SchemaMigrations` records applied migrations
- `DatabaseService.Initialize()` shrinks (EnsureColumn chain reduced or baseline-only)
- Critical path services have golden-file fixtures

---



### Phase 3 — UX lifetime quick wins (04 Option A)

**Goal:** Fix stale UI without restructuring VMs.


| Work                                                                                      | Size | Depends on                                                      |
| ----------------------------------------------------------------------------------------- | ---- | --------------------------------------------------------------- |
| `INavigationAware` (or similar) on `ViewModelBase`                                        | S    | —                                                               |
| `MainViewModel.NavigateTo` calls `OnNavigatedTo` / `OnNavigatedFrom`                      | S    | —                                                               |
| Per-workspace refresh policy (Library refresh + restore selection, Settings reload, etc.) | M    | Manual audit in [04](./04-transient-vs-singleton-viewmodels.md) |
| Cancel Torrent `_operationCts` on navigate away                                           | S    | —                                                               |


**Why before Phase 4 splits:** Low risk, immediate user-visible benefit, no XAML binding churn.

**Exit criteria:**

- Repro script “Auto-Track → News → Library” shows updated grid without manual Refresh
- Settings reflect disk on each visit (or dirty-tracking if user edits)

---



### Phase 4 — Structural splits (03)

**Goal:** Maintainable units; prepare optional transient VMs later.

Suggested **order within Phase 4** (lowest runtime risk first):


| Step | Target                                                                 | Size | Why this order                                             |
| ---- | ---------------------------------------------------------------------- | ---- | ---------------------------------------------------------- |
| 4.1  | `SettingsViewModel` → section sub-VMs or partial files + user controls | M–L  | Mostly form/test actions; isolated from Auto-Track         |
| 4.2  | `AutoTrackService` → discovery / hunt / reconcile services             | M    | High business value; tests from Phase 2 protect hunt logic |
| 4.3  | `LibraryViewModel` → catalog vs detail                                 | L    | Most commands, most event wiring                           |
| 4.4  | `DatabaseService` → migrations + repositories                          | L    | Pair with Phase 2 migration runner                         |
| 4.5  | Optional: `TorrentWorkspaceViewModel`, `FetchJobService`               | M–L  | Same debt class as Library                                 |


**Exit criteria:**

- No file > ~800 lines without justification
- Each new type has owner documented in `AI_CONTEXT.md`
- Test count still green after each split

---



### Phase 5 — VM lifetime deep cut (04 Option B/C) — optional

**Goal:** Memory and guaranteed fresh state where Phase 3 hooks are not enough.


| Work                                                            | Size | Depends on                |
| --------------------------------------------------------------- | ---- | ------------------------- |
| Pilot transient `FindAddViewModel`                              | S    | 4.1+ helpful              |
| Scoped/singleton coordinator + resettable view state (Option C) | M    | 4.3                       |
| Full transient workspace VMs                                    | L    | 4.3, strong test coverage |


**Skip Phase 5 if:** Phase 3 hooks + poster cache service solve stale UI and memory.

---



### Visual timeline (summary)

```
Phase 0   [Decisions]
Phase 1   [Tests bootstrap] ──────── [Migration quick win]
Phase 2   [Full migrations] ─────── [Expand tests]
Phase 3   [Navigation refresh hooks]
Phase 4   [Settings split] → [AutoTrack split] → [Library split] → [DB repos]
Phase 5   [Transient VMs] (optional)
          ─────────────────────────────────────────────────────────► time
```

**Minimum viable refactor path** (if time is limited): **Phase 1 + Phase 3** only — stops data purge, adds parser tests, fixes stale UI. Defer full migration runner and splits.

**Professional “done” path:** Phases 1–4; Phase 5 only if metrics show memory pain.

---



## If you tried to fix “all four at once”


| Risk                        | Why                                                                  |
| --------------------------- | -------------------------------------------------------------------- |
| **Regression avalanche**    | Schema + VM lifetime + splits + no tests = no way to bisect failures |
| **Merge/review impossible** | Thousands of lines across DI, XAML, SQL, tests                       |
| **Long freeze on features** | Auto-Track and cart work stops for weeks                             |
| **Rollback hard**           | Single commit cannot revert one concern                              |


**Exception:** A dedicated “refactor branch” living for months with continuous merges from `main` can work — but still ship **phase by phase** to `main`, not one PR.

---



## Are these “industry standards”?

Short answer: **three of four are universal good practice; one is an architectural choice with tradeoffs.**

### 1. Database migration versioning — **Yes, standard**


| Context                        | Expectation                                                                                               |
| ------------------------------ | --------------------------------------------------------------------------------------------------------- |
| **General software**           | Versioned migrations (Flyway, Liquibase, EF Core Migrations, Rails migrations)                            |
| **Desktop apps with local DB** | Same — SQLite on disk must upgrade in place (VS Code, Discord, many Electron apps, mobile Room/Core Data) |
| **Your app today**             | `EnsureColumn` on every startup is a **common solo-dev shortcut**, not a long-term standard               |
| **FetchJobs purge every boot** | **Not standard** — that is a legacy workaround                                                            |


**Desktop-specific note:** Desktop apps often ship less frequently than web, so migrations may run months apart. That makes **recorded migration history** more important, not less — you forget what ran.

---



### 2. Unit tests for critical paths — **Yes, standard**


| Context                          | Expectation                                                                               |
| -------------------------------- | ----------------------------------------------------------------------------------------- |
| **Professional / team software** | Tests expected for business logic, especially parsers and scoring                         |
| **Solo personal tools**          | Often skipped early; debt accumulates exactly as in your app                              |
| **WPF / desktop**                | UI tests (Appium, WinAppDriver) are optional; **unit tests on non-UI logic** are standard |
| **Microsoft / .NET culture**     | xUnit/NUnit/MSTest in CI is normal for libraries and many apps                            |


**Desktop-specific note:** Extracting testable logic from code-behind/VMs is harder than in ASP.NET — hence optional `Core` library pattern ([02 Option B](./02-unit-tests-critical-paths.md)). Referencing the WPF project from tests ([02 Option A](./02-unit-tests-critical-paths.md)) is a valid **first step**, not “wrong.”

---



### 3. Split large ViewModels/services — **Strong convention, not a law**


| Context                | Expectation                                                                                                                 |
| ---------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| **Clean Code / SOLID** | Classes should have one reason to change; 2k+ lines is a **code smell**                                                     |
| **Enterprise WPF**     | Often uses separate views/user controls per feature area, coordinators, or “use case” services                              |
| **Reality**            | Many shipped WPF apps have giant `MainViewModel` or `SettingsViewModel` — it works until it does not                        |
| **Your sizes**         | 2k–2.4k line VMs and 1.5k line services are **past** typical team comfort (~300–500 lines per type is a common soft target) |


**Desktop-specific note:** WPF encourages binding many commands to one VM — that **accelerates** monolith growth. Splitting by **settings section** or **Library catalog vs detail** matches how the UI is already structured.

---



### 4. Transient vs singleton ViewModels — **Architectural choice, not one standard**


| Approach                           | Who uses it                                                                |
| ---------------------------------- | -------------------------------------------------------------------------- |
| **Singleton VMs**                  | Quick DI tutorials, small apps, preserve tab state                         |
| **Transient + navigation hooks**   | PRISM `IRegionMemberLifetime`, `INavigationAware`, some MVVM frameworks    |
| **Fresh ViewModel per navigation** | Web-style mental model; growing in modern desktop (e.g. view state stores) |


**There is no WPF/Microsoft mandate** for singleton vs transient. Both are valid.

**Your app’s issue** is not “singletons are wrong” — it is **singletons without a refresh contract** while background services mutate the DB. [04 Option A](./04-transient-vs-singleton-viewmodels.md) (hooks) is the **most common fix** before going transient.

**Desktop-specific note:** Persist **preferences** (`settings.json`) separately from **live data** (grid contents). You already do this partially for Library/Torrent selection — extend the pattern rather than conflating “remember my sort” with “keep stale cards forever.”

---



## Standards summary table


| Item                    | Industry standard?   | Desktop app norm?                       | Your gap severity                                  |
| ----------------------- | -------------------- | --------------------------------------- | -------------------------------------------------- |
| 01 Migration versioning | ✅ Yes                | ✅ Expected for persisted SQLite         | **High** — purge-on-boot is actively harmful       |
| 02 Unit tests           | ✅ Yes                | ✅ Expected for logic; UI tests optional | **High** — cart/Auto-Track logic is complex        |
| 03 Split large types    | ⚠️ Strong convention | ⚠️ Common debt in WPF MVVM              | **Medium–High** — maintainability, not correctness |
| 04 VM lifetime          | ❌ Choice             | ⚠️ Hooks or scopes usual in larger apps | **Medium** — UX/memory; hooks often enough         |


---



## Suggested “program” view (for planning sprints)

Treat as **one initiative**, **multiple epics**, **sequential epics**:


| Epic                   | Phases | Outcome                          |
| ---------------------- | ------ | -------------------------------- |
| **E1 Stability**       | 1b, 2  | Trustworthy DB upgrades          |
| **E2 Correctness**     | 1a, 2  | Regression safety                |
| **E3 UX freshness**    | 3      | Stale UI fixed                   |
| **E4 Maintainability** | 4      | Smaller types, clearer ownership |
| **E5 Optional polish** | 5      | Memory/transient if needed       |


Epics E1 and E2 can overlap in calendar time but should **merge to main independently**.

---



## Locked decisions

Recorded from owner input (Aug 2026) plus agent-chosen standards where gaps remained. **Sprint schedule:** [05-sprint-timeline.md](./05-sprint-timeline.md).

| #   | Question                                        | Locked choice |
| --- | ----------------------------------------------- | ------------- |
| 1   | Migration: C → A, or jump to A?                 | **Jump to A** — `SchemaMigrations` + numbered scripts from Sprint 2 (skip Option C-only path) |
| 2   | Tests: Option A (WPF ref) or B (Core lib)?      | **B — `MediaManager.Core`** + xUnit from Sprint 1 |
| 3   | Phase 3 before any Phase 4 split?               | **Yes** — navigation hooks in Sprint 4, splits Sprint 5+ |
| 4   | Include `TorrentWorkspaceViewModel` in Phase 4? | **Yes** — Sprint 9 with DB repos + FetchJobService |
| 5   | Target Phase 5 at all, or stop after hooks?     | **Reserved for future** — hooks only for this initiative |
| 6   | Minimum bar to call initiative “done”?          | **E1 + E2 + E3 + E4** (Sprint 10 closeout) |

### Agent-chosen standards (delegated)

| Topic | Choice |
| ----- | ------ |
| Migration implementation | Homegrown runner (Option A pattern), not FluentMigrator |
| Migration failure | Block startup; error dialog + restore-from-backup guidance |
| FetchJobs table | One-time purge in migration 002; table drop deferred |
| Fresh install | Run full migration chain from 001 |
| Settings split | Sub-ViewModels + user controls (03 Option B) |
| VM lifetime for initiative | Option A — `INavigationAware` hooks only |
| Test stack | xUnit + FluentAssertions; coverlet advisory (no CI gate in initiative) |
| Settings on navigate | Dirty-tracking before reload (Sprint 6) |
| Coverage enforcement | Advisory ≥80% line coverage on parser + evaluation by end Sprint 3 (coverlet locally; no CI threshold in initiative) |
| CI test step | Local `dotnet test` + Release x64 build gate every sprint ([BUILD.md](../BUILD.md)); GitHub Actions deferred to post-initiative |
| Test exposure | `InternalsVisibleTo` on Core for fixture builders; public API tests for parser/evaluation |
| Schema baseline | Documented in [schema-inventory.md](./schema-inventory.md); export source [schema-export-initiative-start.sql](./schema-export-initiative-start.sql) |

---



## Related reading

- **[06-ai-execution-guide.md](./06-ai-execution-guide.md)** — **mandatory workflow:** git check before code; Plan Mode → `sprint-plans/sprint-NN-local-plan.md` before Agent Mode per sprint  
- **[05-sprint-timeline.md](./05-sprint-timeline.md)** — sprint-by-sprint goals, test gates, migration milestones, unit test matrix (Sprint 1 marked complete)  
- [planning/sprint-plans/README.md](./sprint-plans/README.md) — local plan template  
- [planning/README.md](./README.md) — index of per-item deep dives  
- [IMPROVEMENTS.md](../IMPROVEMENTS.md) — original evaluation  
- [AI_CONTEXT.md](../AI_CONTEXT.md) — module map for agents after refactors

