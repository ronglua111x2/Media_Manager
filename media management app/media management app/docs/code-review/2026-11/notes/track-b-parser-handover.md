# Track B (parser) — hand-over notes

Branch `claude/fix-parser` (from `main` ff2caa3), worktree `.claude/worktrees/ui-audit-b26564`. Not pushed, not merged. Final suites: Core 386 passed / 4 skipped, App 199 passed / 1 skipped (Release, App x64).

## Commits
| Commit | Step |
|---|---|
| `52cd12e` | P0-B: 35 parser cases (`TorrentCandidateParserTests.Parse_AuditHardCase_MatchesDesiredResult`) + `MediaManager.App.Tests/Pack/PackScenarioTests.cs` (A–M, SPEC-01, shorts) |
| `7a537d6` | PR-6: `TorrentFileRules` (Core): video extensions, `IsSampleOrProof`, revision/quality/size ordering, `SelectEpisodeFile`; `Analyze(..., fileSizes)` |
| `8462bf9` | PR-7: `Infer` → Invalid on failed validation; Sonarr absolute + `AbsoluteEpisodeMapper`; same revision/quality collisions → Skipped; natural sort for specials |
| `7dc343b` | PR-8: precedence SxxEyy > folder; single-season folders only; `Part N` groups; special keyword gate; `PackMovieRules`; NCOP01; shorts named like episodes held for review |
| `7a2547c` | PR-9: multi-episode `S01E01-E02`, migration `010_sourceitems_last_episode`, availability over the range |
| `20ea778` | PR-11: daily by air date, Inspect count, compact suffix `\b`, Sonarr year-as-season dropped |

PR-10 skipped: `fix/network-package-1` is not an ancestor of `main` (checked again at the end).

## Remaining skips
- `PackScenarioTests.I_SeasonSpecialFiles_AreNotGuessedOntoUnrelatedSpecials` — `Skip = "PARSE-09 (Track C, PR-13)"`. Track C: un-skip after PR-13. Helper `PackScenario.ResolveSpecialsOffline` mirrors the offline branch of `SpecialMappingOrchestrator.ResolveAsync`; adapt it if that API changes.
- Parser rows 12–15 (`S2 - 05`, `100 - 05`, `86 - … - 05`, `No. 8 - 05` with absolute allowed) — `Skip = "Report section 8 (search, feat/native-search)"`, search-owned.

## Files the other tracks also touch
- `Services/AutoTorrentLinkService.cs`: `GetCompletedVideoFilesAsync` drops samples; new `BuildFileSizes`; both `Analyze` calls in `PrepareSeasonPackLinkCoreAsync` pass `fileSizes: fileSizes`; `SelectEpisodeFile` keeps `files.Count == 1` and delegates the rest to `TorrentFileRules.SelectEpisodeFile`; in `LinkSeasonPackFromInventoryCoreAsync` the RegularEpisode case now builds `regularItem`, sets `regularItem.LastEpisodeNumber = entry.MatchedLastEpisodeNumber`, then `LinkSourceItem(regularItem, result, index)` (PR-9).
- `Services/TrackedShowService.cs` (Track A PR-5/PR-16): `GetAvailableEpisodeKeys` uses `.SelectMany(GetOutputEpisodeKeys)`; new private `GetOutputEpisodeKeys` expands `LastEpisodeNumber`. The `.Where(...)` filter is untouched.
- `Services/DatabaseService.cs`: `SourceItems.LastEpisodeNumber` appended as the last SELECT column (reader index 38), INSERT/VALUES/ON CONFLICT SET, parameter, `EnsureColumn`. Migration number 010; 009 left for Track A. `MigrationRunnerTests` lists end with `"008_…", "010_sourceitems_last_episode"` — merging A's 009 means inserting it between.
- `Services/HardlinkService.cs`: `BuildOutputPath` adds `-E{last:00}` when `LastEpisodeNumber > episode` and no Mapped* numbers.
- `Services/PackSpecialPayloadBuilder.cs` (Track C, PR-7): natural sort in the two `ThenBy` of `BuildContext`; Ordinal skipped when `HasRepeatedLocalIndex(context.Candidates)` (helper above `BuildCandidateMetadata`).
- `Services/PackTorrentInventoryAnalyzer.cs`: optional `fileSizes`; samples Skipped first; movie via `PackMovieRules`; Part-N groups and `FlagSpecialLikeEpisodes` add Skipped entries + `inventory.Warnings` (Track C's review window could show them); file season follows the resolution; Inspect no longer counts unnumbered files.
- `Models/PackFileEntry.cs`: new `MatchedLastEpisodeNumber`. `Models/PackEpisodeResolution.cs`: new `LastEpisodeNumber`. `Models/SourceItem.cs`: new `LastEpisodeNumber`.

## Not done / follow-ups
- Single-episode torrents: `LinkEpisodeCoreAsync` does not set `LastEpisodeNumber`, so a hunted file `S01E01E02` links as one episode (it is found for either episode now). Track A owns that method.
- Shorts named like episodes are caught by title (matches an S00 title and no regular title) or by size inside the pack (< 1/4 of the pack median). A pack that contains only untitled shorts is not caught: there is no in-pack reference size, and comparing with the show's other linked files would need `AutoTorrentLinkService` to read their sizes.

## Real-data check
Harness `linkcheck` in this session's scratchpad (`…/a978e3bc-…/scratchpad/linkcheck/`), runs on copies of the DB export and torrent lists in the audit session's scratchpad; never `<StateFolder>`. Baseline `main` vs final branch: 271 linked episodes map to the same targets (94 single-episode same file, 162 pack regular same S/E, 15 pack specials stay specials), 16 orphans unchanged; the only change in all pack runs is the GroupK batch, 9/12 → 12/12.
