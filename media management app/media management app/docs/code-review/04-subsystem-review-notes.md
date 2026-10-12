# Subsystem Review Notes

This document records review coverage, including areas where no defect was
found. It is intentionally self-contained. Existing project documentation was
used to locate workflows and confirm intent, while current code remained the
authority.

## Coverage status

| Area | Status | Findings |
|---|---|---|
| Startup, DI, shutdown, tray | Reviewed | AUD-004 |
| Settings and state folder | Reviewed | None promoted |
| SQLite and migrations | Reviewed | AUD-003 |
| Backup and restore | Reviewed | AUD-003 |
| Scan and media import | Reviewed | None promoted |
| Hardlinks, symlinks, NFO | Reviewed | AUD-005 |
| Library delete and reconcile | Reviewed | AUD-005 |
| Recipes and candidate evaluation | Reviewed | None promoted |
| Torrent cart and add gate | Reviewed | AUD-001, AUD-002, AUD-006 |
| Pack analysis, link, cleanup | Reviewed | None promoted |
| Auto-Track and schedulers | Reviewed | AUD-001, AUD-002, AUD-006 |
| qBittorrent integration | Reviewed | AUD-001, AUD-002 |
| TMDB integration | Reviewed | None promoted |
| Jellyfin and WebView2 | Reviewed | None promoted |
| Gemini integration | Reviewed | None promoted |
| Google Drive | Reviewed | AUD-003 |
| WARP and process restart | Reviewed | None promoted |
| WPF workspaces and bindings | Reviewed | AUD-004 |
| Core algorithms and tests | Reviewed | None promoted |
| Build, packages, publish | Reviewed | AUD-007 |
| Vendored parser boundary | Spot-checked | None promoted |

## Startup, DI, and application lifetime

Reviewed `App.xaml.cs`, `MainWindow`, `MainViewModel`, tray handling, crash
hooks, background-mode transitions, and disposal.

Verified safeguards:

- settings load precedes database initialization;
- migration failure blocks startup;
- setup gating prevents schedulers from starting before setup completes;
- safe test mode suppresses background jobs;
- DI graph validation is enabled;
- schedulers, symlink coordination, refresh, cleanup, and tray services are
  disposed during normal exit;
- the single-instance mutex is released on exit and before first-run restart.

Review notes:

- crash logging observes dispatcher faults but intentionally does not recover
  from them;
- shutdown cleanup is grouped under broad exception handling, so one disposal
  failure can skip later explicit disposals before service-provider disposal;
  no concrete leaked-resource failure was demonstrated;
- background-mode debounce is intentional, but the event's worker-thread
  affinity is not safe for current UI subscribers (AUD-004).

## Settings, credentials, and state folder

Reviewed `SettingsService`, settings models, bootstrap pointer behavior,
workspace writes, backup redaction, and log messages.

Verified safeguards:

- writes use a lock and a temporary file followed by overwrite move;
- state-folder overrides are preserved rather than silently replaced by a JSON
  value;
- bootstrap logs record only whether a TMDB token is configured;
- cloud-backup settings redact TMDB, Gemini, qBittorrent, and Jellyfin
  credentials;
- no direct credential value interpolation into application log messages was
  found.

Review notes:

- live settings are plaintext by design and inherit protection from the
  selected state folder and Windows account;
- several singleton ViewModels save the shared settings object. This creates
  coupling but did not prove lost updates in the current single-instance
  process;
- Settings navigation reloads disk state, so unsaved UI edits can be discarded.
  This is consistent with an explicit Save workflow and was not promoted.

## SQLite and migrations

Reviewed `DatabaseService`, `IDatabaseService`, all embedded migrations, the
C# blacklist rebuild migration, transaction sites, parameter construction, and
migration tests.

Verified safeguards:

- each numbered migration is applied and recorded in one transaction;
- migration failures roll back and block startup;
- the blacklist rebuild preserves legacy URL/hash data and is covered by an
  upgrade test;
- DML uses SQLite parameters;
- dynamic schema identifiers and update-column names are selected from
  code-owned constants, not user input;
- online SQLite backup is used for backup snapshots;
- migration tests cover empty databases, idempotent re-run, legacy FetchJobs,
  and legacy blacklist schema.

Review notes:

- runtime `CREATE TABLE IF NOT EXISTS` and `EnsureColumn` logic still coexist
  with numbered migrations, increasing future drift risk;
- most operations use a connection per call, and scan/link batches are not one
  large transaction. Partial batches are recoverable on retry and were not
  classified as defects;
- no explicit application-wide writer gate exists, which is material during
  in-process restore (AUD-003).

## Backup and restore

