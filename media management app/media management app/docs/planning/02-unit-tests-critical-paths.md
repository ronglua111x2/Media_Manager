# Planning: Unit Tests for Critical Paths

**Priority:** High  
**Source:** [IMPROVEMENTS.md](../IMPROVEMENTS.md) § Priority: High #2  
**Status:** Sprint 1 implemented — Core library + 20 parser tests on `auto-torrent` (commit pending)

---

## Problem statement

Complex, regex-heavy logic drives torrent search, candidate scoring, pack episode mapping, and malware validation — but **there is no automated test project** in the solution. Regressions in recipe behavior, Auto-Track hunts, or pack linking can only be caught by manual runs against live qBittorrent/TMDB.

A single parser tweak in `TorrentCandidateParser` can silently reject good releases or accept wrong episodes across Library, Torrent workspace, and Auto-Track.

---

## Why it happened

The app was built as a **single WPF executable** (`media management app.csproj`, `net8.0-windows`, WPF + WinForms). Features were validated end-to-end on the developer’s machine. Pure functions and services were colocated with UI-oriented code without extracting testable boundaries upfront.

The recipe/cart pipeline grew organically:

- Search query building → qBittorrent plugins → parse filenames → score → validate content → cart

Each stage works in production, but **no golden-file fixtures** capture expected behavior for anime absolute numbering, multi-season packs, OVA patterns, etc.

---

## Current behavior

### No test infrastructure

