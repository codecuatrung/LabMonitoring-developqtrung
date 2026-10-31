<#
.SYNOPSIS
  Packs the source needed to build the production stack (backend, frontend, deploy, docs) into one .tar.gz
  so it can be copied to the server with scp - no git push needed.
.DESCRIPTION
  Left out on purpose: build output and dependencies (node_modules, bin, obj, dist, .angular), the agent
  sources (agents are shipped as a zip through the web UI), and anything secret or server-specific -
  deploy/.env and deploy/certs stay untouched on the server. Line endings of *.sh files are forced to LF.
.EXAMPLE
  ./deploy/pack-for-server.ps1                       # -> labmonitoring-src.tar.gz in the repo root
  ./deploy/pack-for-server.ps1 -Server user@192.168.1.28   # also prints (does not run) the copy commands
#>
param(
    [string]$Output = 'labmonitoring-src.tar.gz',
    [string]$Server
)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $repo
try {
    # A CRLF shell script fails on Linux ("bad interpreter"), so normalise the ones we ship.
    Get-ChildItem deploy -Filter *.sh | ForEach-Object {
        $text = [IO.File]::ReadAllText($_.FullName)
        if ($text.Contains("`r")) {
            [IO.File]::WriteAllText($_.FullName, $text.Replace("`r`n", "`n"), (New-Object Text.UTF8Encoding($false)))
            Write-Host "Converted $($_.Name) to LF line endings."
        }
    }

    if (Test-Path $Output) { Remove-Item $Output -Force }

    tar -czf $Output `
        --exclude=node_modules --exclude=.angular --exclude=dist --exclude=bin --exclude=obj `
        --exclude=.vs --exclude=.git --exclude='*.log' --exclude='*.user' `
        --exclude=deploy/.env --exclude=deploy/certs --exclude=deploy/docker-compose.dev.yml `
        backend frontend deploy docs
    if ($LASTEXITCODE -ne 0) { throw "tar failed with exit code $LASTEXITCODE" }

    $size = [math]::Round((Get-Item $Output).Length / 1MB, 2)
    Write-Host "Created $Output ($size MB)." -ForegroundColor Green

    $leaf = Split-Path $Output -Leaf
    $target = if ($Server) { $Server } else { '<user>@192.168.1.28' }
    Write-Host ''
    Write-Host 'Next (from this machine):' -ForegroundColor Cyan
    Write-Host "  scp $Output ${target}:~/"
    Write-Host 'Then on the server:' -ForegroundColor Cyan
    Write-Host "  mkdir -p ~/LabMonitoring && tar -xzf ~/$leaf -C ~/LabMonitoring"
    Write-Host '  cd ~/LabMonitoring && docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env up -d --build'
}
finally {
    Pop-Location
}