Reviewed scheduled/manual backup locking, database snapshot creation, settings
redaction, recipe packaging, manifest checksums, Drive history, restore
staging, and first-run restore.

Verified safeguards:

- overlapping backup runs are prevented;
- backup uses a SQLite online snapshot rather than copying the live DB file;
- restore stages zip contents in a unique temporary directory;
- recipe filenames are flattened when copied from restore payload;
- a pinned test launch can refuse restore into the live legacy state folder;
- backed-up settings are placed in a review file instead of being applied
  automatically.

Review notes:

- manifest checksums are generated but not validated by restore;
- restore's active-process database replacement is the material issue
  (AUD-003).

## Scan, import, hardlink, symlink, and NFO

Reviewed scanner/parser entry points, media import, library path resolution,
hardlink creation/removal, symlink synchronization/coordinator, NFO writes,
empty-folder cleanup, source reconciliation, and library deletion.

Verified safeguards:

- unresolved TV identities and unmapped anime episodes cannot be hardlinked;
- hardlinks require source and destination on the same volume;
- source existence and target collisions are checked before the native call;
- hardlink removal refuses when linked and source paths are equal;
- hardlink removal and empty-folder cleanup are constrained to generated or
  configured library roots;
- symlink empty-folder cleanup is constrained to the unified root;
- pack cleanup aborts metadata deletion if unlinking reports errors;
- external imports do not delete the original file when source and linked path
  are the same.

Review notes:

- general library deletion does not use the pack cleanup's abort-on-error rule
  (AUD-005);
- source-missing reconciliation exists but has no current production caller;
- file and database changes in bulk link operations are per-item by design, so
  interruptions can leave a reported partial result.

## Recipes, search, evaluation, and Core validation

Reviewed recipe persistence/defaults, query templates, title resolution,
torrent parsing, candidate evaluation, quality scoring, pack grouping and
pattern inference, and content validation.

Verified safeguards:

- recipe evaluation is centralized in Core services used by cart and
  Auto-Track paths;
- candidate evaluation rejects title, year, episode, quality, seed, size, and
  recipe-rule mismatches;
- content validation checks dangerous extensions, double-extension
  obfuscation, and main-payload mismatch;
- Core tests cover query rendering, parser variants, evaluation decisions,
  content validation, pack grouping/cleanup, recipe overrides, and rating
  aggregation.

Review notes:

- `AutomationFlowService.RunNowAsync` directly adds a torrent but has no caller;
- `CandidateMatcher` is legacy dead code parallel to current Core evaluation;
- recipe import is exposed only by an unused service API;
- Sonarr parsing is used for pack resolution while Core regex parsing is used
  for search evaluation. No contradictory real filename was demonstrated.

## Torrent cart, add gate, and cleanup

Reviewed cart creation and persistence, selected candidates, disk assignment,
all `AddTorrentAsync` call sites, blacklist reads/writes, metadata polling,
content validation, deletion, and reconciliation.

Verified safeguards:

- all currently reachable cart and Auto-Track add paths use
  `ITorrentAddGateService`;
- listing URLs and infohashes are normalized before blacklist storage/lookups;
- torrent URLs are limited to magnet, `bc://bt`, HTTP, and HTTPS forms;
- known blacklisted infohashes are checked after the add resolves;
- rejected content is blacklisted after cleanup is **verified**;
- unverified cleanup pauses (best-effort), throws `TorrentCleanupFailedException`,
  and halts the current cart/Auto-Track add batch (AUD-002);

Material findings:

- torrent starts unpaused before validation (AUD-001, accepted trade-off);
- blacklist database failures permit the add path to continue (AUD-006).

Review notes:

- manual torrent adoption is heuristic but requires a unique match;
- torrent path construction trusts qBittorrent's file list, which is an
  accepted local-service trust boundary in current code;
- the blacklist schema names its media key `ShowId`, but practical cross-media
  collision requires an identical URL or infohash.

## Pack and Auto-Track workflows

Reviewed season/episode selection, candidate retries, WARP leases, scheduler
locks, pack inventory analysis, special mapping, link batching, cleanup, and
reconciliation.

Verified safeguards:

- Auto-Track phases use semaphores to avoid overlapping discovery, hunt, and
  reconciliation runs;
- cancellation is carried through network searches and polling;
- link events are coalesced during bulk mutation;
- cleanup matching uses provenance and path evidence;
- cleanup stops before database deletion if link removal fails;
- Gemini-proposed mappings can require user confirmation.

Review notes:

- pack linking can complete partially and reports counts/messages rather than
  rolling back already-created links;
- external/network integration paths are not covered by automated integration
  tests.

## External integrations

### qBittorrent

