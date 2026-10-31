<#
.SYNOPSIS
  Installs the LabAgent Windows Service on a client machine. Run as Administrator.
.DESCRIPTION
  Reads ServerBaseUrl/RoomCode from install-config.txt (a plain text file next to this script, or
  pass -ConfigFile) so a technician can just open that file in Notepad, edit the two values, save,
  and run this script with no arguments. -ServerBaseUrl/-RoomCode command-line arguments (if given)
  override whatever is in the config file. Expects the published output (publish.ps1) in the same
  folder as this script, or pass -BinPath.
.EXAMPLE
  ./install-service.ps1                       # reads install-config.txt next to this script
.EXAMPLE
  ./install-service.ps1 -ServerBaseUrl https://labmonitoring.school.local -RoomCode LAB-01
#>
param(
    [string]$ServerBaseUrl,
    [string]$RoomCode,
    [string]$ConfigFile = (Join-Path $PSScriptRoot 'install-config.txt'),
    [string]$ServiceName = 'LabMonitoringAgent',
    [string]$BinPath = (Join-Path $PSScriptRoot 'LabMonitoring.Agent.exe'),
    [string]$InstallDir = 'C:\Program Files\LabMonitoring\Agent'
)

$ErrorActionPreference = 'Stop'

# install-config.txt: "Key=Value" lines, "#" starts a comment, blank lines ignored. Command-line
# -ServerBaseUrl/-RoomCode (if passed) win over whatever the file has.
$configValues = @{}
if (Test-Path $ConfigFile) {
    foreach ($line in Get-Content $ConfigFile) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        $parts = $trimmed.Split('=', 2)
        if ($parts.Count -eq 2) { $configValues[$parts[0].Trim()] = $parts[1].Trim() }
    }
}
if (-not $ServerBaseUrl) { $ServerBaseUrl = $configValues['ServerBaseUrl'] }
if (-not $RoomCode) { $RoomCode = $configValues['RoomCode'] }
if (-not $RoomCode) { $RoomCode = '' }

if (-not $ServerBaseUrl) {
    throw "ServerBaseUrl chua duoc cau hinh. Mo file '$ConfigFile' bang Notepad, sua dong ServerBaseUrl=..., luu lai roi chay lai script nay (hoac truyen -ServerBaseUrl)."
}
# NOTE: IsInRole(string) resolves the string as an ACCOUNT NAME (translates "Administrator" to the
# special, usually-disabled built-in Administrator user's SID via NTAccount.Translate) — not a check
# for membership in the BUILTIN\Administrators GROUP. Must use the WindowsBuiltInRole enum overload,
# which correctly resolves to SID S-1-5-32-544, or every real admin user who isn't literally logged
# in as that built-in account fails this check despite a properly elevated token.
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script as Administrator.'
}

Write-Host "ServerBaseUrl = $ServerBaseUrl"
Write-Host "RoomCode      = $RoomCode"

# Copy binaries to a stable install location
$source = Split-Path $BinPath -Parent
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item "$source\*" $InstallDir -Recurse -Force
$exe = Join-Path $InstallDir 'LabMonitoring.Agent.exe'

# Machine-level configuration (overrides appsettings.json via env vars)
[Environment]::SetEnvironmentVariable('Agent__ServerBaseUrl', $ServerBaseUrl, 'Machine')
[Environment]::SetEnvironmentVariable('Agent__RoomCode', $RoomCode, 'Machine')

if (Get-Service $ServiceName -ErrorAction SilentlyContinue) {
    sc.exe stop $ServiceName | Out-Null
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

sc.exe create $ServiceName binPath= "`"$exe`"" start= auto DisplayName= "Lab Monitoring Agent"
sc.exe description $ServiceName "Thu thap trang thai/metrics va thuc thi lenh dieu khien cho he thong Lab Monitoring."
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000
sc.exe start $ServiceName

Write-Host "Service '$ServiceName' installed and started." -ForegroundColor Green
