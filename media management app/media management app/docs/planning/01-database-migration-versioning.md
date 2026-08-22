# Planning: Database Migration Versioning

**Priority:** High  
**Source:** [IMPROVEMENTS.md](../IMPROVEMENTS.md) § Priority: High #1  
**Status:** Sprint 2 implemented (Option A runner live) — see [05 Sprint 2](./05-sprint-timeline.md#sprint-2--migration-runner-option-a-w4w5-12-h)

---

## Problem statement

The SQLite database has **no recorded schema version**. Every app startup runs a long chain of `CREATE TABLE IF NOT EXISTS` plus ~127 additive `EnsureColumn` calls inside `DatabaseService.Initialize()`. When a breaking change is needed (e.g. renaming a column), the code uses ad-hoc table rebuilds. Worse, **`PurgeLegacyFetchJobs()` deletes all rows from `FetchJobs` on every init**, which is a blunt workaround for legacy data rather than a real migration.

There is no way to answer: *“What schema version is this database on?”* or *“Which migrations have already run?”* Upgrades depend on implicit ordering in one 3,200+ line file, and a mistake can cause silent data loss or failed inserts.

---

## Why it happened

Media Manager evolved feature-by-feature (Auto-Track, torrent cart, blacklist, pack linking) without upfront schema design. The natural pattern for a solo/personal app was:

1. Add columns with `ALTER TABLE` when a feature needs them.
2. Ship the feature.
3. Repeat.

`EnsureColumn` is simple and worked for additive changes. When `TorrentBlacklist` needed `InfoHash` instead of legacy `TorrentHash`, a one-off rebuild was added inline rather than a general migration framework. Similarly, when `FetchJobs` was superseded by `TorrentCartOrders` and in-memory search caches, the fix was **`DELETE FROM FetchJobs` on every startup** instead of a versioned “drop legacy table” migration run once.

`SettingsService` already migrates JSON settings in memory on load — but the **database layer never got the same treatment**.

---

## Current behavior

### Initialization flow

On startup, `App.xaml.cs` calls `DatabaseService.Initialize(stateFolder)` which:

1. Opens `{StateFolder}/media-manager.db`
2. Creates/migrates all tables in a fixed order
3. Calls `PurgeLegacyFetchJobs()` unconditionally
4. Logs readiness

```50:136:Services/DatabaseService.cs
    public void Initialize(string stateFolder)
    {
        // ...
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        // CREATE TABLE IF NOT EXISTS SourceItems ...
        EnsureColumn(connection, "SourceItems", "MediaKind", ...);
        // ... many EnsureColumn calls across all tables ...
        InitializeSeriesMappings(connection);
        InitializeTrackedShows(connection);
        // ... FetchJobs, TorrentCartOrders, TorrentBlacklist ...
        PurgeLegacyFetchJobs(connection);
        _logger.Info("SQLite database is ready", ...);
    }
```

### EnsureColumn (additive migration)

```376:392:Services/DatabaseService.cs
    private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({tableName});";
        // ... if column missing ...
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        alter.ExecuteNonQuery();
    }
```

- Runs **every startup** for every listed column (cheap if column exists, but no audit trail).
- Cannot remove/rename columns safely.
- No transaction wrapping across multi-step migrations.

### One-off table rebuild (TorrentBlacklist)

When legacy `TorrentHash` column exists, the code creates `TorrentBlacklist_v2`, copies data, drops old table, renames — all inline in `InitializeTorrentBlacklist()` (lines ~3032–3096). This pattern is **not reusable** and is easy to get wrong on second run.

### FetchJobs purge

```2542:2551:Services/DatabaseService.cs
    private void PurgeLegacyFetchJobs(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM FetchJobs;";
        var deleted = command.ExecuteNonQuery();
        // logs if deleted > 0
    }
```

Documented in [STATE_FOLDER.md](../STATE_FOLDER.md#fetchjobs-legacy-purge). `FetchJobService` no longer persists to this table; search state lives in `TorrentCartOrders` and in-memory dictionaries.

### Tables (10)

| Table | Role |
|-------|------|
| `SourceItems` | Scanned/imported files, link/symlink state |
| `SeriesMappings` | Parsed title → TMDB cache |
| `TrackedShows` | Show tracking + Auto-Track config (~30+ migrated columns) |
| `TrackedSeasons` | Season/pack torrent state |
| `TrackedEpisodes` | Episode availability + torrent state |
| `TrackedMovies` | Movie tracking |
| `FetchJobs` | **Legacy** — schema kept, rows purged |
| `TorrentCartOrders` | Torrent cart orders |
| `TorrentCartOrderCandidates` | Ranked candidates per order |
| `TorrentBlacklist` | Rejected torrents per show |

See [FEATURES.md](../FEATURES.md) §11 and [AI_CONTEXT.md](../AI_CONTEXT.md) `migration_strategy`.

---

## Impact

| Risk | Detail |
|------|--------|
| **Data loss** | Unconditional `DELETE FROM FetchJobs`; future “cleanup” migrations could repeat this pattern |
| **Failed upgrades** | User on old DB + new code may hit column/type mismatches if rebuild logic has bugs |
| **No rollback story** | Backups exist (Google Drive), but no migration-down or version checkpoint |
| **Maintenance cost** | All schema knowledge in one file; hard to review diffs or test migrations in isolation |
| **False confidence** | `CREATE TABLE IF NOT EXISTS` with full column list in CREATE does not alter existing tables — **real schema for old DBs is CREATE + all EnsureColumn calls**, easy to misunderstand |

---

## Affected areas

| Area | Files / artifacts |
|------|-------------------|
| DB init & CRUD | `Services/DatabaseService.cs`, `Services/IDatabaseService.cs` |
| Consumers | All `I*Service` types that read/write SQLite (~20+ services) |
| Backup | `DatabaseService.CreateSafeSnapshot()` — must run after migrations |
| State folder | `{StateFolder}/media-manager.db` |
| Docs | `docs/STATE_FOLDER.md`, `docs/APP_OVERVIEW.md`, `docs/AI_CONTEXT.md` |
| Legacy purge | `Services/FetchJobService.cs` (in-memory only; table unused for persistence) |

---

## Constraints

- **Single-user, single Windows machine** — no multi-tenant migration coordination.
- **Production data in place** — real `D:\MediaManagerState\media-manager.db` must upgrade without manual SQL.
- **Backup before migrate** — `CreateSafeSnapshot()` and Google Drive backup must remain safe; migrations should not hold locks longer than necessary.
- **SQLite limitations** — limited `ALTER TABLE`; renames/drops need table rebuild pattern.
- **Do not break** cart orders, tracked media, blacklist, or `SourceItems` link state during upgrade.
- **FetchJobs** — safe to stop purging once a one-time migration confirms table is unused; consider dropping table in a later version.

---

## Options for resolution

### Option A: Lightweight `SchemaMigrations` table + numbered SQL scripts

Add a table `(Id, Name, AppliedUtc)` and a runner that executes pending migrations in order (001, 002, …). Move existing `EnsureColumn` logic into migration 001 “baseline” or keep EnsureColumn as safety net during transition.

| Pros | Cons |
|------|------|
| Clear audit trail | Must backfill “current state” as migration 001 for existing users |
| Each migration reviewable in its own file | One-time effort to extract ~127 EnsureColumn into baseline |
| Can run destructive ops **once** (FetchJobs purge → migration 002) | Team must agree on numbering/rebase rules |

### Option B: Embedded migration library (e.g. FluentMigrator, DbUp-style)

Use a small migration framework with C# migration classes or embedded SQL.

| Pros | Cons |
|------|------|
| Industry-standard pattern | New dependency; may feel heavy for 10 tables |
| Up/down migrations possible | WPF single-project layout needs a test harness for migrations |
| Good for CI “migrate fresh DB” tests | Learning curve |

### Option C: Incremental hardening (minimal change)

Keep `EnsureColumn` but add `SchemaVersion` integer in a new table; only run new rebuild/purge logic when `version < N`. Stop unconditional FetchJobs purge.

| Pros | Cons |
|------|------|
| Smallest diff | Does not fix scattered logic in `DatabaseService.cs` |
| Quick win for FetchJobs | Still no per-migration history |
| Low risk | Technical debt remains for next breaking change |

---

## Suggested planning steps

1. **Inventory** — Export effective schema from a live DB (`PRAGMA table_info` for each table) and diff against what `Initialize()` assumes.
2. **Design** — Choose Option A or B; define migration naming, idempotency rules, and failure behavior (fail startup vs. log + continue).
3. **Baseline migration** — Represent “schema as of branch `auto-torrent`” as migration 001 so existing users mark complete without changes.
4. **Extract FetchJobs purge** — Replace with migration 002 “clear legacy FetchJobs rows” (run once); remove `PurgeLegacyFetchJobs()` from hot path.
5. **Extract TorrentBlacklist rebuild** — Move into a numbered migration; add test that runs against a DB with old `TorrentHash` column.
6. **Shrink `DatabaseService.Initialize()`** — CREATE TABLE for **new** installs only; all upgrades via runner.
7. **Migration tests** — Empty DB, “v1” fixture DB, backup/restore round-trip (see [02-unit-tests-critical-paths.md](./02-unit-tests-critical-paths.md)).
8. **Document** — Update `STATE_FOLDER.md`, `AI_CONTEXT.md` `migration_strategy`, and operator notes for failed migration recovery.

---

## Open questions

1. **Target approach** — Option A (homegrown), B (library), or C (minimal)?  
2. **FetchJobs table** — Drop entirely in a future migration, or keep empty schema indefinitely?  
3. **Migration failure policy** — Block app startup with error dialog, or offer “restore from backup”?  
4. **Versioning scope** — Track only schema, or also include `settings.json` migration version separately?  
5. **Fresh install path** — Single “create all tables” script vs. running full migration chain from 001?  
6. **WAL mode** — Should migrations explicitly checkpoint WAL before backup (already relevant for `CreateSafeSnapshot`)?
