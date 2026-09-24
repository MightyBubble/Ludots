param(
    [string[]]$Ids = @(),
    [switch]$SkipBuild,
    [switch]$FinalizeOnly
)

$ErrorActionPreference = "Stop"
if ($SkipBuild -and $FinalizeOnly) { throw "SkipBuild and FinalizeOnly cannot be combined." }

$repo = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repo "mods/showcases/performer_raylib_micro_showcases/PerformerRaylibMicroShowcasesMod/assets/Presentation/showcases.manifest.json"
$authoringPath = Join-Path $repo "mods/showcases/performer_raylib_micro_showcases/PerformerRaylibMicroShowcasesMod/assets/Presentation/Authoring/raylib-micro-showcases.authoring.json"
$authoringContractPath = Join-Path $repo "mods/showcases/performer_raylib_micro_showcases/PerformerRaylibMicroShowcasesMod/assets/Presentation/Authoring/raylib-micro-showcases.contract.json"
$generatorPath = Join-Path $repo "scripts/generate-raylib-micro-assets.mjs"
$runtimeInputHashScript = Join-Path $repo "scripts/raylib-micro-runtime-inputs.mjs"
$recordingEvidenceScript = Join-Path $repo "scripts/raylib-micro-recording-evidence.mjs"
$dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source

function Stop-ProcessTree {
    param([int]$RootProcessId)

    $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $RootProcessId" -ErrorAction Stop)
    foreach ($child in $children) {
        Stop-ProcessTree -RootProcessId ([int]$child.ProcessId)
    }
    try {
        $process = [Diagnostics.Process]::GetProcessById($RootProcessId)
    } catch [ArgumentException] {
        return
    }
    Stop-Process -InputObject $process -Force -ErrorAction Stop
}

foreach ($requiredInput in @($manifestPath, $authoringPath, $authoringContractPath, $generatorPath, $runtimeInputHashScript, $recordingEvidenceScript)) {
    if (!(Test-Path -LiteralPath $requiredInput)) {
        throw "Raylib micro showcase recording input not found: $requiredInput"
    }
}

$authoringContract = Get-Content -LiteralPath $authoringContractPath -Raw -Encoding UTF8 | ConvertFrom-Json
$recording = $authoringContract.recording
$artifactRoot = [string]$recording.artifactRoot
$FrameCount = [int]$recording.captureFrameCount
$ScreenshotFrame = [int]$recording.posterFrame
$Framerate = [int]$recording.framesPerSecond
$VideoSeconds = [int]$recording.durationSeconds
$VideoWidth = [int]$recording.width
$VideoHeight = [int]$recording.height
$AutoExitGraceFrames = [int]$recording.autoExitGraceFrames
$targetFrameCount = $Framerate * $VideoSeconds
$launcherCliProject = Join-Path $repo ([string]$recording.launcherCliProject)
$launcherCli = Join-Path $repo (Join-Path ([string]$recording.launcherCliOutput) ([string]$recording.launcherCliEntryAssembly))
$raylibApp = Join-Path $repo (Join-Path ([string]$recording.raylibAppOutput) ([string]$recording.raylibAppEntryAssembly))
$launcherUserConfigPath = Join-Path $repo ([string]$recording.launcherUserConfig)
$launcherUserConfigEnvironmentVariable = [string]$recording.launcherUserConfigEnvironmentVariable
$launcherCliPrefix = @("exec", "--roll-forward", "Major", $launcherCli)
$launcherCliBuildTimeoutMilliseconds = [int]$recording.launcherCliBuildTimeoutMilliseconds
$raylibAppBuildTimeoutMilliseconds = [int]$recording.raylibAppBuildTimeoutMilliseconds
$entryBuildTimeoutMilliseconds = [int]$recording.entryBuildTimeoutMilliseconds
$launchTimeoutMilliseconds = [int]$recording.launchTimeoutMilliseconds
$encodeTimeoutMilliseconds = [int]$recording.encodeTimeoutMilliseconds
$contactSheetColumns = [int]$recording.contactSheetColumns
$contactSheetTileWidth = [int]$recording.contactSheetTileWidth
$contactSheetTileHeight = [int]$recording.contactSheetTileHeight
$contactSheetLabelHeight = [int]$recording.contactSheetLabelHeight
$workRoot = Join-Path $repo ([string]$recording.workRoot)
$logRoot = Join-Path $repo ([string]$recording.logRoot)
$summaryPath = Join-Path $repo ([string]$recording.recordingSummaryPath)
$contactSheetPath = Join-Path $repo ([string]$recording.contactSheetPath)
$captureEnvironment = $recording.captureEnvironmentVariables

