param(
    [string[]]$Ids = @(),
    [int]$FrameCount = 48,
    [int]$ScreenshotFrame = 24,
    [int]$Framerate = 8,
    [int]$VideoSeconds = 2,
    [switch]$SkipBuild,
    [switch]$FinalizeOnly
)

$ErrorActionPreference = "Stop"
if ($FrameCount -le 0) { throw "FrameCount must be positive." }
if ($ScreenshotFrame -lt 1 -or $ScreenshotFrame -gt $FrameCount) {
    throw "ScreenshotFrame must be within 1..FrameCount."
}
if ($Framerate -le 0) { throw "Framerate must be positive." }
if ($VideoSeconds -le 0) { throw "VideoSeconds must be positive." }
if ($SkipBuild -and $FinalizeOnly) { throw "SkipBuild and FinalizeOnly cannot be combined." }
$targetFrameCount = $Framerate * $VideoSeconds
if ($FrameCount % $targetFrameCount -ne 0) {
    throw "FrameCount must be evenly divisible by Framerate * VideoSeconds."
}

$repo = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repo "mods/showcases/performer_raylib_micro_showcases/PerformerRaylibMicroShowcasesMod/assets/Presentation/showcases.manifest.json"
$authoringPath = Join-Path $repo "mods/showcases/performer_raylib_micro_showcases/PerformerRaylibMicroShowcasesMod/assets/Presentation/Authoring/raylib-micro-showcases.authoring.json"
$generatorPath = Join-Path $repo "scripts/generate-raylib-micro-assets.mjs"
$launcherCliProject = Join-Path $repo "src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj"
$launcherCli = Join-Path $repo "src/Tools/Ludots.Launcher.Cli/bin/Release/net8.0/Ludots.Launcher.Cli.dll"
$dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
$launcherCliPrefix = @("exec", "--roll-forward", "Major", $launcherCli)

function Stop-ProcessTree {
    param([int]$RootProcessId)

    $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $RootProcessId" -ErrorAction SilentlyContinue)
    foreach ($child in $children) {
        Stop-ProcessTree -RootProcessId ([int]$child.ProcessId)
    }
    Stop-Process -Id $RootProcessId -Force -ErrorAction SilentlyContinue
}

foreach ($requiredInput in @($manifestPath, $authoringPath, $generatorPath)) {
    if (!(Test-Path -LiteralPath $requiredInput)) {
        throw "Raylib micro showcase recording input not found: $requiredInput"
    }
}
$authoringSha256 = (Get-FileHash -LiteralPath $authoringPath -Algorithm SHA256).Hash.ToLowerInvariant()
$generatorSha256 = (Get-FileHash -LiteralPath $generatorPath -Algorithm SHA256).Hash.ToLowerInvariant()

if (!(Test-Path -LiteralPath $launcherCliProject)) {
    throw "Launcher CLI project not found: $launcherCliProject"
}

$ffmpeg = Get-Command ffmpeg -ErrorAction SilentlyContinue
if ($null -eq $ffmpeg) {
    $wingetFfmpeg = Join-Path $env:LOCALAPPDATA "Microsoft/WinGet/Packages/Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe/ffmpeg-8.1.2-full_build/bin/ffmpeg.exe"
    if (Test-Path -LiteralPath $wingetFfmpeg) {
        $ffmpegPath = $wingetFfmpeg
    } else {
        throw "ffmpeg is required for real mp4 recording, but it was not found on PATH or the known WinGet path."
    }
} else {
    $ffmpegPath = $ffmpeg.Source
}
$ffprobePath = Join-Path (Split-Path -Parent $ffmpegPath) "ffprobe.exe"
if (!(Test-Path -LiteralPath $ffprobePath)) {
    throw "ffprobe is required beside ffmpeg for recording verification: $ffprobePath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$Ids = @($Ids | ForEach-Object { $_ -split "," } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Ids.Count -gt 0) {
    $selected = @($manifest | Where-Object { $Ids -contains $_.id })
    $missing = @($Ids | Where-Object { $manifest.id -notcontains $_ })
    if ($missing.Count -gt 0) {
        throw "Unknown raylib micro showcase id(s): $($missing -join ', ')"
    }
} else {
    $selected = @($manifest)
}

