# 00 — Executive summary

**Status:** Docs only. Matching still uses leftover show columns on TV snapshot/pack.

## Symptom

TV cart (Breaking Bad S03E13, recipe **Common TV no Probe Pagi High Quality**):

- Recipe Candidate Filter allows `2160p` + `1080p`.
- Snapshot search for `Breaking Bad` returned **842** rows (same query the user used in qBittorrent, then filtered by episode name).
- Cart kept **2** candidates, both **1080p**.
- Manual qBittorrent list included **2160p** episode rows.

Cart debug wrote `cart-debug_*.txt` but only `HuntMatch` lines for the 2 accepted rows — no per-row `REJECT` reasons.

Movies with a 2160p recipe still accept 4K. Live DB: **every** show and movie has `PreferredQuality = 1080p`.

## Root cause (one gate)

TV **snapshot** and **season pack** matching do **not** use recipe `qualityAllowList`. They parse `TrackedShows.PreferredQuality` (import default `"1080p"`) and exact-match it.

`TorrentQuality.MatchesSelectedQuality` is exact: `2160p` does not pass an allow list of `["1080p"]`.

That column is leftover **per-show Auto Torrent** prefs from before recipes. There is **no Settings / Library / Cart editor**. Add/import hardcodes `"1080p"`. All 33 shows and 10 movies on the live DB are still that default.

**This is not Auto-Track quality.** Auto-Track uses `AutoTrackMinQuality` / `AutoTrackAllowedQualities` (and global Settings) **after** matching. Breaking Bad has those Auto-Track fields empty.

**This is not a qBittorrent API regression.** Engines returned hundreds of rows.

## Why movies still find 4K

Movie Run Cart uses `AutomationFlowService.DryRunMovieAsync` → `CandidateEvaluationService.EvaluateMovie` → recipe Candidate Filter `qualityAllowList`.

`TrackedMovies.PreferredQuality` is also `1080p` on all 10 movies and is **not** consulted on that path. Older movie `cart-debug` files show `QualityMismatch ... options: 2160p` from the **recipe**, not from the movie column.

## Two quality systems plus one leftover

| Layer | Owner | TV snapshot / pack | TV parallel search | Movie cart |
|-------|--------|--------------------|--------------------|------------|
| Recipe Candidate Filter | Current core | **Ignored** for quality/seeders/audio | **Used** | **Used** |
| `PreferredQuality` (+ audio, min seeders) | Leftover, no UI | **Wins** | Unused | Unused |
| Auto-Track min/allowed quality | Current core, hunt only | After match | After match | N/A |

Live `settings.json` has `AutoTrack.Search.ForceParallelEpisodeSearch: false`. Hunt therefore follows the **recipe snapshot flag**. A snapshot recipe on this machine uses leftover show quality for **both cart and hunt**.

If Force Parallel is turned on, hunt switches to `EvaluateEpisode` (recipe) and the leftover quality gate is skipped for hunt only — cart snapshot would still use it.

## What this pack is for

Map leftover readers so a later code plan can point TV snapshot/pack matching at the recipe filter (like movies) **without dropping columns**.
