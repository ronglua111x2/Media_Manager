# 04 — Decouple plan (later code; no DROP)

**Do not implement in the docs request.** This is the outline for a later Plan Mode / code pass.

**Do not** `ALTER TABLE ... DROP COLUMN` or delete `FetchJobs` in that first pass. Keep INSERT defaults (`1080p`, `''`, `0`) so existing rows and `NOT NULL` stay valid.

## Goal

TV snapshot and pack matching must use the **same quality/seeders/exclude/size rules as movie cart**: recipe Candidate Filter (`CandidateEvaluationService` or a shared helper). Leftover show/movie pref columns become unused for matching.

Auto-Track policy stays a **second** gate after matching.

## Suggested sequence

### 1. Point snapshot matching at the recipe filter

In [`FetchJobService.MapSnapshotCandidatesAsync`](../../Services/FetchJobService.cs) / [`SnapshotCandidateMatcher`](../../Services/SnapshotCandidateMatcher.cs):

- Stop passing `ParseQualities(show.PreferredQuality)`.
- Apply Candidate Filter the same way as `EvaluateEpisode` (quality allow list, min seeders, min/max size, include/exclude, blocked groups).
- Keep snapshot-specific identity (title variants, SxxExx, release kind, year).
- Prefer **one** evaluator (`EvaluateEpisode` over snapshot rows) over keeping two filter implementations in sync.

### 2. Point pack matching at the pack recipe filter

[`GetPackRejectReason`](../../Services/FetchJobService.cs) today uses `show.PreferredQuality`. Use pack recipe Candidate Filter quality (and seeders if that is intended for packs). Do not silently keep show `1080p`.

### 3. Leave columns in place; stop new meaning

- Keep writing `"1080p"` on import so upsert does not fail.
- Do not restore UI for `UpdatePreferences`.
- Optional later: stop copying prefs on refresh except identity; still no DROP.

### 4. Unify TV cart debug with movie dry-run

After matching uses recipe rejects, `WriteHuntMatch` is not enough. For snapshot, log `RejectSummary` + per-row verdict (or call `WriteRecipeEvaluation` on evaluated snapshot rows). Cap file size if 2000 rows is too large (summaries + accepted + sample rejects is acceptable).

### 5. Dead code (optional same pass or hygiene)

- `MapEpisodeCandidates` / unused movie quality helper — delete or wire; do not leave a third path that reads `PreferredQuality`.
- `UpdatePreferredQuality` with no UI — keep as no-op-safe DB API until DROP, or mark obsolete in comments/docs only.

### 6. Explicitly later (not first code pass)

- SQL DROP of pref columns
- DROP `FetchJobs`
- Deduplicate `qualityAllowList` so only Query Builder + Candidate Filter store it
- Settings 7-VM / AutoTorrent JSON fallback cleanup

### 7. Tests to add when coding

- Snapshot match **accepts 2160p** when recipe filter allows 2160p and show `PreferredQuality` is `1080p`.
- Snapshot **rejects 720p** when filter is 1080p+2160p.
- Movie path unchanged (recipe still wins).
- Pack path uses pack recipe allow list, not show column.
- Hunt still applies Auto-Track size/min-quality **after** recipe match (President Curtis-style policy must not regress).

## Non-goals for the first code pass

- Changing Query Builder `{title}` snapshot queries
- Raising snapshot timeout / pagination
- Migrating live `1080p` rows to `2160p` (unnecessary if matching ignores the column)
- Auto-Track Settings redesign