if ($selected.Count -eq 0) {
    throw "No raylib micro showcases selected for recording."
}

if (!$FinalizeOnly -and !$SkipBuild) {
    $cliBuildOutPath = Join-Path $repo "artifacts/raylib-performer-micro-showcases/build-launcher-cli.out.txt"
    $cliBuildErrPath = Join-Path $repo "artifacts/raylib-performer-micro-showcases/build-launcher-cli.err.txt"
    $buildOutPath = Join-Path $repo "artifacts/raylib-performer-micro-showcases/build-selected.out.txt"
    $buildErrPath = Join-Path $repo "artifacts/raylib-performer-micro-showcases/build-selected.err.txt"
    $appBuildOutPath = Join-Path $repo "artifacts/raylib-performer-micro-showcases/build-app.out.txt"
    $appBuildErrPath = Join-Path $repo "artifacts/raylib-performer-micro-showcases/build-app.err.txt"
    Write-Host "== Building launcher CLI once"
    $cliBuildProcess = Start-Process -FilePath $dotnetPath `
        -ArgumentList @("build", $launcherCliProject, "-c", "Release", "-nologo", "-clp:ErrorsOnly") `
        -WorkingDirectory $repo `
        -PassThru `
        -RedirectStandardOutput $cliBuildOutPath `
        -RedirectStandardError $cliBuildErrPath `
        -WindowStyle Hidden
    $cliBuildProcess.WaitForExit(300000) | Out-Null
    if (!$cliBuildProcess.HasExited) {
        Stop-ProcessTree -RootProcessId $cliBuildProcess.Id
        throw "Launcher CLI prebuild did not exit. See $cliBuildOutPath and $cliBuildErrPath"
    }
    if ($null -ne $cliBuildProcess.ExitCode -and $cliBuildProcess.ExitCode -ne 0) {
        throw "Launcher CLI prebuild failed with exit code $($cliBuildProcess.ExitCode). See $cliBuildErrPath"
    }
    if (!(Test-Path -LiteralPath $launcherCli)) {
        throw "Launcher CLI prebuild did not produce: $launcherCli"
    }

    Write-Host "== Building Raylib app"
    $appBuildProcess = Start-Process -FilePath $dotnetPath `
        -ArgumentList ($launcherCliPrefix + @("build", "app", "--adapter", "raylib")) `
        -WorkingDirectory $repo `
        -PassThru `
        -RedirectStandardOutput $appBuildOutPath `
        -RedirectStandardError $appBuildErrPath `
        -WindowStyle Hidden
    $appBuildProcess.WaitForExit(300000) | Out-Null
    if (!$appBuildProcess.HasExited) {
        Stop-ProcessTree -RootProcessId $appBuildProcess.Id
        throw "Raylib app prebuild did not exit. See $appBuildOutPath and $appBuildErrPath"
    }
    if ($null -ne $appBuildProcess.ExitCode -and $appBuildProcess.ExitCode -ne 0) {
        throw "Raylib app prebuild failed with exit code $($appBuildProcess.ExitCode). See $appBuildErrPath"
    }

    $buildArgs = @("cli", "build")
    $buildArgs += @($selected | ForEach-Object { [string]$_.binding })
    $buildArgs += @("--adapter", "raylib", "--build", "auto")
    Write-Host "== Building $($selected.Count) selected Raylib micro showcase entry mod(s)"
    $buildProcess = Start-Process -FilePath $dotnetPath `
        -ArgumentList ($launcherCliPrefix + $buildArgs[1..($buildArgs.Count - 1)]) `
        -WorkingDirectory $repo `
        -PassThru `
        -RedirectStandardOutput $buildOutPath `
        -RedirectStandardError $buildErrPath `
        -WindowStyle Hidden
    $buildProcess.WaitForExit(300000) | Out-Null
    if (!$buildProcess.HasExited) {
        Stop-ProcessTree -RootProcessId $buildProcess.Id
        throw "Raylib micro showcase prebuild did not exit. See $buildOutPath and $buildErrPath"
    }

    if ($null -ne $buildProcess.ExitCode -and $buildProcess.ExitCode -ne 0) {
        throw "Raylib micro showcase prebuild failed with exit code $($buildProcess.ExitCode). See $buildErrPath"
    }
}

