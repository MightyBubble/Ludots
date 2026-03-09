param(
    [switch]$NoInstall,
    [switch]$NoBrowser,
    [switch]$Headless
)

$ErrorActionPreference = 'Stop'

$repoRoot = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) ".."
$bridgeProj = Join-Path $repoRoot 'src\Tools\Ludots.Editor.Bridge\Ludots.Editor.Bridge.csproj'
$launcherDir = Join-Path $repoRoot 'src\Tools\Ludots.Launcher.React'
$tmpDir = Join-Path $repoRoot '.tmp'
$pidFile = Join-Path $tmpDir 'launcher-processes.json'

if (-not (Test-Path $bridgeProj)) { throw "Bridge project not found: $bridgeProj" }
if (-not (Test-Path $launcherDir)) { throw "Launcher React dir not found: $launcherDir" }

if (-not $NoInstall) {
    $nodeModules = Join-Path $launcherDir 'node_modules'
    if (-not (Test-Path $nodeModules)) {
        Push-Location $launcherDir
        try {
            npm ci
        } finally {
            Pop-Location
        }
    }
}

New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null

if ($Headless) {
    $bridgeLog = Join-Path $tmpDir 'launcher-bridge.log'
    $bridgeErr = Join-Path $tmpDir 'launcher-bridge.err.log'
    $launcherLog = Join-Path $tmpDir 'launcher-react.log'
    $launcherErr = Join-Path $tmpDir 'launcher-react.err.log'

    $bridge = Start-Process -PassThru -FilePath dotnet -WorkingDirectory $repoRoot -ArgumentList @('run', '--project', $bridgeProj) -WindowStyle Hidden -RedirectStandardOutput $bridgeLog -RedirectStandardError $bridgeErr
    $launcher = Start-Process -PassThru -FilePath cmd.exe -WorkingDirectory $launcherDir -ArgumentList @('/c', 'npm', 'run', 'dev') -WindowStyle Hidden -RedirectStandardOutput $launcherLog -RedirectStandardError $launcherErr

    @{ bridgePid = $bridge.Id; launcherPid = $launcher.Id } | ConvertTo-Json | Set-Content -Encoding UTF8 -Path $pidFile

    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest -UseBasicParsing -TimeoutSec 2 -Uri 'http://localhost:5299/health'
            if ($r.StatusCode -eq 200) { break }
        } catch { }
        Start-Sleep -Milliseconds 300
    }

    if (-not $NoBrowser) { Start-Process 'http://localhost:5174/' }
    exit 0
}

$bridgeCmd = "cd /d `"$repoRoot`"; dotnet run --project `"$bridgeProj`""
$launcherCmd = "cd /d `"$launcherDir`"; npm run dev"

Start-Process -FilePath powershell -ArgumentList @('-NoExit', '-Command', $bridgeCmd) -WorkingDirectory $repoRoot | Out-Null
Start-Process -FilePath powershell -ArgumentList @('-NoExit', '-Command', $launcherCmd) -WorkingDirectory $launcherDir | Out-Null

if (-not $NoBrowser) {
    Start-Sleep -Milliseconds 800
    Start-Process 'http://localhost:5174/'
}
