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

| Bump | When |
|------|------|
| **patch** `1.2.0 → 1.2.1` | Bug fixes only, no schema change |
| **minor** `1.2.1 → 1.3.0` | New features, **or any new DB migration** |
| **major** `1.3.0 → 2.0.0` | Breaking state-folder / settings format |

Rule of thumb: rolling back across a **minor** may cross a migration (see [Rollback](#rollback)).

---

## Cutting a release

1. All changes are merged into `main` through PRs (CI green).
2. Tag `main` and push the tag:

   ```bash
   git switch main && git pull --ff-only
   git tag v1.3.0
   git push origin v1.3.0
   ```

3. `release.yml` runs on the tag:
   - fails if the tagged commit is not on `main`
   - restore → build → test → single-file publish (same composite action as CI)
   - checks the exe `ProductVersion` matches the tag
   - creates the GitHub Release with auto-generated notes and assets:
     - `MediaManager-v1.3.0-win-x64.zip` — exe, `Assets/`, `THIRD_PARTY_NOTICES.md`, `licenses/` (Sonarr.Parser GPL-3.0 license + attribution)
     - `SHA256SUMS.txt`

Pre-release: a tag with a hyphen (`v1.3.0-rc.1`) is published as a **pre-release**. `install-release.ps1` without `-Tag` ignores pre-releases (GitHub "latest" is stable only).

A failed release workflow leaves no Release. Fix on a branch → PR → merge, delete the bad tag (`git push origin :refs/tags/v1.3.0`, `git tag -d v1.3.0`) and tag again.

---

## Installing on the real machine

```powershell
& "D:\VScode\Misc\Media_Manager\media management app\media management app\scripts\install-release.ps1"            # latest stable
& "...\scripts\install-release.ps1" -Tag v1.3.0                                                                      # specific version
```

Default install folder: `D:\App-Disk-D\Media_Manager\App` (`-InstallDir` to change).

What it does:

1. Downloads the release zip + `SHA256SUMS.txt`, verifies the hash.
2. If the installed exe is running, asks you to **exit from the tray** and waits (120 s). `-Force` kills it instead (next start reports `UncleanShutdown`; avoid during Auto-Track/backup).
3. Renames the current exe to `media management app.exe.bak-<old version>` (date if unversioned); keeps the newest 3 backups.
4. Extracts the zip over the install folder (other files such as WebView2 data are untouched) and starts the app.

Do not copy `bin\…\publish` builds onto the real machine any more — install only tagged releases so every crash report maps to a tag.

## Rollback

```powershell
& "...\scripts\install-release.ps1" -Rollback        # newest .bak-* exe
& "...\scripts\install-release.ps1" -Tag v1.2.0      # or reinstall any older release
```

**Migrations:** the DB migrates forward on first start of a newer build. An older build may not read a newer schema. Before rolling back across a **minor** bump, restore a backup taken before the upgrade (Settings → Backup), or confirm the newer release added no migration (`MediaManager.Core/Migrations`).

---

## Debugging a crash from a release

- The crash report and `last-exit.json` carry the exact version + commit SHA → `git checkout <sha>` to get matching source.
- Release builds use `DebugType=embedded`: symbols live inside the assemblies in the exe, so exception stack traces include file:line and Visual Studio / WinDbg resolve dumps from `%LocalAppData%\CrashDumps` without separate `.pdb` files. (Loose `.pdb` files next to the exe are deleted at startup by `PublishJunkCleanup`.)
