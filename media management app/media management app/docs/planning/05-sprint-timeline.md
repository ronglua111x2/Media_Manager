# Sprint Timeline — Four High-Priority Initiative

**Audience:** Solo developer (part-time, ~10–15 h/week)  
**Initiative scope:** E1 Stability + E2 Correctness + E3 UX freshness + E4 Maintainability  
**Status:** Planning — locked decisions recorded; no code yet  
**Related:** [00-integrated-roadmap.md](./00-integrated-roadmap.md) · [01](./01-database-migration-versioning.md) · [02](./02-unit-tests-critical-paths.md) · [03](./03-split-large-viewmodels-services.md) · [04](./04-transient-vs-singleton-viewmodels.md)

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
| **Done definition**          | **E1 + E2 + E3 + E4** complete; Phase 5 explicitly out of scope                                                                          | User-locked minimum bar.                                                                                                                                                              |


### Timeline at a glance


| Metric                | Value                                                                                      |
| --------------------- | ------------------------------------------------------------------------------------------ |
| **Sprint count**      | **11** (Sprint 0 kickoff through Sprint 10 closeout)                                       |
| **Calendar duration** | **~22 weeks (~5.5 months)** at 10–15 h/week                                                |
| **Parallelism**       | Minimal — solo dev; only Sprint 1–2 have slight overlap (Core move vs migration design)    |
| **Merge strategy**    | One sprint merge (or small PRs) per sprint; never combine migration runner + Library split |


### Epic → sprint map

```
Sprint 0   [Kickoff & design]
Sprint 1   [Core lib + parser tests]          ─┐ E2 start
Sprint 2   [Migration runner A + mig tests]   ─┤ E1
Sprint 3   [Critical-path test matrix]        ─┘ E2
Sprint 4   [Navigation refresh hooks]           E3
Sprint 5   [Settings split (1/2)]             ─┐
Sprint 6   [Settings split (2/2)]               │
Sprint 7   [AutoTrack service split]            ├ E4
Sprint 8   [Library split]                      │
Sprint 9   [DB repos + Torrent/FetchJob split]  ─┘
Sprint 10  [Closeout & regression]              All epics verified
```

---

## 2. Locked decisions


| #   | Question                                   | User choice                                             | Agent standard (where delegated)                                                   |
| --- | ------------------------------------------ | ------------------------------------------------------- | ---------------------------------------------------------------------------------- |
| 1   | Migration path                             | **Jump to Option A** (numbered scripts + history table) | Homegrown runner, not FluentMigrator; baseline 001 = schema as of initiative start |
| 2   | Test layout                                | **Option B — `MediaManager.Core`**                      | xUnit test project references Core only; WPF app references Core                   |
| 3   | Phase 3 before Phase 4?                    | **Yes**                                                 | Sprint 4 (hooks) before Sprint 5+ (splits)                                         |
| 4   | Include `TorrentWorkspaceViewModel` in E4? | **Yes**                                                 | Sprint 9 alongside DB repos and FetchJobService                                    |
| 5   | Phase 5 (transient VMs)?                   | **Reserved for future**                                 | Hooks-only for initiative; document triggers to reopen Phase 5                     |
| 6   | Initiative “done”                          | **E1 + E2 + E3 + E4**                                   | Sprint 10 exit checklist                                                           |
| 7   | Migration failure policy                   | *(gap)*                                                 | Block startup + restore guidance                                                   |
| 8   | FetchJobs table fate                       | *(gap)*                                                 | One-time purge migration; drop table deferred                                      |
| 9   | Fresh install vs upgrade                   | *(gap)*                                                 | Single migration chain from 001                                                    |
| 10  | Settings split depth                       | *(gap)*                                                 | Sub-VMs + user controls per section                                                |
| 11  | Settings dirty-tracking                    | *(gap)*                                                 | Required before `LoadFromSettings()` on every Settings visit                       |
| 12  | Coverage enforcement                       | *(gap)*                                                 | Advisory ≥80% line coverage on parser + evaluation by end Sprint 3                 |
| 13  | CI test step                               | *(gap)*                                                 | Local `dotnet test` gate every sprint; GitHub Actions deferred to post-initiative  |


---

## 3. Sprint overview table


