# Sprint 02 — Local plan / completion record

**Branch:** `auto-torrent`  
**Date:** Aug 2026  
**Source:** [05-sprint-timeline.md § Sprint 2](../05-sprint-timeline.md#sprint-2--migration-runner-option-a-w4w5-12-h)  
**Cursor plan:** Sprint 2 Migration Runner (Option A)

## Git baseline

- [x] Work on canonical branch `auto-torrent`
- [ ] Tag `four-pillars-sprint-02` after manual DB checklist + commit

## Scope IN / OUT

| In | Out |
| -- | --- |
| `SchemaMigrations` + ordered runner | Extract all EnsureColumn into files |
| `001_baseline`, `002_fetchjobs_legacy_purge` | Drop FetchJobs table |
| Remove `PurgeLegacyFetchJobs` hot path | ViewModels / repo split |
| Migration unit tests | Navigation hooks |
| Docs: `STATE_FOLDER.md`, `AI_CONTEXT.md` | |

**003 TorrentBlacklist:** deferred (TODO in `InitializeTorrentBlacklist`).

## Definition of Done

- [x] `SchemaMigrations` records each applied migration
- [x] No unconditional FetchJobs purge in `Initialize()`
- [x] Migration tests in `dotnet test`
- [x] `STATE_FOLDER.md` + `AI_CONTEXT.md` updated
- [x] Manual DB checklist (human) — see 05 Sprint 2
- [x] Commit + tag `four-pillars-sprint-02`

## Files touched

- `MediaManager.Core/Migrations/MigrationRunner.cs`
- `MediaManager.Core/Migrations/DatabaseMigrationException.cs`
- `MediaManager.Core/Migrations/001_baseline.sql`
- `MediaManager.Core/Migrations/002_fetchjobs_legacy_purge.sql`
- `MediaManager.Core/MediaManager.Core.csproj` (Sqlite + EmbeddedResource)
- `MediaManager.Core.Tests/Migrations/MigrationRunnerTests.cs`
- `Services/DatabaseService.cs`
- `App.xaml.cs`
- `docs/STATE_FOLDER.md`, `docs/AI_CONTEXT.md`, planning docs

## Test gate

```bash
dotnet test MediaManager.Core.Tests/MediaManager.Core.Tests.csproj -c Release
dotnet build "media management app.csproj" -c Release -p:Platform=x64
```

Result (Aug 2026): **23 tests passed**; Release x64 **0 errors**.

## Errors encountered

| Error | Fix |
| ----- | --- |
| CS8207 discard in expression tree (`TryParse(..., out _)`) | Helper `IsRoundtripDateTime` |
| Ambiguous StrReplace on `connection.Open()` | More unique context around Initialize |
| Non-interpolated `{Environment.NewLine}` in MessageBox text | Use `$"..."` string |
| VS CS0234/CS0246 Migrations not found on Rebuild WPF-only | List Core in `.slnx`; Rebuild Solution; BUILD.md note |

## What you do next

Sprint 2 closed. Start Sprint 3 with Plan Mode + `sprint-03-local-plan.md` per 06 §2.0.
