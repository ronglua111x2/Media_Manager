# Modernization Options

**Decision:** User chose **not** to pick a full modernization track. Sprint 5/6 **will not resume.** Options below remain reference only.  
All options preserve `D:\MediaManagerState` (or user StateFolder) as the runtime path; differences are **structure and save semantics**.

---

## Option A — Minimal refactor (VM split only, current save model)

**What:** Extract section sub-VMs + UserControls per [03-split-large-viewmodels-services.md](../planning/03-split-large-viewmodels-services.md) Option B. Keep single `settings.json`, monolithic Save applies all sections.

**Pros:** Matches original four-pillars plan; smallest doc change.  
**Cons:** **Does not fix** UI/JSON mismatch; Integrations extract remains dangerous; dirty tracking hard.

**Verdict:** ❌ **Not recommended** without at least Apply* splits and UI realign from [05-split-boundaries-recommendation.md](./05-split-boundaries-recommendation.md).

---

## Option B — Section Apply facades + UI realign (recommended baseline)

**What:**

1. Split `ApplyAutoTorrentSettings` → connection vs storage (or single owner VM)
2. Split Jellyfin Apply slices or unify under Auto-Track VM
3. Move qBit, Jellyfin creds, WARP cards to logical tabs **or** keep layout with facade merge
4. Extract section VMs with `LoadFrom` / `ApplyTo` interfaces
5. Hygiene: no `Current` mutation from preview/WARP keystrokes
6. Then Sprint 5/6 VM + XAML extraction

**Pros:** Fixes root coupling; enables dirty tracking per section draft; no migration for users.  
**Cons:** Medium effort; some UI tab moves; one coordinated PR before split.

**Verdict:** ✅ **Recommended** for solo dev — best risk/reward before Sprint 6.

---

## Option C — Draft / commit settings model (three-state)

**What:** Introduce explicit layers:

```
SettingsDraft (per section or whole)
    ↓ Apply on Save
AppSettings Current (singleton, services read this)
    ↓ SettingsService.Save()
settings.json
```

- VM sections edit **draft** copies
- Services continue reading `Current` until Save
- Test buttons apply **draft → Current** temporarily OR test against draft via parameters
- `OnNavigatedTo`: reload draft from disk if not dirty

**Pros:** Clean Sprint 6 dirty prompt; tests stop clobbering unrelated `Current` fields.  
**Cons:** Larger refactor; must audit all `Current` writers (Library/Torrent/News/AutoTrack/Backup).

**Hybrid:** Draft only inside Settings workspace; workspace Ui* stays as today.

**Verdict:** ✅ **Strong fit** if Sprint 6 dirty-tracking is mandatory — pair with Option B.

---

## Option D — Split persistence files (multi-file settings)

**What:** Separate files under StateFolder, e.g.:

```
settings.json          — core: StateFolder, Startup, Logs, Theme
integrations.json      — TMDB, Gemini, Warp
autotrack.json         — AutoTrack
autotorrent.json       — AutoTorrent
backup.json            — Backup (non-secret metadata)
```

`SettingsService` becomes loader coordinator; migration reads legacy monolithic `settings.json` once.

**Pros:** Clear ownership; parallel saves; smaller diffs; easier testing.  
**Cons:** **Highest effort**; migration script; atomic multi-file save; all readers must update; backup/restore must bundle files.

**Verdict:** ⏸ **Defer** — consider for post-initiative if Option B still feels fragile.

---

## Option E — Extract settings editing to Core (`MediaManager.Core`)

**What:** Move `AppSettings`, normalization, Apply logic to Core as pure functions:

```csharp
public static class SettingsNormalizer { ... }
public static class AutoTrackSettingsMapper { ... }
```

WPF VMs become thin; unit-test Apply/load round-trips.

**Pros:** Aligns with E2 testing strategy; prevents duplicate clamp logic.  
**Cons:** Core must not reference WPF; map VM DTOs separately.

**Verdict:** ✅ **Compatible add-on** to Option B/C — do mapper extraction when splitting Apply methods.

---

## Comparison matrix

| Criterion | A VM only | B Facades + realign | C Draft model | D Multi-file |
|-----------|-----------|---------------------|---------------|--------------|
| Fixes Integrations split | No | Yes | Yes | Yes |
| User migration | None | None | None | One-time |
| Effort | Low | Medium | Medium–High | High |
| Sprint 6 dirty tracking | Hard | Medium | Easier | Easier |
| Risk of silent bugs | High | Medium | Low | Medium (migration) |

---

## Suggested decision path

1. **Adopt Option B** as next implementation sprint ("Settings modernization 1").
2. **Design Option C** draft model on paper while doing B — implement dirty prompt in Sprint 6 if B alone insufficient.
3. **Option E** in same sprint as Apply splits (Core mappers + 5–10 round-trip tests).
4. **Revisit Option D** only if multi-writer `UiSettings` and monolithic Save remain painful after B+C.

---

## What NOT to do

- Do not rename `TmdbReadAccessToken` to `TmdbToken` during modernization (binding churn without value).
- Do not split `SettingsViewModel` by partial classes only (Option A in 03 doc) — organizational win only.
- Do not add second Save button per section until dirty model is understood.
- Do **not** resume Sprint 5 or 6. Settings 7-VM split is cancelled. These options are historical.
- Do not resume Sprint 5 Integrations extract (would have required an Apply split first).

---

## Open questions for user

1. **UI realign:** OK to move qBittorrent + Jellyfin connection + WARP from Integrations tab to Torrent/Auto-Track tabs?
2. **UiSettings:** Keep in `settings.json` or move library/torrent/news UI state to separate `ui-state.json`?
3. **Draft model:** Required for Sprint 6 dirty prompt, or is section-level dirty enough?

Record answers in [07-pre-sprint-checklist.md](./07-pre-sprint-checklist.md) when decided.
