param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ArgsList
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($null -eq $ArgsList) {
    $ArgsList = @()
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")
$dotnetHostScript = Join-Path $scriptDir "dotnet-host.ps1"
$bridgeHealthUrl = "http://localhost:5299/health"
$launcherUrl = "http://localhost:5299/launcher/index.html"

if (-not (Test-Path $dotnetHostScript)) {
    throw "dotnet host helper not found: $dotnetHostScript"
}

. $dotnetHostScript
function Wait-BridgeReady {
    param(
        [string]$Url,
        [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-RestMethod -Uri $Url -Method Get -TimeoutSec 2
            if ($response.ok -eq $true) {
                return
            }
        } catch {
        }

        Start-Sleep -Milliseconds 500
    }

    throw "Bridge did not become ready within $TimeoutSeconds seconds: $Url"
}

Set-Location $scriptDir

if ($ArgsList.Length -gt 0 -and $ArgsList[0] -eq "cli") {
    $cliArgs = @()
    if ($ArgsList.Length -gt 1) {
        $cliArgs = $ArgsList[1..($ArgsList.Length - 1)]
    }

    $cliProject = Join-Path $repoRoot "src\Tools\Ludots.Launcher.Cli\Ludots.Launcher.Cli.csproj"
    $exitCode = Invoke-DotnetProject -ProjectPath $cliProject -WorkingDirectory $repoRoot -Arguments $cliArgs
    if ($exitCode -ne 0) { exit $exitCode }
    exit 0
}

Push-Location (Join-Path $repoRoot "src\Tools\Ludots.Launcher.React")
try {
    if (-not (Test-Path "node_modules")) {
        npm ci
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }

    npm run build
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
} finally {
    Pop-Location
}

$bridgeReady = $false
try {
    $response = Invoke-RestMethod -Uri $bridgeHealthUrl -Method Get -TimeoutSec 2
    $bridgeReady = $response.ok -eq $true
} catch {
    $bridgeReady = $false
}

if (-not $bridgeReady) {
    $bridgeProject = Join-Path $repoRoot "src\Tools\Ludots.Editor.Bridge\Ludots.Editor.Bridge.csproj"
    Start-DotnetProject -ProjectPath $bridgeProject -WorkingDirectory $repoRoot | Out-Null

    Wait-BridgeReady -Url $bridgeHealthUrl -TimeoutSeconds 60
}

Start-Process $launcherUrl | Out-Null
