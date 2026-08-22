# Media Manager — Recipe JSON Schema

Recipes define how the app searches, filters, and scores torrent candidates. They are stored as JSON files in `{StateFolder}/Recipes/` with extension `.rcp`.

**Code:** `Models/SearchRecipe.cs`, `Services/RecipeService.cs`, `Services/RecipeRuntimeSettings.cs`

---

## File Format

- **Serialization:** JSON, camelCase property names, enums as strings
- **Filename:** `{recipeId}.rcp`
- **Built-in IDs:** `default-tv`, `default-tv-pack`, `default-movie` (cannot be deleted)
- **User IDs:** 32-char hex GUID (no dashes), e.g. `604977d8bdb0487a8642fbe4987373f3`

---

## Root Object: `SearchRecipe`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `recipeId` | string | yes | Unique id; also the filename stem |
| `version` | int | yes | Schema version (currently `1`) |
| `mainFlowVersion` | string | yes | Pipeline id (currently `"recipe-flow-v1"`) |
| `name` | string | yes | Display name in UI |
| `targetKind` | string enum | yes | `"TvEpisode"`, `"TvSeasonPack"`, or `"Movie"` |
| `modules` | array | yes | Ordered pipeline modules (see below) |

### Example (minimal TV episode default)

From live `default-tv.rcp`:

```json
{
  "recipeId": "default-tv",
  "version": 1,
  "mainFlowVersion": "recipe-flow-v1",
  "name": "Default TV Recipe",
  "targetKind": "TvEpisode",
  "modules": [ /* 6 modules — see Module Pipeline */ ]
}
```

---

## Module Pipeline

Each recipe has **six required modules** (legacy `AddTorrent` / `LinkOutput` blocks are stripped on load):

| Order | `blockType` | Purpose |
|-------|-------------|---------|
| 0 | `Identity` | Title aliases, library English titles, parallel search overrides |
| 10 | `QueryBuilder` | Query templates and custom queries |
| 20 | `SearchSource` | qBittorrent plugin search, pagination, snapshot mode |
| 30 | `CandidateParser` | Filename parsing, episode numbering mode, metadata probe |
| 40 | `CandidateFilter` | Quality/seeders/size/audio filters, engine priority |
| 50 | `Scoring` | Weighted ranking; pack extras boost for season packs |

Legacy `PackExtrasPriority` modules are migrated into `Scoring.extensionData` on load.

---

## Module Object: `RecipeModuleConfig`

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `moduleId` | string | auto GUID | Stable module instance id |
| `blockType` | string enum | — | One of `RecipeBlockType` values |
| `order` | int | — | Sort order (0, 10, 20, …) |
| `schemaVersion` | int | 1 | Per-module schema version |
| `isEnabled` | bool | true | Skip module when false |
| `displayName` | string | block name | UI label |
| `aliases` | string[] | `[]` | Extra search title aliases |
| `queryTemplates` | string[] | `[]` | Query patterns with placeholders |
| `customQueries` | string[] | `[]` | Fixed queries (no templating) |
| `qualityAllowList` | string[] | `["1080p"]` | Allowed resolutions |
| `preferredAudioCodec` | string | `""` | e.g. `"AAC"`, `"FLAC"` |
| `minimumSeeders` | int | 0 | Hard filter |
| `minimumSizeBytes` | long? | null | Hard filter |
| `maximumSizeBytes` | long? | null | Hard filter |
| `includeTerms` | string[] | `[]` | Must appear in candidate name |
| `excludeTerms` | string[] | `[]` | Disqualify if present |
| `preferTerms` | string[] | `[]` | Scoring boost terms |
| `preferredReleaseGroups` | string[] | `[]` | Release group allow list |
| `blockedReleaseGroups` | string[] | `[]` | Release group block list |
| `plugins` | string | `"enabled"` | `"enabled"` or comma-separated plugin names |
| `category` | string | `"all"` | qBittorrent search category |
| `resultLimit` | int | 100 | Max results per search (1–5000) |
| `savePath` | string | `""` | Override download path (usually empty) |
| `torrentCategory` | string | `"AutoTorrent"` | qBittorrent category on add |
| `tags` | string | `""` | qBittorrent tags on add |
| `paused` | bool | false | Passed to add request (gate service may override) |
| `extensionData` | object | `{}` | String key/value module-specific settings |

---

## Query Template Placeholders

Used in `QueryBuilder.queryTemplates`:

| Placeholder | Source |
|-------------|--------|
| `{title}` | Resolved show/movie title (+ aliases) |
| `{year}` | First air year / movie year |
| `{season}` | Season number (unpadded) |
| `{season:00}` | Season zero-padded |
| `{episode}` | Episode number (unpadded) |
| `{episode:00}` | Episode zero-padded |
| `{quality}` | Target quality from filter/scoring |
| `{audio}` | Preferred audio codec |

### Example templates (TV episode)