if ($SkipBuild) {
    $raylibApp = Join-Path $repo "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0/Ludots.App.Raylib.dll"
    foreach ($requiredBinary in @($launcherCli, $raylibApp)) {
        if (!(Test-Path -LiteralPath $requiredBinary)) {
            throw "SkipBuild requires an existing Release binary: $requiredBinary"
        }
    }
}

$existingById = @{}
$summaryPath = Join-Path $repo "artifacts/raylib-performer-micro-showcases/recording-summary.json"
if (Test-Path -LiteralPath $summaryPath) {
    $existingSummary = Get-Content -LiteralPath $summaryPath -Raw -Encoding UTF8 | ConvertFrom-Json
    for ($index = 0; $index -lt $existingSummary.Count; $index++) {
        $entry = $existingSummary[$index]
        $existingById[[string]$entry.id] = $entry
    }
}

if (!$FinalizeOnly) {
  foreach ($item in $selected) {
    $id = [string]$item.id
    $binding = [string]$item.binding
    $artifactDir = Join-Path $repo ([string]$item.artifactDir)
    $posterPath = Join-Path $repo ([string]$item.poster)
    $videoPath = Join-Path $repo ([string]$item.recording)
    $framesDir = Join-Path $artifactDir "frames"
    $videoFramesDir = Join-Path $artifactDir "video-frames"
    $stdoutPath = Join-Path $artifactDir "launch.out.txt"
    $stderrPath = Join-Path $artifactDir "launch.err.txt"
    $ffmpegOutPath = Join-Path $artifactDir "ffmpeg.out.txt"
    $ffmpegErrPath = Join-Path $artifactDir "ffmpeg.err.txt"

    New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
    Remove-Item -LiteralPath $framesDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $videoFramesDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $framesDir | Out-Null
    New-Item -ItemType Directory -Force -Path $videoFramesDir | Out-Null
    Remove-Item -LiteralPath $posterPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $videoPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $ffmpegOutPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $ffmpegErrPath -Force -ErrorAction SilentlyContinue

    Write-Host "== Recording $id ($binding)"

    $step = [int]($FrameCount / $targetFrameCount)
    $videoFrameNumbers = New-Object System.Collections.Generic.List[int]
    for ($frame = $step; $frame -le $FrameCount; $frame += $step) {
        $videoFrameNumbers.Add([int]$frame)
    }
    $frames = New-Object System.Collections.Generic.List[int]
    $frames.AddRange($videoFrameNumbers)
    if (!$frames.Contains($ScreenshotFrame)) {
        $frames.Add($ScreenshotFrame)
    }
    $frames = @($frames | Sort-Object -Unique)

    $env:LUDOTS_TAKE_SCREENSHOT_PATH = (Join-Path $framesDir "frame.png")
    $env:LUDOTS_TAKE_SCREENSHOT_FRAMES = ($frames -join ",")
    $env:LUDOTS_TAKE_SCREENSHOT_FRAME = [string]$ScreenshotFrame
    $env:LUDOTS_MIN_RUNTIME_MS_BEFORE_SCREENSHOT = "0"
    $env:LUDOTS_AUTO_EXIT_FRAME = [string]($FrameCount + 8)
    $env:LUDOTS_RAYLIB_AUTO_ORBIT_DEG_PER_SEC = "0"
    $env:LUDOTS_RAYLIB_LIGHTWEIGHT_DIAGNOSTIC_HUD = "0"
    $fixedDeltaSeconds = $VideoSeconds / [double]$FrameCount
    $env:LUDOTS_RAYLIB_FIXED_FRAME_DELTA_SECONDS = $fixedDeltaSeconds.ToString(
        "R",
        [Globalization.CultureInfo]::InvariantCulture)

    $launch = Start-Process -FilePath $dotnetPath `
        -ArgumentList ($launcherCliPrefix + @("launch", $binding, "--adapter", "raylib", "--build", "never", "--wait")) `
        -WorkingDirectory $repo `
        -PassThru `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -WindowStyle Hidden

    $launch.WaitForExit(180000) | Out-Null
    if (!$launch.HasExited) {
        Stop-ProcessTree -RootProcessId $launch.Id
        throw "Raylib launch did not exit for showcase $id."
    }

    if ($null -ne $launch.ExitCode -and $launch.ExitCode -ne 0) {
        throw "Raylib launch failed for showcase $id with exit code $($launch.ExitCode). See $stderrPath"
    }

    $capturedFrames = @(Get-ChildItem -LiteralPath $framesDir -Filter "frame_*.png" | Sort-Object Name)
    if ($capturedFrames.Count -eq 0) {
        throw "No Raylib screenshots were created for showcase $id in $framesDir"
    }

    $capturedFrameNumbers = @($capturedFrames | ForEach-Object {
        if ($_.BaseName -notmatch '_f(?<frame>\d{4})$') {
            throw "Unexpected screenshot file name for showcase ${id}: $($_.Name)"
        }
        [int]$Matches.frame
    })
    if (($capturedFrameNumbers -join ',') -ne ($frames -join ',')) {
        throw "Showcase $id captured frames $($capturedFrameNumbers -join ',') but expected $($frames -join ',')."
    }

    $posterSource = @($capturedFrames | Where-Object { $_.BaseName -match ('_f{0:0000}$' -f $ScreenshotFrame) })
    if ($posterSource.Count -ne 1) {
        throw "Showcase $id did not produce exactly one poster source at frame $ScreenshotFrame."
    }
    Copy-Item -LiteralPath $posterSource[0].FullName -Destination $posterPath -Force

    for ($index = 0; $index -lt $videoFrameNumbers.Count; $index++) {
        $frameNumber = $videoFrameNumbers[$index]
        $videoFrame = @($capturedFrames | Where-Object { $_.BaseName -match ('_f{0:0000}$' -f $frameNumber) })
        if ($videoFrame.Count -ne 1) {
            throw "Showcase $id did not produce exactly one video source at frame $frameNumber."
        }
        $dest = Join-Path $videoFramesDir ("frame_{0:0000}.png" -f ($index + 1))
        Copy-Item -LiteralPath $videoFrame[0].FullName -Destination $dest -Force
    }

    $ffmpegArgs = @(
        "-hide_banner",
        "-y",
        "-framerate", [string]$Framerate,
        "-i", (Join-Path $videoFramesDir "frame_%04d.png"),
        "-vf", "scale=1280:-2",
        "-c:v", "libx264",
        "-pix_fmt", "yuv420p",
        "-movflags", "+faststart",
        $videoPath
    )

    $capture = Start-Process -FilePath $ffmpegPath `
        -ArgumentList $ffmpegArgs `
        -WorkingDirectory $repo `
        -PassThru `
        -RedirectStandardOutput $ffmpegOutPath `
        -RedirectStandardError $ffmpegErrPath `
        -WindowStyle Hidden

    $capture.WaitForExit(60000) | Out-Null
    if (!$capture.HasExited) {
        $capture | Stop-Process -Force
        throw "ffmpeg did not exit for showcase $id."
    }

    if ($null -ne $capture.ExitCode -and $capture.ExitCode -ne 0) {
        throw "ffmpeg failed for showcase $id with exit code $($capture.ExitCode). See $ffmpegErrPath"
    }

    if (!(Test-Path -LiteralPath $videoPath)) {
        throw "Expected recording was not created: $videoPath"
    }

    if (!(Test-Path -LiteralPath $posterPath)) {
        throw "Expected poster screenshot was not created: $posterPath"
    }

    $probeJson = & $ffprobePath `
        -v error `
        -count_frames `
        -select_streams v:0 `
        -show_entries stream=nb_read_frames,duration `
        -of json `
        $videoPath
    if ($LASTEXITCODE -ne 0) {
        throw "ffprobe failed for showcase $id with exit code $LASTEXITCODE."
    }
    $probe = $probeJson | ConvertFrom-Json
    $stream = @($probe.streams)[0]
    if ($null -eq $stream -or [int]$stream.nb_read_frames -ne $targetFrameCount) {
        throw "Showcase $id video frame count is '$($stream.nb_read_frames)'; expected $targetFrameCount."
    }
    $duration = [double]::Parse(
        [string]$stream.duration,
        [Globalization.CultureInfo]::InvariantCulture)
    if ([Math]::Abs($duration - $VideoSeconds) -gt (1.0 / $Framerate)) {
        throw "Showcase $id video duration is $duration seconds; expected $VideoSeconds."
    }

      $existingById[$id] = [pscustomobject]@{
          id = $id
          binding = $binding
          video = $videoPath
          poster = $posterPath
          authoringSha256 = $authoringSha256
          generatorSha256 = $generatorSha256
      }
    }
}

