# Build Guide — Media Manager (AI & developers)

Canonical build/test commands for this repo on **Windows x64**, verified with **.NET SDK 10.0.400** and **Visual Studio 2026 MSBuild 18.9** (Aug 2026).

## Project layout (no solution file)

**Before any sprint work:** confirm git branch (`auto-torrent`) — see [06-ai-execution-guide.md §2.0](planning/06-ai-execution-guide.md#20-mandatory-pre-sprint-workflow-git--plan-mode).

| Item | Path (relative to app root) |
|------|-----------------------------|
| **App root** | `d:\VScode\Misc\Media_Manager\media management app\media management app\` |
| **Main WPF project** | `media management app.csproj` |
| **Core library** | `MediaManager.Core\MediaManager.Core.csproj` |
| **Unit tests** | `MediaManager.Core.Tests\MediaManager.Core.Tests.csproj` |
| **Third-party parser** | `ThirdParty\Sonarr.Parser\MediaManager.Sonarr.Parser.csproj` |
| **Solution (`.sln`)** | *None* — build the main `.csproj` (it pulls in `ProjectReference`s). |

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
dotnet test "$TEST_PROJ" -c Release -v normal
```

### Publish (single-file self-contained)

Matches `.csproj` publish settings; slower than `build` — use for release packaging smoke, not every AI edit loop.

```bash
dotnet publish "$PROJ" -c Release -p:Platform=x64 -v minimal
```

Output: `bin\x64\Release\net8.0-windows10.0.17763.0\win-x64\publish\`

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

## Quick reference

| Task | Command |
|------|---------|
| Restore | `dotnet restore "$PROJ"` |
| **Merge gate build** | `dotnet build "$PROJ" -c Release -p:Platform=x64 -v minimal` |
| Debug build | `dotnet build "$PROJ" -c Debug -p:Platform=x64 -v minimal` |
| Publish | `dotnet publish "$PROJ" -c Release -p:Platform=x64 -v minimal` |
| Tests (future) | `dotnet test "<Tests.csproj>" -c Release -v normal` |
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
| `dotnet test` passes but no tests ran | Tests not added yet; tested WPF exe project | Point `dotnet test` at `*Tests.csproj` when it exists |
| NU1100 / restore errors | Offline or missing SDK | Install .NET 8 SDK + Windows desktop workload; run `dotnet restore` |
| WPF markup errors | XAML compile | Read `error MC` / `error CS` lines; rebuild after fixing XAML |

**Clean rebuild:**

```bash
dotnet clean "$PROJ" -c Release -p:Platform=x64
rm -rf "$APP_ROOT/obj" "$APP_ROOT/bin" "$APP_ROOT/ThirdParty/Sonarr.Parser/obj" "$APP_ROOT/ThirdParty/Sonarr.Parser/bin"
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
| `dotnet test` on WPF `.csproj` | OK exit, **no tests** |

