# AI Execution Guide — Four-Pillars Refactor Initiative

**Audience:** Solo developer using Cursor (or similar AI coding agents)  
**Companion docs:** [05-sprint-timeline.md](./05-sprint-timeline.md) — sprint goals, DoD, test gates (source of truth); [BUILD.md](../BUILD.md) — **canonical build/test commands** (x64 merge gate)  
**Status:** Active — Sprint 0–2 complete; use §2.0 workflow before Sprint 3+

---

## 1. Mindset shift: AI-accelerated vs solo-human timeline

The [05-sprint-timeline](./05-sprint-timeline.md) assumes **~22 weeks at 10–15 h/week (~118 h)** because a solo human must context-switch, type boilerplate, and debug without instant recall of 157 service files.

With **AI-generated code + you as reviewer/architect**, the bottleneck moves from *writing* to *verifying*. Realistic compression:

| Phase | Human-only (05 doc) | AI-assisted (this guide) | Why it compresses |
| ----- | ------------------- | ------------------------ | ----------------- |
| **Foundation** (S0–S3) | 8 weeks | **Week 1** (3–5 days) | S0 is docs-only (same session). S1–S3 are mechanical moves + test scaffolding — AI excels at project creation, namespace moves, and xUnit fixtures. |
| **UX + Settings** (S4–S6) | 6 weeks | **Week 2** (3–4 days) | S4 is a small interface + wiring. S5–S6 repeat the same sub-VM pattern seven times — batch 2–3 sections per session. |
| **Service/VM splits** (S7–S9) | 6 weeks | **Week 3** (4–5 days) | Largest diffs, but AI can extract classes quickly. **Do not merge S7–S9 into one PR** — human verification of events/DB is the limiter. |
| **Closeout** (S10) | 2 weeks | **Week 4** (1–2 days) | Checklist-driven; mostly manual smoke. |

**Target calendar:** **3–4 weeks** focused effort (or **4–6 weeks** part-time), preserving sprint *order* and merge gates — not sprint count.

### Which sprints merge or run same-day

| Merge / same-day OK | Keep separate (never combine in one merge) |
| ------------------- | ------------------------------------------ |
| **S0 + start S1** same day (design doc AM, Core scaffold PM) | **S2 migration runner + S8 Library split** |
| **S5 + S6** consecutive sessions, still **separate merges** (5 then 6) | **S2 + S9** (two touches to DB init) |
| **S1 design overlap with S2** while S1 builds (AI drafts migration file layout during S0) | **S4 hooks + S9 Torrent split** (navigation + long ops) |
| Multiple **parser test cases** in one S1 session | Any sprint **without** `dotnet test` + Release x64 build green ([BUILD.md](../BUILD.md)) |

