<#
.SYNOPSIS
  Install a Media Manager GitHub Release (or roll back) into the local install folder.

.EXAMPLE
  .\install-release.ps1                    # latest stable release
  .\install-release.ps1 -Tag v1.2.0        # specific version (also used to roll forward/back)
  .\install-release.ps1 -Rollback          # restore the newest .bak-* exe
  .\install-release.ps1 -InstallDir D:\Temp\MM-test -NoStart

.NOTES
  The app runs in the tray (close-to-tray) and has no remote exit command, so the script asks you to
  exit it from the tray menu and waits. -Force kills the process instead; the next start then reports
  an UncleanShutdown and SQLite writes in flight are lost.
  See docs/RELEASING.md.
#>
[CmdletBinding(DefaultParameterSetName = 'Install')]
param(
    [Parameter(ParameterSetName = 'Install')]
    [string]$Tag,

    [Parameter(ParameterSetName = 'Rollback', Mandatory)]
    [switch]$Rollback,

    [string]$InstallDir = 'D:\App-Disk-D\Media_Manager\App',
    [string]$Repo = 'ronglua111x2/Media_Manager',
    [int]$KeepBackups = 3,
    [int]$ExitTimeoutSeconds = 120,
    [switch]$Force,
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$ExeName = 'media management app.exe'
$ProcessName = [IO.Path]::GetFileNameWithoutExtension($ExeName)
$ExePath = Join-Path $InstallDir $ExeName

function Get-ExeVersion([string]$Path) {
    if (-not (Test-Path $Path)) { return $null }
    $v = (Get-Item $Path).VersionInfo.ProductVersion
    if ([string]::IsNullOrWhiteSpace($v) -or $v -eq '1.0.0.0') { return $null }
    return $v
}

function Wait-AppExit {
    $running = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and ([IO.Path]::GetFullPath($_.Path) -eq [IO.Path]::GetFullPath($ExePath)) }
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
    Write-Host "Backed up current exe -> $(Split-Path $bak -Leaf)"

    Get-ChildItem -Path $InstallDir -Filter "$ExeName.bak-*" |
        Sort-Object LastWriteTime -Descending |
        Select-Object -Skip $KeepBackups |
        ForEach-Object { Remove-Item $_.FullName; Write-Host "Pruned old backup $($_.Name)" }
}

function Start-App {
    if ($NoStart) { return }
    Start-Process -FilePath $ExePath -WorkingDirectory $InstallDir
    Write-Host 'Started Media Manager.'
}

if (-not (Test-Path $InstallDir)) { New-Item -ItemType Directory -Force $InstallDir | Out-Null }

if ($Rollback) {
    $bak = Get-ChildItem -Path $InstallDir -Filter "$ExeName.bak-*" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $bak) { throw "No $ExeName.bak-* found in $InstallDir" }
    Write-Host "Rolling back to $($bak.Name)" -ForegroundColor Cyan
    Write-Warning 'If the newer version ran a DB migration, restore a matching backup first (docs/RELEASING.md).'
    Wait-AppExit
    $current = "$ExePath.rolledback-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    if (Test-Path $ExePath) { Move-Item $ExePath $current }
    Move-Item $bak.FullName $ExePath
    Write-Host "Restored $(Get-ExeVersion $ExePath). Previous exe kept as $(Split-Path $current -Leaf)."
    Start-App
    return
}

# Resolve release
$headers = @{ 'User-Agent' = 'MediaManager-installer'; 'Accept' = 'application/vnd.github+json' }
$api = if ($Tag) { "https://api.github.com/repos/$Repo/releases/tags/$Tag" } else { "https://api.github.com/repos/$Repo/releases/latest" }
$release = Invoke-RestMethod -Uri $api -Headers $headers
$Tag = $release.tag_name
$zipAsset = $release.assets | Where-Object { $_.name -like 'MediaManager-*-win-x64.zip' } | Select-Object -First 1
$sumAsset = $release.assets | Where-Object { $_.name -eq 'SHA256SUMS.txt' } | Select-Object -First 1
if (-not $zipAsset -or -not $sumAsset) { throw "Release $Tag is missing the zip or SHA256SUMS.txt asset." }

$current = Get-ExeVersion $ExePath
Write-Host "Installing $Tag into $InstallDir (current: $(if ($current) { $current } else { 'none/unversioned' }))" -ForegroundColor Cyan

# Download + verify
$work = Join-Path $env:TEMP "MediaManager-install-$Tag"
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $work | Out-Null
$zipPath = Join-Path $work $zipAsset.name
$sumPath = Join-Path $work $sumAsset.name
Invoke-WebRequest -Uri $zipAsset.browser_download_url -OutFile $zipPath -Headers $headers -UseBasicParsing
Invoke-WebRequest -Uri $sumAsset.browser_download_url -OutFile $sumPath -Headers $headers -UseBasicParsing

$expected = (Get-Content $sumPath | Where-Object { $_ -match [regex]::Escape($zipAsset.name) } | Select-Object -First 1) -split '\s+' | Select-Object -First 1
$actual = (Get-FileHash $zipPath -Algorithm SHA256).Hash
if (-not $expected -or $actual -ne $expected.ToUpperInvariant()) { throw "SHA256 mismatch for $($zipAsset.name): expected $expected, got $actual" }
Write-Host 'SHA256 verified.'

$extract = Join-Path $work 'extract'
Expand-Archive -Path $zipPath -DestinationPath $extract -Force
if (-not (Test-Path (Join-Path $extract $ExeName))) { throw "$ExeName not found in $($zipAsset.name)" }

# Swap
Wait-AppExit
Backup-CurrentExe
Copy-Item -Path (Join-Path $extract '*') -Destination $InstallDir -Recurse -Force
Remove-Item -Recurse -Force $work

Write-Host "Installed $(Get-ExeVersion $ExePath)" -ForegroundColor Green
Start-App