$requireComplete = $FinalizeOnly -or $Ids.Count -eq 0
$missingResults = New-Object System.Collections.Generic.List[string]
$results = @($manifest | ForEach-Object {
    $manifestId = [string]$_.id
    if (!$existingById.ContainsKey($manifestId)) {
        $missingResults.Add($manifestId)
        return
    }

    $entry = $existingById[$manifestId]
    if ([string]$entry.authoringSha256 -ne $authoringSha256 -or [string]$entry.generatorSha256 -ne $generatorSha256) {
        $missingResults.Add($manifestId)
        return
    }
    if (!(Test-Path -LiteralPath ([string]$entry.video)) -or !(Test-Path -LiteralPath ([string]$entry.poster))) {
        $missingResults.Add($manifestId)
        return
    }
    $entry
})
if ($requireComplete -and $missingResults.Count -gt 0) {
    throw "Recording summary is incomplete after capture: missing $($missingResults -join ', ')"
}
$summaryJson = ConvertTo-Json -InputObject @($results) -Depth 4
[IO.File]::WriteAllText($summaryPath, $summaryJson + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))

& node $generatorPath --authoring $authoringPath
if ($LASTEXITCODE -ne 0) {
    throw "Report regeneration failed after recording with exit code $LASTEXITCODE."
}

