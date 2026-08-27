# 03 — Evaluation and debug paths

Three evaluators exist. Only one is “recipes as edited in the UI” for TV snapshot.

## Evaluators

| Type | File | Quality source | Seeders / exclude / size |
|------|------|----------------|---------------------------|
| `CandidateEvaluationService` | [`MediaManager.Core/Services/CandidateEvaluationService.cs`](../../MediaManager.Core/Services/CandidateEvaluationService.cs) | Filter `QualityAllowList` | Filter module |
| `SnapshotCandidateMatcher` | [`Services/SnapshotCandidateMatcher.cs`](../../Services/SnapshotCandidateMatcher.cs) | `selectedQualities` from **show** | `show.MinimumSeeders`; no exclude terms |
| `CandidateMatcher` | [`Services/CandidateMatcher.cs`](../../Services/CandidateMatcher.cs) | Caller-supplied list (show prefs) | `show.MinimumSeeders` |

`SnapshotCandidateMatcher` still takes recipe **scoring weights** and **prefer terms**. Mixed: score like a recipe, filter like 2010s Auto Torrent prefs.

`CandidateMatcher` is only used from unused `MapEpisodeCandidates`.

## Call graph (simplified)

```text
Run Cart TV episodes
  FetchEpisodeCandidatesAsync
    snapshot? SnapshotCandidateMatcher(show.PreferredQuality)
              WriteHuntMatch (accepted only)
    else      EvaluateEpisode(recipe)
              WriteHuntMatch (accepted only)

Run Cart movies / recipe DryRun
  EvaluateMovie / EvaluateEpisode
  WriteRecipeEvaluation (every row ACCEPT/REJECT + RejectSummary)

Hunt
  FetchEpisodeCandidatesAsync (same as cart; ForceParallel can disable snapshot)
  WriteHuntMatch
  then AutoTrackCandidatePolicyService
  WriteHuntPolicy if policy wipes matches
```

## Cart debug split

[`HuntCandidateDebugWriter`](../../Services/HuntCandidateDebugWriter.cs):

| Method | Used by | Output |
|--------|---------|--------|
| `WriteRecipeEvaluation` | `AutomationFlowService.TryWriteCandidateDebugLog` | All rows, reject summaries |
| `WriteHuntMatch` | `FetchJobService.TryWriteHuntMatchDebug` | `SearchRows` + `MATCH` lines for **kept** candidates only |
| `WriteHuntPolicy` | `AutoTrackService` | Policy rejects after recipe/snapshot match |

TV cart with debug enabled looks “too generic” because it uses `WriteHuntMatch`. Movie dry-run looks like the old full dump.

Rejections inside `SnapshotCandidateMatcher` (including `does not match selected quality options: 1080p`) are **not** written to the debug file. That is why 2160p absence cannot be proven from `cart-debug_20260827_161547_874.txt` (842 rows, 2 MATCH lines).

## Evidence files (this incident)

| File | What it shows |
|------|----------------|
| `D:\MediaManagerState\logs\20260827_160342_729_systemlog.txt` | Snapshot `Query='Breaking Bad'`, 842 rows, `recipeMatched=2` |
| `D:\MediaManagerState\logs\cart-debug_20260827_161547_874.txt` | HuntMatch only, two 1080p names |
| `D:\MediaManagerState\logs\cart-debug_20260804_235107_261.txt` | Movie evaluation format (ACCEPT/REJECT) |
| `D:\MediaManagerState\Recipes\ccd35f00a56c45fe978548060b1bdfc4.rcp` | Filter `2160p,1080p`; Search Source snapshot on; debug on |

## Exact-match quality

[`TorrentQuality.MatchesSelectedQuality`](../../MediaManager.Core/Torrent/TorrentQuality.cs): empty allow list = allow all; otherwise **string equality**. No “1080p or better”.
