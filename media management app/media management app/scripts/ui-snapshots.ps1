<#
.SYNOPSIS
  Capture PNG snapshots of every main Media Manager screen at 1280x720 (dev tooling).

.DESCRIPTION
  1. Builds Debug (exe lands under bin\...\net8.0-*, the "dev build" path that never touches the
     Windows AutoStart task).
  2. Refreshes a sandbox copy of the state folder. The live folder is only READ (allow-list of files,
     opened with shared read access); nothing is written there.
  3. Runs the app with --state-folder <sandbox> --ui-snapshots <out>. The app cuts off qBittorrent,
     Jellyfin, WARP, Gemini, TMDB, Drive backup, Auto-Track and symlink sync in the
     sandbox settings, visits every workspace, Settings section, the console and a few dialogs, writes
     PNGs + index.md and exits. It runs beside the live app (separate mutex).
  4. Output: <repo root>\ui-snapshots\<yyyy-MM-dd_HHmm>_<shortsha>\ and a mirror in ui-snapshots\latest\
     (latest\clutter-inventory.md, if present, is kept).

  ui-snapshots\ is git-ignored: the images show real library titles. Never commit or upload them.
  See docs/UI_SNAPSHOTS.md.

.EXAMPLE
  .\scripts\ui-snapshots.ps1
  .\scripts\ui-snapshots.ps1 -SkipBuild -NoRefresh
  .\scripts\ui-snapshots.ps1 -SettleMs 4000      # slower machine: wait longer per screen
