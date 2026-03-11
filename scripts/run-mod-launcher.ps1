param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ArgsList
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")
$bridgeHealthUrl = "http://localhost:5299/health"
$launcherUrl = "http://localhost:5299/launcher/"

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

    dotnet run --project ..\src\Tools\Ludots.Launcher.Cli\Ludots.Launcher.Cli.csproj -c Release -- @cliArgs
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

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
    Start-Process `
        -FilePath "dotnet" `
        -ArgumentList @("run", "--project", (Join-Path $repoRoot "src\Tools\Ludots.Editor.Bridge\Ludots.Editor.Bridge.csproj"), "-c", "Release") `
        -WorkingDirectory $repoRoot | Out-Null

    Wait-BridgeReady -Url $bridgeHealthUrl -TimeoutSeconds 60
}

Start-Process $launcherUrl | Out-Null
