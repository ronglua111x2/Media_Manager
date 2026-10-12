<#
.SYNOPSIS
  Register (or remove) the MediaManager\AutoUpdate scheduled task that keeps the installed app on the newest release.

.EXAMPLE
  .\register-auto-update.ps1                  # install updater + register task, then run it once
  .\register-auto-update.ps1 -Unregister      # remove the task (stay on the current version)

.NOTES
  Triggers: at logon (no delay, so a staged update is swapped in before MediaManager\AutoStart starts the app
  10 s later) and every hour (download/stage; apply only if the app is not running).
  The task runs <InstallDir>\updater\install-release.ps1 -Auto. Every release ships a fresh copy of that
  script, so the updater updates itself. Log: <InstallDir>\updater\update.log. See docs/RELEASING.md.
#>
param(
    [string]$InstallDir = 'D:\App-Disk-D\Media_Manager\App',
    [switch]$Unregister
)

$ErrorActionPreference = 'Stop'
$TaskPath = '\MediaManager\'
$TaskName = 'AutoUpdate'

if ($Unregister) {
    Unregister-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "Removed task $TaskPath$TaskName."
    return
}

$updaterDir = Join-Path $InstallDir 'updater'
$updater = Join-Path $updaterDir 'install-release.ps1'
New-Item -ItemType Directory -Force $updaterDir | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot 'install-release.ps1') -Destination $updater -Force

$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$updater`" -Auto -InstallDir `"$InstallDir`"" `
    -WorkingDirectory $updaterDir

$user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$atLogon = New-ScheduledTaskTrigger -AtLogOn -User $user
$hourly = New-ScheduledTaskTrigger -Once -At (Get-Date).Date.AddMinutes(5) -RepetitionInterval (New-TimeSpan -Hours 1)
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 30) -MultipleInstances IgnoreNew
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -Action $action -Trigger $atLogon, $hourly `
    -Settings $settings -Principal $principal `
    -Description 'Stages the newest Media Manager GitHub Release and swaps it in when the app is not running.' -Force | Out-Null

Write-Host "Registered $TaskPath$TaskName (at logon + hourly) -> $updater" -ForegroundColor Green
Start-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName
Write-Host "Started a first run. Log: $(Join-Path $updaterDir 'update.log')"
