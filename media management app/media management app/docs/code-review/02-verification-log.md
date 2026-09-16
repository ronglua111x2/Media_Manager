# Verification Log

## Environment and baseline

- Date: 2026-09-16
- Branch: `auto-torrent`
- Commit: `4f4e970c2c8c33e57e797203ccdcc7781aae24df`
- Baseline working tree: clean before audit documents were added
- Application target: `net8.0-windows10.0.17763.0`
- Verification platform: Release, x64
- MSBuild:
  `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe`

No live state folder, media library, qBittorrent instance, Jellyfin instance, or
cloud backup was used.

## Dependency restore

Command:

```bash
dotnet restore "media management app.csproj"
```

Result: succeeded; all projects were already up to date.

Warnings:

- `NU1701`: `OpenTK 3.3.1` restored with .NET Framework assets.
- `NU1701`: `OpenTK.GLWpfControl 3.3.0` restored with .NET Framework assets.
- `NU1701`: `SkiaSharp.Views.WPF 3.119.0` restored with .NET Framework assets.

These are transitive dependencies of the WPF chart stack. They are recorded as
a compatibility risk, not a demonstrated runtime failure.

## Core tests

Command:

```bash
dotnet test "MediaManager.Core.Tests/MediaManager.Core.Tests.csproj" \
  -c Release -p:Platform=x64 -v normal --no-restore
```

Result:

- Passed: 207
- Failed: 0
- Skipped: 0
- Build warnings: 0
- Build errors: 0
- Test adapter: xUnit 2.5.3, 64-bit .NET 8.0.31

The output also displayed the Fluent Assertions 8 commercial-use licensing
notice. This is a licensing/compliance decision, not a test failure.

Existing documentation that says 79 tests pass is stale. The measured
baseline is 207.

## Application build

Command:

```bash
MSYS_NO_PATHCONV=1 \
  "/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/amd64/MSBuild.exe" \
  "media management app.csproj" \
  /restore:false /p:Configuration=Release /p:Platform=x64 /v:minimal /nologo
```

Result: succeeded.

Built outputs:

- `MediaManager.Core.dll`
- `MediaManager.Sonarr.Parser.dll`
- `media management app.dll`

Compiler/build errors: 0.  
First-party compiler warnings: 0.  
Package warnings: the same three `NU1701` compatibility warnings listed above.

## Static review checks

The review included repository-wide searches and caller tracing for:

- torrent add/delete entry points and validation gates;
- file and directory deletion, copy, move, hardlink, and symlink operations;
- shell and process launches;
- credential-bearing fields in logs;
- dynamic SQL construction and parameterization;
- `async void`, asynchronous timer handlers, fire-and-forget tasks, and
  dispatcher marshaling;
- empty catch blocks and intentionally swallowed cancellation/disposal errors;
- background service startup and shutdown;
- migration transactions and schema initialization;
- backup restore and active database lifetime;
- workspace navigation and singleton ViewModel lifecycle.

## AUD-002 follow-up verification

Command:

```bash
dotnet test "MediaManager.App.Tests/MediaManager.App.Tests.csproj" \
  -c Release -p:Platform=x64 -v minimal
```

Result: 9 passed, 0 failed.

Command:

```bash
dotnet test "MediaManager.Core.Tests/MediaManager.Core.Tests.csproj" \
  -c Release -p:Platform=x64 -v minimal --no-restore
```

Result: 207 passed, 0 failed.

64-bit MSBuild Release/x64 of `media management app.csproj` succeeded again
after the cleanup-failure change. Compiler/build errors: 0. First-party
compiler warnings: 0. Package warnings: the same three `NU1701` warnings.

Isolated live qBittorrent delete-failure injection was not run. The live state
folder was not used.

## Runtime verification limits

Destructive and integration scenarios were not run because safe verification
would require isolated qBittorrent, Jellyfin, Google Drive, WARP, media roots,
and copied state. Findings that depend on service timing or hostile external
responses are explicitly marked as statically proven behavior with runtime
impact not reproduced.