foreach ($requiredInput in @($launcherCliProject, $launcherUserConfigPath)) {
    if (!(Test-Path -LiteralPath $requiredInput -PathType Leaf)) {
        throw "Raylib micro showcase recording input not found: $requiredInput"
    }
}

try {
    $launcherUserConfig = Get-Content -LiteralPath $launcherUserConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
} catch {
    throw "Launcher user config must be valid JSON: $launcherUserConfigPath. $($_.Exception.Message)"
}
if ($null -eq $launcherUserConfig -or
    $launcherUserConfig -is [Array] -or
    @($launcherUserConfig.PSObject.Properties).Count -ne 0) {
    throw "Launcher user config must be an explicit empty JSON object: $launcherUserConfigPath"
}
$managedEnvironmentNames = @($launcherUserConfigEnvironmentVariable) + @(
    $captureEnvironment.PSObject.Properties | ForEach-Object { [string]$_.Value })
$previousEnvironment = @{}
foreach ($environmentName in $managedEnvironmentNames) {
    $previousEnvironment[$environmentName] = [Environment]::GetEnvironmentVariable(
        $environmentName,
        [EnvironmentVariableTarget]::Process)
}

try {
    [Environment]::SetEnvironmentVariable(
        $launcherUserConfigEnvironmentVariable,
        $launcherUserConfigPath,
        [EnvironmentVariableTarget]::Process)
    if ([Environment]::GetEnvironmentVariable($launcherUserConfigEnvironmentVariable) -ne $launcherUserConfigPath) {
        throw "Failed to set the explicit Launcher user config for this recording process."
    }

& node $generatorPath --authoring $authoringPath --skip-report
if ($LASTEXITCODE -ne 0) {
    throw "Raylib micro showcase generation failed before recording with exit code $LASTEXITCODE."
}

if (!(Test-Path -LiteralPath $launcherCliProject)) {
    throw "Launcher CLI project not found: $launcherCliProject"
}

$ffmpegPath = (Get-Command ffmpeg -ErrorAction Stop).Source
$null = (Get-Command ffprobe -ErrorAction Stop).Source

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

New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
New-Item -ItemType Directory -Force -Path $workRoot | Out-Null

if (!$FinalizeOnly -and !$SkipBuild) {
    $cliBuildOutPath = Join-Path $logRoot "build-launcher-cli.out.txt"
    $cliBuildErrPath = Join-Path $logRoot "build-launcher-cli.err.txt"
    $buildOutPath = Join-Path $logRoot "build-selected.out.txt"
    $buildErrPath = Join-Path $logRoot "build-selected.err.txt"
    $appBuildOutPath = Join-Path $logRoot "build-app.out.txt"
    $appBuildErrPath = Join-Path $logRoot "build-app.err.txt"
    Write-Host "== Building launcher CLI once"
    $cliBuildProcess = Start-Process -FilePath $dotnetPath `
        -ArgumentList @("build", $launcherCliProject, "-c", "Release", "-nologo", "-clp:ErrorsOnly") `
        -WorkingDirectory $repo `
        -PassThru `
        -RedirectStandardOutput $cliBuildOutPath `
        -RedirectStandardError $cliBuildErrPath `
        -WindowStyle Hidden
    $cliBuildProcess.WaitForExit($launcherCliBuildTimeoutMilliseconds) | Out-Null
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
    $appBuildProcess.WaitForExit($raylibAppBuildTimeoutMilliseconds) | Out-Null
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
    $buildProcess.WaitForExit($entryBuildTimeoutMilliseconds) | Out-Null
    if (!$buildProcess.HasExited) {
        Stop-ProcessTree -RootProcessId $buildProcess.Id
        throw "Raylib micro showcase prebuild did not exit. See $buildOutPath and $buildErrPath"
    }

    if ($null -ne $buildProcess.ExitCode -and $buildProcess.ExitCode -ne 0) {
        throw "Raylib micro showcase prebuild failed with exit code $($buildProcess.ExitCode). See $buildErrPath"
    }
}

if ($SkipBuild) {
    foreach ($requiredBinary in @($launcherCli, $raylibApp)) {
        if (!(Test-Path -LiteralPath $requiredBinary)) {
            throw "SkipBuild requires an existing Release binary: $requiredBinary"
        }
    }
}

$runtimeInputSha256 = (& node $runtimeInputHashScript --repo $repo).Trim()
if ($LASTEXITCODE -ne 0 -or $runtimeInputSha256 -notmatch '^[a-f0-9]{64}$') {
    throw "Raylib micro runtime input fingerprint failed: '$runtimeInputSha256'."
}

if (!$FinalizeOnly) {
  foreach ($item in $selected) {
    $id = [string]$item.id
    $binding = [string]$item.binding
    $artifactDir = Join-Path $repo ([string]$item.artifactDir)
    $posterPath = Join-Path $repo ([string]$item.poster)
    $videoPath = Join-Path $repo ([string]$item.recording)
    $workDir = Join-Path $workRoot $id
    $entryLogDir = Join-Path $logRoot $id
    $framesDir = Join-Path $workDir "frames"
    $videoFramesDir = Join-Path $workDir "video-frames"
    $stdoutPath = Join-Path $entryLogDir "launch.out.txt"
    $stderrPath = Join-Path $entryLogDir "launch.err.txt"
    $ffmpegOutPath = Join-Path $entryLogDir "ffmpeg.out.txt"
    $ffmpegErrPath = Join-Path $entryLogDir "ffmpeg.err.txt"

    New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
    New-Item -ItemType Directory -Force -Path $entryLogDir | Out-Null
    if (Test-Path -LiteralPath $workDir) {
        Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction Stop
    }
    New-Item -ItemType Directory -Force -Path $framesDir | Out-Null
    New-Item -ItemType Directory -Force -Path $videoFramesDir | Out-Null
    foreach ($file in @($posterPath, $videoPath, $ffmpegOutPath, $ffmpegErrPath)) {
        if (Test-Path -LiteralPath $file) {
            Remove-Item -LiteralPath $file -Force -ErrorAction Stop
        }
    }

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

    [Environment]::SetEnvironmentVariable([string]$captureEnvironment.screenshotPath, (Join-Path $framesDir "frame.png"), "Process")
    [Environment]::SetEnvironmentVariable([string]$captureEnvironment.screenshotFrames, ($frames -join ","), "Process")
    [Environment]::SetEnvironmentVariable([string]$captureEnvironment.screenshotFrame, [string]$ScreenshotFrame, "Process")
    $disabledNumericFeature = "0"
    [Environment]::SetEnvironmentVariable([string]$captureEnvironment.minimumRuntimeMilliseconds, $disabledNumericFeature, "Process")
    [Environment]::SetEnvironmentVariable([string]$captureEnvironment.autoExitFrame, [string]($FrameCount + $AutoExitGraceFrames), "Process")
    [Environment]::SetEnvironmentVariable([string]$captureEnvironment.autoOrbitDegreesPerSecond, $disabledNumericFeature, "Process")
    [Environment]::SetEnvironmentVariable([string]$captureEnvironment.lightweightDiagnosticHud, $disabledNumericFeature, "Process")
    $fixedDeltaSeconds = $VideoSeconds / [double]$FrameCount
    [Environment]::SetEnvironmentVariable(
        [string]$captureEnvironment.fixedFrameDeltaSeconds,
        $fixedDeltaSeconds.ToString("R", [Globalization.CultureInfo]::InvariantCulture),
        "Process")

    $launch = Start-Process -FilePath $dotnetPath `
        -ArgumentList ($launcherCliPrefix + @("launch", $binding, "--adapter", "raylib", "--build", "never", "--wait")) `
        -WorkingDirectory $repo `
        -PassThru `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -WindowStyle Hidden

    $launch.WaitForExit($launchTimeoutMilliseconds) | Out-Null
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
        "-vf", "scale=${VideoWidth}:${VideoHeight}",
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

    $capture.WaitForExit($encodeTimeoutMilliseconds) | Out-Null
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

      & node $recordingEvidenceScript `
          --repo $repo `
          --runtime-input-sha256 $runtimeInputSha256 `
          --update $id
      if ($LASTEXITCODE -ne 0) {
          throw "Recording evidence update failed for showcase $id with exit code $LASTEXITCODE."
      }
      Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction Stop
    }
}

$requireComplete = $FinalizeOnly -or $Ids.Count -eq 0
$evidenceArgs = @(
    $recordingEvidenceScript,
    "--repo", $repo,
    "--runtime-input-sha256", $runtimeInputSha256,
    "--validate"
)
& node @evidenceArgs
if ($LASTEXITCODE -ne 0) {
    throw "Recording evidence finalization failed with exit code $LASTEXITCODE."
}

$summary = Get-Content -LiteralPath $summaryPath -Raw -Encoding UTF8 | ConvertFrom-Json
$results = @($summary | Where-Object { [string]$_.runtimeInputSha256 -eq $runtimeInputSha256 })
$resultIds = @($results | ForEach-Object { [string]$_.id })
$missingResults = @($manifest | Where-Object { $resultIds -notcontains [string]$_.id } | ForEach-Object { [string]$_.id })
if ($requireComplete -and $missingResults.Count -gt 0) {
    throw "Current recording evidence is incomplete: $($missingResults -join ', ')"
}

& node $generatorPath --authoring $authoringPath
if ($LASTEXITCODE -ne 0) {
    throw "Report regeneration failed after recording with exit code $LASTEXITCODE."
}

Add-Type -AssemblyName System.Drawing
$rows = [Math]::Ceiling($results.Count / $contactSheetColumns)
$sheetWidth = [int]($contactSheetColumns * $contactSheetTileWidth)
$sheetHeight = [int]($rows * $contactSheetTileHeight)
$sheet = [System.Drawing.Bitmap]::new($sheetWidth, $sheetHeight)
$graphics = [System.Drawing.Graphics]::FromImage($sheet)
$graphics.Clear([System.Drawing.Color]::FromArgb(5, 9, 14))
$labelBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(12, 19, 28))
$textBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(240, 245, 250))
$font = New-Object System.Drawing.Font "Segoe UI", 8
try {
    for ($index = 0; $index -lt $results.Count; $index++) {
        $x = ($index % $contactSheetColumns) * $contactSheetTileWidth
        $y = [Math]::Floor($index / $contactSheetColumns) * $contactSheetTileHeight
        $image = [System.Drawing.Image]::FromFile((Join-Path $repo ([string]$results[$index].poster.path)))
        try {
            $graphics.DrawImage($image, $x, $y, $contactSheetTileWidth, $contactSheetTileHeight - $contactSheetLabelHeight)
        } finally {
            $image.Dispose()
        }
        $graphics.FillRectangle($labelBrush, $x, $y + $contactSheetTileHeight - $contactSheetLabelHeight, $contactSheetTileWidth, $contactSheetLabelHeight)
        $graphics.DrawString(("{0:00} {1}" -f ($index + 1), [string]$results[$index].id), $font, $textBrush, $x + 8, $y + $contactSheetTileHeight - 18)
    }

    $sheet.Save($contactSheetPath, [System.Drawing.Imaging.ImageFormat]::Png)
} finally {
    $font.Dispose()
    $textBrush.Dispose()
    $labelBrush.Dispose()
    $graphics.Dispose()
    $sheet.Dispose()
}

$finalEvidenceArgs = @(
    $recordingEvidenceScript,
    "--repo", $repo,
    "--runtime-input-sha256", $runtimeInputSha256,
    "--validate"
)
if ($requireComplete) {
    $finalEvidenceArgs += "--require-complete"
}
& node @finalEvidenceArgs
if ($LASTEXITCODE -ne 0) {
    throw "Recording evidence post-report validation failed with exit code $LASTEXITCODE."
}

if ($FinalizeOnly) {
    Write-Host "Verified $($results.Count) total showcase recordings. Summary: $summaryPath"
} elseif ($missingResults.Count -gt 0) {
    Write-Host "Recorded $($selected.Count) showcase(s); $($missingResults.Count) remain. Partial summary: $summaryPath"
} else {
    Write-Host "Recorded $($selected.Count) showcase(s); verified $($results.Count) total. Summary: $summaryPath"
}
} finally {
    foreach ($environmentName in $managedEnvironmentNames) {
        [Environment]::SetEnvironmentVariable(
            $environmentName,
            $previousEnvironment[$environmentName],
            [EnvironmentVariableTarget]::Process)
    }
}
