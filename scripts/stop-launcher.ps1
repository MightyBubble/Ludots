$ErrorActionPreference = 'Stop'

$repoRoot = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) ".."
$pidFile = Join-Path $repoRoot '.tmp\launcher-processes.json'

if (-not (Test-Path $pidFile)) {
    Write-Host "No pid file: $pidFile"
    exit 0
}

$pids = Get-Content -Raw -Path $pidFile | ConvertFrom-Json
$all = @()
if ($pids.bridgePid) { $all += [int]$pids.bridgePid }
if ($pids.launcherPid) { $all += [int]$pids.launcherPid }

foreach ($procId in $all) {
    try {
        Stop-Process -Id $procId -Force -ErrorAction Stop
        Write-Host "Stopped PID $procId"
    } catch {
        Write-Host "PID $procId not running"
    }
}

try { Remove-Item -Force $pidFile } catch { }
