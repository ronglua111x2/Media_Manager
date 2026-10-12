# Sprint 05 — Local plan (FROZEN)

**Branch:** `auto-torrent` @ `d5129b4` (Aug 2026)  
**Status:** 🧊 **Frozen / cancelled** — do **not** resume Sprint 5 or 6. Settings 7-VM split cancelled with E4.  
**Audit docs:** [../settings-modernization/README.md](../settings-modernization/README.md) (historical)

## Why frozen (was pause)

User observed repeated mismatch between Settings **UI sections** and **ViewModel/JSON ownership**. Code audit confirmed:

- Integrations tab edits `AutoTorrent`, `AutoTrack.Jellyfin`, and `Warp` — not "integrations" JSON roots
- `ApplyAutoTorrentSettings` and `ApplyAutoTrackSettings` span multiple tabs
- Naive sub-VM split (original Sprint 5 scope) risks silent binding bugs and Save clobbering

**Later decision (Aug 2026):** User did **not** pick a modernization track. Remaining E4 sprints frozen. This local plan is **not** a resume trigger.

## Original scope (from 05-sprint-timeline § Sprint 5) — historical

| In | Out |
| -- | --- |
| Host keeps `SelectedSettingsSection`, save orchestration | All 7 sections in one sprint |
| Sub-VMs: Integrations, Backup, System + panels | AutoTrack/Library splits |
| DI registration | Behavior change to values |

## Revised scope (historical — will not resume)

See [../settings-modernization/05-split-boundaries-recommendation.md](../settings-modernization/05-split-boundaries-recommendation.md). **Do not implement.**

1. **Phase A:** System + Backup sections only
2. **Phase B:** Apply* splits + UI realign OR facades — **before** Integrations extract
3. **Phase C:** Sprint 6 remainder + dirty tracking

## Definition of Done (original — deferred)

- [ ] Integrations, Backup, System extracted; host file smaller
- [ ] XAML binds to nested section paths
- [ ] `dotnet test` + MSBuild x64 green

## Findings summary (for Sprint 6)

Full detail in settings-modernization docs. Top items:

1. Jellyfin split across Integrations + Auto-Track UI, one `AutoTrack.Jellyfin` object
2. qBittorrent UI under Integrations, storage under Torrent tab, one `AutoTorrent` object
3. WARP under Integrations UI, `Warp` JSON root, Auto-Track hunt consumer
4. `UiSettings` written by Library/Torrent/News VMs — not Settings host
5. Live mutation of `Current` without Save (preview, WARP path)
6. Test commands call full `Apply*` — side effects on unrelated fields
7. `SystemSettingsViewModel` naming collision with System tab section VM

## Test gate (unchanged)

```bash
APP_ROOT="d:/VScode/Misc/Media_Manager/media management app/media management app"
dotnet test "$APP_ROOT/MediaManager.Core.Tests/MediaManager.Core.Tests.csproj" -c Release -v normal
dotnet build "$APP_ROOT/media management app.csproj" -c Release -p:Platform=x64 -v minimal
```

## Next steps

**None for Sprint 5.** Do not choose a modernization option to resume this sprint. Next code work: [poster-flash-surgical-fix.md](./poster-flash-surgical-fix.md).
