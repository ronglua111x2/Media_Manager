#Requires -RunAsAdministrator
# Remove the Media Manager LocalDumps registry key.
$ErrorActionPreference = 'Stop'
$exeName = 'media management app.exe'
$key = "HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\$exeName"
if (Test-Path $key) {
    Remove-Item -Path $key -Recurse -Force
    Write-Host "LocalDumps disabled for $exeName"
} else {
    Write-Host "LocalDumps was not set for $exeName"
}
