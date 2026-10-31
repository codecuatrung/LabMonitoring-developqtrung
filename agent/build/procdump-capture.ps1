<#
.SYNOPSIS
  Downloads ProcDump (Sysinternals) and registers it as the debugger for the RemoteHelper.exe via
  Image File Execution Options, so it auto-attaches and captures the crash reason the moment the
  process starts (works even if the process dies before any of its own code runs). Run in an
  Administrator PowerShell window.
#>
$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script in an Administrator PowerShell window.'
}

$toolDir = "$env:TEMP\procdump"
New-Item -ItemType Directory -Force -Path $toolDir | Out-Null
if (-not (Test-Path "$toolDir\procdump64.exe")) {
    Write-Host "Downloading ProcDump..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri "https://download.sysinternals.com/files/Procdump.zip" -OutFile "$toolDir\pd.zip"
    Expand-Archive -Path "$toolDir\pd.zip" -DestinationPath $toolDir -Force
}

$logsDir = 'C:\ProgramData\LabMonitoring\Agent\logs'
New-Item -ItemType Directory -Force -Path $logsDir | Out-Null
$dumpDir = Join-Path $logsDir 'procdump-out'
New-Item -ItemType Directory -Force -Path $dumpDir | Out-Null
$procdumpLog = Join-Path $logsDir 'procdump-stdout.txt'
Remove-Item $procdumpLog -ErrorAction SilentlyContinue
Get-ChildItem $dumpDir -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

# Windows appends the original exe path + its args to the end of the "Debugger" value when it
# intercepts a launch via Image File Execution Options — so the actual redirect has to live inside
# a wrapper script (using %*), not directly in the Debugger value itself.
$wrapperBat = Join-Path $toolDir 'ifeo-wrapper.bat'
@"
@echo off
"$toolDir\procdump64.exe" -ma -accepteula -x "$dumpDir" %* >> "$procdumpLog" 2>&1
"@ | Set-Content -Path $wrapperBat -Encoding ASCII

$exeName = 'LabMonitoring.Agent.RemoteHelper.exe'
$ifeoPath = "SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\$exeName"
# No quotes around $wrapperBat: Windows appends the original exe path + args to the END of this
# whole Debugger value, and nested quoting here breaks cmd.exe's /c parsing (that path has no
# spaces, so it's safe to leave unquoted and dodge the problem entirely).
$cmdExe = Join-Path $env:SystemRoot 'System32\cmd.exe'
$debuggerValue = "`"$cmdExe`" /c $wrapperBat"

# Write via the .NET Registry API with an explicit 64-bit view — PowerShell's own process bitness
# (32 vs 64-bit powershell.exe) determines which view "HKLM:\SOFTWARE\..." resolves to via WOW64
# redirection, but the Agent service is always a 64-bit process and will only ever read the real
# (non-redirected) 64-bit view. Setting both explicitly removes that mismatch as a variable.
foreach ($view in 'Registry64', 'Registry32') {
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine', $view)
    $key = $base.CreateSubKey($ifeoPath)
    $key.SetValue('Debugger', $debuggerValue)
    $key.Close()
    $base.Close()
}

Write-Host "Debugger value written (64-bit and 32-bit views): $debuggerValue" -ForegroundColor Cyan

Write-Host ""
Write-Host "READY - now go to the browser, open the computer page, click 'Remote dieu khien'." -ForegroundColor Yellow
Write-Host "Wait until 'Da ket thuc' appears, then come back here and press Enter." -ForegroundColor Yellow
Read-Host "Press Enter after you clicked Remote dieu khien"

Start-Sleep -Seconds 3
Write-Host "Removing the IFEO debugger hook..." -ForegroundColor Cyan
foreach ($view in 'Registry64', 'Registry32') {
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine', $view)
    $base.DeleteSubKeyTree($ifeoPath, $false)
    $base.Close()
}

Write-Host ""
Write-Host "Done. ProcDump output: $procdumpLog" -ForegroundColor Green
Write-Host "Dump files (if any) in: $dumpDir" -ForegroundColor Green
if (Test-Path $procdumpLog) {
    Write-Host ""
    Write-Host "--- procdump-stdout.txt ---" -ForegroundColor Cyan
    Get-Content $procdumpLog
}
