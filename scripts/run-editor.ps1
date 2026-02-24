param(
    [switch]$NoInstall,
    [switch]$NoBrowser,
    [switch]$Headless
)

$ErrorActionPreference = 'Stop'

$repoRoot = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) ".."
$bridgeProj = Join-Path $repoRoot 'src\Tools\Ludots.Editor.Bridge\Ludots.Editor.Bridge.csproj'
$reactDir = Join-Path $repoRoot 'src\Tools\Ludots.Editor.React'
$tmpDir = Join-Path $repoRoot '.tmp'
$pidFile = Join-Path $tmpDir 'editor-processes.json'

if (-not (Test-Path $bridgeProj)) { throw "Bridge project not found: $bridgeProj" }
if (-not (Test-Path $reactDir)) { throw "React editor dir not found: $reactDir" }

if (-not $NoInstall) {
    $nodeModules = Join-Path $reactDir 'node_modules'
    if (-not (Test-Path $nodeModules)) {
        Push-Location $reactDir
        try {
            npm ci
        } finally {
            Pop-Location
        }
    }
}

New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null

if ($Headless) {
    $bridgeLog = Join-Path $tmpDir 'bridge.log'
    $bridgeErr = Join-Path $tmpDir 'bridge.err.log'
    $editorLog = Join-Path $tmpDir 'editor.log'
    $editorErr = Join-Path $tmpDir 'editor.err.log'

    $bridge = Start-Process -PassThru -FilePath dotnet -WorkingDirectory $repoRoot -ArgumentList @('run', '--project', $bridgeProj) -WindowStyle Hidden -RedirectStandardOutput $bridgeLog -RedirectStandardError $bridgeErr
    $editor = Start-Process -PassThru -FilePath cmd.exe -WorkingDirectory $reactDir -ArgumentList @('/c', 'npm', 'run', 'dev') -WindowStyle Hidden -RedirectStandardOutput $editorLog -RedirectStandardError $editorErr

    @{ bridgePid = $bridge.Id; editorPid = $editor.Id } | ConvertTo-Json | Set-Content -Encoding UTF8 -Path $pidFile

    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest -UseBasicParsing -TimeoutSec 2 -Uri 'http://localhost:5299/health'
            if ($r.StatusCode -eq 200) { break }
        } catch { }
        Start-Sleep -Milliseconds 300
    }

    if (-not $NoBrowser) { Start-Process 'http://localhost:5173/' }
    exit 0
}

$bridgeCmd = "cd /d `"$repoRoot`"; dotnet run --project `"$bridgeProj`""
$editorCmd = "cd /d `"$reactDir`"; npm run dev"

Start-Process -FilePath powershell -ArgumentList @('-NoExit', '-Command', $bridgeCmd) -WorkingDirectory $repoRoot | Out-Null
Start-Process -FilePath powershell -ArgumentList @('-NoExit', '-Command', $editorCmd) -WorkingDirectory $reactDir | Out-Null

if (-not $NoBrowser) {
    Start-Sleep -Milliseconds 800
    Start-Process 'http://localhost:5173/'
}
