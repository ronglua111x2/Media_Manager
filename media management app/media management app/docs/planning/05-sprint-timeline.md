# Sprint Timeline — Four High-Priority Initiative

**Audience:** Solo developer (part-time, ~10–15 h/week)  
**Initiative scope:** E1 Stability + E2 Correctness + E3 UX freshness + E4 Maintainability  
**Status:** **Initiative complete at E1+E2+E3.** Sprints 0–4 done on `auto-torrent`. **Sprints 5–10 (E4 structural splits) are frozen/cancelled.** Do not resume Sprint 5–10. Poster flash is a surgical follow-up, not Sprint 8.  
**Canonical dev branch:** `auto-torrent` (not `origin/main`, which is ~76 commits behind)  
**Workflow:** Git check + Plan Mode local plan before every new sprint — see [06-ai-execution-guide.md §2.0](./06-ai-execution-guide.md#20-mandatory-pre-sprint-workflow-git--plan-mode). After each closed sprint: [progress review §2.1a](./06-ai-execution-guide.md#21a-post-sprint-progress-review-mandatory-after-each-closed-sprint).  
**Related:** [00-integrated-roadmap.md](./00-integrated-roadmap.md) · [01](./01-database-migration-versioning.md) · [02](./02-unit-tests-critical-paths.md) · [03](./03-split-large-viewmodels-services.md) · [04](./04-transient-vs-singleton-viewmodels.md) · [06-ai-execution-guide.md](./06-ai-execution-guide.md)

**Calendar assumption:** Each sprint = **2 calendar weeks** at ~12 h effective effort. Adjust dates when you start; week numbers are relative to Sprint 0 kickoff.

---

## 1. Executive summary

This initiative turns four intertwined debt items into a **sequenced, test-gated refactor program** — not a big-bang release. Work ships to `main` sprint-by-sprint; each sprint has a clear outcome, automated test gate, and manual checklist.

### Chosen stack of decisions


| Area                         | Choice                                                                                                                                   | Rationale                                                                                                                                                                             |
| ---------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Database migrations**      | **Option A — homegrown `SchemaMigrations` + numbered SQL/C# scripts** (user: jump straight to A)                                         | Industry-standard *pattern* (Flyway/DbUp/EF-style history table) without adding a heavy dependency for 10 SQLite tables. Single source of truth: every install runs migrations 001→N. |
| **Migration failure policy** | **Block startup** with error dialog; message points to `CreateSafeSnapshot()` / Google Drive restore                                     | Data safety over silent corruption. Desktop apps cannot “roll forward” easily on a broken schema.                                                                                     |
| **FetchJobs legacy table**   | **One-time purge in migration 002**; keep empty schema in v1 of initiative; optional migration 003+ to `DROP TABLE` later                | Removes harmful every-boot `DELETE`; table drop is cosmetic and can wait.                                                                                                             |
| **Fresh install path**       | **Run full migration chain from 001** (no separate “create all tables” fork)                                                             | One code path; fewer drift bugs between new and upgraded DBs.                                                                                                                         |
| **Unit tests**               | **Option B — extract `MediaManager.Core` class library** (user choice)                                                                   | Clean net8.0 test boundary; fast CI later; aligns with SOLID and future splits.                                                                                                       |
| **Test stack**               | xUnit + FluentAssertions; coverlet advisory (no enforced CI threshold in initiative)                                                     | Standard .NET stack; coverage tracked manually until CI epic.                                                                                                                         |
| **Test exposure**            | `InternalsVisibleTo` on Core for fixture builders; public API tests for parser/evaluation                                                | Minimal surface change; avoids testing-only public APIs on WPF project.                                                                                                               |
| **VM lifetime (E3)**         | **Option A — singleton VMs + `INavigationAware` hooks** (Phase 3 before splits)                                                          | Most common WPF fix; user confirmed Phase 3 before Phase 4. Phase 5 (transient VMs) **reserved for future** — not part of “done”.                                                     |
| **Split depth (E4)**         | **Option B — section sub-ViewModels + user controls**; services extracted where logic-heavy (AutoTrack phases, Library catalog/detail)   | Matches existing UI structure (`SettingsSection`, Library catalog vs detail); better SRP than partial files alone.                                                                    |
| **Phase 4 scope**            | Settings → AutoTrack service → Library → DatabaseService repos → **TorrentWorkspaceViewModel** + FetchJobService (user: include Torrent) | Lowest runtime risk first; highest-value hunt logic protected by tests before split.                                                                                                  |
| **Done definition**          | **E1 + E2 + E3** complete; **E4 frozen**; Phase 5 explicitly out of scope                                                                | User-locked Aug 2026: personal app is complete enough; remaining splits are maintainability, not usefulness.                                                                          |


### Timeline at a glance


| Metric                | Value                                                                                      |
| --------------------- | ------------------------------------------------------------------------------------------ |
| **Sprint count**      | **11 planned; 0–4 shipped; 5–10 frozen**                                                   |
| **Calendar duration** | Stopped after Sprint 4. Remaining E4 weeks are not scheduled.                              |
| **Parallelism**       | Minimal — solo dev; only Sprint 1–2 had slight overlap (Core move vs migration design)     |
| **Merge strategy**    | One sprint merge (or small PRs) per sprint; never combine migration runner + Library split |


### Epic → sprint map

```
Sprint 0   [Kickoff & design]
Sprint 1   [Core lib + parser tests]          ─┐ E2 start
Sprint 2   [Migration runner A + mig tests]   ─┤ E1
Sprint 3   [Critical-path test matrix]        ─┘ E2
Sprint 4   [Navigation refresh hooks]           E3  ← initiative stops here
Sprint 5–6 [Settings split]                   ─┐
Sprint 7   [AutoTrack service split]            │  FROZEN / cancelled
Sprint 8   [Library catalog/detail split]       ├ E4 (design kept in 03; do not resume)
Sprint 9   [DB repos + Torrent/FetchJob split]  │
Sprint 10  [Closeout & regression]              ─┘
```

**Do not start Sprint 5–10.** Poster flash: [poster-flash-surgical-fix.md](./sprint-plans/poster-flash-surgical-fix.md) — not Sprint 8.

### Freeze rationale (Aug 2026)

1. No further product features. This is a personal Sonarr/*arr + Jellyfin + indexer replacement and is complete enough for daily use.
2. Remaining four-pillars **E4** sprints (Settings 7-VM, Auto-Track phase split, Library catalog/detail, DB repos/Torrent, closeout) are **frozen/cancelled as a program** — maintainability, not usefulness.
3. **Sprints 0–4 stay done and kept** (E1 migrations, E2 Core tests, E3 nav hooks).
4. Auto-Track and Library are the daily core; splitting them without a feature is the larger risk. Settings VM split is a landmine (UI tabs ≠ JSON/Apply) with no daily-use win.
5. The Library poster flash is the **only leftover bug** from that initiative. Fix it surgically — not via a Library or Auto-Track split. Optional Settings hygiene (Sprint 4.5) is out of this freeze task and is not a reason to resume Sprint 5.

---

## 2. Locked decisions


| #   | Question                                   | User choice                                             | Agent standard (where delegated)                                                   |
| --- | ------------------------------------------ | ------------------------------------------------------- | ---------------------------------------------------------------------------------- |
| 1   | Migration path                             | **Jump to Option A** (numbered scripts + history table) | Homegrown runner, not FluentMigrator; baseline 001 = schema as of initiative start |
| 2   | Test layout                                | **Option B — `MediaManager.Core`**                      | xUnit test project references Core only; WPF app references Core                   |
| 3   | Phase 3 before Phase 4?                    | **Yes** — then **E4 frozen** after Sprint 4             | Sprint 4 shipped; Sprint 5+ splits will not run                                    |
| 4   | Include `TorrentWorkspaceViewModel` in E4? | **Yes (design only)**                                   | Sprint 9 frozen with the rest of E4                                                |
| 5   | Phase 5 (transient VMs)?                   | **Reserved / not in scope**                             | Unchanged — hooks-only; do not reopen as part of this freeze                       |
| 6   | Initiative “done”                          | **E1 + E2 + E3** (E4 cancelled)                         | No Sprint 10 closeout. Poster flash is a separate surgical fix                     |
| 14  | E4 structural splits (S5–10)               | **Frozen / cancelled**                                  | Personal app works; splits are maintainability; Auto-Track/Library daily-core risk; Settings 7-VM is a landmine (UI tabs ≠ JSON/Apply) |
| 7   | Migration failure policy                   | **Block startup** + restore guidance                    | Error dialog → `CreateSafeSnapshot()` / Google Drive restore                       |
| 8   | FetchJobs table fate                       | **One-time purge in 002**                               | Keep empty schema in v1; optional 003+ to `DROP TABLE` later                       |
| 9   | Fresh install vs upgrade                   | **Single chain from 001**                               | No separate “create all tables” fork                                               |
| 10  | Settings split depth                       | **Sub-VMs + user controls** (03 Option B)               | Matches existing `SettingsSection` UI structure                                    |
| 11  | Settings dirty-tracking                    | **Required before reload** (Sprint 6)                   | `LoadFromSettings()` on every Settings visit only when not dirty                   |
| 12  | Coverage enforcement                       | **Advisory ≥80%** on parser + evaluation (Sprint 3)     | coverlet locally; no enforced CI threshold in initiative                           |
| 13  | CI test step                               | **Local gate every sprint**                             | `dotnet test` + Release x64 build; GitHub Actions deferred to post-initiative      |


---

## 3. Sprint overview table


| Sprint | Weeks (rel.) | Est. hours | Status | Goal (outcome)                         | Primary deliverable                                                           | Test gate                                     |
| ------ | ------------ | ---------- | ------ | -------------------------------------- | ----------------------------------------------------------------------------- | --------------------------------------------- |
| **0**  | W0–W1        | 8          | ✅ Done | Decisions locked; repos designed       | Migration inventory, Core project plan, branch strategy                       | N/A (docs only)                               |
| **1**  | W2–W3        | 12         | ✅ Done | Testable Core boundary exists          | `MediaManager.Core` + xUnit project; parser tests ≥15                         | `dotnet test` green; MSBuild x64 app build    |
| **2**  | W4–W5        | 12         | ✅ Done | Trustworthy DB upgrades                | `SchemaMigrations` runner; 001 baseline + 002 FetchJobs once; migration tests | Migration tests + manual DB upgrade           |
| **3**  | W6–W7        | 12         | ✅ Done | Critical logic regression-safe         | Evaluation, search, pack, validation test suites                              | ≥40 unit tests total; manual cart smoke       |
| **4**  | W8–W9        | 12         | ✅ Done | Stale UI fixed via navigation contract | `INavigationAware`; per-workspace refresh; Torrent cart continues off-tab     | Manual stale-UI repro scripts pass            |
| **5**  | W10–W11      | 12         | 🧊 Frozen | Settings maintainable (half)           | *Cancelled — do not resume.* Audit docs remain historical.                    | —                                             |
| **6**  | W12–W13      | 12         | 🧊 Frozen | Settings fully decomposed              | *Cancelled — do not resume.*                                                  | —                                             |
| **7**  | W14–W15      | 12         | 🧊 Frozen | AutoTrack phases isolated              | *Cancelled — do not resume.*                                                  | —                                             |
| **8**  | W16–W17      | 14         | 🧊 Frozen | Library split for catalog vs detail    | *Cancelled.* Poster flash is **not** this split — see surgical plan.          | —                                             |
| **9**  | W18–W19      | 14         | 🧊 Frozen | DB + Torrent debt reduced              | *Cancelled — do not resume.*                                                  | —                                             |
| **10** | W20–W21      | 10         | 🧊 Frozen | Initiative formally complete           | *Cancelled.* Initiative is done at E1+E2+E3.                                  | —                                             |


**Shipped:** Sprints 0–4. **Frozen:** 5–10. Do not treat the original ~22 week / 118 h estimate as remaining work.

---

## 4. Per-sprint detail

### Sprint 0 — Kickoff & design (W0–W1, ~8 h)

#### Goal

All technical choices are written, migration baseline is inventoried, and Core extraction order is agreed — zero production behavior change.

#### Scope


| In                                                                                                        | Out                      |
| --------------------------------------------------------------------------------------------------------- | ------------------------ |
| Export live DB schema (`PRAGMA table_info` all tables)                                                    | Any code merge to `main` |
| Numbered migration file layout (`Migrations/001_baseline.sql`, etc.)                                      | Implementing runner      |
| List of types moving to `MediaManager.Core` (parser, evaluation, search, pack, validation file-list path) | Moving types yet         |
| Branch naming: `initiative/refactor-sprint-N` or long-lived `initiative/four-pillars` with sprint tags    | Full CI pipeline         |


#### Migration milestone

Design only: baseline **001** = effective schema at initiative start; **002** = one-time FetchJobs row clear + mark legacy; **003** (optional later) = TorrentBlacklist rebuild extraction from inline code.

#### Test strategy

- **Unit tests added:** None  
- **Manual checklist:** N/A  
- **Regression areas:** None

#### Definition of Done

- [x] Schema inventory doc or spreadsheet checked into `docs/planning/` ([schema-inventory.md](./schema-inventory.md))
- [x] Migration numbering rules written (integer prefix, idempotent, never edit applied scripts)
- [x] Core type move list approved ([schema-inventory.md §5](./schema-inventory.md#5-core-type-move-list))
- [x] [00-integrated-roadmap.md](./00-integrated-roadmap.md) locked decisions table complete

#### Risk / rollback

Zero runtime risk. Rollback = don’t start Sprint 1.

---

### Sprint 1 — Core library & parser tests (W2–W3, ~12 h)

**Status:** ✅ **Complete** (Aug 2026) — committed on `auto-torrent`.

#### Goal

Pure torrent logic lives in `**MediaManager.Core**` with a running xUnit suite — parser behavior is frozen before any schema or UI work.

#### Scope


| In                                                                                | Out                                  |
| --------------------------------------------------------------------------------- | ------------------------------------ |
| New `MediaManager.Core` (net8.0) + `MediaManager.Core.Tests` (xUnit)              | Full evaluation/pack move (Sprint 3) |
| Move: `TorrentCandidateParser`, shared models/enums needed by parser              | Migration runner                     |
| Fixture helpers: `RecipeBuilder`, `TrackedShowBuilder`, sample torrent names      | VM changes                           |
| ≥15 parser tests: SxxExx, 1x01, absolute anime, season packs, OVA, quality tokens |                                      |


#### Migration milestone

None (DB untouched).

#### Test strategy

**Unit tests added**

- `TorrentCandidateParserTests` — minimum 15 cases from [02 § suggested first test cases](./02-unit-tests-critical-paths.md)

**Manual test checklist**

- [x] App launches; search torrent workspace still parses candidates (smoke)
- [x] MSBuild x64 Release build succeeds
- [x] `dotnet test` on solution passes (20 parser tests)

**Regression areas**

- Torrent workspace search results display
- Auto-Track hunt still parses filenames (no logic change expected)

#### Definition of Done

- [x] Core + Tests projects in solution; WPF project references Core
- [x] No duplicate type definitions (moved, not copied)
- [x] ≥15 parser tests green (20 tests)
- [x] `AI_CONTEXT.md` notes Core project
- [x] Git commit on `auto-torrent` + tag `four-pillars-sprint-01`
- [ ] Fixture helpers `RecipeBuilder`, `TrackedShowBuilder` — **deferred to Sprint 3** (not required for parser-only gate)

#### Session notes (Aug 2026)

- Canonical branch: `**auto-torrent**` (not stale `origin/main`).
- `MediaKind` moved to Core; `TorrentQualityScoring` remains in WPF (scoring deps).
- Core projects need `<Platforms>AnyCPU;x64</Platforms>` for VS Debug builds.
- Errors encountered: WPF glob picked up test files (fixed via `Compile Remove`); `Tokenize` made public for cross-assembly callers.

#### Risk / rollback

**Risk:** Namespace/move breaks WPF references. **Rollback:** Revert Core commit; types remain in WPF project. Keep move PR isolated.

---

### Sprint 2 — Migration runner (Option A) (W4–W5, ~12 h)

**Status:** ✅ **Complete** (Aug 2026) — committed on `auto-torrent`; tag `four-pillars-sprint-02`.

#### Goal

**Every database records applied migrations**; FetchJobs purge runs **once**, not every boot; fresh and upgraded DBs use the same path.

#### Scope


| In                                                                                        | Out                                                                  |
| ----------------------------------------------------------------------------------------- | -------------------------------------------------------------------- |
| `SchemaMigrations` table `(Id, Name, AppliedUtc)`                                         | Extracting all 127 `EnsureColumn` into files (incremental OK in 001) |
| Runner: apply pending migrations in order, transactional per migration                    | Full `DatabaseService` repository split (Sprint 9)                   |
| **001_baseline** — marks current effective schema (existing users no-op or fast-path)     | Dropping FetchJobs table                                             |
| **002_fetchjobs_legacy_purge** — `DELETE FROM FetchJobs` once                             | Navigation hooks                                                     |
| Remove unconditional `PurgeLegacyFetchJobs()` from init hot path                          |                                                                      |
| Move TorrentBlacklist rebuild toward **003** if time permits; else leave inline with TODO |                                                                      |
| Migration tests in Core.Tests or dedicated test project                                   |                                                                      |


#### Migration milestone

- **M1:** ✅ Runner live; history table populated on first run after upgrade
- **M2:** ✅ FetchJobs purge demoted to migration 002
- **003:** Deferred to Sprint 3 (inline TorrentBlacklist rebuild kept with `TODO Sprint 3`)

#### Test strategy

**Unit tests added**

- [x] `MigrationRunnerTests`: empty DB → all migrations applied, `SchemaMigrations` row count correct
- [ ] `MigrationRunnerTests`: legacy fixture DB with old `TorrentHash` column → upgrades without data loss (**deferred with 003**)
- [x] `MigrationRunnerTests`: running runner twice is idempotent (no duplicate applies)
- [x] Extra: FetchJobs rows purged once; second runner pass does not delete new rows

**Automated gate (already green)**

```bash
APP_ROOT="d:/VScode/Misc/Media_Manager/media management app/media management app"
dotnet test "$APP_ROOT/MediaManager.Core.Tests/MediaManager.Core.Tests.csproj" -c Release -v normal
# Expected: 23 passed (20 parser + 3 migration)
dotnet build "$APP_ROOT/media management app.csproj" -c Release -p:Platform=x64 -v minimal
```

**Manual test checklist** (human, Aug 2026 — `D:\MediaManagerState_sprint2test`)

1. **Prepare a throwaway state folder**
  - [x] Used `D:\MediaManagerState_sprint2test` (Release x64 against disposable StateFolder)
2. **Upgrade / first-migrate smoke**
  - [x] App starts with no migration error dialog
     [x] `SchemaMigrations` has `001_baseline` + `002_fetchjobs_legacy_purge` with `AppliedUtc` (sqlite3 verified ~23:20 local / `16:20:08Z`)
3. **Second start (idempotent / no purge spam)**
  - [x] Quit and launch again against the same folder
     [x] Log `20260822_232815_154_systemlog.txt`: only `Initializing` + `SQLite database is ready` — **no** re-apply of 001/002, **no** FetchJobs purge
     [x] `SchemaMigrations` still exactly two rows
4. **Fresh install path**
  - [x] Empty/new DB path: log `20260822_232518_983_systemlog.txt` shows apply 001 then 002 then ready; sqlite3 `AppliedUtc` `16:25:19Z` matches
5. **Backup / snapshot**
  - [x] Manual Google Drive backup succeeded after migrate (`BackupService` uploaded at `2026-08-22 16:28:32Z`)
6. **Light regression**
  - [x] Spot-checked by human (workspaces / cart / library as exercised during session)

**Regression areas**

- App startup
- Auto-Track read/write tracked media
- Torrent cart persistence
- Google Drive backup trigger from Settings

#### Definition of Done

- [x] `SchemaMigrations` records each applied migration
- [x] No unconditional FetchJobs purge in `Initialize()`
- [x] Migration tests in `dotnet test` (3 tests; suite total 23)
- [x] `STATE_FOLDER.md` + `AI_CONTEXT.md` migration section updated
- [x] Manual DB checklist above (human)
- [x] Git commit on `auto-torrent` + tag `four-pillars-sprint-02`

#### Session notes (Aug 2026)

**Delivered**


| Artifact  | Location                                                                                                   |
| --------- | ---------------------------------------------------------------------------------------------------------- |
| Runner    | `MediaManager.Core/Migrations/MigrationRunner.cs`                                                          |
| Exception | `MediaManager.Core/Migrations/DatabaseMigrationException.cs`                                               |
| SQL       | `001_baseline.sql`, `002_fetchjobs_legacy_purge.sql` (embedded resources)                                  |
| Tests     | `MediaManager.Core.Tests/Migrations/MigrationRunnerTests.cs`                                               |
| Wire-up   | `DatabaseService.Initialize()` runs runner first; `App.xaml.cs` shuts down on `DatabaseMigrationException` |
| Solution  | Parent `media management app.slnx` lists Core + Tests + WPF (fixes VS stale-Core rebuild)                  |
| Docs      | `docs/STATE_FOLDER.md`, `docs/AI_CONTEXT.md`, `docs/BUILD.md`, planning docs                               |


**Transition design:** `EnsureColumn` chain **kept** as safety net (not extracted in Sprint 2). `PurgeLegacyFetchJobs` **removed**. Optional **003** TorrentBlacklist rebuild **deferred**.

**Errors encountered (and fixes)**


| Error                                                                            | Cause                                                                                                   | Fix                                                                                                                 |
| -------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| `CS8207: An expression tree may not contain a discard` in `MigrationRunnerTests` | FluentAssertions `OnlyContain` lambda used `DateTime.TryParse(..., out _)`                              | Extracted helper `IsRoundtripDateTime(string)`                                                                      |
| First `StrReplace` on `Initialize()` open/connection block matched ambiguously   | Many `connection.Open()` sites in `DatabaseService.cs`                                                  | Retargeted with more surrounding context (`Initializing SQLite database…`)                                          |
| Dialog string used `{Environment.NewLine}` inside a non-interpolated literal     | Would show literal braces                                                                               | Switched message construction to `$"..."` interpolated string                                                       |
| VS **Release                                                                     | x64**: `CS0234` / `CS0246` — `media_management_app.Migrations` / `DatabaseMigrationException` not found | `.slnx` listed only the WPF project; **Rebuild media management app** skipped fresh `MediaManager.Core` (stale DLL) |
| Git Bash / VS confusion on “wrong project”                                       | Opening parent `.slnx` is correct; Core lives under nested `media management app/` folder               | Clarify in BUILD.md + sprint notes                                                                                  |


**Not an error (by design):** migration **003** not shipped; inline blacklist rebuild remains with `// TODO Sprint 3`.

**Manual verify notes:** Fresh-install log proves 001→002 apply; second-start log proves skip; sqlite3 confirmed history rows. Full production-data upgrade smoke optional later (test folder DB was empty during verify).

#### Risk / rollback

**Risk:** Baseline mis-identifies already-migrated DB → duplicate alters or skipped columns. **Mitigation:** Test against copy of real DB before production path. **Rollback:** Restore `.db` from snapshot; revert runner commit (EnsureColumn chain still in previous release).

---

### Sprint 3 — Critical-path test expansion (W6–W7, ~12 h)

**Status:** ✅ **Complete** (Aug 2026) — committed on `auto-torrent`; tag `four-pillars-sprint-03`.

#### Goal

Auto-Track/cart **business logic** is covered by golden fixtures — safe to refactor services in Sprints 7–9.

#### Scope


| In                                                                                                                                                                                                | Out                                                         |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------- |
| Move to Core: `CandidateEvaluationService`, `SearchPlanBuilder`, `SearchTitleResolver`, `PackSeasonFileGrouper`, `PackEpisodePatternInferrer`, `TorrentContentValidationService` (file-list path) | FetchJobService/AutoTrackService tests (integration; later) |
| Mock `ISearchTitleResolver` where needed                                                                                                                                                          | UI tests                                                    |
| Golden JSON/text fixtures for anonymized torrent names                                                                                                                                            | 80% enforced in CI                                          |


#### Migration milestone

**003:** Shipped in Sprint 3 — `003_torrentblacklist_rebuild` (C# conditional); inline rebuild removed from `DatabaseService`.

#### Test strategy

**Unit tests added** (target counts)


| Area                                        | Tests | Focus                                                         |
| ------------------------------------------- | ----- | ------------------------------------------------------------- |
| `CandidateEvaluationService`                | ≥12   | Accept/reject matrix, `CandidateRejectReason`, anime absolute |
| `SearchPlanBuilder` + `SearchTitleResolver` | ≥8    | Query templates, alias expansion                              |
| `PackSeasonFileGrouper`                     | ≥5    | Multi-season folders, Extras excluded                         |
| `PackEpisodePatternInferrer`                | ≥10   | Stem inference edge cases (Appendix A scenarios)              |
| `TorrentContentValidationService`           | ≥8    | `.exe` disguised, double extension, sample count              |


**Manual test checklist** (human, Aug 2026 — `D:\MediaManagerState`)

- [x] Add torrent to cart manually; candidate list sane
- [x] Pack link review opens with correct episode mapping (one show)
- [ ] Auto-Track hunt on one tracked show (dev qBittorrent) — **deferred** to later sprint smoke / Sprint 7 hunt verify
- [x] Release x64 start applied `003_torrentblacklist_rebuild` once (`SchemaMigrations` Id=3, AppliedUtc `2026-08-23T03:28:18Z`)

**Regression areas**

- Parser (Sprint 1) — full suite must stay green
- Migrations (Sprint 2) — run migration tests before merge

#### Definition of Done

- [x] ≥40 unit tests total in Core.Tests (**80**: 20 parser + 4 migration + 16 eval + 10 search + 6 grouper + 12 inferrer + 10 validation)
- [x] Advisory ≥80% line coverage on parser + evaluation (coverlet: parser **90.9%**, evaluation **94.8%**)
- [x] No WPF reference from test project
- [x] Manual checklist 2/3 + production DB migration 003 verified (Auto-Track hunt deferred)
- [x] Git commit on `auto-torrent` + tag `four-pillars-sprint-03`

#### Session notes (Aug 2026)

**Delivered**


| Artifact                                | Location                                                                           |
| --------------------------------------- | ---------------------------------------------------------------------------------- |
| Evaluation / search / pack / validation | `MediaManager.Core/Services/`                                                      |
| Models + RecipeRuntimeSettings          | `MediaManager.Core/Models/`, `MediaManager.Core/Services/RecipeRuntimeSettings.cs` |
| qBit wrapper                            | `Services/QbittorrentTorrentContentValidationService.cs`                           |
| Tests                                   | `MediaManager.Core.Tests/{Evaluation,Search,Pack,Validation,Fixtures}/`            |
| Builders                                | `RecipeBuilder`, `TrackedShowBuilder`, `FakeSearchTitleResolver`                   |
| Golden JSON                             | `MediaManager.Core.Tests/Fixtures/*.json`                                          |
| Migration 003                           | `TorrentBlacklistRebuildMigration.cs` + marker SQL                                 |


**003:** shipped (conditional rebuild; legacy `TorrentHash` fixture test). **DI:** `ITorrentContentValidationService` → App wrapper; other services still registered in `App.xaml.cs`.

**Errors encountered (and fixes)**


| Error                                              | Cause                                                              | Fix                                                    |
| -------------------------------------------------- | ------------------------------------------------------------------ | ------------------------------------------------------ |
| XAML MC3050 `ShowSeriesStatus` / `RecipeBlockType` | Types moved to Core; `clr-namespace` without assembly looks in WPF | `assembly=MediaManager.Core` on Library + Recipe xmlns |
| Compact stem tests expected `S01E05`               | Inferrer compact regex is `S0105` (no `E`)                         | Tests use compact `S0105` form                         |


**Automated gate:** migration filter 4 passed; full suite **80 passed**; Release x64 **0 errors**.

#### Risk / rollback

**Risk:** Moving types breaks DI registration. **Rollback:** Single revert of Core move commit.

---

### Sprint 4 — Navigation refresh hooks (E3) (W8–W9, ~12 h)

**Status:** ✅ **Complete** (Aug 2026) — committed on `auto-torrent`; tag `four-pillars-sprint-04`.

#### Goal

**Returning to a workspace shows fresh data** without manual Refresh; background Auto-Track/cart updates visible on next visit.

#### Scope


| In                                                                                                   | Out                                           |
| ---------------------------------------------------------------------------------------------------- | --------------------------------------------- |
| `INavigationAware` (`OnNavigatedTo` / `OnNavigatedFrom`) on `ViewModelBase`                          | Transient VMs (Phase 5)                       |
| `MainViewModel.NavigateTo` invokes hooks                                                             | Splitting large VMs                           |
| Per-workspace refresh policy (see [04 § draft behaviors](./04-transient-vs-singleton-viewmodels.md)) | Full `IWorkspaceRefreshService` unless needed |
| Torrent cart/search **keep running** when navigating away (explicit Stop only)                       | Cancel `_operationCts` on leave               |
| FindAdd: `RefreshExistingMedia()` on navigate                                                        | Settings dirty-tracking (Sprint 6)            |


#### Migration milestone

None.

#### Test strategy

**Unit tests added**

- Optional: `NavigationAwareTests` for a small coordinator if extracted to Core (low priority)
- **Regression:** Full Core test suite green

**Manual test checklist** (stale-UI repro scripts) — tick as you run:

- [x] **1** Auto-Track run → News → Library — Library grid reflects new/changed tracked media without Refresh
- [x] **2** Cart add in Torrent → Library → Torrent — Cart badge/state updated
- [x] **3** Edit `settings.json` externally → Settings workspace — Reload shows disk values (dirty prompt = Sprint 6)
- [x] **4** Start torrent search → navigate away mid-search → return to Torrent — Search/cart **still running** (or completed); Stop still works; no forced cancel on leave
- [x] **5** FindAdd → Library → FindAdd — Existing-media flags updated
- [x] **6** Long session: minimize to tray → restore → Library — Poster reload policy still works

**Regression areas**

- Library/Torrent UI state persistence (`settings.json`)
- Auto-Track scheduler
- Main window navigation/shortcuts

#### Definition of Done

- [x] All seven workspace VMs implement refresh policy (documented in code or `AI_CONTEXT.md`)
- [x] Repro scripts 1–5 pass (checkboxes above)
- [x] No new singleton memory leaks from duplicate event subscriptions
- [x] Torrent (and similar UI long-ops) do **not** cancel solely because the user changed workspace
- [x] Git commit on `auto-torrent` + tag `four-pillars-sprint-04`

#### Risk / rollback

**Risk:** Over-refresh causes slow tab switches on large libraries. **Mitigation:** Catalog refresh may be incremental later; for initiative, full `RefreshLibrary()` acceptable per [04](./04-transient-vs-singleton-viewmodels.md). **Accepted:** cart/search UI updates may continue while on another tab (single-user personal app). **Rollback:** Revert hook wiring; VMs behave as before.

#### Session notes (Aug 2026)

- Policy change vs early Sprint 4 draft: **no** `_operationCts.Cancel()` on `OnNavigatedFrom` — multitask across tabs preferred for personal single-user use.
- Cancel path remains explicit **Stop** on Torrent workspace.
- Human stale-UI scripts **1–6** passed (Aug 2026).
- **Known debt (not S4, not S8):** Library detail poster flashes **No cover** when `Reconciled`/`PackReconciled` fires a full `LoadSelectedMediaAsync` while the user is already on Library. Fix surgically: [poster-flash-surgical-fix.md](./sprint-plans/poster-flash-surgical-fix.md). Do **not** extract `LibraryDetailViewModel` and do **not** resume Sprint 8.

---

### Sprint 5 — Settings split part 1 (E4) (W10–W11, ~12 h)

**Status:** 🧊 **Frozen / cancelled** (Aug 2026). Do **not** resume. Audit remains historical.  
**Audit:** [settings-modernization/](../settings-modernization/README.md) · Local plan: [sprint-05-local-plan.md](./sprint-plans/sprint-05-local-plan.md) (superseded)

#### Goal

**Three settings sections** are owned by dedicated sub-VMs and user controls — pattern proven for the rest.

**Freeze (was pause):** UI tabs (7) do not match JSON/`Apply*` ownership. User chose **not** to pick a full modernization track and **not** to run the 7-VM split. E4 is cancelled; optional Sprint 4.5 hygiene is not this task.

#### Scope


| In                                                                                                 | Out                                |
| -------------------------------------------------------------------------------------------------- | ---------------------------------- |
| Host `SettingsViewModel` keeps `SelectedSettingsSection`, save orchestration                       | All 7 sections (Sprint 6)          |
| Sub-VMs + `IntegrationsSettingsPanel.xaml`, `BackupSettingsPanel.xaml`, `SystemSettingsPanel.xaml` | AutoTrack/Library splits           |
| DI registration for section VMs                                                                    | Behavior change to settings values |


#### Migration milestone

None.

#### Test strategy

**Unit tests added**

- None required if refactor-only; optional tests for any settings validation logic moved to Core

**Manual test checklist**

- [ ] Each migrated section: load values on open, edit, persist after Save/app restart
- [ ] Test TMDB / qBittorrent / Gemini / Jellyfin / WARP buttons still work (Integrations)
- [ ] Google Drive backup + restore smoke (Backup)
- [ ] State folder browse, logging level (System)
- [ ] Section switching does not lose unsaved edits in current section (within-session)

**Regression areas**

- All settings sections (including not-yet-split — still on host)
- `settings.json` round-trip

#### Definition of Done

- [ ] Integrations, Backup, System extracted; host file smaller
- [ ] XAML binds to `{Binding Integrations.TmdbToken}` style paths
- [ ] `dotnet test` + MSBuild x64 green

#### Risk / rollback

**Risk:** Binding path errors silent in UI. **Mitigation:** Click every control in migrated sections. **Rollback:** Revert to monolithic Settings VM.

#### Session notes (Aug 2026 — audit, no code merge)

**Findings (historical — do not use as a resume trigger):**

| # | Finding | Impact |
| --- | --------- | ------ |
| 1 | Jellyfin UI split: credentials on **Integrations**, refresh/log on **Auto-Track**; one `AutoTrack.Jellyfin` object | Cannot extract Integrations VM without Apply split |
| 2 | qBittorrent UI on **Integrations**, download folders on **Torrent Storage**; one `ApplyAutoTorrentSettings()` | Same |
| 3 | WARP UI on **Integrations**, JSON root `Warp`, consumed by Auto-Track hunt | Belongs with Auto-Track domain |
| 4 | `Save()` applies all sections; test buttons call partial `Apply*` (sometimes full AutoTorrent/AutoTrack) | Split + dirty-tracking landmines |
| 5 | `UiSettings` written by Library/Torrent/News VMs — not Settings host | Whole-file Save last-writer-wins |
| 6 | Live `Current` mutation: library preview, WARP path keystrokes | In-memory drift before Save |
| 7 | `SystemSettingsViewModel` = workspace VM; collides with planned System **section** VM name | Rename to `SettingsWorkspaceViewModel` when split resumes |

**Not resumed.** Design only: [settings-modernization/05-split-boundaries-recommendation.md](../settings-modernization/05-split-boundaries-recommendation.md).

---

### Sprint 6 — Settings split part 2 (E4) (W12–W13, ~12 h)

**Status:** 🧊 **Frozen / cancelled.** Do not resume. Design below is historical.

#### Goal

**SettingsViewModel fully decomposed** — no section remains in the monolith; dirty-tracking protects reload-on-navigate.

#### Scope


| In                                                                                    | Out                                         |
| ------------------------------------------------------------------------------------- | ------------------------------------------- |
| Sub-VMs: Library, AutoTrack, TorrentStorage, Notifications                            | Library/AutoTrack service splits            |
| User controls per section                                                             |                                             |
| **Dirty-tracking** on Settings: prompt if `OnNavigatedTo` would clobber unsaved edits | Per-section Save buttons (keep global Save) |
| Reduce `SettingsViewModel.cs` to coordinator (~300 lines target)                      |                                             |


#### Migration milestone

None.

#### Test strategy

**Unit tests added**

- Optional: unit test for dirty-flag coordinator logic if extracted

**Manual test checklist**

- [ ] All 7 sections: field edit + save + restart
- [ ] Navigate away from Settings with unsaved edit → prompt
- [ ] Sprint 4 script #3 with dirty prompt behavior
- [ ] Symlink sync, notification test, torrent storage browse

**Regression areas**

- Entire Settings workspace
- Sprint 4 navigation hooks for Settings

#### Definition of Done

- [ ] All sections in sub-VMs; `SettingsViewModel` under ~400 lines
- [ ] `SettingsView.xaml` split into user controls or tab content templates
- [ ] Document ownership in `AI_CONTEXT.md`

#### Risk / rollback

Same as Sprint 5.

---

### Sprint 7 — AutoTrack service split (E4) (W14–W15, ~12 h)

**Status:** 🧊 **Frozen / cancelled.** Do not resume. Auto-Track is daily core; split without a feature is the larger risk. Design below is historical.

#### Goal

**AutoTrack phases are independently testable and maintainable** behind existing `IAutoTrackService` façade.

#### Scope


| In                                                                                   | Out                              |
| ------------------------------------------------------------------------------------ | -------------------------------- |
| `AutoTrackTmdbDiscoveryService`, `AutoTrackHuntService`, `AutoTrackReconcileService` | Library VM split                 |
| `AutoTrackService` delegates; locks stay in façade or move with clear ownership      | FetchJobService split (Sprint 9) |
| Hunt service: extract WARP/qBittorrent preflight block                               |                                  |


#### Migration milestone

None.

#### Test strategy

**Unit tests added**

- Extend tests for hunt **policy** pieces already in Core (`AutoTrackCandidatePolicyService` if moved)
- Characterization test: hunt entry rejects when preflight fails (mocked dependencies)

**Manual test checklist**

- [ ] TMDB discovery run on schedule or manual
- [ ] Torrent hunt completes for one show
- [ ] Background reconcile links completed download
- [ ] SSL/WARP recovery path (if enabled in settings)
- [ ] Scheduler still fires; no duplicate hunts (locks)

**Regression areas**

- Sprint 3 evaluation tests (must stay green)
- Auto-Track UI (`AutoTrackViewModel`)
- Notifications after hunt

#### Definition of Done

- [ ] No single AutoTrack file > ~600 lines without justification
- [ ] Public API unchanged (`IAutoTrackService`)
- [ ] Tests green

#### Risk / rollback

**Risk:** Lock/semaphore regression → duplicate hunts. **Mitigation:** Manual concurrent run attempt. **Rollback:** Revert split; monolithic service restored.

---

### Sprint 8 — Library split (E4) (W16–W17, ~14 h)

**Status:** 🧊 **Frozen / cancelled.** Do **not** extract `LibraryCatalogViewModel` / `LibraryDetailViewModel`. Poster flash is **not** owned by this sprint.

#### Goal

**Library catalog and detail concerns separated** — grid commands vs detail/pack/cart commands no longer share one 2.4k-line type.

#### Scope


| In                                                                                  | Out                                                              |
| ----------------------------------------------------------------------------------- | ---------------------------------------------------------------- |
| `LibraryCatalogViewModel` — grid, sort, filter, selection, persist UI state         | Torrent workspace split                                          |
| `LibraryDetailViewModel` (or nested) — seasons, episodes, pack link, cart on detail | Full service extraction (LibraryImportCoordinator optional stub) |
| Host `LibraryViewModel` composes catalog + detail; XAML `ContentControl` for detail | Database repos                                                   |


**Poster flash — moved out of this sprint:**  
Auto-Track / torrent `Reconciled` (and `PackReconciled`) currently call full `LoadSelectedMediaAsync` on `LibraryViewModel`, which clears `SelectedPosterImage` then rebuilds detail. While the user is already on Library with a media selected, the cover flashes **No cover**. **Do not** wait for a catalog/detail split. Surgical plan: [poster-flash-surgical-fix.md](./sprint-plans/poster-flash-surgical-fix.md).


#### Migration milestone

None.

#### Test strategy

**Unit tests added**

- None mandatory; optional tests if import/pack logic moved to Core service

**Manual test checklist**

- [ ] Refresh library, sort, filter, search
- [ ] Select show → detail loads; select movie → movie detail
- [ ] Pack link / unlink / review dialog
- [ ] Add to cart from library detail
- [ ] Import external media flow
- [ ] TMDB refresh on show
- [ ] Delete media; watch status; rating
- [ ] Sprint 4 Library navigation script still passes
- [ ] Selection restored from `settings.json` after restart
- [ ] Reconcile while Library detail is open — poster does **not** reset to No cover (S4 known debt)

**Regression areas**

- Cart events (`CartChanged`)
- Reconciliation / pack reconcile events
- Poster memory release on background mode

#### Definition of Done

- [ ] `LibraryViewModel.cs` (host) under ~500 lines; catalog/detail in separate files
- [ ] All 47 commands accounted for (none orphaned)
- [ ] Tests + build green

#### Risk / rollback

**Risk:** Event subscription duplicated or dropped. **Mitigation:** Checklist for cart/reconcile handlers. **Rollback:** Revert split PR.

---

### Sprint 9 — Database repos + Torrent/FetchJob (E4) (W18–W19, ~14 h)

**Status:** 🧊 **Frozen / cancelled.** Do not resume. Design below is historical.

#### Goal

**DatabaseService sheds migration + table ownership**; Torrent workspace and FetchJob orchestration are split to match Library/Settings scale.

#### Scope


| In                                                                                           | Out                                                  |
| -------------------------------------------------------------------------------------------- | ---------------------------------------------------- |
| Extract `MigrationRunner` from `DatabaseService` (if not already thin)                       | Phase 5 transient VMs                                |
| Repository interfaces per aggregate: `ITrackedShowRepository`, `ISourceItemRepository`, etc. | Perfect repository granularity — pragmatic slices OK |
| `DatabaseService` becomes thin coordinator or phased out of CRUD                             |                                                      |
| `TorrentWorkspaceViewModel` → catalog vs cart/search panes (mirror Library pattern)          |                                                      |
| `FetchJobService` — extract search orchestration helpers; optional split from hunt           |                                                      |


#### Migration milestone

- **M3:** `Initialize()` no longer contains long `EnsureColumn` chains — upgrades only via runner
- Optional **004+**: drop empty `FetchJobs` table

#### Test strategy

**Unit tests added**

- Migration tests still pass after runner extraction
- Any pure helpers moved from FetchJob to Core get unit tests

**Manual test checklist**

- [ ] Full app regression on DB operations: CRUD tracked show, episode, cart order, blacklist
- [ ] Torrent workspace: search, assign recipe, cart run, restore UI state
- [ ] Fetch job polling / cancel
- [ ] Upgrade DB from Sprint 2 backup copy again (migration idempotency)

**Regression areas**

- All DB-touching features
- Sprint 2 migrations
- Torrent + Library workspaces

#### Definition of Done

- [ ] `DatabaseService.cs` under ~800 lines (CRUD delegated)
- [ ] `TorrentWorkspaceViewModel` split; file under ~800 lines each
- [ ] Migration runner is standalone component with tests
- [ ] E1 exit criteria met ( trustworthy upgrades )

#### Risk / rollback

**Highest sprint risk** — touches persistence layer and large VM. **Mitigation:** DB backup before merge; run migration tests + manual CRUD checklist. **Rollback:** Restore DB snapshot; revert repo split (keep migration runner if stable separately).

---

### Sprint 10 — Closeout & final regression (W20–W21, ~10 h)

**Status:** 🧊 **Frozen / cancelled.** Initiative is **done at E1+E2+E3**. No E4 closeout sprint.

#### Goal (historical)

**Initiative declared done** per original E1–E4 bar; documentation matches code; no unjustified giant files remain.

#### Scope


| In                                                                         | Out                   |
| -------------------------------------------------------------------------- | --------------------- |
| Final manual regression pass (full app)                                    | Phase 5 transient VMs |
| Update `AI_CONTEXT.md`, `FEATURES.md` module map, `IMPROVEMENTS.md` status | New features          |
| Line-count audit; document exceptions >800 lines                           | CI/CD epic            |
| Optional: local `dotnet test` script for future CI                         |                       |


#### Migration milestone

Freeze migration numbering until next feature schema change.

#### Test strategy

**Unit tests added**

- Fill gaps from critical-path matrix (any ❌ remaining)

**Manual test checklist — full regression**

- [ ] Fresh install (empty state folder)
- [ ] Upgrade from pre-initiative DB backup
- [ ] Library / Torrent / Auto-Track / Settings / FindAdd / News / Recipe — smoke each
- [ ] Auto-Track end-to-end overnight soak (optional)
- [ ] Backup + restore Google Drive
- [ ] Symlink sync sample

**Regression areas**

- Everything

#### Definition of Done

- [ ] User locked bar met: **E1 + E2 + E3** (E4 frozen — this sprint cancelled)
- [ ] ≥40 unit tests; parser/evaluation coverage ≥80% advisory
- [ ] No unconditional FetchJobs purge
- [ ] Stale UI scripts pass
- [ ] No file > ~800 lines without documented exception
- [ ] Phase 5 triggers documented (see §7)

#### Risk / rollback

N/A — verification sprint.

---

## 5. Critical-path unit test matrix

Legend: ✅ = primary sprint for coverage · 🔄 = extend existing · — = not in initiative scope · (i) = integration/manual only


| Feature / component                             | S0  | S1    | S2    | S3          | S4  | S5–10    |
| ----------------------------------------------- | --- | ----- | ----- | ----------- | --- | -------- |
| **TorrentCandidateParser**                      | —   | ✅ ≥15 | 🔄    | 🔄          | 🔄  | 🔄       |
| **CandidateEvaluationService**                  | —   | —     | —     | ✅ ≥12       | 🔄  | 🔄       |
| **SearchPlanBuilder**                           | —   | —     | —     | ✅           | 🔄  | 🔄       |
| **SearchTitleResolver**                         | —   | —     | —     | ✅           | 🔄  | 🔄       |
| **PackSeasonFileGrouper**                       | —   | —     | —     | ✅           | 🔄  | 🔄       |
| **PackEpisodePatternInferrer**                  | —   | —     | —     | ✅ ≥10       | 🔄  | 🔄       |
| **TorrentContentValidationService** (file list) | —   | —     | —     | ✅ ≥8        | 🔄  | 🔄       |
| **Migration runner** (empty + legacy DB)        | —   | —     | ✅     | 🔄          | —   | 🔄       |
| **TorrentBlacklist rebuild**                    | —   | —     | ✅/003 | 🔄          | —   | 🔄       |
| **RecipeRuntimeSettings**                       | —   | —     | —     | 🔄 optional | —   | —        |
| **AutoTrackCandidatePolicyService**             | —   | —     | —     | 🔄          | —   | ✅ S7     |
| **FetchJobService** orchestration               | —   | —     | —     | —           | —   | (i) S9   |
| **AutoTrackService** phases                     | —   | —     | —     | —           | —   | (i) S7   |
| **Pack link coordinator**                       | —   | —     | —     | —           | —   | (i) S8   |
| **Navigation / INavigationAware**               | —   | —     | —     | —           | (i) | 🔄       |
| **Settings sub-VMs**                            | —   | —     | —     | —           | —   | (i) S5–6 |
| **Library catalog/detail**                      | —   | —     | —     | —           | —   | (i) S8   |
| **Repository CRUD**                             | —   | —     | —     | —           | —   | (i) S9   |


**Post-Sprint 3 rule:** Any PR touching parser, evaluation, search, pack, or validation must run `dotnet test` before merge.

---

## 6. SOLID mapping by sprint


| Sprint  | Primary principles          | How                                                                |
| ------- | --------------------------- | ------------------------------------------------------------------ |
| **0**   | **D** Dependency Inversion  | Core boundary designed; WPF depends on abstractions to be moved    |
| **1**   | **S** Single Responsibility | Parser isolated from WPF shell                                     |
| **2**   | **O** Open/Closed           | New schema changes = new migration file, not edit init chain       |
| **3**   | **S**, **D**                | Pure services testable via interfaces/mocks                        |
| **4**   | **I** Interface Segregation | Small `INavigationAware` contract vs fat VM base                   |
| **5–6** | **S**                       | One sub-VM per settings section — one reason to change             |
| **7**   | **S**, **D**                | AutoTrack phases; hunt depends on `IFetchJobService` etc.          |
| **8**   | **S**                       | Catalog vs detail — split reasons to change                        |
| **9**   | **S**, **I**, **D**         | Repositories per aggregate; thin coordinator                       |
| **10**  | **L**                       | Sub-VMs substitutable via host bindings without breaking save/load |


---

## 7. Post-initiative state (“done” looks like)

### Stability (E1)

- `SchemaMigrations` table lists every applied migration with timestamp
- No `DELETE FROM FetchJobs` on every startup
- `DatabaseService.Initialize()` delegates to migration runner; EnsureColumn chain retired
- Operator docs describe failed-migration recovery (restore snapshot)

### Correctness (E2)

- `MediaManager.Core` + `MediaManager.Core.Tests` in solution
- ≥40 unit tests on parser, evaluation, search, pack, validation, migrations
- Local `dotnet test` is the standard pre-merge check
- Core library has no WPF dependency

### UX freshness (E3)

- `INavigationAware` wired for all workspaces
- Documented refresh policy per workspace in `AI_CONTEXT.md`
- Torrent (and similar workspace UI) long operations **continue** when navigating away; explicit Stop cancels
- Settings reload respects dirty-tracking

### Maintainability (E4) — **frozen / not required for done**

Original split targets (Settings 7-VM, AutoTrack phases, Library catalog/detail, Torrent split, DB repos) **will not ship**. Design remains in [03](./03-split-large-viewmodels-services.md). Large files stay as they are.

### Explicitly NOT done (reserved)

### Explicitly NOT done (reserved)

- **Phase 5 transient VMs** — reopen if: working set > X MB after 24h session, or hooks insufficient for Recipe/FindAdd caches
- **GitHub Actions CI** — separate improvement item
- **Recipe validation on save** — Medium priority in IMPROVEMENTS
- **UI automation tests** (WinAppDriver etc.)

### Suggested Phase 5 trigger checklist (future)

- [ ] Task Manager working set > 1.5 GB with normal library after 8h
- [ ] Stale UI bug reopened despite hooks
- [ ] User wants web-style “always fresh” tabs for FindAdd/Torrent only

---

## 8. Manual regression master checklist (Sprint 10)

Use as copy-paste test run; mark date and app version.

```
[ ] Startup / shutdown / tray minimize-restore
[ ] DB upgrade from legacy backup
[ ] Fresh install migration chain
[ ] Library: browse, detail, import, delete, pack link, cart add
[ ] Torrent: search, recipe, cart run; cart continues if user switches tabs (Stop still works)
[ ] Auto-Track: discovery, hunt, reconcile, notifications
[ ] FindAdd: TMDB search, add show/movie
[ ] Recipe workspace: open, edit, save .rcp
[ ] News: weekly cards load
[ ] Settings: all 7 sections save/load; integration test buttons
[ ] Backup/restore Google Drive
[ ] Symlink sync (if configured)
[ ] Stale UI scripts (Sprint 4 table)
[ ] dotnet test — all green
[ ] MSBuild x64 Release build
```

---

## 9. AI-accelerated schedule

The original **~22 week** table assumed all 11 sprints. **That schedule is obsolete.** Initiative stopped after Sprint 4. Days 7–20 (Sprints 5–10) below are **historical — do not run.**

**How to execute:** [06-ai-execution-guide.md](./06-ai-execution-guide.md) — session rhythm, prompt templates, Sprint 0 start checklist, pre-merge review.


| Calendar (focused) | Sprints     | Primary deliverable              | Merge gate (unchanged)                 |
| ------------------ | ----------- | -------------------------------- | -------------------------------------- |
| Day 1              | 0 → 1 start | Schema inventory + Core scaffold | S0: docs DoD; S1: build + test         |
| Day 2              | 1           | ≥15 parser tests                 | `dotnet test`; MSBuild x64             |
| Day 3              | 2           | Migration runner + 001/002       | Migration tests + DB copy upgrade      |
| Day 4–5            | 3           | ≥40 tests; evaluation in Core    | coverlet advisory; cart smoke          |
| Day 6              | 4           | `INavigationAware` wired         | Stale-UI repro scripts 1–5             |
| Day 7–20           | 5–10        | **Frozen — do not execute**      | —                                      |


**Do not resume S5–S10.** Next code work is the surgical poster fix only: [poster-flash-surgical-fix.md](./sprint-plans/poster-flash-surgical-fix.md).

---

## Related reading

- [06-ai-execution-guide.md](./06-ai-execution-guide.md) — **how to execute** with AI (prompts, review checklist, risks)  
- [00-integrated-roadmap.md](./00-integrated-roadmap.md) — dependency map and locked decisions  
- [planning/README.md](./README.md) — index of all planning docs  
- [IMPROVEMENTS.md](../IMPROVEMENTS.md) — source priorities  
- [AI_CONTEXT.md](../AI_CONTEXT.md) — update after each sprint merge

