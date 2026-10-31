<#
.SYNOPSIS
  Downloads Process Monitor (Sysinternals) if needed, captures a system trace while you click
  "Remote dieu khien" on the web UI, then exports it to CSV for analysis. Run in an Administrator
  PowerShell window.
#>
$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script in an Administrator PowerShell window.'
}

$procmonDir = "$env:TEMP\procmon"
New-Item -ItemType Directory -Force -Path $procmonDir | Out-Null
if (-not (Test-Path "$procmonDir\Procmon64.exe")) {
    Write-Host "Downloading Process Monitor..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri "https://download.sysinternals.com/files/ProcessMonitor.zip" -OutFile "$procmonDir\pm.zip"
    Expand-Archive -Path "$procmonDir\pm.zip" -DestinationPath $procmonDir -Force
}

$logsDir = 'C:\ProgramData\LabMonitoring\Agent\logs'
New-Item -ItemType Directory -Force -Path $logsDir | Out-Null
$pml = Join-Path $logsDir 'procmon-capture.pml'
$csv = Join-Path $logsDir 'procmon-capture.csv'
Remove-Item $pml, $csv -ErrorAction SilentlyContinue

Write-Host "Starting system trace..." -ForegroundColor Cyan
Start-Process "$procmonDir\Procmon64.exe" -ArgumentList '/AcceptEula', '/Quiet', '/Minimized', '/BackingFile', $pml
Start-Sleep -Seconds 2

Write-Host ""
Write-Host "READY - now go to the browser, open the computer page, click 'Remote dieu khien'." -ForegroundColor Yellow
Write-Host "Wait until 'Da ket thuc' appears, then come back here and press Enter." -ForegroundColor Yellow
Read-Host "Press Enter after you clicked Remote dieu khien"

Write-Host "Stopping trace..." -ForegroundColor Cyan
Start-Process "$procmonDir\Procmon64.exe" -ArgumentList '/Terminate' -Wait
Start-Sleep -Seconds 2

Write-Host "Exporting to CSV..." -ForegroundColor Cyan
Start-Process "$procmonDir\Procmon64.exe" -ArgumentList '/OpenLog', $pml, '/SaveAs', $csv -Wait

Write-Host ""
Write-Host "Done! CSV file: $csv" -ForegroundColor Green
