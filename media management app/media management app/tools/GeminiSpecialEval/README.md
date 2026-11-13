# GeminiSpecialEval

Measures how well Gemini maps special/OVA files to TMDB Season 0, per model and per prompt version, so a prompt
change is measured instead of guessed, and so we can decide later whether a model is reliable enough to apply its
answers without the review window.

It is a console tool, not a test: it is not in the solution, CI or `dotnet test`, and only the `run` command sends
requests.

## What it measures

Each case is a pack with known answers: a fictional show's TMDB episode list, the pack's file list, and the S00
episode each special really is (null = not a TMDB special, leaving it out is right). For every case the tool runs
the app's own code: `PackSpecialPayloadBuilder` → `GeminiSpecialMappingProvider` → `GeminiApiClient` (router pinned
to one model) → `SpecialMappingOrchestrator` with every hard check of `SpecialMappingValidator`. Only the HTTP
handler belongs to the tool.

Every file Gemini was asked about ends up as one of:

| Outcome | Meaning |
|---|---|
| Correct | linked to the right S00 episode |
| **Wrong** | linked to another S00 episode (or a non-special was linked): a silent wrong link, the critical number |
| Dropped ✓ | Gemini was right but the app's checks removed the pair (false alarm) |
| Dropped ✗ | Gemini was wrong and the checks removed the pair (the validator saved a wrong link) |
| Left out | Gemini gave no answer for a real special (goes to review) |
| Abstain ✓ | not a TMDB special, and Gemini left it out |
| Error | no usable answer (HTTP error, timeout, unparseable); the app fell back to its rules |

The report also gives coverage (correct / real specials), the wrong rate among applied links with its 95% Wilson
upper bound (0 wrong of 40 still means "up to 8.8%"), Gemini's raw pair accuracy before the checks, stability
across samples, latency, and the rules tier (files the rules place without Gemini).

## Cases

- `fixtures`: the test project's `SpecialCases` (the report 10.4 layouts, the two-season brass pack, the 32 minis,
  the recap before season 1), compiled in from `MediaManager.App.Tests/Special/Fixtures`.
- `eval`: `Cases/EvalOnlyCases.cs`, aimed at what prompt v3 changed (order in a root OVA folder, order against the
  pairs in `ctx.fx`) and at abstaining. Every answer must be deducible from the payload.
- `private`: `cases/*.json` in the data folder, built from real packs. They never go in the repo, and a case is
  used only when `groundTruthConfirmedByUser` is true (the user confirmed every expected episode). Shape:

```json
{
  "name": "short-id", "showTitle": "…", "tmdbId": 1, "note": "…", "tags": ["real"],
  "episodes": [[1, 1, "Episode 1", "2020-01-03"], [0, 1, "Special title", "2020-03-01"]],
  "files": ["Pack/Season 1/…S01E01.mkv", "Pack/Specials/…SP01.mkv"],
  "expected": { "Pack/Specials/…SP01.mkv": 1 },
  "groundTruthConfirmedByUser": false
}
```

## Prompt variants

`v3` (the live prompt; the variant named after `GeminiSpecialMappingProvider.SpecialPromptVersion`) passes the
provider's prompt and payload through untouched. `v2` is a frozen copy of the prompt of commit `92e6b58` (the
self-test checks its hash) and derives payload v2 from the live payload (v2 and v3 differ only in `v` and in the
`ctx.fx` entries). The app's runtime behaviour does not change. When the app moves to v4, freeze v3 in
`Prompts/PromptVariants.cs` before comparing.

## Data folder

Everything the tool writes goes to `--data-dir` (or `GEMINI_EVAL_DIR`), which must be outside any git work tree:

```
quota.json                      the eval's own request ledger (per model, per Pacific day)
app-state/<model>/              router state and model catalog of the pinned stack
runs/<runId>/run.json           what was planned (models, prompts, prompt hashes, cases)
runs/<runId>/samples/*.json     one record per request: case, model, prompt, hashes, payload, raw HTTP response
runs/<runId>/report.md          the run's report (built by replaying the recordings)
reports/                        replay and self-test reports
cases/*.json                    private cases
```

## Quota and key

The free tier is shared with the app (same key, same project), so the tool keeps its own ledger and refuses to go
over: at most 100 requests per Pacific day over all 500/day models together (Flash-Lite), and at most 5 per day on
each 20/day model. Every request is written to the ledger before it is sent; failed ones count. A run whose plan
does not fit is refused up front, and nothing is sent without `--yes`.

The key comes from `GEMINI_EVAL_API_KEY` or `GEMINI_API_KEY`, or with `--key-from-settings <settings.json>` from the
app's settings file, opened read-only. It is never printed or written: every file goes through a scrubber, and
request headers are never recorded.

## Usage

Build once (from this folder): `dotnet build -c Debug -p:Platform=x64`, then run
`bin/x64/Debug/net8.0-windows10.0.17763.0/GeminiSpecialEval.exe <command>`.

```
selftest  [--data-dir D]                                  fake answers through the real pipeline (0 requests)
list      [--cases …] [--verbose]                         cases, rule tier, files sent, payload sizes
plan      --models M --prompts v2,v3 --data-dir D          requests a run would send vs today's caps
run       --models M --prompts v2,v3 --data-dir D --yes    the live run
run       … --offline-responder oracle|shifted|silent|flaky   rehearsal of a run, nothing sent (use a scratch data folder)
replay    --run <runId>[,<runId>…] --data-dir D           re-score recordings with today's code (0 requests)
quota     --data-dir D                                    today's eval requests and what is left
```

Several runs (for example one per day, to stay inside the caps) can be replayed together into one report:
`replay --run idA,idB`. A recording whose payload or prompt the app would no longer send is left out of the
table and listed under "Replay notes".