```
{title} S{season:00}E{episode:00} {quality} {audio}
{title} {year} S{season:00}E{episode:00} {quality}
{title} {season}x{episode:00} {quality}
```

### Example templates (TV pack)

```
{title} S{season:00} complete {quality} {audio}
{title} season {season} {quality}
{title} S{season:00} pack {quality}
```

### Example templates (anime-style user recipe)

From live `604977d8bdb0487a8642fbe4987373f3.rcp`:

```
{title} {episode} {quality}
{title} - {episode} {quality}
```

---

## extensionData Keys

All values are **strings** in JSON (parsed to int/bool at runtime). Keys defined in `RecipeRuntimeSettings`:

### SearchSource module

| Key | Default (from AutoTorrent settings) | Range | Description |
|-----|-------------------------------------|-------|-------------|
| `parallelSearchCount` | `MaxParallelSearches` | 1–8 | Concurrent plugin searches |
| `maxCandidatesPerFetch` | `MaxCandidatesPerFetch` | 1–10 | Top N candidates kept |
| `useShowSnapshotSearch` | setting | bool | Bulk snapshot search mode |
| `snapshotTargetResults` | 2000 | 100–5000 | Target hits for snapshot |
| `snapshotTimeoutSeconds` | 60 | 30–300 | Snapshot wall timeout |
| `localMatchWorkers` | 4 | 1–8 | Parallel local matchers |
| `enableSearchPagination` | TV: false, else true | bool | Paginate plugin results |
| `paginationPageSize` | 500 | 50–1000 | Results per page |
| `paginationMaxPagesMovie` | 3 | 1–10 | Max pages (movies) |
| `paginationMaxPagesTvParallel` | 1 | 1–10 | Max pages (TV parallel) |
| `paginationMaxPagesTvSnapshot` | 2 | 1–10 | Max pages (TV snapshot) |
| `searchIdleTimeoutSecondsTvParallel` | 8 | 0–120 | Idle timeout between results |

### Identity / QueryBuilder modules

| Key | Description |
|-----|-------------|
| `useLibraryEnglishTitles` | Use TMDB English titles for search |
| `maxLibraryAlternativeTitlesForSearch` | Cap on alt titles (0–16) |
| `skipDefaultTitle` | Skip primary title in query rotation |
| `sanitizeQuery` | Strip problematic characters |
| `parallelSearchCount` | Can also appear on Identity/QueryBuilder for overrides |

### CandidateParser module

| Key | Values | Description |
|-----|--------|-------------|
| `episodeNumberingMode` | `"Standard TV"`, `"Anime absolute"` | How episode numbers are parsed |
| `enableCandidateMetadataProbe` | `"True"` / `"False"` | Probe torrent metadata before scoring |

### CandidateFilter module

| Key | Description |
|-----|-------------|
| `enginePriorityMode` | Engine ordering mode |
| `enginePriority` | Comma-separated plugin order |
| `engineWeight` | Per-engine weight override |

### Scoring module

| Key | Default | Description |
|-----|---------|-------------|
| `qualityWeight`, `audioWeight`, `seedersWeight`, etc. | — | Scoring weights |
| `sizePreference` | `"Prefer larger"` | Size scoring bias |
| `packExtrasPriorityEnabled` | `"True"` (pack only) | Boost packs with OVA/special keywords |
| `packExtrasPriorityScore` | `2500` | Score boost amount |

---

## Normalization on Load/Save

`RecipeService.NormalizeRecipe()`:

- Assigns missing `moduleId` GUIDs
- Ensures all six required modules exist
- Migrates legacy `PackExtrasPriority` → `Scoring`
- Removes deprecated `AddTorrent` / `LinkOutput` modules
- Migrates legacy `customQuery` extension key → `customQueries` array
- Clamps numeric limits, fills missing SearchSource pagination keys

---

## User Recipe Example (anime parallel search)

From live state — `604977d8bdb0487a8642fbe4987373f3.rcp` ("Common Anime no Probe Parallel"):

- **targetKind:** `TvEpisode`
- **Identity:** `useLibraryEnglishTitles=True`, `parallelSearchCount=3`
- **QueryBuilder:** anime-style `{title} {episode}` templates
- **CandidateParser:** `episodeNumberingMode=Anime absolute`, probe disabled
- **CandidateFilter:** `minimumSeeders=10`, qualities `2160p` + `1080p`

---

## Import / Export

- **Import:** `RecipeService.ImportRecipe(path)` — assigns new GUID if id collision
- **Export:** `RecipeService.ExportRecipe(id, folder)` — copies `.rcp` with sanitized name
- **Duplicate:** New GUID, name suffixed with `" Copy"`

---

## Related Docs

- [STATE_FOLDER.md](./STATE_FOLDER.md) — where recipes live on disk
- [FEATURES.md](./FEATURES.md) §7 — Recipe workspace UI
- [APP_OVERVIEW.md](./APP_OVERVIEW.md) — recipe role in acquisition flow