**Rule:** Compress *calendar*, not *quality gates*. Each sprint still has its own branch/PR and DoD from [05 §4](./05-sprint-timeline.md#4-per-sprint-detail).

---

## 2. How to work with AI on this initiative

### 2.0 Mandatory pre-sprint workflow (git + Plan Mode)

**Do this before every new sprint — and before any AI writes code.**

#### Step A — Git check (always first)

Run and read output **before** opening Agent Mode or approving a large diff:

```bash
cd "d:/VScode/Misc/Media_Manager"
git status -sb
git branch --show-current
git log --oneline -5
# Compare canonical branch vs remote:
git rev-list --left-right --count auto-torrent...origin/auto-torrent
git rev-list --left-right --count auto-torrent...origin/main
```

| Check | Action if wrong |
| ----- | ---------------- |
| On **`auto-torrent`** (canonical dev branch) | `git checkout auto-torrent` — do **not** implement on stale `origin/main` |
| Working tree clean or intentionally stashed | Stash/commit before switching sprints |
| Sprint 0 docs merged if starting S1+ | `git merge main --ff-only` only when bringing planning commits forward |
| No surprise untracked files in diff | Remove or `.gitignore` before merge |

**Rule:** If git state is unclear, stop and fix branch/checkout **before** coding. Wrong-branch work was the root cause of Debug build confusion (Core DLL path / missing `MediaKind`).

#### Step B — Plan Mode → local plan doc (every new sprint)

**Do not** start a new sprint directly in **Agent Mode** with a one-shot “implement Sprint N” prompt.

1. Switch Cursor to **Plan Mode** (not Agent Mode).
2. Attach `@docs/planning/05-sprint-timeline.md` (target sprint §4) and related docs from §2.2.
3. Produce a **local plan document** before implementation:

   **Path:** `docs/planning/sprint-plans/sprint-NN-local-plan.md`  
   **Example:** `docs/planning/sprint-plans/sprint-02-local-plan.md`

4. Local plan must include (minimum):
   - Git baseline (branch, commit hash, clean/dirty)
   - Scope IN / OUT copied from 05
   - Definition of Done checklist copied verbatim
   - File move list or touch list (from grep / schema-inventory)
   - Test gate commands from [BUILD.md](../BUILD.md)
   - Risks and rollback one-liner

5. **Review and edit** the local plan yourself (5–10 min).
6. Only then open **Agent Mode** with:  
   `Implement from @docs/planning/sprint-plans/sprint-NN-local-plan.md`  
   plus `@docs/planning/05-sprint-timeline.md` for the sprint section.

| Mode | When | Output |
| ---- | ---- | ------ |
| **Plan Mode** | Start of each sprint | `sprint-plans/sprint-NN-local-plan.md` (committed with sprint or in same PR) |
| **Agent Mode** | After plan approved | Code + tests + doc checkboxes |

**Anti-pattern:** Agent Mode + “implement Sprint 2” with no local plan → scope creep, wrong branch, skipped gates.

#### Step C — Sprint folder convention

```
docs/planning/sprint-plans/
  sprint-01-local-plan.md   ← optional retro for S1
  sprint-02-local-plan.md   ← create in Plan Mode before S2 Agent session
  …
```

Commit the local plan in the same sprint PR (or immediately after Plan Mode) so future sessions have a frozen scope artifact.

---

### 2.1 Session rhythm

0. **Git check** (§2.0 Step A) — confirm branch and baseline.
1. **One sprint focus per session** (or per calendar day for large sprints like S8/S9).
2. Open a **fresh Agent chat** per sprint merge — avoids stale context and hallucinated file paths.
3. **You merge; AI proposes.** Never stack two sprints in one PR.
4. End every session with: build → test → manual smoke → commit message draft.

### 2.2 What to attach every time

| Always `@` | Sprint-specific `@` |
| ---------- | ------------------- |
| `docs/planning/05-sprint-timeline.md` (current sprint §4) | S1–3: `docs/planning/02-unit-tests-critical-paths.md` |
| `docs/BUILD.md` | Sprint merge gate: copy-paste build/test commands |
| `docs/AI_CONTEXT.md` | S2, S9: `docs/planning/01-database-migration-versioning.md` |
| Relevant source files (see playbook §4) | S4: `docs/planning/04-transient-vs-singleton-viewmodels.md` |
| | S5–9: `docs/planning/03-split-large-viewmodels-services.md` |

Paste the **acceptance criteria** from 05's Definition of Done for that sprint into the prompt (copy the checklist verbatim).

### 2.3 Prompt templates by sprint type

#### Core extraction (S1, S3)

```
Implement Sprint [N] from @docs/planning/05-sprint-timeline.md (Sprint [N] section only).

Locked decisions: Option B MediaManager.Core, xUnit + FluentAssertions, move don't copy types.

Scope IN: [paste In table from 05]
Scope OUT: [paste Out table from 05]

Definition of Done:
[paste DoD checklist from 05]

After changes: Release x64 build + dotnet test — see @docs/BUILD.md.
Do not change runtime behavior. Do not start migration runner or VM splits.
```

#### Migration (S2, parts of S9)

```
Implement Sprint 2 migration runner per @docs/planning/05-sprint-timeline.md and @docs/planning/01-database-migration-versioning.md.

Policy: block startup on failure; SchemaMigrations history table; 001 baseline + 002 FetchJobs one-time purge.
Remove unconditional PurgeLegacyFetchJobs from init hot path.

Add MigrationRunnerTests: empty DB, idempotent re-run, legacy fixture DB if available.

Do NOT extract full DatabaseService repos (Sprint 9). Do NOT add navigation hooks.

Definition of Done: [paste from 05 Sprint 2]
```

#### Tests only (S3 extension)

```
Expand MediaManager.Core.Tests per Sprint 3 in @docs/planning/05-sprint-timeline.md.
Use golden fixtures from @docs/planning/02-unit-tests-critical-paths.md.
Target: ≥40 total tests; advisory 80% line coverage on parser + evaluation (coverlet locally).
Move types to Core; no WPF reference from test project.
Regression: Sprint 1 parser tests must stay green.
```

#### Navigation hooks (S4)

```
Implement INavigationAware per Sprint 4 in @docs/planning/05-sprint-timeline.md and @docs/planning/04-transient-vs-singleton-viewmodels.md.

Wire MainViewModel.NavigateTo to OnNavigatedTo/From on all workspace VMs.
Cancel Torrent _operationCts on navigate away. FindAdd: RefreshExistingMedia on navigate.

Do NOT split ViewModels. Do NOT add transient DI registration.

Manual repro scripts 1–5 in 05 must pass after implementation — list what you changed for each script.
```

#### VM / service split (S5–S9)

```
Refactor per Sprint [N] in @docs/planning/05-sprint-timeline.md and @docs/planning/03-split-large-viewmodels-services.md.

Pattern: section sub-ViewModels + user controls; host orchestrates save/load.
Preserve all commands and bindings — no orphaned ICommand.

Scope IN/OUT: [paste from 05]
DoD: [paste checklist]

After refactor: dotnet test green + Release x64 build — see @docs/BUILD.md.
List every XAML binding path that changed so I can click-test.
```

### 2.4 Pre-merge review checklist (human)

Run this **before** merging any sprint branch. **Exact commands:** [BUILD.md](../BUILD.md).

```bash
APP_ROOT="d:/VScode/Misc/Media_Manager/media management app/media management app"
PROJ="$APP_ROOT/media management app.csproj"
dotnet restore "$PROJ"
dotnet build "$PROJ" -c Release -p:Platform=x64 -v minimal --no-restore
# When MediaManager.Core.Tests exists:
# dotnet test "$APP_ROOT/MediaManager.Core.Tests/MediaManager.Core.Tests.csproj" -c Release -v normal
```

Checklist:

```
[ ] Git: on auto-torrent (or agreed sprint branch); status reviewed (§2.0)
[ ] Sprint local plan exists: docs/planning/sprint-plans/sprint-NN-local-plan.md
[ ] git diff reviewed — no unrelated files, no .env/secrets
[ ] Release x64 build green (commands above, or ./scripts/build.sh Release x64)
[ ] dotnet test on MediaManager.Core.Tests — all green
[ ] Sprint-specific manual checklist from 05 §4 — each item checked
[ ] AI_CONTEXT.md updated if structure/DI changed
[ ] No duplicate event subscriptions (grep += for new handlers; verify -= or weak refs)
[ ] Migration sprint only: tested copy of real media-manager.db, not just empty DB
[ ] XAML split sprint: opened every tab/section touched
```

### 2.5 Branch strategy

**Canonical development branch:** `auto-torrent`  
(`origin/main` is ~76 commits behind — do not treat it as the active codebase.)

**Recommended:**

```
auto-torrent                         ← primary integration branch for this initiative
 └── sprint/00-kickoff-design        ← merged (Sprint 0 tag: four-pillars-sprint-00)
 └── sprint/01-core-parser           ← optional; or commit directly on auto-torrent
 └── sprint/02-migration-runner
 └── … through sprint/10-closeout
```

- **Small initiative:** merge each `sprint/NN-*` into **`auto-torrent`** after gates pass; optionally fast-forward local `main` when ready to publish.
- **Safer initiative:** use `initiative/four-pillars` as intermediate, merge to `auto-torrent` at Sprint 10.
- **Tag** each merge: `four-pillars-sprint-01`, etc. (Sprint 0 tagged; Sprint 1 tag pending commit).

**Never:** combine migration runner (S2) with Library or Torrent VM splits (S8/S9) in one PR.

### 2.6 When to stop AI and verify manually

| Stop AI immediately | Why |
| ------------------- | --- |
| Before merging **any** migration | Restore path must be tested on **your** DB copy |
| After **S4** implementation | Stale-UI bugs need eyes on real navigation |
| When **bindings look wrong** but build succeeds | WPF fails silently |
| **S8/S9** event wiring | CartChanged, reconcile handlers — grep and click-test |
| AI proposes **editing an applied migration script** | Violates numbering rules — reject |
| AI adds **`InternalsVisibleTo`** broadly or makes test-only APIs public | Review exposure |
| **Duplicate types** or “copy instead of move” | Breaks single source of truth |

---

## 3. Start NOW — Sprint 0 checklist (today)

Do these in order; total time **~2–4 hours** human + one AI session for schema export doc.

### Step 1 — Branch and baseline tag (15 min)

```bash
cd "d:/VScode/Misc/Media_Manager/media management app/media management app"
git checkout -b sprint/00-kickoff-design
git tag four-pillars-pre-sprint-0   # optional rollback anchor
```

### Step 2 — Export live DB schema (30 min)

1. Copy `%STATE_FOLDER%/media-manager.db` to a temp path (default: `D:\MediaManagerState`).
2. Open with [DB Browser for SQLite](https://sqlitebrowser.org/) or `sqlite3`:

```bash
sqlite3 "D:/MediaManagerState/media-manager.db" ".schema" > docs/planning/schema-export-initiative-start.sql
sqlite3 "D:/MediaManagerState/media-manager.db" "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;"
```

3. For each table: `PRAGMA table_info(TableName);` — capture in spreadsheet or markdown.

### Step 3 — Write migration numbering rules (15 min)

Create `docs/planning/schema-inventory.md` (or appendix) with:

- Integer prefix `001_`, `002_`, … — **never edit** applied scripts
- One migration = one transactional unit
- Fresh install runs full chain from 001
- 001 = baseline at initiative start; 002 = FetchJobs one-time purge

### Step 4 — Approve Core type move list (15 min)

From [02-unit-tests-critical-paths.md](./02-unit-tests-critical-paths.md) and Sprint 1/3 in 05:

- **Sprint 1:** `TorrentCandidateParser` + shared enums/models
- **Sprint 3:** `CandidateEvaluationService`, `SearchPlanBuilder`, `SearchTitleResolver`, pack/validation services

Check into `docs/planning/schema-inventory.md` or a `core-type-move-list.md` section.

### Step 5 — First AI prompt (paste into Cursor)

See **§5 Sprint 0** below — full copy-paste block.

### Step 6 — Sprint 0 DoD sign-off

When AI finishes the doc PR, confirm [05 Sprint 0 Definition of Done](./05-sprint-timeline.md#sprint-0--kickoff--design-w0w1-8-h):

- [ ] Schema inventory checked in
- [ ] Migration numbering rules written
- [ ] Core type move list approved
- [ ] [00-integrated-roadmap.md](./00-integrated-roadmap.md) locked decisions complete

Merge `sprint/00-kickoff-design` → `main` (docs only, zero runtime risk).

---

## 4. Sprint-by-sprint AI playbook (S0–S10)

DoD and test gates: always [05 §4 Per-sprint detail](./05-sprint-timeline.md#4-per-sprint-detail).

### Sprint 0 — Kickoff & design

| | |
| --- | --- |
| **Goal** | Decisions written; schema inventoried; Core move order agreed — zero code behavior change. |
| **AI sessions** | **1** (docs + schema export formatting) |
| **Human only** | Export DB from *your* state folder; approve type list; merge when checklist complete. |
| **Merge gate** | Docs PR only; no app rebuild required. |

**First prompt (copy-paste):**

```
You are working on Media Manager WPF/C# at this repo root.

Execute Sprint 0 ONLY from @docs/planning/05-sprint-timeline.md (Sprint 0 section).

Also read @docs/planning/00-integrated-roadmap.md for locked decisions.

Tasks:
1. Create docs/planning/schema-inventory.md with:
   - Table list and PRAGMA column definitions (I will paste sqlite output below or from schema-export-initiative-start.sql)
   - Migration file layout: Migrations/001_baseline.sql, 002_fetchjobs_legacy_purge.sql
   - Numbering rules: integer prefix, idempotent, never edit applied scripts
2. Add core-type-move-list section: Sprint 1 moves (TorrentCandidateParser + deps) and Sprint 3 moves (evaluation, search, pack, validation) per 05 and @docs/planning/02-unit-tests-critical-paths.md
3. Document branch strategy: sprint/NN-* branches off main, tags four-pillars-sprint-NN

Do NOT implement code. Do NOT create MediaManager.Core yet.

Definition of Done from 05 Sprint 0:
[paste checklist after reading file]

Here is my schema export:
[paste .schema output or attach schema-export-initiative-start.sql]
```

---

### Sprint 1 — Core library & parser tests

| | |
| --- | --- |
| **Status** | ✅ **Complete** — tagged `four-pillars-sprint-01` on `auto-torrent` |
| **Goal** | `MediaManager.Core` + ≥15 parser tests; WPF references Core. |
| **AI sessions** | **1–2** |
| **Key files** | `MediaManager.Core/`, `MediaManager.Core.Tests/`, `media management app.csproj` |
| **Human only** | Smoke: app launch, torrent search parses — **passed** |
| **Merge gate** | `dotnet test` 20/20 green; Release + Debug x64 build green |

**Delivered:** Parser + `TorrentReleaseKind` + `MediaKind` + `TorrentQuality.Detect` in Core; 20 xUnit tests; `TorrentQualityScoring` in WPF.

**Deferred to Sprint 3:** `RecipeBuilder`, `TrackedShowBuilder` fixture helpers.

**Before Sprint 2:** Commit Sprint 1 on `auto-torrent`, tag `four-pillars-sprint-01`, create `sprint-plans/sprint-02-local-plan.md` in **Plan Mode**.

**First prompt (for future reference — S1 already done):**

```
Implement Sprint 1 from @docs/planning/05-sprint-timeline.md and @docs/planning/02-unit-tests-critical-paths.md.

Create MediaManager.Core (net8.0) and MediaManager.Core.Tests (xUnit, FluentAssertions).
Move TorrentCandidateParser and required models/enums — MOVE not COPY.
Add InternalsVisibleTo for test fixtures only if needed.
Minimum 15 TorrentCandidateParserTests from 02 suggested cases.

WPF project references Core. No migration runner. No ViewModel changes.

DoD: [paste Sprint 1 checklist from 05]

Build: Release x64 per @docs/BUILD.md. Run dotnet test before finishing.
Update docs/AI_CONTEXT.md with Core project note.
```

---

### Sprint 2 — Migration runner

| | |
| --- | --- |
| **Goal** | `SchemaMigrations` + runner; 001 baseline + 002 FetchJobs purge once. |
| **AI sessions** | **2–3** (runner + tests + edge cases) — done Aug 2026 |
| **Key files** | `MediaManager.Core/Migrations/*`, `DatabaseService.cs`, `App.xaml.cs`, `STATE_FOLDER.md` |
| **Human only** | Upgrade **copy** of production DB; spot-check counts; second startup no FetchJobs spam — see [05 Sprint 2 manual checklist](./05-sprint-timeline.md#sprint-2--migration-runner-option-a-w4w5-12-h). |
| **Merge gate** | ✅ Migration unit tests + Release x64 build + manual DB checklist; tag `four-pillars-sprint-02`. |
| **Deferred** | Migration 003 (TorrentBlacklist rebuild) → Sprint 3 |

**First prompt:**

```
Implement Sprint 2 from @docs/planning/05-sprint-timeline.md and @docs/planning/01-database-migration-versioning.md.

SchemaMigrations table; apply pending migrations in order; transactional per migration.
001_baseline = effective schema at initiative start.
002_fetchjobs_legacy_purge = DELETE FROM FetchJobs once.
Remove unconditional PurgeLegacyFetchJobs from Initialize() hot path.
Block startup on migration failure with dialog pointing to CreateSafeSnapshot / restore.

Add MigrationRunnerTests (empty DB, idempotent re-run). Optional 003 for TorrentBlacklist if time.

Do NOT split DatabaseService repos (Sprint 9). Do NOT touch ViewModels.

DoD: [paste Sprint 2 checklist]

Run dotnet test and Release x64 build per @docs/BUILD.md.
```

---

### Sprint 3 — Critical-path test expansion

| | |
| --- | --- |
| **Goal** | ≥40 unit tests; evaluation/search/pack/validation in Core. |
| **AI sessions** | **2–3** |
| **Human only** | coverlet ≥80% advisory on parser+evaluation; manual cart + pack link smoke. |
| **Merge gate** | ≥40 tests; no WPF ref from test project; migration tests still green. |

**First prompt:**

```
Implement Sprint 3 from @docs/planning/05-sprint-timeline.md and @docs/planning/02-unit-tests-critical-paths.md.

Move to Core: CandidateEvaluationService, SearchPlanBuilder, SearchTitleResolver,
PackSeasonFileGrouper, PackEpisodePatternInferrer, TorrentContentValidationService (file-list path).

Add golden fixtures; test counts per 05 table (≥12 evaluation, ≥8 search, ≥5 pack grouper, ≥10 pattern inferrer, ≥8 validation).
Total ≥40 tests in Core.Tests.

Run migration tests from Sprint 2 first. dotnet test must be all green.
Optional: migration 003 for TorrentBlacklist if not done in Sprint 2.

DoD: [paste Sprint 3 checklist]
```

---

### Sprint 4 — Navigation refresh hooks

| | |
| --- | --- |
| **Goal** | `INavigationAware`; fresh data on workspace return; cancel Torrent ops on leave. |
| **AI sessions** | **1–2** |
| **Key files** | `ViewModelBase`, `MainViewModel`, each workspace VM |
| **Human only** | All 6 stale-UI repro scripts in 05 §4 Sprint 4 table. |
| **Merge gate** | Scripts 1–5 pass; no new event leaks. |

**First prompt:**

```
Implement Sprint 4 (E3) from @docs/planning/05-sprint-timeline.md and @docs/planning/04-transient-vs-singleton-viewmodels.md.

Add INavigationAware (OnNavigatedTo / OnNavigatedFrom) on ViewModelBase.
MainViewModel.NavigateTo invokes hooks for all seven workspace VMs.
Per-workspace refresh policy per 04. Cancel Torrent _operationCts on OnNavigatedFrom.
FindAdd: RefreshExistingMedia() on navigate.

NO transient VMs. NO Settings dirty-tracking (Sprint 6). NO VM splits.

DoD: [paste Sprint 4 checklist]
Document refresh policy in AI_CONTEXT.md.
```

---

### Sprint 5 — Settings split part 1

| | |
| --- | --- |
| **Goal** | Integrations + Backup + System → sub-VMs + user controls. |
| **AI sessions** | **1–2** |
| **Human only** | Click every control in 3 sections; integration test buttons; Drive backup smoke. |
| **Merge gate** | dotnet test + Release x64 build ([BUILD.md](../BUILD.md)); binding paths verified. |

**First prompt:**

```
Implement Sprint 5 from @docs/planning/05-sprint-timeline.md and @docs/planning/03-split-large-viewmodels-services.md.

Extract sub-VMs + IntegrationsSettingsPanel, BackupSettingsPanel, SystemSettingsPanel.
Host SettingsViewModel keeps SelectedSettingsSection and save orchestration.
XAML: {Binding Integrations.TmdbToken} style paths. Register section VMs in DI.

Do NOT migrate remaining 4 sections (Sprint 6). No behavior change to settings values.

DoD: [paste Sprint 5 checklist]
List all binding path changes for my manual click-test.
```

---

### Sprint 6 — Settings split part 2

| | |
| --- | --- |
| **Goal** | All 7 sections in sub-VMs; dirty-tracking on navigate. |
| **AI sessions** | **1–2** |
| **Human only** | Unsaved edit → navigate away → prompt; Sprint 4 script #3 with dirty behavior. |
| **Merge gate** | SettingsViewModel ~≤400 lines; all sections save/load. |

**First prompt:**

```
Implement Sprint 6 from @docs/planning/05-sprint-timeline.md.

Remaining sections: Library, AutoTrack, TorrentStorage, Notifications — sub-VMs + user controls.
Dirty-tracking: prompt if OnNavigatedTo would clobber unsaved edits. Global Save retained.

DoD: [paste Sprint 6 checklist]
SettingsViewModel coordinator ~300 lines target. Update AI_CONTEXT.md ownership map.
```

---

### Sprint 7 — AutoTrack service split

| | |
| --- | --- |
| **Goal** | Discovery / Hunt / Reconcile services behind `IAutoTrackService` façade. |
| **AI sessions** | **2** |
| **Human only** | Full Auto-Track manual run; concurrent hunt attempt; scheduler still fires. |
| **Merge gate** | Hunt tests green; no file >~600 lines unjustified. |

**First prompt:**

```
Implement Sprint 7 from @docs/planning/05-sprint-timeline.md and @docs/planning/03-split-large-viewmodels-services.md.

Extract AutoTrackTmdbDiscoveryService, AutoTrackHuntService, AutoTrackReconcileService.
AutoTrackService delegates; preserve locks/semaphore ownership clearly.
Public API unchanged: IAutoTrackService.

Do NOT split Library or FetchJobService (later sprints).

DoD: [paste Sprint 7 checklist]
```

---

### Sprint 8 — Library split

| | |
| --- | --- |
| **Goal** | Catalog vs detail ViewModels; host composes both. |
| **AI sessions** | **2–3** |
| **Human only** | All Library manual checklist items; cart/reconcile events; Sprint 4 Library script. |
| **Merge gate** | 47 commands accounted for; host ~≤500 lines. |

**First prompt:**

```
Implement Sprint 8 from @docs/planning/05-sprint-timeline.md and @docs/planning/03-split-large-viewmodels-services.md.

LibraryCatalogViewModel: grid, sort, filter, selection, UI state persist.
LibraryDetailViewModel: seasons, episodes, pack link, cart on detail.
Host LibraryViewModel composes both; XAML ContentControl for detail pane.

Account for all 47 commands — none orphaned. Verify CartChanged and reconcile handlers.

DoD: [paste Sprint 8 checklist]
```

---

### Sprint 9 — DB repos + Torrent/FetchJob split

| | |
| --- | --- |
| **Goal** | Migration runner extracted; repositories; Torrent VM + FetchJob split. |
| **AI sessions** | **3–4** (highest risk — split into implement vs fix sessions) |
| **Human only** | **DB backup before merge**; full CRUD checklist; migration idempotency on Sprint 2 backup. |
| **Merge gate** | DatabaseService ~≤800 lines; E1 exit criteria; migration tests pass. |

**First prompt:**

```
Implement Sprint 9 from @docs/planning/05-sprint-timeline.md.

Extract MigrationRunner from DatabaseService if not already standalone.
Repository interfaces: ITrackedShowRepository, ISourceItemRepository, etc. — pragmatic slices.
DatabaseService thin coordinator. TorrentWorkspaceViewModel → catalog vs cart/search panes.
FetchJobService: extract search orchestration helpers.

Initialize() must not contain long EnsureColumn chains — upgrades via runner only.

Do NOT implement Phase 5 transient VMs.

DoD: [paste Sprint 9 checklist]
I will test on a DB backup copy before merge.
```

---

### Sprint 10 — Closeout & regression

| | |
| --- | --- |
| **Goal** | E1–E4 verified; docs match code; initiative formally done. |
| **AI sessions** | **1** (doc updates + gap tests) |
| **Human only** | Full manual regression ([05 §8](./05-sprint-timeline.md#8-manual-regression-master-checklist-sprint-10)); optional overnight soak. |
| **Merge gate** | All Sprint 10 DoD + post-initiative state (05 §7). |

**First prompt:**

```
Sprint 10 closeout per @docs/planning/05-sprint-timeline.md.

1. Fill remaining unit test gaps from critical-path matrix.
2. Line-count audit — flag files >800 lines without justification.
3. Update AI_CONTEXT.md, FEATURES.md module map, IMPROVEMENTS.md status for E1–E4 complete.
4. Document Phase 5 reopen triggers (05 §7).

Do not add new features. dotnet test + Release x64 build ([BUILD.md](../BUILD.md)) must pass.

DoD: [paste Sprint 10 checklist]
```

---

## 5. Compressed timeline table (AI-assisted)

Assumes **focused days** (~4–6 h/day human review + AI execution). Adjust if part-time.

| Calendar | Sprints | Cumulative outcome | Human focus that week |
| -------- | ------- | ------------------ | --------------------- |
| **Day 1** | S0 → S1 start | Schema doc + Core projects created | Export DB; review type move list |
| **Day 2** | S1 finish | ≥15 parser tests green | App smoke: torrent parse |
| **Day 3** | S2 | Migration runner live | **DB copy upgrade test** |
| **Day 4–5** | S3 | ≥40 tests; Core logic frozen | coverlet report; cart smoke |
| **Day 6** | S4 | Stale UI fixed | 6 repro scripts |
| **Day 7–9** | S5 → S6 | Settings fully split | Click all settings controls |
| **Day 10–11** | S7 | AutoTrack phases split | Hunt + scheduler manual |
| **Day 12–14** | S8 | Library catalog/detail | Pack link + cart events |
| **Day 15–17** | S9 | DB repos + Torrent split | **Backup DB**; CRUD regression |
| **Day 18–20** | S10 + buffer | Initiative done | Full §8 regression checklist |

**Part-time mapping:** ~2 focused days per week → **4–6 weeks** total.

See also the summary table in [05-sprint-timeline.md § AI-accelerated schedule](./05-sprint-timeline.md#9-ai-accelerated-schedule).

---

## 6. Risks when moving fast with AI

| Risk | Symptom | Mitigation |
| ---- | ------- | ---------- |
| **Hallucinated migrations** | SQL references non-existent columns; 001 doesn't match real schema | S0 schema inventory from **live** DB; AI never invents columns — paste `PRAGMA table_info` |
| **Edited applied migration** | AI "fixes" 001 after you tested | Reject; add 004+ instead; rule in every migration prompt |
| **Missed event unsubscribes** | Memory growth; duplicate handlers; double cart updates | After S4/S8/S9: grep `+=` new subscriptions; confirm `-=` in `OnNavigatedFrom` or dispose |
| **Silent XAML binding breaks** | Controls empty; no build error | AI must list binding path changes; you click every control in touched views |
| **Copy vs move to Core** | CS0101 duplicate type errors or subtle wrong type used | `dotnet build` + grep for duplicate class names |
| **Baseline mis-detection (S2)** | Duplicate ALTER on upgrade | Test **copy** of production DB before merge; keep pre-S2 tag |
| **Over-refresh (S4)** | Slow tab switches | Acceptable for initiative per 04; note for later optimization |
| **Scope creep in split PRs** | AI refactors "while we're here" | Paste Scope OUT table; reject unrelated diffs |
| **Skipping manual gates** | Green tests but broken hunt in production | Post-S3 rule: parser/evaluation changes require test + manual smoke |

---

## 7. Definition of "done" per sprint

**Authoritative checklists** live in [05-sprint-timeline.md §4 Per-sprint detail](./05-sprint-timeline.md#4-per-sprint-detail). This guide does not redefine them.

Quick reference:

| Sprint | Done when (summary) | 05 anchor |
| ------ | ------------------- | --------- |
| **0** | Schema inventory + migration rules + Core move list approved | [§ Sprint 0 DoD](./05-sprint-timeline.md#sprint-0--kickoff--design-w0w1-8-h) |
| **1** | Core + Tests in solution; ≥15 parser tests; no duplicate types | [§ Sprint 1 DoD](./05-sprint-timeline.md#sprint-1--core-library--parser-tests-w2w3-12-h) |
| **2** | SchemaMigrations; no boot-time FetchJobs purge; migration tests | [§ Sprint 2 DoD](./05-sprint-timeline.md#sprint-2--migration-runner-option-a-w4w5-12-h) |
| **3** | ≥40 tests; ≥80% advisory coverage parser+evaluation | [§ Sprint 3 DoD](./05-sprint-timeline.md#sprint-3--critical-path-test-expansion-w6w7-12-h) |
| **4** | All workspace refresh policies; repro scripts 1–5 pass | [§ Sprint 4 DoD](./05-sprint-timeline.md#sprint-4--navigation-refresh-hooks-e3-w8w9-12-h) |
| **5** | 3 settings sections extracted; bindings work | [§ Sprint 5 DoD](./05-sprint-timeline.md#sprint-5--settings-split-part-1-e4-w10w11-12-h) |
| **6** | All 7 sections; dirty-tracking; host ~≤400 lines | [§ Sprint 6 DoD](./05-sprint-timeline.md#sprint-6--settings-split-part-2-e4-w12w13-12-h) |
| **7** | AutoTrack phases split; IAutoTrackService unchanged | [§ Sprint 7 DoD](./05-sprint-timeline.md#sprint-7--autotrack-service-split-e4-w14w15-12-h) |
| **8** | Library catalog/detail; all commands accounted | [§ Sprint 8 DoD](./05-sprint-timeline.md#sprint-8--library-split-e4-w16w17-14-h) |
| **9** | Repos + Torrent split; DatabaseService ~≤800 lines; E1 met | [§ Sprint 9 DoD](./05-sprint-timeline.md#sprint-9--database-repos--torrentfetchjob-e4-w18w19-14-h) |
| **10** | E1+E2+E3+E4 complete; full regression; docs updated | [§ Sprint 10 DoD](./05-sprint-timeline.md#sprint-10--closeout--final-regression-w20w21-10-h) |

**Initiative complete** = [05 §7 Post-initiative state](./05-sprint-timeline.md#7-post-initiative-state-done-looks-like).

---

## Related reading

- [05-sprint-timeline.md](./05-sprint-timeline.md) — sprint scope, hours estimate, test matrix  
- [00-integrated-roadmap.md](./00-integrated-roadmap.md) — why order matters  
- [planning/README.md](./README.md) — index  
- [AI_CONTEXT.md](../AI_CONTEXT.md) — update after each merge