#>
[CmdletBinding()]
param(
    [string]$SourceState = 'D:\MediaManagerState',
    [string]$SandboxState = (Join-Path $env:LOCALAPPDATA 'MediaManagerUiSnapshot\state'),
    [string]$OutputRoot,
    [switch]$SkipBuild,
    [switch]$NoRefresh,
    [int]$SettleMs = 2500,
    [int]$TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'

$ProjectDir = Split-Path -Parent $PSScriptRoot
$Csproj = Join-Path $ProjectDir 'media management app.csproj'
$SandboxMarker = '.ui-snapshot-sandbox'

function Get-FullPath([string]$Path) {
    return [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
}

function Test-IsSameOrInside([string]$Path, [string]$Root) {
    $p = Get-FullPath $Path
    $r = Get-FullPath $Root
    return $p.Equals($r, [StringComparison]::OrdinalIgnoreCase) -or
        $p.StartsWith($r + '\', [StringComparison]::OrdinalIgnoreCase)
}

function Copy-SharedRead([string]$From, [string]$To) {
    # The live app may hold the file open (SQLite); open with shared read/write so the copy never blocks it.
    $src = [IO.File]::Open($From, [IO.FileMode]::Open, [IO.FileAccess]::Read,
        [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try {
        $dst = [IO.File]::Open($To, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $src.CopyTo($dst) } finally { $dst.Dispose() }
    }
    finally { $src.Dispose() }
}

#region Paths and guards
$RepoRoot = (& git -C $ProjectDir rev-parse --show-toplevel).Trim()
if (-not $RepoRoot) { throw 'Not inside a git checkout.' }
$RepoRoot = Get-FullPath $RepoRoot
if (-not $OutputRoot) { $OutputRoot = Join-Path $RepoRoot 'ui-snapshots' }

$liveFolders = @($SourceState, 'D:\MediaManagerState', (Join-Path $env:LOCALAPPDATA 'MediaManager\State'))
$pointer = Join-Path $env:LOCALAPPDATA 'MediaManager\state-path.txt'
if (Test-Path -LiteralPath $pointer) {
    $pointed = (Get-Content -LiteralPath $pointer -Raw).Trim()
    if ($pointed) { $liveFolders += $pointed }
}
foreach ($live in $liveFolders) {
    if ((Test-IsSameOrInside $SandboxState $live) -or (Test-IsSameOrInside $live $SandboxState)) {
        throw "Sandbox '$SandboxState' overlaps the live state folder '$live'. Pick another -SandboxState."
    }
}
#endregion

#region Build
Push-Location $ProjectDir
try {
    if (-not $SkipBuild) {
        Write-Host 'Building Debug...'
        & dotnet build $Csproj -c Debug -nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)." }
    }
    $targetPath = (& dotnet msbuild $Csproj -nologo -getProperty:TargetPath -p:Configuration=Debug).Trim()
}
finally { Pop-Location }

$Exe = [IO.Path]::ChangeExtension($targetPath, '.exe')
if (-not (Test-Path -LiteralPath $Exe)) { throw "Built exe not found: $Exe" }
$segments = $Exe -split '[\\/]'
$binIndex = [Array]::FindIndex($segments, [Predicate[string]] { param($s) $s -ieq 'bin' })
$isDevPath = $binIndex -ge 0 -and @($segments | Select-Object -Skip ($binIndex + 1) | Where-Object { $_ -like 'net*' }).Count -gt 0
if (-not $isDevPath) {
    throw "Refusing to run '$Exe': sandbox exes must live under bin\...\net*, or Settings Save can repoint the AutoStart task."
}
#endregion

#region Sandbox state (read-only copy of the live folder)
if (-not $NoRefresh -or -not (Test-Path -LiteralPath $SandboxState)) {
    if (-not (Test-Path -LiteralPath $SourceState)) { throw "Source state folder not found: $SourceState" }
    if (Test-Path -LiteralPath $SandboxState) {
        if (-not (Test-Path -LiteralPath (Join-Path $SandboxState $SandboxMarker))) {
            throw "'$SandboxState' exists but is not a snapshot sandbox (no $SandboxMarker). Refusing to wipe it."
        }
        Remove-Item -LiteralPath $SandboxState -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $SandboxState | Out-Null
    Set-Content -LiteralPath (Join-Path $SandboxState $SandboxMarker) -Value 'UI snapshot sandbox; safe to delete.' -Encoding ascii

    Write-Host "Copying state (read-only) into the sandbox..."
    $files = @('settings.json', 'media-manager.db', 'media-manager.db-wal', 'media-manager.db-shm',
        'gemini-models.json', 'gemini-quota.json', 'gemini-mapping-cache.json', 'imdb-ids.json', 'indexer-state.json')
    foreach ($name in $files) {
        $from = Join-Path $SourceState $name
        if (Test-Path -LiteralPath $from) { Copy-SharedRead $from (Join-Path $SandboxState $name) }
    }

    foreach ($dir in @('Recipes', 'posters')) {
        $from = Join-Path $SourceState $dir
        if (-not (Test-Path -LiteralPath $from)) { continue }
        & robocopy $from (Join-Path $SandboxState $dir) /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy '$dir' failed ($LASTEXITCODE)." }
    }
}
#endregion

#region Run
$sha = (& git -C $RepoRoot rev-parse --short HEAD).Trim()
$branch = (& git -C $RepoRoot rev-parse --abbrev-ref HEAD).Trim()
$dirty = if (& git -C $RepoRoot status --porcelain --untracked-files=no) { ' (uncommitted changes)' } else { '' }
$stamp = Get-Date -Format 'yyyy-MM-dd_HHmm'
$RunDir = Join-Path $OutputRoot "${stamp}_$sha"
$LatestDir = Join-Path $OutputRoot 'latest'
if (Test-Path -LiteralPath $RunDir) { Remove-Item -LiteralPath $RunDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $RunDir | Out-Null

# The exe is requireAdministrator, so it starts through ShellExecute (UAC) and does not inherit this
# shell's environment: run details go in a request file the app reads.
$request = @{ commit = "$sha on $branch$dirty"; settleMs = $SettleMs } | ConvertTo-Json -Compress
[IO.File]::WriteAllText((Join-Path $RunDir 'snapshot-request.json'), $request)

Write-Host "Running snapshot mode: $RunDir"
$arguments = "--state-folder `"$SandboxState`" --ui-snapshots `"$RunDir`""
$process = Start-Process -FilePath $Exe -ArgumentList $arguments -WorkingDirectory (Split-Path -Parent $Exe) -PassThru
$null = $process.Handle  # keeps ExitCode readable after exit (Windows PowerShell quirk)
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    throw "Snapshot run timed out after $TimeoutSeconds s and was killed. Partial output: $RunDir"
}
$exitCode = $process.ExitCode
#endregion

#region Report
$errorFile = Join-Path $RunDir 'error.txt'
if (Test-Path -LiteralPath $errorFile) {
    Write-Warning ("Snapshot run error: " + (Get-Content -LiteralPath $errorFile -Raw))
}
$pngs = @(Get-ChildItem -LiteralPath $RunDir -Filter '*.png' -ErrorAction SilentlyContinue)
if ($pngs.Count -eq 0) { throw "No images were written (exit code $exitCode). Check $SandboxState\logs." }

& robocopy $RunDir $LatestDir /MIR /XF clutter-inventory.md /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy to latest failed ($LASTEXITCODE)." }

& git -C $RepoRoot check-ignore -q $RunDir
if ($LASTEXITCODE -ne 0) {
    Write-Warning "$OutputRoot is NOT git-ignored. Do not commit these images (they show real titles)."
}

Write-Host ''
Write-Host "Snapshots: $($pngs.Count) images, exit code $exitCode."
Write-Host "  $RunDir"
Write-Host "  $LatestDir"
if ($exitCode -ne 0) {
    Write-Warning 'Some screens failed; see the Failed list in index.md.'
    exit $exitCode
}
#endregion
