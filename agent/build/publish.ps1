<#
.SYNOPSIS
  Publishes the LabAgent as a self-contained win-x64 build ready to copy to a client machine.
.EXAMPLE
  ./build/publish.ps1                 # -> agent/artifacts/agent
  ./build/publish.ps1 -Runtime win-x64 -Output C:\temp\labagent
#>
param(
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [string]$Output = "$PSScriptRoot/../artifacts/agent"
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../src/LabMonitoring.Agent/LabMonitoring.Agent.csproj'
$remoteHelperProject = Join-Path $PSScriptRoot '../src/LabMonitoring.Agent.RemoteHelper/LabMonitoring.Agent.RemoteHelper.csproj'
$updaterProject = Join-Path $PSScriptRoot '../src/LabMonitoring.Agent.Updater/LabMonitoring.Agent.Updater.csproj'

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -o $Output

# Remote control helper (ADR-0013): a separate, ordinary (non-single-file) self-contained publish —
# RemoteControlCoordinator locates it at "<agent exe dir>/RemoteHelper/…exe". Windows-only, so skip
# it entirely when publishing for another runtime (e.g. linux-x64).
if ($Runtime -eq 'win-x64') {
    dotnet publish $remoteHelperProject `
        -c $Configuration `
        -r $Runtime `
        --self-contained `
        -o (Join-Path $Output 'RemoteHelper')

    # Auto-update helper (SRS H1) — same shape as RemoteHelper: AgentUpdateExecutor locates it at
    # "<agent exe dir>/Updater/…exe" and copies it out to %ProgramData% before running it (its whole
    # job is to move the "<agent exe dir>" it started in, so it can't keep running from there).
    dotnet publish $updaterProject `
        -c $Configuration `
        -r $Runtime `
        --self-contained `
        -o (Join-Path $Output 'Updater')
}

# install-service.ps1/uninstall-service.ps1 expect to sit next to the published exe (see their
# default -BinPath) — copy them in so the whole client bundle is just this one folder.
Copy-Item (Join-Path $PSScriptRoot 'install-service.ps1') $Output -Force
Copy-Item (Join-Path $PSScriptRoot 'uninstall-service.ps1') $Output -Force

# install-config.txt: only copy the template if the output folder doesn't already have one, so
# re-running publish.ps1 into the same -Output never clobbers a value a technician already edited
# there (e.g. after copying the folder to a client machine and customizing it in place).
$configDest = Join-Path $Output 'install-config.txt'
if (-not (Test-Path $configDest)) {
    Copy-Item (Join-Path $PSScriptRoot 'install-config.txt') $configDest
}

Write-Host ""
Write-Host "Published to: $(Resolve-Path $Output)" -ForegroundColor Green
Write-Host "Copy that whole folder to the client machine, edit install-config.txt there, then run install-service.ps1 as Administrator."
