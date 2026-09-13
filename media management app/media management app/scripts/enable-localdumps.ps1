#Requires -RunAsAdministrator
# Enable Windows Error Reporting mini dumps for Media Manager.
# Dumps go to the default folder: %LOCALAPPDATA%\CrashDumps
# Filename: media management app.exe.<pid>.dmp
$ErrorActionPreference = 'Stop'
$exeName = 'media management app.exe'
$key = "HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\$exeName"
New-Item -Path $key -Force | Out-Null
New-ItemProperty -Path $key -Name DumpType -PropertyType DWord -Value 1 -Force | Out-Null
New-ItemProperty -Path $key -Name DumpCount -PropertyType DWord -Value 5 -Force | Out-Null
Remove-ItemProperty -Path $key -Name DumpFolder -ErrorAction SilentlyContinue
Write-Host "LocalDumps enabled for $exeName"
Write-Host "DumpType=1 (mini), DumpCount=5, folder=%LOCALAPPDATA%\CrashDumps"