| Sprint | Weeks (rel.) | Est. hours | Goal (outcome)                         | Primary deliverable                                                           | Test gate                                     |
| ------ | ------------ | ---------- | -------------------------------------- | ----------------------------------------------------------------------------- | --------------------------------------------- |
| **0**  | W0–W1        | 8          | Decisions locked; repos designed       | Migration inventory, Core project plan, branch strategy                       | N/A (docs only)                               |
| **1**  | W2–W3        | 12         | Testable Core boundary exists          | `MediaManager.Core` + xUnit project; parser tests ≥15                         | `dotnet test` green; MSBuild x64 app build    |
| **2**  | W4–W5        | 12         | Trustworthy DB upgrades                | `SchemaMigrations` runner; 001 baseline + 002 FetchJobs once; migration tests | Migration tests + manual DB upgrade           |
| **3**  | W6–W7        | 12         | Critical logic regression-safe         | Evaluation, search, pack, validation test suites                              | ≥40 unit tests total; manual cart smoke       |
| **4**  | W8–W9        | 12         | Stale UI fixed via navigation contract | `INavigationAware`; per-workspace refresh; Torrent cancel on leave            | Manual stale-UI repro scripts pass            |
| **5**  | W10–W11      | 12         | Settings maintainable (half)           | Integrations + Backup + System section sub-VMs + user controls                | Unit tests green; settings manual checklist   |
| **6**  | W12–W13      | 12         | Settings fully decomposed              | Remaining 4 section sub-VMs; host orchestrates save/load                      | Same + dirty-tracking verified                |
| **7**  | W14–W15      | 12         | AutoTrack phases isolated              | Discovery / Hunt / Reconcile services + façade                                | Hunt tests still green; Auto-Track manual run |
| **8**  | W16–W17      | 14         | Library split for catalog vs detail    | `LibraryCatalogViewModel` + `LibraryDetailViewModel` (or nested host)         | Library manual checklist; tests green         |
| **9**  | W18–W19      | 14         | DB + Torrent debt reduced              | Migration runner extracted; repositories; Torrent VM + FetchJob split         | Migration tests + torrent workspace manual    |
| **10** | W20–W21      | 10         | Initiative formally complete           | Docs updated; no file >800 lines unjustified; final regression                | Full manual regression pass                   |


**Total:** ~11 sprints, ~22 weeks, ~118 h estimated.

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

- [ ] Schema inventory doc or spreadsheet checked into `docs/planning/` (or appendix in this file’s PR)
- [ ] Migration numbering rules written (integer prefix, idempotent, never edit applied scripts)
- [ ] Core type move list approved
- [ ] [00-integrated-roadmap.md](./00-integrated-roadmap.md) locked decisions table complete

#### Risk / rollback

Zero runtime risk. Rollback = don’t start Sprint 1.

---

### Sprint 1 — Core library & parser tests (W2–W3, ~12 h)

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

- [ ] App launches; search torrent workspace still parses candidates (smoke)
- [ ] MSBuild x64 Release build succeeds
- [ ] `dotnet test` on solution passes

**Regression areas**

- Torrent workspace search results display
- Auto-Track hunt still parses filenames (no logic change expected)

#### Definition of Done

- [ ] Core + Tests projects in solution; WPF project references Core
- [ ] No duplicate type definitions (moved, not copied)
- [ ] ≥15 parser tests green
- [ ] `AI_CONTEXT.md` notes Core project

#### Risk / rollback

**Risk:** Namespace/move breaks WPF references. **Rollback:** Revert Core commit; types remain in WPF project. Keep move PR isolated.

---

### Sprint 2 — Migration runner (Option A) (W4–W5, ~12 h)

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

- **M1:** Runner live; history table populated on first run after upgrade
- **M2:** FetchJobs purge demoted to migration 002

#### Test strategy

**Unit tests added**

- `MigrationRunnerTests`: empty DB → all migrations applied, `SchemaMigrations` row count correct
- `MigrationRunnerTests`: legacy fixture DB with old `TorrentHash` column → upgrades without data loss (if 003 shipped)
- `MigrationRunnerTests`: running runner twice is idempotent (no duplicate applies)

**Manual test checklist**

- [ ] Copy production `media-manager.db` to temp folder; point app at it; upgrade succeeds
- [ ] Second app start: no FetchJobs delete log spam
- [ ] Fresh state folder: app creates DB via migration chain
- [ ] `CreateSafeSnapshot()` still works post-migrate
- [ ] Tracked shows, cart orders, blacklist rows intact (spot-check counts)

**Regression areas**

- App startup
- Auto-Track read/write tracked media
- Torrent cart persistence
- Google Drive backup trigger from Settings

#### Definition of Done

- [ ] `SchemaMigrations` records each applied migration
- [ ] No unconditional FetchJobs purge in `Initialize()`
- [ ] Migration tests in `dotnet test`
- [ ] `STATE_FOLDER.md` + `AI_CONTEXT.md` migration section updated

#### Risk / rollback

