# Media Manager — Evaluation & Improvement Suggestions

This document evaluates the application as of branch `auto-torrent` (commit `631c3d7`) and proposes concrete improvements.

---

## Overall Evaluation

Media Manager is a **mature, feature-rich personal automation tool** that successfully integrates multiple external systems (qBittorrent, Jellyfin, TMDB, Gemini, Google Drive, WARP) into a cohesive Windows desktop workflow. The codebase demonstrates strong domain modeling, extensive background automation, and thoughtful safety measures (torrent validation, blacklist, single-instance).

### Strengths

1. **End-to-end automation** — Auto-Track covers TMDB discovery through hardlinking with minimal user intervention
2. **Flexible acquisition** — Recipe system with modular scoring/search/quality configuration rivals dedicated *arr apps
3. **Safety-first torrent adds** — Running add → file-list polling validation → continue/blacklist pipeline reduces malware risk (post `631c3d7`; no longer paused-add/resume)
4. **Dual library strategy** — Hardlinks for organization + symlinks for Jellyfin is a pragmatic NTFS pattern
5. **Rich settings surface** — Seven settings sections with test buttons for every integration
6. **Observability** — File/UI/console logging, operation progress, notification catalog, debug sessions
7. **Resilience patterns** — WARP recovery, qBittorrent process restart, Jellyfin refresh debouncing, backup scheduler
8. **MVVM consistency** — CommunityToolkit.Mvvm, DI singleton services, view templates for workspace switching

### Weaknesses & Risks

