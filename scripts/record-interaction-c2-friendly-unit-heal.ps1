param(
    [switch]$SkipTests,
    [switch]$SkipLauncher,
    [switch]$SkipMedia
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$artifactRoot = Join-Path $repoRoot "artifacts\acceptance\interaction-c2-friendly-unit-heal"
$visualDir = Join-Path $artifactRoot "visual"
$screensDir = Join-Path $visualDir "screens"
$launcherLog = Join-Path $visualDir "launcher-run.log"
$ffconcatPath = Join-Path $visualDir "interaction-c2-friendly-unit-heal.ffconcat"
$mp4Path = Join-Path $visualDir "interaction-c2-friendly-unit-heal.mp4"
$gifPath = Join-Path $visualDir "interaction-c2-friendly-unit-heal.gif"
$gameJsonPath = Join-Path $repoRoot "mods\InteractionShowcaseMod\assets\game.json"
$originalGameJson = $null

New-Item -ItemType Directory -Force -Path $artifactRoot, $visualDir | Out-Null

Push-Location $repoRoot
try {
    if (-not $SkipTests) {
        dotnet test .\src\Tests\GasTests\GasTests.csproj -c Release --filter C2FriendlyUnitHeal
        if ($LASTEXITCODE -ne 0) {
            throw "C2 test suite failed with exit code $LASTEXITCODE."
        }
    }

    if (-not $SkipLauncher) {
        $originalGameJson = Get-Content $gameJsonPath -Raw -Encoding UTF8
        $gameJson = $originalGameJson | ConvertFrom-Json
        $gameJson.startupMapId = "interaction_c2_friendly_unit_heal"
        $gameJson | ConvertTo-Json -Depth 10 | Set-Content -Path $gameJsonPath -Encoding UTF8

        .\scripts\run-mod-launcher.cmd cli launch InteractionShowcaseMod --adapter raylib --record artifacts/acceptance/interaction-c2-friendly-unit-heal/visual *> $launcherLog
        if ($LASTEXITCODE -ne 0) {
            throw "Launcher recording failed with exit code $LASTEXITCODE. See $launcherLog"
        }
    }

    if (-not $SkipMedia) {
        $frames = @(
            @{ Name = "000_start.png"; Duration = "1.2" }
            @{ Name = "001_order_submitted.png"; Duration = "1.0" }
            @{ Name = "002_heal_applied.png"; Duration = "1.8" }
            @{ Name = "003_hostile_target_blocked.png"; Duration = "1.4" }
            @{ Name = "004_dead_ally_blocked.png"; Duration = "1.4" }
        )

        foreach ($frame in $frames) {
            $framePath = Join-Path $screensDir $frame.Name
            if (-not (Test-Path $framePath)) {
                throw "Missing expected frame: $framePath"
            }
        }

        $concatLines = New-Object System.Collections.Generic.List[string]
        $concatLines.Add("ffconcat version 1.0")
        foreach ($frame in $frames) {
            $framePath = (Join-Path $screensDir $frame.Name).Replace("\", "/")
            $concatLines.Add("file '$framePath'")
            $concatLines.Add("duration $($frame.Duration)")
        }
        $lastFramePath = (Join-Path $screensDir $frames[-1].Name).Replace("\", "/")
        $concatLines.Add("file '$lastFramePath'")
        $concatLines | Set-Content -Path $ffconcatPath -Encoding ASCII

        ffmpeg -y -safe 0 -f concat -i $ffconcatPath -vf "fps=30,format=yuv420p" $mp4Path | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "ffmpeg mp4 generation failed with exit code $LASTEXITCODE."
        }

        ffmpeg -y -safe 0 -f concat -i $ffconcatPath -vf "fps=12,scale=1200:-1:flags=lanczos" $gifPath | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "ffmpeg gif generation failed with exit code $LASTEXITCODE."
        }
    }
}
finally {
    if ($null -ne $originalGameJson) {
        $originalGameJson | Set-Content -Path $gameJsonPath -Encoding UTF8
    }

    Pop-Location
}

Write-Host "Interaction C2 friendly unit heal evidence refreshed."
Write-Host "  Headless: $artifactRoot"
Write-Host "  Visual:   $visualDir"
Write-Host "  MP4:      $mp4Path"
Write-Host "  GIF:      $gifPath"
Write-Host "  Launcher: $launcherLog"
