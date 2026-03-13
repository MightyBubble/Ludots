param(
    [switch]$SkipTests,
    [switch]$SkipLauncher,
    [switch]$SkipMedia
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$artifactRoot = Join-Path $repoRoot "artifacts\acceptance\interaction-b1-self-buff"
$visualDir = Join-Path $artifactRoot "visual"
$screensDir = Join-Path $visualDir "screens"
$launcherLog = Join-Path $visualDir "launcher-run.log"
$ffconcatPath = Join-Path $visualDir "interaction-b1-self-buff.ffconcat"
$mp4Path = Join-Path $visualDir "interaction-b1-self-buff.mp4"
$gifPath = Join-Path $visualDir "interaction-b1-self-buff.gif"

New-Item -ItemType Directory -Force -Path $artifactRoot, $visualDir | Out-Null

Push-Location $repoRoot
try {
    if (-not $SkipTests) {
        dotnet test .\src\Tests\GasTests\GasTests.csproj -c Release --filter B1SelfBuff_ProducesHeadlessAcceptanceArtifacts
        if ($LASTEXITCODE -ne 0) {
            throw "Headless acceptance test failed with exit code $LASTEXITCODE."
        }
    }

    if (-not $SkipLauncher) {
        .\scripts\run-mod-launcher.cmd cli launch InteractionShowcaseMod --adapter raylib --record artifacts/acceptance/interaction-b1-self-buff/visual *> $launcherLog
        if ($LASTEXITCODE -ne 0) {
            throw "Launcher recording failed with exit code $LASTEXITCODE. See $launcherLog"
        }
    }

    if (-not $SkipMedia) {
        $frames = @(
            @{ Name = "000_start.png"; Duration = "1.2" }
            @{ Name = "001_order_submitted.png"; Duration = "1.0" }
            @{ Name = "002_buff_active.png"; Duration = "1.8" }
            @{ Name = "003_buff_expired.png"; Duration = "1.4" }
            @{ Name = "004_silenced_blocked.png"; Duration = "1.4" }
            @{ Name = "005_insufficient_mana.png"; Duration = "1.6" }
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
    Pop-Location
}

Write-Host "Interaction B1 self buff evidence refreshed."
Write-Host "  Headless: $artifactRoot"
Write-Host "  Visual:   $visualDir"
Write-Host "  MP4:      $mp4Path"
Write-Host "  GIF:      $gifPath"
Write-Host "  Launcher: $launcherLog"