- Solution contains **one main project** + `ThirdParty/Sonarr.Parser` reference.
- No `*Tests.csproj`, no xUnit/NUnit/MSTest packages, no CI test step ([IMPROVEMENTS.md](../IMPROVEMENTS.md) item #18 is Lower priority).
- `ThirdParty/**` is excluded from main compile — parser is a separate project reference only.

### Critical path: search → score → cart

```
SearchPlanBuilder.Build*Queries()
    → qBittorrent search (external)
    → TorrentCandidateParser.Parse()
    → CandidateEvaluationService.Evaluate*()
    → TorrentCartService / FetchJobService
    → TorrentContentValidationService.ValidateFilesAsync()
    → TorrentAddGateService (orchestration)
```

**Auto-Track** uses the same evaluation stack via `AutoTrackService` → `FetchJobService` → `CandidateEvaluationService`.

### Target classes (from IMPROVEMENTS.md)

| Class | File | Lines | Testability |
|-------|------|-------|-------------|
| `TorrentCandidateParser` | `Services/TorrentCandidateParser.cs` | ~509 | **High** — static, pure parsing |
| `CandidateEvaluationService` | `Services/CandidateEvaluationService.cs` | ~298 | **High** — needs mock `ISearchTitleResolver` + fixture recipes/shows |
| `SearchPlanBuilder` | `Services/SearchPlanBuilder.cs` | ~202 | **High** — query strings from recipe + media |
| `SearchTitleResolver` | `Services/SearchTitleResolver.cs` | ~84 | **High** — alias/title resolution |
| `PackSeasonFileGrouper` | `Services/PackSeasonFileGrouper.cs` | ~101 | **High** — groups files by season |
| `PackEpisodePatternInferrer` | `Services/PackEpisodePatternInferrer.cs` | ~418 | **High** — infers episode numbers from stems |
| `TorrentContentValidationService` | `Services/ITorrentContentValidationService.cs` | ~313 | **Medium** — `ValidateFilesAsync` is pure given file list + settings; `ValidateAsync` hits qBittorrent |

Example evaluation entry point:

```15:65:Services/CandidateEvaluationService.cs
    public RecipeCandidateResult EvaluateEpisode(SearchRecipe recipe, TrackedShow show, TrackedEpisode episode, TorrentSearchResult result)
    {
        var usesAnimeAbsolute = RecipeRuntimeSettings.UsesAnimeAbsoluteEpisodeNumbering(recipe);
        var parsed = TorrentCandidateParser.Parse(result.FileName, usesAnimeAbsolute);
        // ... reject reasons, title match, scoring ...
        return Accepted(result, qualityScore, titleMatch.Score, ...);
    }
```

Parser handles SxxExx, absolute anime, season ranges, OVA/special patterns:

```42:46:Services/TorrentCandidateParser.cs
public static class TorrentCandidateParser
{
    private static readonly Regex EpisodeRegex = new(
        @"(?<title>.*?)(?:\bS(?<season>\d{1,3})E(?<episode>\d{1,4})\b|...",
```

Validation ignores subs/images, flags dangerous extensions:

```20:31:Services/ITorrentContentValidationService.cs
/// Validates torrent file lists for dangerous extensions, double-extension obfuscation,
/// and main-payload format mismatch. Extra files (.nfo, images, subs) are ignored.
public sealed class TorrentContentValidationService : ITorrentContentValidationService
{
    private static readonly HashSet<string> PayloadIgnoreExtensions = new(...) { ".nfo", ".srt", ... };
```

### Related large untested areas (out of scope for first pass)

- `FetchJobService.cs` (~1,473 lines) — orchestration + qBittorrent polling
- `AutoTrackService.cs` (~1,564 lines) — scheduling, WARP, notifications
- `RecipeRuntimeSettings` — recipe module flag interpretation

These benefit from **integration tests** later; first ROI is pure logic.

---

## Impact

| Risk | Detail |
|------|--------|
| **Wrong torrent selected** | Scoring/parser bug → Auto-Track adds bad release |
| **False rejects** | Good releases filtered → manual cart workaround |
| **Pack link failures** | `PackEpisodePatternInferrer` wrong → Gemini cost or manual review |
| **Security regression** | Validation bypass → dangerous file extensions accepted |
| **Refactoring fear** | Large VMs/services hard to change without tests ([03-split-large-viewmodels-services.md](./03-split-large-viewmodels-services.md)) |
| **Recipe changes** | User `.rcp` files in `{StateFolder}/Recipes/` — no validation tests on save (Medium priority item #6) |

---

## Affected areas

| Area | Notes |
|------|-------|
| **New test project** | e.g. `MediaManager.Tests` referencing main app or extracted core library |
| **Services listed above** | Primary test targets |
| **Models** | `SearchRecipe`, `TrackedShow`, `TorrentSearchResult`, `TorrentContentFile` — fixture builders |
| **Common** | `CandidateRejectReason`, `TorrentReleaseKind`, `RecipeBlockType` enums |
| **ThirdParty** | `MediaManager.Sonarr.Parser` — may need tests if filename parsing extends Sonarr |
| **Build** | MSBuild x64; user rule: build with MSBuild x64 to verify |
| **CI** | Future GitHub Actions (IMPROVEMENTS #18) |

---

## Constraints

- **WPF host project** — test project should target `net8.0` (not `-windows`) where possible to run on CI without UI; may need to reference main assembly (WPF) or extract a `MediaManager.Core` class library (larger refactor).
- **No live services in unit tests** — mock qBittorrent/TMDB/Gemini; use `ValidateFilesAsync` not `ValidateAsync` for validation tests.
- **Deterministic** — no wall-clock or network in unit tests.
- **Fixtures from real state** — anonymized torrent names from debug logs / `CartCandidateDebugSession` are valuable golden files.
- **Do not block shipping** — tests additive; app behavior unchanged until refactors.

---

## Options for resolution

### Option A: Test project referencing main WPF assembly

Add `MediaManager.Tests` (xUnit) with `ProjectReference` to `media management app.csproj`. Test public/internal services via `InternalsVisibleTo`.

| Pros | Cons |
|------|------|
| Fastest to set up | Pulls WPF/windows dependencies into test runner |
| No production code moves | Slower test compile; CI needs windows runner |

### Option B: Extract `MediaManager.Core` class library

Move parser, evaluation, search plan, pack inferrer, validation (file-list path) into a net8.0 library; WPF app references Core; tests reference Core only.

| Pros | Cons |
|------|------|
| Clean boundaries; fast CI | Upfront refactor; namespace/migration work |
| Enables future non-WPF tools | Touches many files |

### Option C: Snapshot / characterization tests only

Record inputs/outputs of current behavior before refactors; assert unchanged until intentionally updated.

| Pros | Cons |
|------|------|
| Locks current behavior including bugs | Large fixture maintenance |
| Good for parser | Less readable failure messages |

**Recommendation for planning:** Start with **Option A** for speed; plan **Option B** if CI or extract-core becomes painful.

---

## Suggested planning steps

1. **Create test project** — xUnit, FluentAssertions (optional), coverlet for coverage (optional).
2. **Fixture helpers** — `RecipeBuilder`, `TrackedShowBuilder`, sample `TorrentSearchResult` from real torrent names (document source in test comments).
3. **Parser tests first** — Highest density of edge cases (`TorrentCandidateParser`): S01E01, 1x01, absolute EP, season packs, OVA, specials, quality tokens.
4. **Search plan tests** — Template rendering, alias expansion via `SearchTitleResolver`, sanitize flags.
5. **Evaluation tests** — Accept/reject matrix per `CandidateRejectReason`; anime absolute vs standard numbering.
6. **Pack tests** — `PackSeasonFileGrouper.Group`, `PackEpisodePatternInferrer.TryInferEpisode` with multi-season folder trees (see FEATURES Appendix A).
7. **Validation tests** — `.exe` disguised as `.mkv`, double extensions, pack sample count, disabled validation flag.
8. **Wire CI** — `dotnet test` on push (Windows runner); MSBuild x64 build + test script locally.
9. **Define coverage goal** — e.g. 80% line coverage on parser + evaluation before refactoring `FetchJobService`.

### Suggested first test cases (examples)

| Scenario | Class |
|----------|-------|
| `"Show.Name.S02E05.1080p..."` → S=2 E=5 | `TorrentCandidateParser` |
| Absolute `"Anime Name - 12 [...]"` with recipe flag | `TorrentCandidateParser` + `CandidateEvaluationService` |
| Title alias from recipe Identity module | `SearchTitleResolver` |
| Reject release kind "sample" for episode target | `CandidateEvaluationService` |
| Pack with `Extras/` folder excluded | `PackSeasonFileGrouper` |
| `virus.exe` inside torrent file list | `TorrentContentValidationService` |

---

## Open questions

1. **Option A vs B** — Accept WPF test reference short-term, or invest in Core extraction first?  
2. **InternalsVisibleTo** — Expose internal helpers vs. test only public API?  
3. **Fixture source** — **Resolved:** Yes — anonymized release filenames committed in [fixtures/torrent-release-names.json](./fixtures/torrent-release-names.json) (Aug 2026).  
4. **Integration tests** — Scope qBittorrent mock server later, or never?  
5. **Recipe files** — Load real `.rcp` from `docs/` or `STATE_FOLDER` as test data?  
6. **Coverage threshold** — Enforce in CI or advisory only?
