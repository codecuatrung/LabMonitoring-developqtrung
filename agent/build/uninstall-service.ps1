<#
.SYNOPSIS
  Stops and removes the LabAgent Windows Service. Run as Administrator.
#>
param(
    [string]$ServiceName = 'LabMonitoringAgent',
    [string]$InstallDir = 'C:\Program Files\LabMonitoring\Agent',
    [switch]$KeepFiles
)

$ErrorActionPreference = 'Stop'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script as Administrator.'
}

if (Get-Service $ServiceName -ErrorAction SilentlyContinue) {
    sc.exe stop $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    sc.exe delete $ServiceName | Out-Null
    Write-Host "Service '$ServiceName' removed." -ForegroundColor Green
} else {
    Write-Host "Service '$ServiceName' not found."
}

[Environment]::SetEnvironmentVariable('Agent__ServerBaseUrl', $null, 'Machine')
[Environment]::SetEnvironmentVariable('Agent__RoomCode', $null, 'Machine')

if (-not $KeepFiles -and (Test-Path $InstallDir)) {
    Remove-Item $InstallDir -Recurse -Force
    Write-Host "Removed $InstallDir"
}
