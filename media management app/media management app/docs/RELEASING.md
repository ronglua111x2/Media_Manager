# Releasing Media Manager

How versions are numbered, how a release is cut, and how it is installed or rolled back on the real machine.

Related: [BUILD.md](./BUILD.md) (build/test/CI) · workflows `.github/workflows/ci.yml`, `.github/workflows/release.yml` at the git root.

---

## Versioning

The version comes from git tags via [MinVer](https://github.com/adamralph/minver) (`Directory.Build.props`). Nothing in source holds a version number.

| Commit | Version stamped into the exe |
|--------|------------------------------|
| Tagged `v1.2.0` | `1.2.0+<sha>` |
| 3 commits after `v1.2.0` (dev / PR build) | `1.2.1-alpha.0.3+<sha>` |
| No `v*` tag yet | `1.0.0-alpha.0.N+<sha>` |

Only tags with the `v` prefix count; `four-pillars-sprint-*` tags are ignored.

Where the version shows up:

- system log on every start: `Media Manager <version> starting (pid …)`
- crash reports (`Version:` line), `session.alive`, `last-exit.json` (so an `UncleanShutdown` names the build that died)
- backup manifest `AppVersion`
- exe file properties (`ProductVersion`)

### Which number to bump

Chosen by PR label, see [Version bump = PR label](#1-version-bump--pr-label).

| Bump | When |
|------|------|
| **patch** `1.2.0 → 1.2.1` | Bug fixes only, no schema change |
| **minor** `1.2.1 → 1.3.0` | New features, **or any new DB migration** |
| **major** `1.3.0 → 2.0.0` | Breaking state-folder / settings format |

Rule of thumb: rolling back across a **minor** may cross a migration (see [Rollback](#rollback)).

---

## Continuous delivery (automatic)

Merge to `main` → GitHub Release → installed on the machine. No manual tagging or copying.

```
PR merged into main ──► auto-release.yml ──► tag vX.Y.Z ──► release.yml ──► GitHub Release
                                                                               │
  MediaManager\AutoUpdate task (logon + hourly) ◄─────────────────────────────┘
    1. download + SHA256 verify + extract into <InstallDir>\.staged\<tag>   (app may keep running)
    2. app not running → swap in (rename, < 1 s); app running → wait for next logon / hourly run
```

### 1. Version bump = PR label

| PR label | Result of merging |
|----------|-------------------|
| *(none)* | **patch** release `1.2.0 → 1.2.1` |
| `release:minor` | **minor** `1.2.1 → 1.3.0` — new features, **any new DB migration** |
| `release:major` | **major** `1.3.0 → 2.0.0` — breaking state-folder / settings format |
| `release:skip` | no release |

PRs that only touch `*.md`, `docs/` or `.github/` are skipped automatically. The bump starts from the newest **stable** `vX.Y.Z` tag (pre-release tags are ignored); the first release is `v1.0.0`.

Release without a merge: Actions → **Auto release** → *Run workflow* (choose the bump). A hand-pushed tag also works (`git tag v1.3.0-rc.1 && git push origin v1.3.0-rc.1`); a hyphenated tag becomes a **pre-release**, which the auto-updater ignores.

### 2. `release.yml` (called by auto-release, or on a pushed tag)

- fails if the tagged commit is not on `main`
- restore → build → test → single-file publish (same composite action as CI)
- checks the exe `ProductVersion` matches the tag
- GitHub Release with generated notes and assets:
  - `MediaManager-vX.Y.Z-win-x64.zip` — exe, `Assets/`, `THIRD_PARTY_NOTICES.md`, `licenses/` (Sonarr.Parser GPL-3.0), `updater/install-release.ps1`
  - `SHA256SUMS.txt`

A failed release leaves the tag without a Release. Fix it through a PR (that merge releases the next patch) and delete the orphan tag: `git push origin :refs/tags/vX.Y.Z`.

### 3. Auto-update on the machine (one-time setup)

```powershell
& "D:\VScode\Misc\Media_Manager\media management app\media management app\scripts\register-auto-update.ps1"
```

- Copies the updater to `D:\App-Disk-D\Media_Manager\App\updater\install-release.ps1` and registers task **`MediaManager\AutoUpdate`**: at logon (no delay, so it runs before `MediaManager\AutoStart`'s 10 s delay) and hourly.
- Never kills the app. The app sits in the tray all day, so a new release is normally **staged** by the hourly run and **applied at the next logon/reboot**, or on the next hourly run after you exit the app from the tray.
- Every release ships a new `updater\install-release.ps1`, so the updater updates itself.
- Log: `<InstallDir>\updater\update.log`. Previous exe kept as `media management app.exe.bak-<version>` (newest 3).
- Stop auto-updates: `register-auto-update.ps1 -Unregister`.

### Manual install

```powershell
& "...\scripts\install-release.ps1"                 # latest stable now (asks you to exit the app from the tray)
& "...\scripts\install-release.ps1" -Tag v1.3.0     # specific version, incl. pre-releases
```

`-Force` kills the running app instead of waiting (next start reports `UncleanShutdown`; avoid during Auto-Track/backup).

Do not copy `bin\…\publish` builds onto the real machine — install only releases so every crash report maps to a tag.

## Rollback

```powershell
& "...\scripts\register-auto-update.ps1" -Unregister   # first, or AutoUpdate re-installs the newest release
& "...\scripts\install-release.ps1" -Rollback          # newest .bak-* exe
& "...\scripts\install-release.ps1" -Tag v1.2.0        # or reinstall any older release
```

Re-run `register-auto-update.ps1` to go back onto auto-updates. A bad release is usually better fixed forward: merge the fix, and the next patch release rolls out automatically.

**Migrations:** the DB migrates forward on first start of a newer build. An older build may not read a newer schema. Before rolling back across a **minor** bump, restore a backup taken before the upgrade (Settings → Backup), or confirm the newer release added no migration (`MediaManager.Core/Migrations`).

---

## Debugging a crash from a release

- The crash report and `last-exit.json` carry the exact version + commit SHA → `git checkout <sha>` to get matching source.
- Release builds use `DebugType=embedded`: symbols live inside the assemblies in the exe, so exception stack traces include file:line and Visual Studio / WinDbg resolve dumps from `%LocalAppData%\CrashDumps` without separate `.pdb` files. (Loose `.pdb` files next to the exe are deleted at startup by `PublishJunkCleanup`.)
