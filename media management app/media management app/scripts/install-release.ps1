<#
.SYNOPSIS
  Install a Media Manager GitHub Release, roll back, or (with -Auto) stage/apply updates unattended.

.EXAMPLE
  .\install-release.ps1                    # latest stable release
  .\install-release.ps1 -Tag v1.2.0        # specific version (also used to roll forward/back)
  .\install-release.ps1 -Rollback          # restore the newest .bak-* exe
  .\install-release.ps1 -Auto              # used by the MediaManager\AutoUpdate scheduled task

.NOTES
  Interactive mode: the app runs in the tray and has no remote exit command, so the script asks you to
  exit it from the tray menu and waits. -Force kills the process instead (next start reports
  UncleanShutdown; SQLite writes in flight are lost).

  -Auto never touches a running app:
    1. download + verify the newest stable release into <InstallDir>\.staged\<tag> (works while the app runs)
    2. if the app is not running, swap the staged files in (rename + move, < 1 s)
  The scheduled task runs it at logon (before MediaManager\AutoStart's 10 s delay) and hourly.
  Log: <InstallDir>\updater\update.log. See docs/RELEASING.md.
#>
[CmdletBinding(DefaultParameterSetName = 'Install')]
param(
    [Parameter(ParameterSetName = 'Install')]
    [string]$Tag,

    [Parameter(ParameterSetName = 'Rollback', Mandatory)]
    [switch]$Rollback,

    [Parameter(ParameterSetName = 'Auto', Mandatory)]
    [switch]$Auto,

    [string]$InstallDir = 'D:\App-Disk-D\Media_Manager\App',
    [string]$Repo = 'ronglua111x2/Media_Manager',
    [int]$KeepBackups = 3,
    [int]$ExitTimeoutSeconds = 120,
    [switch]$Force,
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$ExeName = 'media management app.exe'
$ProcessName = [IO.Path]::GetFileNameWithoutExtension($ExeName)
$ExePath = Join-Path $InstallDir $ExeName
$StagedRoot = Join-Path $InstallDir '.staged'
$LogPath = Join-Path $InstallDir 'updater\update.log'
$Headers = @{ 'User-Agent' = 'MediaManager-installer'; 'Accept' = 'application/vnd.github+json' }

function Write-Log([string]$Message, [string]$Color = 'Gray') {
    Write-Host $Message -ForegroundColor $Color
    if ($Auto) {
        try {
            New-Item -ItemType Directory -Force (Split-Path $LogPath) | Out-Null
            Add-Content -Path $LogPath -Value "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $Message" -Encoding UTF8
        } catch { }
    }
}

function Get-ExeVersion([string]$Path) {
    if (-not (Test-Path $Path)) { return $null }
    $v = (Get-Item $Path).VersionInfo.ProductVersion
    if ([string]::IsNullOrWhiteSpace($v) -or $v -eq '1.0.0.0') { return $null }
    return $v
}

# Returns -1/0/1 comparing semver-ish strings ("1.2.0", "v1.2.1-alpha.0.3+sha"); $null sorts lowest.
function Compare-AppVersion([string]$A, [string]$B) {
    function Split-V([string]$v) {
        $v = ($v.TrimStart('v') -split '\+')[0]
        $parts = $v -split '-', 2
        [pscustomobject]@{ Core = [version]$parts[0]; Pre = if ($parts.Count -gt 1) { $parts[1] } else { $null } }
    }
    if (-not $A) { return $(if ($B) { -1 } else { 0 }) }
    if (-not $B) { return 1 }
    $x = Split-V $A; $y = Split-V $B
    $c = $x.Core.CompareTo($y.Core)
    if ($c -ne 0) { return [Math]::Sign($c) }
    if ($x.Pre -eq $y.Pre) { return 0 }
    if (-not $x.Pre) { return 1 }      # stable > pre-release of the same core
    if (-not $y.Pre) { return -1 }
    return [Math]::Sign([string]::CompareOrdinal($x.Pre, $y.Pre))
}

function Get-RunningApp {
    # Path is null when the app runs elevated (AutoStart uses /rl HIGHEST) and we do not: count it as running.
    Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
        Where-Object { -not $_.Path -or ([IO.Path]::GetFullPath($_.Path) -eq [IO.Path]::GetFullPath($ExePath)) }
}

function Wait-AppExit {
    $running = Get-RunningApp
    if (-not $running) { return }

    if ($Force) {
        Write-Warning "Killing pid $($running.Id -join ', ') (-Force). Next start will report UncleanShutdown."
        $running | Stop-Process -Force
        $running | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
        return
    }

    Write-Host "Media Manager is running (pid $($running.Id -join ', ')). Exit it from the tray menu (right-click > Exit)..." -ForegroundColor Yellow
    $deadline = (Get-Date).AddSeconds($ExitTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (-not ($running | Where-Object { -not $_.HasExited })) { Write-Host 'App exited.'; return }
        Start-Sleep -Milliseconds 500
        $running | ForEach-Object { $_.Refresh() }
    }
    throw "App still running after $ExitTimeoutSeconds s. Exit it from the tray and re-run, or pass -Force."
}

function Backup-CurrentExe {
    if (-not (Test-Path $ExePath)) { return }
    $old = Get-ExeVersion $ExePath
    $suffix = if ($old) { ($old -split '\+')[0] } else { Get-Date -Format 'yyyyMMdd-HHmmss' }
    $bak = "$ExePath.bak-$suffix"
    if (Test-Path $bak) { $bak = "$bak-$(Get-Date -Format 'HHmmss')" }
    Move-Item $ExePath $bak
    Write-Log "Backed up current exe -> $(Split-Path $bak -Leaf)"

    Get-ChildItem -Path $InstallDir -Filter "$ExeName.bak-*" |
        Sort-Object LastWriteTime -Descending |
        Select-Object -Skip $KeepBackups |
        ForEach-Object { Remove-Item $_.FullName; Write-Log "Pruned old backup $($_.Name)" }
}

# Moves every file from $Source into $InstallDir (same volume => rename, near-instant).
function Install-Files([string]$Source) {
    Backup-CurrentExe
    Get-ChildItem -Path $Source -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($Source.TrimEnd('\').Length + 1)
        $dest = Join-Path $InstallDir $rel
        New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
        Move-Item -Path $_.FullName -Destination $dest -Force
    }
}

function Start-App {
    if ($NoStart) { return }
    Start-Process -FilePath $ExePath -WorkingDirectory $InstallDir
    Write-Log 'Started Media Manager.'
}

function Get-Release([string]$ReleaseTag) {
    $api = if ($ReleaseTag) { "https://api.github.com/repos/$Repo/releases/tags/$ReleaseTag" } else { "https://api.github.com/repos/$Repo/releases/latest" }
    Invoke-RestMethod -Uri $api -Headers $Headers
}

# Downloads + verifies + extracts a release into $Destination. Writes $Destination\.complete when done.
function Save-Release($Release, [string]$Destination) {
    $zipAsset = $Release.assets | Where-Object { $_.name -like 'MediaManager-*-win-x64.zip' } | Select-Object -First 1
    $sumAsset = $Release.assets | Where-Object { $_.name -eq 'SHA256SUMS.txt' } | Select-Object -First 1
    if (-not $zipAsset -or -not $sumAsset) { throw "Release $($Release.tag_name) is missing the zip or SHA256SUMS.txt asset." }

    $work = "$Destination.download"
    foreach ($p in $work, $Destination) { if (Test-Path $p) { Remove-Item -Recurse -Force $p } }
    New-Item -ItemType Directory -Force $work | Out-Null
    $zipPath = Join-Path $work $zipAsset.name
    $sumPath = Join-Path $work $sumAsset.name
    Invoke-WebRequest -Uri $zipAsset.browser_download_url -OutFile $zipPath -Headers $Headers -UseBasicParsing
    Invoke-WebRequest -Uri $sumAsset.browser_download_url -OutFile $sumPath -Headers $Headers -UseBasicParsing

    $expected = (Get-Content $sumPath | Where-Object { $_ -match [regex]::Escape($zipAsset.name) } | Select-Object -First 1) -split '\s+' | Select-Object -First 1
    $actual = (Get-FileHash $zipPath -Algorithm SHA256).Hash
    if (-not $expected -or $actual -ne $expected.ToUpperInvariant()) { throw "SHA256 mismatch for $($zipAsset.name): expected $expected, got $actual" }
    Write-Log "SHA256 verified for $($zipAsset.name)."

    Expand-Archive -Path $zipPath -DestinationPath $Destination -Force
    Remove-Item -Recurse -Force $work
    if (-not (Test-Path (Join-Path $Destination $ExeName))) { throw "$ExeName not found in $($zipAsset.name)" }
    Set-Content -Path (Join-Path $Destination '.complete') -Value $Release.tag_name
}

if (-not (Test-Path $InstallDir)) { New-Item -ItemType Directory -Force $InstallDir | Out-Null }

# ---------------------------------------------------------------- Rollback
if ($Rollback) {
    $bak = Get-ChildItem -Path $InstallDir -Filter "$ExeName.bak-*" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $bak) { throw "No $ExeName.bak-* found in $InstallDir" }
    Write-Host "Rolling back to $($bak.Name)" -ForegroundColor Cyan
    Write-Warning 'If the newer version ran a DB migration, restore a matching backup first (docs/RELEASING.md).'
    Write-Warning 'The AutoUpdate task will re-install the newest release; disable it first if you want to stay on this version.'
    Wait-AppExit
    $current = "$ExePath.rolledback-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    if (Test-Path $ExePath) { Move-Item $ExePath $current }
    Move-Item $bak.FullName $ExePath
    Write-Host "Restored $(Get-ExeVersion $ExePath). Previous exe kept as $(Split-Path $current -Leaf)."
    Start-App
    return
}

# ---------------------------------------------------------------- Auto (scheduled task)
if ($Auto) {
    $mutex = New-Object System.Threading.Mutex($false, 'Local\MediaManager.Updater')
    if (-not $mutex.WaitOne(0)) { return }   # another updater run is in progress
    try {
        $installed = Get-ExeVersion $ExePath

        # 1. Apply an already staged release first (no network needed; fast enough to beat AutoStart at logon).
        $staged = Get-ChildItem -Path $StagedRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { Test-Path (Join-Path $_.FullName '.complete') } |
            Sort-Object { $_.Name } -Descending | Select-Object -First 1
        if ($staged -and (Compare-AppVersion $staged.Name $installed) -gt 0) {
            if (Get-RunningApp) {
                Write-Log "Staged $($staged.Name); app is running, will apply when it is not."
            } else {
                Remove-Item (Join-Path $staged.FullName '.complete')
                Install-Files $staged.FullName
                Remove-Item -Recurse -Force $staged.FullName
                $installed = Get-ExeVersion $ExePath
                Write-Log "Applied staged update -> $installed" 'Green'
            }
        }

        # 2. Look for a newer stable release and stage it.
        try { $release = Get-Release $null } catch { Write-Log "Release check failed: $($_.Exception.Message)" 'Yellow'; return }
        if ((Compare-AppVersion $release.tag_name $installed) -le 0) { return }

        $dest = Join-Path $StagedRoot $release.tag_name
        if (-not (Test-Path (Join-Path $dest '.complete'))) {
            Write-Log "New release $($release.tag_name) (installed: $(if ($installed) { $installed } else { 'unversioned' })). Staging..."
            Save-Release $release $dest
            Get-ChildItem -Path $StagedRoot -Directory | Where-Object { $_.FullName -ne $dest } | Remove-Item -Recurse -Force
        }

        # 3. Apply now if the app is not running; otherwise the next run (logon / hourly) applies it.
        if (Get-RunningApp) {
            Write-Log "Staged $($release.tag_name); app is running, will apply when it is not."
        } else {
            Remove-Item (Join-Path $dest '.complete')
            Install-Files $dest
            Remove-Item -Recurse -Force $dest
            Write-Log "Installed $(Get-ExeVersion $ExePath)" 'Green'
        }
    } catch {
        Write-Log "Auto update failed: $($_.Exception.Message)" 'Red'
        exit 1
    } finally {
        $mutex.ReleaseMutex()
    }
    return
}

# ---------------------------------------------------------------- Interactive install
$release = Get-Release $Tag
$current = Get-ExeVersion $ExePath
Write-Host "Installing $($release.tag_name) into $InstallDir (current: $(if ($current) { $current } else { 'none/unversioned' }))" -ForegroundColor Cyan

$dest = Join-Path $StagedRoot $release.tag_name
Save-Release $release $dest
Remove-Item (Join-Path $dest '.complete')
Wait-AppExit
Install-Files $dest
Remove-Item -Recurse -Force $dest

Write-Host "Installed $(Get-ExeVersion $ExePath)" -ForegroundColor Green
Start-App