Add-Type -AssemblyName System.Drawing
$columns = 5
$rows = [Math]::Ceiling($results.Count / $columns)
$tileWidth = 320
$tileHeight = 180
$labelHeight = 22
$sheet = New-Object System.Drawing.Bitmap ($columns * $tileWidth), ($rows * $tileHeight)
$graphics = [System.Drawing.Graphics]::FromImage($sheet)
$graphics.Clear([System.Drawing.Color]::FromArgb(5, 9, 14))
$labelBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(12, 19, 28))
$textBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(240, 245, 250))
$font = New-Object System.Drawing.Font "Segoe UI", 8
try {
    for ($index = 0; $index -lt $results.Count; $index++) {
        $x = ($index % $columns) * $tileWidth
        $y = [Math]::Floor($index / $columns) * $tileHeight
        $image = [System.Drawing.Image]::FromFile([string]$results[$index].poster)
        try {
            $graphics.DrawImage($image, $x, $y, $tileWidth, $tileHeight - $labelHeight)
        } finally {
            $image.Dispose()
        }
        $graphics.FillRectangle($labelBrush, $x, $y + $tileHeight - $labelHeight, $tileWidth, $labelHeight)
        $graphics.DrawString(("{0:00} {1}" -f ($index + 1), [string]$results[$index].id), $font, $textBrush, $x + 8, $y + $tileHeight - 18)
    }

    $contactSheetPath = Join-Path $repo "artifacts/raylib-performer-micro-showcases/poster-contact-sheet.png"
    $sheet.Save($contactSheetPath, [System.Drawing.Imaging.ImageFormat]::Png)
} finally {
    $font.Dispose()
    $textBrush.Dispose()
    $labelBrush.Dispose()
    $graphics.Dispose()
    $sheet.Dispose()
}

if ($FinalizeOnly) {
    Write-Host "Verified $($results.Count) total showcase recordings. Summary: $summaryPath"
} elseif ($missingResults.Count -gt 0) {
    Write-Host "Recorded $($selected.Count) showcase(s); $($missingResults.Count) remain. Partial summary: $summaryPath"
} else {
    Write-Host "Recorded $($selected.Count) showcase(s); verified $($results.Count) total. Summary: $summaryPath"
}
