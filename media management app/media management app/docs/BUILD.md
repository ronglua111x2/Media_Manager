# Build Guide — Media Manager (AI & developers)

Canonical build/test commands for this repo on **Windows x64**, verified with **.NET SDK 10.0.400** and **Visual Studio 2026 MSBuild 18.9** (Aug 2026).

## Project layout

**Before any work:** branch from up-to-date `main` and land changes through a PR with green CI — see [CI](#ci-github-actions) and [06-ai-execution-guide.md §2.0](planning/06-ai-execution-guide.md#20-mandatory-pre-sprint-workflow-git--plan-mode).

| Item | Path (relative to app root) |
|------|-----------------------------|
| **App root** | `d:\VScode\Misc\Media_Manager\media management app\media management app\` |
| **Main WPF project** | `media management app.csproj` |
| **Core library** | `MediaManager.Core\MediaManager.Core.csproj` |
| **Unit tests** | `MediaManager.Core.Tests\MediaManager.Core.Tests.csproj` |
| **App tests** | `MediaManager.App.Tests\MediaManager.App.Tests.csproj` (Windows cleanup/halt) |
| **Third-party parser** | `ThirdParty\Sonarr.Parser\MediaManager.Sonarr.Parser.csproj` |
| **Solution (`.slnx`)** | `..\media management app.slnx` (parent folder — WPF + Core + both test projects; used by CI) |
| **SDK pin** | `global.json` at the git root (`D:\VScode\Misc\Media_Manager`) — SDK 10.0.4xx (`rollForward: latestFeature`) |

### Target framework & platform

| Setting | Value |
|---------|--------|
| TFM (app) | `net8.0-windows10.0.17763.0` (WPF) |
| TFM (Sonarr.Parser) | `net8.0` |
| **Platforms** | `AnyCPU`, **`x64`** (prefer **`x64`** for merge gates) |
| **RID** | `win-x64` (set in main `.csproj`; output is always under `win-x64\`) |
| Publish | `PublishSingleFile` + `SelfContained` |

### Output directories (after build)

| Config | Typical output |
|--------|----------------|
| **Release \| x64** | `bin\x64\Release\net8.0-windows10.0.17763.0\win-x64\` |
| **Debug \| x64** | `bin\x64\Debug\net8.0-windows10.0.17763.0\win-x64\` |
| **Publish (Release \| x64)** | `bin\x64\Release\net8.0-windows10.0.17763.0\win-x64\publish\` |

---

## Recommended commands (copy-paste)

Set the app root once (Git Bash / WSL-style shell on Windows):

```bash
APP_ROOT="d:/VScode/Misc/Media_Manager/media management app/media management app"
PROJ="$APP_ROOT/media management app.csproj"
cd "$APP_ROOT"
```

### Primary build (AI default) — `dotnet build`

Works reliably in Git Bash, restores NuGet packages, and builds `ThirdParty/Sonarr.Parser` via `ProjectReference`.

**Release merge gate (use before every sprint merge):**

```bash
dotnet restore "$PROJ"
dotnet build "$PROJ" -c Release -p:Platform=x64 -v minimal --no-restore
```

**Debug (local iteration):**

```bash
dotnet build "$PROJ" -c Debug -p:Platform=x64 -v minimal
```

**One-liner wrapper (optional):**

```bash
./scripts/build.sh Release x64
```

### Tests — `dotnet test`

```bash
TEST_PROJ="$APP_ROOT/MediaManager.Core.Tests/MediaManager.Core.Tests.csproj"
APP_TEST_PROJ="$APP_ROOT/MediaManager.App.Tests/MediaManager.App.Tests.csproj"
dotnet test "$TEST_PROJ" -c Release -p:Platform=x64 -v normal
# Expected (2026-10-12): 248 passed; Core.Tests references Core only (no WPF).
dotnet test "$APP_TEST_PROJ" -c Release -p:Platform=x64 -v minimal
# Expected (2026-10-25): 53 passed.
```

### Publish (single-file self-contained)

Matches `.csproj` publish settings; slower than `build` — use for release packaging smoke, not every AI edit loop.

```bash
dotnet publish "$PROJ" -c Release -p:Platform=x64 -v minimal
```

Output: `bin\x64\Release\net8.0-windows10.0.17763.0\win-x64\publish\`

Publish output is a single `media management app.exe` (plus `Assets\`). Native DLLs are self-extracted to `%TEMP%\.net\media management app\<hash>`; `BundleExtractionGuard` holds every extracted DLL open for the session so Storage Sense / cleanmgr cannot delete lazily loaded ones (`PenImc_cor3.dll` was deleted mid-session on 2026-10-23 and the app crashed with `DllNotFoundException`).

---

## CI (GitHub Actions)

Workflow: `.github/workflows/ci.yml` at the git root. Runs on every PR into `main`, every push to `main`, and manually (`workflow_dispatch`). Runner: `windows-latest` (WPF + App.Tests need Windows).

Steps (identical to the merge gate above, but via the solution):

```bash
SLN="media management app/media management app.slnx"   # from git root
dotnet restore "$SLN"
dotnet build "$SLN" -c Release -p:Platform=x64 --no-restore
dotnet test  "$SLN" -c Release -p:Platform=x64 --no-build --logger trx
dotnet publish "media management app/media management app/media management app.csproj" -c Release -p:Platform=x64 -o out/publish
```

| Output | Where |
|--------|-------|
| Pass/fail | PR **Checks** tab — required check `build-test` (branch protection on `main`) |
| Test results (`.trx`) | Run page → Artifacts → `test-results-<sha>` |
| Publish build of the PR | Run page → Artifacts → `MediaManager-pr<N>-<sha>` (kept 7 days) — use it to try a PR build before merging |

Run the same commands locally before pushing. If the app is running from `bin\x64\Release\…`, the build fails with `MSB3027` (exe locked) — exit the app from the tray first.

**Daily flow:** `git switch main && git pull --ff-only` → `git switch -c feat/<name>` → commit → `git push -u origin HEAD` → `gh pr create` (add label `release:minor` / `release:major` / `release:skip` if the default patch release is wrong) → squash-merge when `build-test` is green → the merge becomes a GitHub Release automatically; install it with `scripts/install-release.ps1` when you want it ([RELEASING.md](./RELEASING.md#continuous-delivery-automatic)).

---

## MSBuild x64 (Visual Studio)

Use when you need **full MSBuild** behavior (file loggers, binlog, VS-aligned builds).

**64-bit MSBuild path (this machine):**

`C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe`

**Git Bash requirement:** MSYS converts `/p:...` into a Windows path. Set `MSYS_NO_PATHCONV=1` or use `dotnet msbuild` instead.

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/amd64/MSBuild.exe"
MSYS_NO_PATHCONV=1 "$MSBUILD" "$PROJ" /restore:false /p:Configuration=Release /p:Platform=x64 /v:minimal /nologo
```

**Equivalent (no Git Bash `/p:` pitfall):**

```bash
dotnet msbuild "$PROJ" -p:Configuration=Release -p:Platform=x64 -restore:false -v:minimal
```

---

## AI-readable logs

| Goal | Command |
|------|---------|
| Short console, errors still visible | `-v minimal` (default for AI) |
| More MSBuild target detail | `-v normal` or `dotnet msbuild ... -v:normal` |
| Save full text log | `dotnet build ... -v:normal 2>&1 \| tee build.log` |
| MSBuild file logger | `MSYS_NO_PATHCONV=1 "$MSBUILD" "$PROJ" /fl "/flp:logfile=$APP_ROOT/build.log;verbosity=normal"` |
| Binary log (open in Visual Studio) | `dotnet msbuild "$PROJ" -bl:"$APP_ROOT/artifacts/build.binlog"` |
| Grep failures only | `grep -E "error (CS|MSB|NETSDK)" build.log` |

Console logger (errors/warnings only):

```bash
dotnet build "$PROJ" -c Release -p:Platform=x64 \
  --consoleLoggerParameters:Summary\;ErrorsOnly\;WarningsOnly
```

---

## External dependencies

### NuGet

All package references are in `media management app.csproj`. Restore is automatic on `dotnet build` / `dotnet publish`, or explicit:

```bash
dotnet restore "$PROJ"
```

### ThirdParty — Sonarr.Parser

- **Reference:** `ProjectReference` to `ThirdParty\Sonarr.Parser\MediaManager.Sonarr.Parser.csproj`
- **Excluded from default compile globs** (`DefaultItemExcludes` + `Compile Remove`) so parser sources are not compiled twice
- **Build order:** MSBuild builds Sonarr.Parser first, then the WPF app
- **Warnings:** Nullable warnings (`CS86xx`, `CS8618`) in ported GPL code are expected; **0 errors** in a healthy build
- **License:** GPL-3.0 — see `ThirdParty/Sonarr.Parser/LICENSE.md` and `ATTRIBUTION.md`

No separate restore step is required for ThirdParty beyond building the main project.

### Publish profiles

`Properties/PublishProfiles/FolderProfile*.pubxml` exist but use legacy `Any CPU` platform in profile metadata. Prefer explicit CLI:

`dotnet publish -c Release -p:Platform=x64`

---

## Versioning & symbols

- Version is stamped from the nearest `vX.Y.Z` git tag by **MinVer** (`Directory.Build.props`). Local/PR builds look like `1.2.1-alpha.0.3+<sha>`; only tagged builds are plain `1.2.0`. Full rules: [RELEASING.md](./RELEASING.md).
- A shallow clone or a source copy without `.git` builds as `0.0.0-alpha.0` / `1.0.0-alpha.0` — expected, not an error.
- Release builds use `DebugType=embedded` (no `.pdb` files in output; symbols are inside the assemblies).
- Code reads the version through `Common/AppVersionInfo.Informational`.

---

## Quick reference

| Task | Command |
|------|---------|
| Restore | `dotnet restore "$PROJ"` |
| **Merge gate build** | `dotnet build "$PROJ" -c Release -p:Platform=x64 -v minimal` |
| Debug build | `dotnet build "$PROJ" -c Debug -p:Platform=x64 -v minimal` |
| Publish | `dotnet publish "$PROJ" -c Release -p:Platform=x64 -v minimal` |
| Tests (Core) | `dotnet test "$APP_ROOT/MediaManager.Core.Tests/MediaManager.Core.Tests.csproj" -c Release -p:Platform=x64` |
| Tests (App cleanup) | `dotnet test "$APP_ROOT/MediaManager.App.Tests/MediaManager.App.Tests.csproj" -c Release -p:Platform=x64` |
| MSBuild x64 (VS) | `MSYS_NO_PATHCONV=1 amd64\MSBuild.exe "$PROJ" /p:Configuration=Release /p:Platform=x64` |
| Script | `./scripts/build.sh [Release\|Debug] [x64]` |

---

## Common failures & fixes

| Symptom | Likely cause | Fix |
|---------|----------------|-----|
| `MSB1008: Only one project can be specified` with `p:Configuration=...` | Git Bash ate `/p:` as a path | Use `MSYS_NO_PATHCONV=1`, or `dotnet build` / `dotnet msbuild` |
| `CS0579` duplicate assembly attributes | WPF `_wpftmp` project | Already handled in `.csproj` via `_wpftmp` `PropertyGroup`; clean `obj/` if stale |
| Sonarr.Parser not found | Missing `ThirdParty/Sonarr.Parser` | Ensure submodule/folder present; path must match `ProjectReference` |
| Wrong output folder (no `x64`) | Built with `Platform=AnyCPU` | Pass `-p:Platform=x64` for sprint/CI parity |
| `dotnet test` passes but no tests ran | Ran against the WPF `.csproj` | Use `MediaManager.Core.Tests` or `MediaManager.App.Tests` |
| NU1100 / restore errors | Offline or missing SDK | Install .NET 8 SDK + Windows desktop workload; run `dotnet restore` |
| WPF markup errors | XAML compile | Read `error MC` / `error CS` lines; rebuild after fixing XAML |
| `CS0234` / `CS0246` — `media_management_app.Migrations` not found | VS rebuilt WPF only; `MediaManager.Core` stale or wrong config (Sprint 2+) | Close VS → **Rebuild Solution** (not single project). Confirm **Release \| x64** for **all** projects in Configuration Manager. Ensure Solution Explorer lists `MediaManager.Core`. Clean `bin/` + `obj/` under app + Core (see below). Or build from terminal: `dotnet build "media management app/media management app.csproj" -c Release -p:Platform=x64` |

**Clean rebuild:**

```bash
dotnet clean "$PROJ" -c Release -p:Platform=x64
rm -rf "$APP_ROOT/obj" "$APP_ROOT/bin" "$APP_ROOT/ThirdParty/Sonarr.Parser/obj" "$APP_ROOT/ThirdParty/Sonarr.Parser/bin"
rm -rf "$APP_ROOT/MediaManager.Core/obj" "$APP_ROOT/MediaManager.Core/bin"
rm -rf "$APP_ROOT/MediaManager.Core.Tests/obj" "$APP_ROOT/MediaManager.Core.Tests/bin"
rm -rf "$APP_ROOT/MediaManager.App.Tests/obj" "$APP_ROOT/MediaManager.App.Tests/bin"
dotnet build "$PROJ" -c Release -p:Platform=x64
```

---

## Verification log (Aug 2026)

Commands run on this repo (Windows, Git Bash):

| Command | Result |
|---------|--------|
| `dotnet restore` | OK |
| `dotnet build -c Debug -p:Platform=AnyCPU` | OK (Sonarr warnings only) |
| `dotnet build -c Debug -p:Platform=x64` | OK |
| `dotnet build -c Release -p:Platform=x64` | OK |
| `dotnet msbuild -c Release -p:Platform=x64` | OK |
| `MSBuild.exe` (amd64) **without** `MSYS_NO_PATHCONV` | **FAIL** (MSB1008) |
| `MSBuild.exe` (amd64) **with** `MSYS_NO_PATHCONV=1` | OK |
| `dotnet publish -c Release -p:Platform=x64` | OK |
| `dotnet test` on WPF `.csproj` | OK exit, **no tests** (tests live in Core.Tests / App.Tests) |
| `dotnet test` Core.Tests Release/x64 | **248 passed** (2026-10-12, clean clone) |
| `dotnet test` App.Tests Release/x64 | **82 passed** (2026-10-26) |

