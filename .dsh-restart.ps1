# .dsh-restart.ps1 — restart the DeepSeek Harness web profile after the rc.7 upgrade.
# Runs from Task Scheduler so it survives the old harness process being killed.
$ErrorActionPreference = "Stop"

$log     = "C:\001_AI\LudotsProd\.dsh-restart.log"
$stdout  = "C:\001_AI\LudotsProd\.dsh-restart.stdout.log"
$stderr  = "C:\001_AI\LudotsProd\.dsh-restart.stderr.log"
$dshCmd  = "C:\Users\sietg\AppData\Roaming\npm\dsh.cmd"
$workdir = "C:\001_AI\LudotsProd"

function Log([string]$m) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $m"
    try { $line | Out-File -FilePath $log -Append -Encoding utf8 } catch { }
}

Log "=== restart script started ==="

# 0. Record the version the new install reports.
try {
    $v = (& $dshCmd --version 2>&1 | Select-Object -First 1)
    Log "dsh --version => $v"
} catch { Log "version probe failed: $($_.Exception.Message)" }

# 1. Stop whatever currently listens on 3080 (the old harness).
$listener = Get-NetTCPConnection -LocalPort 3080 -State Listen -ErrorAction SilentlyContinue
if ($listener) {
    foreach ($c in $listener) {
        try {
            Log "stopping old harness pid $($c.OwningProcess)"
            Stop-Process -Id $c.OwningProcess -Force -ErrorAction Stop
        } catch { Log "stop failed: $($_.Exception.Message)" }
    }
} else {
    Log "no listener on 3080; nothing to stop"
}

# 2. Wait until the port is free (deadline 3 minutes).
$deadline = (Get-Date).AddMinutes(3)
while ((Get-Date) -lt $deadline) {
    $l = Get-NetTCPConnection -LocalPort 3080 -State Listen -ErrorAction SilentlyContinue
    if (-not $l) { break }
    Start-Sleep -Seconds 2
}
if (Get-NetTCPConnection -LocalPort 3080 -State Listen -ErrorAction SilentlyContinue) {
    Log "ERROR: port 3080 still busy after deadline; aborting"
    exit 1
}
Log "port 3080 free"

# 3. Launch the new harness detached.
$p = Start-Process -FilePath $dshCmd -ArgumentList "web" -WorkingDirectory $workdir -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
Log "launched 'dsh web' as pid $($p.Id)"

# 4. Verify it comes up.
$ok = $false
$deadline2 = (Get-Date).AddMinutes(2)
while ((Get-Date) -lt $deadline2) {
    Start-Sleep -Seconds 3
    $l = Get-NetTCPConnection -LocalPort 3080 -State Listen -ErrorAction SilentlyContinue
    if ($l) { $ok = $true; break }
    if ($p.HasExited) { break }
}
if ($ok) {
    $pid2 = (Get-NetTCPConnection -LocalPort 3080 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1).OwningProcess
    Log "OK: harness listening on 3080 (pid $pid2)"
} else {
    Log "FAILED: harness not listening on 3080 (exited=$($p.HasExited), exitcode=$($p.ExitCode))"
}

# 5. Housekeeping: remove the one-shot task that ran this script.
try { schtasks /delete /tn "DSH-Restart-After-Update" /f | Out-Null } catch { }
Log "=== restart script finished ==="
