# Pre-Sprint Checklist — Historical (Sprint 5/6 not resuming)

This gate is **obsolete as a resume trigger.** User did not pick a modernization option. Settings 7-VM split is **cancelled** with E4. Optional Sprint 4.5 hygiene is **not** in the freeze task and must not be treated as “start Sprint 5.”

## A. Documentation (this audit)

- [x] Read [00-executive-summary.md](./00-executive-summary.md)
- [x] Schema ownership understood ([02-settings-json-schema.md](./02-settings-json-schema.md))
- [x] UI/JSON mismatches documented ([03-ui-section-audit.md](./03-ui-section-audit.md))
- [ ] **User decision:** modernization option chosen ([06-modernization-options.md](./06-modernization-options.md))
- [ ] Update [05-sprint-timeline.md](../planning/05-sprint-timeline.md) Sprint 5 scope with chosen option

## B. User decisions (fill in)

| Question | Decision | Date |
|----------|----------|------|
| Move qBit/Jellyfin/WARP UI to logical tabs? | _pending_ | |
| Draft vs Current vs disk model for dirty tracking? | _pending_ | |
| Rename `SystemSettingsViewModel` → `SettingsWorkspaceViewModel`? | _pending_ | |
| Keep `UiSettings` in same JSON file? | _pending_ | |

## C. Code hygiene (recommended "Sprint 4.5" — small PR)

- [ ] `RefreshLibraryRootPreview` — stop mutating `Current.DefaultLibraryFolderName`
- [ ] `OnWarpExecutablePathChanged` — remove live `ApplyWarpSettings()` (apply on Save/test only)
- [ ] Split `ApplyAutoTorrentSettings` into connection + storage methods (internal)
- [ ] Split `ApplyAutoTrackSettings` into schedule/quality + Jellyfin partials (internal)
- [ ] Add 3–5 Core tests: load JSON fixture → Apply → assert normalized output

## D. Sprint 5 resume (revised)

Only after **C** or explicit waiver:

- [ ] Extract **System** section VM + panel
- [ ] Extract **Backup** section VM + panel
- [ ] Host coordinator + DI
- [ ] **Do not** extract Integrations until B/C decisions implemented
- [ ] `dotnet test` + Release x64 green
- [ ] Manual checklist: System + Backup + regression on other tabs (unchanged host bindings)

## E. Sprint 6 (blocked until 5 + save model)

- [ ] Remaining section VMs (Library, Notifications, Auto-Track, Torrent)
- [ ] Integrations (TMDB + Gemini only) after UI/Apply realign
- [ ] Dirty tracking (scope per chosen option)
- [ ] `SettingsViewModel` ≤ ~400 lines
- [ ] Full 7-section manual checklist

## F. Manual smoke scripts (any settings work)

Use state folder `D:\MediaManagerState` (or disposable copy):

1. Change System log retention → Save → restart app → value retained
2. Change qBit URL on Integrations → Test → **do not Save** → change Torrent download folder → Save → both retained
3. Switch Integrations tab → Gemini catalog path updates (section activated hook)
4. Backup → Connect (partial save) → verify unrelated settings not zeroed in JSON
5. Edit externally `settings.json` → open Settings → reload shows disk values (Sprint 4 script #3)
6. Navigate away mid-edit → (Sprint 6) dirty prompt when implemented

## G. Binding path checklist (when sub-VMs land)

Document prefix changes in sprint local plan — example:

| Old | New (if realigned) |
|-----|-------------------|
| `QbittorrentWebUiUrl` | `TorrentSection.QbittorrentWebUiUrl` |
| `WarpEnabled` | `AutoTrackSection.WarpEnabled` |
| `TmdbReadAccessToken` | `IntegrationsSection.TmdbReadAccessToken` |

Full table to be regenerated when implementation starts.

---

## Sprint 5 freeze record

| Item | Status |
|------|--------|
| Sprint 5 VM split implementation | **Frozen / cancelled** Aug 2026 |
| Reason | UI tab ≠ JSON/Apply; user did not pick a modernization track; E4 program cancelled |
| This audit folder | `docs/settings-modernization/` |
| Next action | **None for Settings splits.** Do not resume Sprint 5/6. |