1. **Windows-only coupling** — Hardlinks, symlinks (admin), WARP CLI, registry startup; no cross-platform path
2. **Schema migration fragility** — Additive `ALTER TABLE` without version tracking; rebuild migrations (e.g. TorrentBlacklist) are one-off
3. **Large monolithic ViewModels** — `SettingsViewModel` (~2000 lines), `LibraryViewModel`, `AutoTrackService` (~1500 lines) hinder maintenance
4. **Singleton ViewModels** — Workspace VMs persist state across navigations; can cause stale UI or memory retention
5. **Legacy FetchJobs purge** — `PurgeLegacyFetchJobs()` on every DB init deletes all `FetchJobs` rows; table is legacy (see [STATE_FOLDER.md](./STATE_FOLDER.md#fetchjobs-legacy-purge))
6. **Tight external dependency chain** — Auto-Track hunt blocked if WARP or qBittorrent WebUI unavailable (by design, but brittle)
7. **No automated tests visible** — Complex scoring, parsing, and pack mapping logic lacks test coverage
8. **Documentation was absent** — Onboarding requires reading source (addressed by this docs folder)

---

## Improvement Suggestions

### Priority: High

Detailed planning: [planning/README.md](./planning/README.md) · Integrated timeline: [planning/00-integrated-roadmap.md](./planning/00-integrated-roadmap.md) · **Sprint schedule:** [planning/05-sprint-timeline.md](./planning/05-sprint-timeline.md)

#### 1. Database Migration Versioning
→ [Planning doc](./planning/01-database-migration-versioning.md)

- **Problem:** No `schema_version` table; migrations are scattered `EnsureColumn` calls and one-off rebuilds
- **Suggestion:** Introduce `SchemaMigrations` table with numbered migrations; replace init-time purge with targeted migration
- **Benefit:** Safer upgrades, auditable schema history, no accidental data loss (FetchJobs purge)
- **Files:** `Services/DatabaseService.cs`

#### 2. Unit Tests for Critical Paths
→ [Planning doc](./planning/02-unit-tests-critical-paths.md)

- **Problem:** Candidate scoring, torrent parsing, pack episode mapping, search plan building are untested
- **Suggestion:** Add xUnit test project covering:
  - `CandidateEvaluationService` / `TorrentCandidateParser`
  - `SearchPlanBuilder` / `SearchTitleResolver`
  - `PackSeasonFileGrouper` / `PackEpisodePatternInferrer`
  - `TorrentContentValidationService`
- **Benefit:** Regression safety for recipe and auto-track behavior

#### 3. Split Large ViewModels/Services
→ [Planning doc](./planning/03-split-large-viewmodels-services.md)

- **Problem:** `SettingsViewModel`, `LibraryViewModel`, `AutoTrackService` are difficult to navigate
- **Suggestion:** Extract sub-VMs (e.g. `IntegrationsSettingsViewModel`, `LibraryLinkingViewModel`) and partial service classes
- **Benefit:** Easier maintenance, clearer ownership, smaller diffs

#### 4. Transient vs Singleton ViewModels
→ [Planning doc](./planning/04-transient-vs-singleton-viewmodels.md)

- **Problem:** Singleton workspace VMs may retain stale selection/state after long sessions
- **Suggestion:** Consider transient VMs recreated on navigate, or explicit `OnNavigatedTo` refresh hooks
- **Benefit:** Fresher UI, reduced memory for unused workspace state

### Priority: Medium

#### 5. Unified Error UX
- **Problem:** Errors surface via logs, status messages, and toasts inconsistently
- **Suggestion:** Central error presentation service with user-friendly messages + "copy details" for logs
- **Benefit:** Better user experience during hunt failures, API errors, link failures

#### 6. Recipe Validation on Save
- **Problem:** Invalid recipe JSON or conflicting module settings may only fail at search time
- **Suggestion:** JSON schema validation + cross-module consistency checks on save
- **Benefit:** Earlier feedback in Recipe workspace

#### 7. Auto-Track Run History
- **Problem:** Only `LastRunSummary` in settings; no persistent run log
- **Suggestion:** `AutoTrackRuns` table or log file with timestamp, phase results, shows processed, errors
- **Benefit:** Debugging failed hunts, trend analysis

#### 8. Import/Export Full State
- **Problem:** Backup requires Google Drive; no local export/import for migration
- **Suggestion:** "Export state bundle" (DB + settings + recipes) to local zip without Drive
- **Benefit:** Machine migration, offline backup

#### 9. qBittorrent Health Dashboard
- **Problem:** Status pill shows ok/not ok; limited detail on search plugin health
- **Suggestion:** Expand integrations panel with plugin list, last search latency, capacity errors
- **Code hook:** `SearchEngineDiagnostics.cs` already exists

#### 10. Pack Link Preview Before AI
- **Problem:** AI pack linking can be slow/ costly; user may not know file count
- **Suggestion:** Pre-flight summary: file count, inferred pattern, estimated Gemini calls
- **Benefit:** Informed consent before AI link

### Priority: Lower

#### 11. Keyboard Shortcuts
- **Suggestion:** Global shortcuts for workspace switch (Ctrl+1–7), refresh, run cart
- **Benefit:** Power-user efficiency

#### 12. Bulk Operations in Library
- **Suggestion:** Multi-select media cards for bulk cart add, bulk TMDB refresh, bulk delete
- **Benefit:** Faster management of large libraries

#### 13. Movie Auto-Track Parity
- **Observation:** Auto-Track is show-centric; movies use manual cart
- **Suggestion:** Optional movie auto-hunt when released/wanted
- **Benefit:** Feature parity

#### 14. Configurable Notification Grouping
- **Suggestion:** Batch hunt progress toasts; quiet hours
- **Benefit:** Reduced notification fatigue

#### 15. WebViewer Session Persistence
- **Suggestion:** Remember last URL/tab per viewer (qBittorrent vs Jellyfin)
- **Benefit:** Convenience

#### 16. Accessibility
- **Suggestion:** Screen reader labels, keyboard navigation for sidebar, focus indicators audit
- **Benefit:** Inclusive design

#### 17. Performance: Large Library Virtualization
- **Observation:** Media card grids may load all cards
- **Suggestion:** VirtualizingStackPanel / pagination for 500+ items
- **Benefit:** UI responsiveness

#### 18. CI/CD Pipeline
- **Suggestion:** GitHub Actions: build x64, run tests, optional signed release
- **Benefit:** Quality gate on `auto-torrent` branch merges

---

## Architecture Observations

### What Works Well

- **Service-oriented DI** — Clear interfaces (`I*Service`) enable mocking and separation
- **Event hubs** — `LibraryLinkEventHub`, scheduler `RunCompleted`, backup `RunStateChanged` decouple components
- **Settings normalization** — `SettingsService.Load()` handles legacy migration gracefully
- **Gate pattern for torrent adds** — `TorrentAddGateService` centralizes validation pipeline

### Areas to Reconsider

| Area | Current | Alternative |
|------|---------|-------------|
| Fetch job tracking | DB table + init purge | In-memory job tracker or proper migration |
| Gemini for specials | API call per pack | Cache mappings in `SpecialMappingCache` (partially done) + manual override library |
| Snapshot search | One search per show | Already optimized; document tradeoffs (idle timeout vs completeness) |
| Symlink admin requirement | Fail silently? | Startup admin elevation prompt or junction fallback |

---

## Documentation Gaps (Resolved / Remaining)

| Topic | Status |
|-------|--------|
| Overall app purpose | Documented in `APP_OVERVIEW.md` |
| Feature catalog | Documented in `FEATURES.md` |
| DB schema | Documented; full columns in `DatabaseService.cs` |
| State folder layout | **Resolved** — [STATE_FOLDER.md](./STATE_FOLDER.md) (from live `D:\MediaManagerState`) |
| Recipe JSON format | **Resolved** — [RECIPE_SCHEMA.md](./RECIPE_SCHEMA.md) |
| Auto-Track eligibility | **Resolved** — `FEATURES.md` §3.7 + source in `AutoTrackTmdbEligibility.cs`, `ShowWeeklyAirDay.cs` |
| Pack linking heuristics | **Resolved** — `FEATURES.md` Appendix A |
| Google Drive OAuth setup | **Resolved** — [STATE_FOLDER.md](./STATE_FOLDER.md#google-drive-oauth) + `GoogleDriveClient.cs` |
| FetchJobs purge | **Resolved** — [STATE_FOLDER.md](./STATE_FOLDER.md#fetchjobs-legacy-purge) |
| Commit 631c3d7 torrent add | **Resolved** — `FEATURES.md` §10.6, `AI_CONTEXT.md` torrent_add_gate |
| PackEpisodeResolver edge cases | Partial — resolver skip reasons not exhaustively listed |
| Gemini special-mapping prompts | Partial — prompt templates in `Services/Gemini/` not documented |

---

## Suggested Roadmap (Informal)

```mermaid
flowchart LR
    Q1[Schema migrations + tests] --> Q2[VM/service splits]
    Q2 --> Q3[Error UX + run history]
    Q3 --> Q4[Bulk ops + movie auto-track]
```

**Phase 1 (Stability):** Migrations, tests, remove FetchJobs purge  
**Phase 2 (Maintainability):** Split large classes, transient VMs  
**Phase 3 (UX):** Error service, run history, local export  
**Phase 4 (Features):** Bulk ops, movie auto-track, notification grouping  

---

## Conclusion

Media Manager is a capable personal media automation platform with production-quality integrations and safety features. The main technical debt lies in **schema evolution**, **test coverage**, and **monolithic classes** rather than fundamental design flaws. Addressing migration versioning and critical-path tests would yield the highest return on investment.
