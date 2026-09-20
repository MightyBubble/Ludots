param(
    [switch]$NoInstall,
    [switch]$NoBrowser,
    [switch]$Headless
)

$ErrorActionPreference = 'Stop'

$repoRoot = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) ".."
$dotnetHostScript = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "dotnet-host.ps1"
$bridgeProj = Join-Path $repoRoot 'src\Tools\Ludots.Editor.Bridge\Ludots.Editor.Bridge.csproj'
$reactDir = Join-Path $repoRoot 'src\Tools\Ludots.Editor.React'
$tmpDir = Join-Path $repoRoot '.tmp'
$pidFile = Join-Path $tmpDir 'editor-processes.json'
$studioUrl = 'http://localhost:5173/'
$bridgeHealth = 'http://localhost:5299/health'

if (-not (Test-Path $bridgeProj)) { throw "Bridge project not found: $bridgeProj" }
if (-not (Test-Path $reactDir)) { throw "React editor dir not found: $reactDir" }
if (-not (Test-Path $dotnetHostScript)) { throw "dotnet host helper not found: $dotnetHostScript" }

. $dotnetHostScript

function Test-HttpOk([string]$Uri) {
    try {
        $r = Invoke-WebRequest -UseBasicParsing -TimeoutSec 2 -Uri $Uri
        return $r.StatusCode -eq 200
    } catch {
        return $false
    }
}

function Wait-HttpOk([string]$Uri, [string]$Name) {
    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline) {
        if (Test-HttpOk $Uri) { return }
        Start-Sleep -Milliseconds 300
    }
    throw "$Name did not become ready at $Uri within 90s"
}

function Open-AuthoringStudio([string]$Url) {
    if ($NoBrowser -or $Headless) {
        Write-Host "studio ready: $Url"
        return
    }

    $candidates = @(
        @{ Path = "$env:ProgramFiles\Google\Chrome\Application\chrome.exe"; Args = @("--app=$Url", "--new-window") },
        @{ Path = "$env:ProgramFiles (x86)\Google\Chrome\Application\chrome.exe"; Args = @("--app=$Url", "--new-window") },
        @{ Path = "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"; Args = @("--app=$Url", "--new-window") },
        @{ Path = "$env:ProgramFiles (x86)\Microsoft\Edge\Application\msedge.exe"; Args = @("--app=$Url", "--new-window") }
    )
    foreach ($item in $candidates) {
        if (Test-Path $item.Path) {
            Start-Process -FilePath $item.Path -ArgumentList $item.Args | Out-Null
            Write-Host "opened authoring studio app window: $($item.Path)"
            return
        }
    }

    Write-Host "no Chrome/Edge for an app window; opening a browser tab (not silent: this host has no --app browser)"
    Start-Process $Url
}

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

if ((Test-HttpOk $bridgeHealth) -and (Test-HttpOk $studioUrl)) {
    Write-Host "authoring studio already running"
    Open-AuthoringStudio $studioUrl
    exit 0
}

if ($Headless) {
    $bridgeLog = Join-Path $tmpDir 'bridge.log'
    $bridgeErr = Join-Path $tmpDir 'bridge.err.log'
    $editorLog = Join-Path $tmpDir 'editor.log'
    $editorErr = Join-Path $tmpDir 'editor.err.log'

    $bridge = $null
    $editor = $null
    if (-not (Test-HttpOk $bridgeHealth)) {
        $bridge = Start-DotnetProject -PassThru -ProjectPath $bridgeProj -WorkingDirectory $repoRoot -WindowStyle Hidden -RedirectStandardOutput $bridgeLog -RedirectStandardError $bridgeErr
    }
    if (-not (Test-HttpOk $studioUrl)) {
        $editor = Start-Process -PassThru -FilePath cmd.exe -WorkingDirectory $reactDir -ArgumentList @('/c', 'npm', 'run', 'dev') -WindowStyle Hidden -RedirectStandardOutput $editorLog -RedirectStandardError $editorErr
    }

    $payload = @{}
    if ($bridge) { $payload.bridgePid = $bridge.Id }
    if ($editor) { $payload.editorPid = $editor.Id }
    $payload | ConvertTo-Json | Set-Content -Encoding UTF8 -Path $pidFile

    Wait-HttpOk $bridgeHealth 'Editor.Bridge'
    Wait-HttpOk $studioUrl 'Vite editor'
    Open-AuthoringStudio $studioUrl
    exit 0
}

if (-not (Test-HttpOk $bridgeHealth)) {
    $bridgeCmd = "cd /d `"$repoRoot`"; . `"$dotnetHostScript`"; Invoke-DotnetProject -ProjectPath `"$bridgeProj`" -WorkingDirectory `"$repoRoot`""
    Start-Process -FilePath powershell -ArgumentList @('-NoExit', '-Command', $bridgeCmd) -WorkingDirectory $repoRoot | Out-Null
}
if (-not (Test-HttpOk $studioUrl)) {
    $editorCmd = "cd /d `"$reactDir`"; npm run dev"
    Start-Process -FilePath powershell -ArgumentList @('-NoExit', '-Command', $editorCmd) -WorkingDirectory $reactDir | Out-Null
}

Wait-HttpOk $bridgeHealth 'Editor.Bridge'
Wait-HttpOk $studioUrl 'Vite editor'
Open-AuthoringStudio $studioUrl