**Risk:** Baseline mis-identifies already-migrated DB → duplicate alters or skipped columns. **Mitigation:** Test against copy of real DB before production path. **Rollback:** Restore `.db` from snapshot; revert runner commit (EnsureColumn chain still in previous release).

---

### Sprint 3 — Critical-path test expansion (W6–W7, ~12 h)

#### Goal

Auto-Track/cart **business logic** is covered by golden fixtures — safe to refactor services in Sprints 7–9.

#### Scope


| In                                                                                                                                                                                                | Out                                                         |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------- |
| Move to Core: `CandidateEvaluationService`, `SearchPlanBuilder`, `SearchTitleResolver`, `PackSeasonFileGrouper`, `PackEpisodePatternInferrer`, `TorrentContentValidationService` (file-list path) | FetchJobService/AutoTrackService tests (integration; later) |
| Mock `ISearchTitleResolver` where needed                                                                                                                                                          | UI tests                                                    |
| Golden JSON/text fixtures for anonymized torrent names                                                                                                                                            | 80% enforced in CI                                          |


#### Migration milestone

Optional **003**: extract inline TorrentBlacklist rebuild to numbered migration (if not done Sprint 2).

#### Test strategy

**Unit tests added** (target counts)


| Area                                        | Tests | Focus                                                         |
| ------------------------------------------- | ----- | ------------------------------------------------------------- |
| `CandidateEvaluationService`                | ≥12   | Accept/reject matrix, `CandidateRejectReason`, anime absolute |
| `SearchPlanBuilder` + `SearchTitleResolver` | ≥8    | Query templates, alias expansion                              |
| `PackSeasonFileGrouper`                     | ≥5    | Multi-season folders, Extras excluded                         |
| `PackEpisodePatternInferrer`                | ≥10   | Stem inference edge cases (Appendix A scenarios)              |
| `TorrentContentValidationService`           | ≥8    | `.exe` disguised, double extension, sample count              |


**Manual test checklist**

- [ ] Add torrent to cart manually; candidate list sane
- [ ] Pack link review opens with correct episode mapping (one show)
- [ ] Auto-Track hunt on one tracked show (dev qBittorrent)

**Regression areas**

- Parser (Sprint 1) — full suite must stay green
- Migrations (Sprint 2) — run migration tests before merge

#### Definition of Done

- [ ] ≥40 unit tests total in Core.Tests
- [ ] Advisory ≥80% line coverage on parser + evaluation (coverlet report locally)
- [ ] No WPF reference from test project

#### Risk / rollback

**Risk:** Moving types breaks DI registration. **Rollback:** Single revert of Core move commit.

---

### Sprint 4 — Navigation refresh hooks (E3) (W8–W9, ~12 h)

#### Goal

**Returning to a workspace shows fresh data** without manual Refresh; background Auto-Track/cart updates visible on next visit.

#### Scope


| In                                                                                                   | Out                                           |
| ---------------------------------------------------------------------------------------------------- | --------------------------------------------- |
| `INavigationAware` (`OnNavigatedTo` / `OnNavigatedFrom`) on `ViewModelBase`                          | Transient VMs (Phase 5)                       |
| `MainViewModel.NavigateTo` invokes hooks                                                             | Splitting large VMs                           |
| Per-workspace refresh policy (see [04 § draft behaviors](./04-transient-vs-singleton-viewmodels.md)) | Full `IWorkspaceRefreshService` unless needed |
| Cancel Torrent `_operationCts` on `OnNavigatedFrom`                                                  | Settings dirty-tracking (Sprint 6)            |
| FindAdd: `RefreshExistingMedia()` on navigate                                                        |                                               |


#### Migration milestone

None.

#### Test strategy

**Unit tests added**

- Optional: `NavigationAwareTests` for a small coordinator if extracted to Core (low priority)
- **Regression:** Full Core test suite green

**Manual test checklist** (stale-UI repro scripts)


| #   | Script                                                               | Expected                                                                       |
| --- | -------------------------------------------------------------------- | ------------------------------------------------------------------------------ |
| 1   | Auto-Track run → News → Library                                      | Library grid reflects new/changed tracked media without Refresh                |
| 2   | Cart add in Torrent → Library → Torrent                              | Cart badge/state updated                                                       |
| 3   | Edit `settings.json` path externally → Settings workspace            | Reload shows disk values (or dirty prompt if editing — full dirty in Sprint 6) |
| 4   | Start torrent search → navigate away mid-search                      | Operation cancelled; no background UI updates on wrong tab                     |
| 5   | FindAdd → Library → FindAdd                                          | Existing-media flags updated                                                   |
| 6   | Long session: minimize to tray (background mode) → restore → Library | Poster reload policy still works                                               |


**Regression areas**