Reviewed login serialization, URL resolution, search, add, files, pause/resume,
delete, and WebView2 viewer behavior. AUD-001 remains an accepted trade-off.
AUD-002 delete swallow is fixed: failures throw, cleanup retries and verifies.

### TMDB

Reviewed bearer-token setup, request construction, retry paths, metadata
mapping, and alternative titles. No credential logging or concrete
state-corruption path was found.

### Jellyfin

Reviewed API-key header construction, library refresh queuing, media
navigation, and embedded viewer. New-window events keep configured-origin URLs
in the viewer and send other HTTP(S) URLs to the default browser. WebView event
handlers are detached on release.

### Gemini

Reviewed API request construction, configurable timeout, fallback models,
quota tracking, cache usage, logging, and confirmation. No raw API-key log
message was found.

### Google Drive

Reviewed OAuth connection, history listing, upload, retention, download, and
backup content. Cloud backups contain redacted settings. Active restore
coordination remains AUD-003.

### WARP and process launch

Reviewed fixed CLI subcommands, configured executable paths, process restart,
shell launch call sites, and host scanning. User-controlled executables are an
explicit settings capability. Fixed TMDB/Drive URLs and viewer HTTP(S) checks
limit ordinary shell-launch exposure.

## WPF workspace review

| Workspace | Navigation/lifecycle behavior reviewed | Result |
|---|---|---|
| News | timed refresh and Auto-Track completion event | No finding |
| Auto-Track | dashboard refresh, scheduler callback, commands | No finding |
| Find/Add | concurrent search cancellation and stale-result guards | No finding |
| Library | detail refresh, link/unlink commands, poster lifecycle | AUD-004 |
| Stats | navigation activation and billboard timers | No finding |
| Torrent | operation cancellation, refresh, poster lifecycle | AUD-004 |
| Recipe | active/inactive recipe refresh and subscriptions | No finding |
| System Settings | reload/save, backup/restore commands | AUD-003 |

Shared WPF observations:

- workspace ViewModels and cached views intentionally live for the process;
- navigation hooks refresh current data without canceling explicitly
  user-started long operations;
- device and viewer callbacks generally marshal to the dispatcher;
- library reconciliation refresh uses a generation/debounce guard;
- Find/Add replaces and disposes cancellation sources with identity checks;
- Stats stops active timers when navigated away or unloaded;
- the Release/x64 XAML build completed without compile-time binding or resource
  errors;
- runtime binding diagnostics and interactive accessibility behavior were not
  exercised.

## Core, tests, and project boundary

Reviewed Core common types, models, search/evaluation services, pack services,
ratings, torrent parser, validation, migrations, fixtures, and app references.

Results:

- all 207 Core tests pass on Release/x64;
- `MediaManager.App.Tests` adds 9 Windows-targeted cleanup/halt tests (all passed);
- strongest test coverage is in migrations, torrent parsing, evaluation,
  queries, validation, pack behavior, recipes, and ratings;
- WPF services, ViewModels, Sonarr integration, and live external workflows
  have no automated integration suite;
- Core intentionally retains the app's `media_management_app.*` namespaces,
  which blurs assembly ownership but causes no runtime ambiguity in the
  current graph;
- migration SQL resources are embedded and discovered correctly;
- the WPF project excludes nested Core, tests, tools, and ThirdParty source
  from default compile globs, avoiding duplicate compilation.

## Build, packages, publish, and licensing

Reviewed all project files, shared Release properties, references, analyzer
settings, target framework/RID, package restore, x64 build, and parser notices.

Results:

- Release/x64 build succeeds with zero first-party compiler errors or warnings;
- the only build warnings are the three dependency `NU1701` warnings in
  AUD-007;
- nullable reference types are enabled;
- warnings are not treated as errors and analyzer policy is SDK-default;
- self-contained `win-x64` settings apply at project level, including normal
  builds; this is inefficient but not a correctness defect;
- Sonarr parser attribution and GPL license files are present;
- Fluent Assertions 8.10 displays a commercial-use license notice. The
  application's commercial/non-commercial status must determine whether a
  paid test-library license is needed.

## Existing resources consulted

The audit reused the current architecture, build, state-folder, settings,
legacy-overlap, qBittorrent API, debug, and critical-path planning documents as
discovery aids. Their historical implementation and test-count claims were
not copied without checking current code and build output.

Material documentation drift observed during the original audit (later updated
outside this folder):

- `docs/BUILD.md`, `docs/AI_CONTEXT.md`, and `docs/planning/` now record 207
  Core tests plus 9 App cleanup tests.
- `docs/APP_OVERVIEW.md` no longer describes pause-on-add as current behavior.
- Remaining historical sprint-plan snapshots (Sprint 3 “80 tests”) were left as
  session records.