- Library/Torrent UI state persistence (`settings.json`)
- Auto-Track scheduler
- Main window navigation/shortcuts

#### Definition of Done

- [ ] All seven workspace VMs implement refresh policy (documented in code or `AI_CONTEXT.md`)
- [ ] Repro scripts 1–5 pass
- [ ] No new singleton memory leaks from duplicate event subscriptions

#### Risk / rollback

**Risk:** Over-refresh causes slow tab switches on large libraries. **Mitigation:** Catalog refresh may be incremental later; for initiative, full `RefreshLibrary()` acceptable per [04](./04-transient-vs-singleton-viewmodels.md). **Rollback:** Revert hook wiring; VMs behave as before.

---

### Sprint 5 — Settings split part 1 (E4) (W10–W11, ~12 h)

#### Goal

**Three settings sections** are owned by dedicated sub-VMs and user controls — pattern proven for the rest.

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

---

### Sprint 6 — Settings split part 2 (E4) (W12–W13, ~12 h)

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

#### Goal

**Library catalog and detail concerns separated** — grid commands vs detail/pack/cart commands no longer share one 2.4k-line type.

#### Scope


| In                                                                                  | Out                                                              |
| ----------------------------------------------------------------------------------- | ---------------------------------------------------------------- |
| `LibraryCatalogViewModel` — grid, sort, filter, selection, persist UI state         | Torrent workspace split                                          |
| `LibraryDetailViewModel` (or nested) — seasons, episodes, pack link, cart on detail | Full service extraction (LibraryImportCoordinator optional stub) |
| Host `LibraryViewModel` composes catalog + detail; XAML `ContentControl` for detail | Database repos                                                   |


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

#### Goal

**Initiative declared done** per E1–E4; documentation matches code; no unjustified giant files remain.

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

- [ ] User locked bar met: **E1 + E2 + E3 + E4**
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
- Torrent long operations cancel on navigate away
- Settings reload respects dirty-tracking

### Maintainability (E4)

- Settings: 7 section sub-VMs + user controls; host coordinates save
- AutoTrack: discovery / hunt / reconcile services + façade
- Library: catalog + detail split
- Torrent workspace: split parallel to Library
- DatabaseService: migration runner + repositories; no 3k-line god class
- No type > ~800 lines without documented exception

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
[ ] Torrent: search, recipe, cart run, cancel on navigate
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

The **~22 week** table in §3 assumes solo-human typing at 10–15 h/week. With Cursor (or similar) generating code and you reviewing merges, the same 11 sprints typically fit **3–4 weeks** focused effort or **4–6 weeks** part-time — **sprint order and merge gates unchanged**.

**How to execute:** [06-ai-execution-guide.md](./06-ai-execution-guide.md) — session rhythm, prompt templates, Sprint 0 start checklist, pre-merge review.

| Calendar (focused) | Sprints | Primary deliverable | Merge gate (unchanged) |
| ------------------ | ------- | ------------------- | ------------------------ |
| Day 1 | 0 → 1 start | Schema inventory + Core scaffold | S0: docs DoD; S1: build + test |
| Day 2 | 1 | ≥15 parser tests | `dotnet test`; MSBuild x64 |
| Day 3 | 2 | Migration runner + 001/002 | Migration tests + DB copy upgrade |
| Day 4–5 | 3 | ≥40 tests; evaluation in Core | coverlet advisory; cart smoke |
| Day 6 | 4 | `INavigationAware` wired | Stale-UI repro scripts 1–5 |
| Day 7–9 | 5 → 6 | Settings 7 section sub-VMs | Per-section manual checklist |
| Day 10–11 | 7 | AutoTrack phase services | Hunt manual + tests green |
| Day 12–14 | 8 | Library catalog/detail split | Library checklist + cart events |
| Day 15–17 | 9 | DB repos + Torrent/FetchJob | **DB backup**; CRUD + migration retest |
| Day 18–20 | 10 | Closeout + full regression | §8 master checklist; E1–E4 verified |

**Do not compress:** one sprint per PR; never combine S2 migrations with S8/S9 VM splits. **Safe same-day pairing:** S0 AM + S1 PM; consecutive S5/S6 sessions with separate merges.

---

## Related reading

- [06-ai-execution-guide.md](./06-ai-execution-guide.md) — **how to execute** with AI (prompts, review checklist, risks)  
- [00-integrated-roadmap.md](./00-integrated-roadmap.md) — dependency map and locked decisions  
- [planning/README.md](./README.md) — index of all planning docs  
- [IMPROVEMENTS.md](../IMPROVEMENTS.md) — source priorities  
- [AI_CONTEXT.md](../AI_CONTEXT.md) — update after each sprint merge

