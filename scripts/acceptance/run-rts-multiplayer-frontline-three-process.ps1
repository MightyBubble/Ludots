param(
    [string]$ProfilePath = (Join-Path $PSScriptRoot "rts-multiplayer-frontline-three-process.profile.json"),
    [string]$ArtifactDirectory = "",
    [string]$HostAddress = "",
    [int]$Port = 0,
    [string]$ConnectionKey = "",
    [string]$FaultProfile = "",
    [int]$CredentialTimeoutSeconds = 0,
    [int]$RunSeconds = -1,
    [switch]$LoadPresentationEvidenceFunctionsOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$script:AcceptanceContaminatingEnvironmentVariables = @(
    "LUDOTS_AUTO_EXIT_FRAME",
    "LUDOTS_MIN_RUNTIME_MS_BEFORE_SCREENSHOT",
    "LUDOTS_TAKE_SCREENSHOT_PATH",
    "LUDOTS_TAKE_SCREENSHOT_FRAME",
    "LUDOTS_TAKE_SCREENSHOT_FRAMES",
    "LUDOTS_TAKE_SCREENSHOT_MILESTONES",
    "LUDOTS_RAYLIB_DIAGNOSTIC_PATH",
    "LUDOTS_RAYLIB_TIMING_LOG_INTERVAL_FRAMES",
    "LUDOTS_RAYLIB_TIMING_SYSTEM_BREAKDOWN",
    "LUDOTS_RAYLIB_LIGHTWEIGHT_DIAGNOSTIC_HUD",
    "LUDOTS_RAYLIB_AUTO_ORBIT_DEG_PER_SEC",
    "LUDOTS_RAYLIB_SYNTHETIC_UI_PLAYBACK",
    "LUDOTS_RAYLIB_SYNTHETIC_UI_START_FRAME",
    "LUDOTS_RAYLIB_SYNTHETIC_UI_END_FRAME",
    "LUDOTS_RAYLIB_SYNTHETIC_UI_START_X",
    "LUDOTS_RAYLIB_SYNTHETIC_UI_START_Y",
    "LUDOTS_RAYLIB_SYNTHETIC_UI_END_X",
    "LUDOTS_RAYLIB_SYNTHETIC_UI_END_Y",
    "LUDOTS_RAYLIB_SYNTHETIC_UI_SCROLL_FRAME",
    "LUDOTS_RAYLIB_SYNTHETIC_UI_SCROLL_DELTA_Y",
    "LUDOTS_RAYLIB_SYNTHETIC_UI_KEY_FRAME",
    "LUDOTS_RAYLIB_SYNTHETIC_UI_KEY",
    "LUDOTS_RAYLIB_SYNTHETIC_UI_KEY_TEXT"
)

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $script:RepoRoot $Path))
}

function Get-DotnetCommand {
    $command = Get-Command "dotnet" -CommandType Application -ErrorAction Stop | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace([string]$command.Source) -or
        [System.IO.Path]::GetExtension([string]$command.Source) -ne ".exe") {
        throw "A directly executable dotnet host is required."
    }

    return [string]$command.Source
}

function ConvertTo-NativeArgument {
    param([AllowEmptyString()][Parameter(Mandatory = $true)][string]$Value)

    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') {
        return $Value
    }

    $builder = [System.Text.StringBuilder]::new()
    [void]$builder.Append('"')
    $backslashCount = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq [char]'\') {
            $backslashCount++
            continue
        }

        if ($character -eq [char]'"') {
            if ($backslashCount -gt 0) {
                [void]$builder.Append(('\' * ($backslashCount * 2)))
            }
            [void]$builder.Append('\"')
            $backslashCount = 0
            continue
        }

        if ($backslashCount -gt 0) {
            [void]$builder.Append(('\' * $backslashCount))
            $backslashCount = 0
        }
        [void]$builder.Append($character)
    }

    if ($backslashCount -gt 0) {
        [void]$builder.Append(('\' * ($backslashCount * 2)))
    }
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Start-CapturedProcess {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$StdoutPath,
        [Parameter(Mandatory = $true)][string]$StderrPath,
        [System.Collections.IDictionary]$EnvironmentVariables = @{}
    )

    $stdoutDirectory = Split-Path -Parent $StdoutPath
    $stderrDirectory = Split-Path -Parent $StderrPath
    New-Item -ItemType Directory -Path $stdoutDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $stderrDirectory -Force | Out-Null

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.Arguments = (($Arguments | ForEach-Object { ConvertTo-NativeArgument -Value $_ }) -join " ")
    foreach ($variableName in $script:AcceptanceContaminatingEnvironmentVariables) {
        [void]$startInfo.Environment.Remove($variableName)
    }
    foreach ($entry in $EnvironmentVariables.GetEnumerator()) {
        $environmentVariableName = [string]$entry.Key
        $environmentVariableValue = [string]$entry.Value
        if ([string]::IsNullOrWhiteSpace($environmentVariableName) -or
            [string]::IsNullOrWhiteSpace($environmentVariableValue)) {
            throw "Process '$Name' environment variable names and values must be non-empty."
        }
        $startInfo.Environment[$environmentVariableName] = $environmentVariableValue
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $stdoutStream = $null
    $stderrStream = $null
    $stdoutCopy = $null
    $stderrCopy = $null
    $processStarted = $false
    try {
        $stdoutStream = [System.IO.File]::Create($StdoutPath)
        $stderrStream = [System.IO.File]::Create($StderrPath)
        if (-not $process.Start()) {
            throw "Failed to start process '$Name'."
        }
        $processStarted = $true
        $stdoutCopy = $process.StandardOutput.BaseStream.CopyToAsync($stdoutStream)
        $stderrCopy = $process.StandardError.BaseStream.CopyToAsync($stderrStream)

        return [pscustomobject]@{
            Name = $Name
            Process = $process
            Pid = $process.Id
            StartedAtUtcTicks = $process.StartTime.ToUniversalTime().Ticks
            StdoutPath = $StdoutPath
            StderrPath = $StderrPath
            StdoutStream = $stdoutStream
            StderrStream = $stderrStream
            StdoutCopy = $stdoutCopy
            StderrCopy = $stderrCopy
            CaptureCompleted = $false
        }
    }
    catch {
        $startFailure = $_.Exception
        $cleanupFailure = $null
        if ($processStarted) {
            try {
                if (-not $process.HasExited) {
                    $process.Kill()
                    if (-not $process.WaitForExit(5000)) {
                        throw "Process '$Name' did not exit while recovering from a start/capture failure."
                    }
                }
                if ($null -ne $stdoutCopy) { [void]$stdoutCopy.GetAwaiter().GetResult() }
                if ($null -ne $stderrCopy) { [void]$stderrCopy.GetAwaiter().GetResult() }
            }
            catch {
                $cleanupFailure = $_.Exception.Message
            }
        }
        if ($null -ne $stdoutStream) { $stdoutStream.Dispose() }
        if ($null -ne $stderrStream) { $stderrStream.Dispose() }
        $process.Dispose()
        if ($null -ne $cleanupFailure) {
            throw [System.InvalidOperationException]::new(
                "Failed to start captured process '$Name': $($startFailure.Message) Cleanup also failed: $cleanupFailure",
                $startFailure)
        }
        throw $startFailure
    }
}

function Complete-ProcessCapture {
    param([Parameter(Mandatory = $true)]$OwnedProcess)

    if ($OwnedProcess.CaptureCompleted) {
        return
    }

    try {
        [void]$OwnedProcess.StdoutCopy.GetAwaiter().GetResult()
        [void]$OwnedProcess.StderrCopy.GetAwaiter().GetResult()
    }
    finally {
        $OwnedProcess.StdoutStream.Dispose()
        $OwnedProcess.StderrStream.Dispose()
        $OwnedProcess.CaptureCompleted = $true
    }
}

function Invoke-CheckedProcess {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$StdoutPath,
        [Parameter(Mandatory = $true)][string]$StderrPath
    )

    $owned = Start-CapturedProcess -Name $Name -FilePath $FilePath -Arguments $Arguments `
        -WorkingDirectory $WorkingDirectory -StdoutPath $StdoutPath -StderrPath $StderrPath
    $owned.Process.WaitForExit()
    Complete-ProcessCapture -OwnedProcess $owned
    if ($owned.Process.ExitCode -ne 0) {
        throw "Process '$Name' failed with exit code $($owned.Process.ExitCode). See $StdoutPath and $StderrPath."
    }
}

function Assert-OwnedProcessesAlive {
    param([Parameter(Mandatory = $true)][System.Collections.IEnumerable]$OwnedProcesses)

    foreach ($owned in $OwnedProcesses) {
        $owned.Process.Refresh()
        if ($owned.Process.HasExited) {
            throw "Process '$($owned.Name)' exited early with code $($owned.Process.ExitCode). See $($owned.StdoutPath) and $($owned.StderrPath)."
        }
    }
}

function Stop-OwnedProcess {
    param([Parameter(Mandatory = $true)]$OwnedProcess)

    $cleanupFailure = $null
    $current = $null
    try {
        $current = [System.Diagnostics.Process]::GetProcessById($OwnedProcess.Pid)
        $sameStart = $current.StartTime.ToUniversalTime().Ticks -eq $OwnedProcess.StartedAtUtcTicks
        if ($sameStart -and -not $current.HasExited) {
            $current.Kill()
            if (-not $current.WaitForExit(5000)) {
                $cleanupFailure = "Owned process '$($OwnedProcess.Name)' did not exit within the cleanup timeout."
            }
        }
    }
    catch [System.ArgumentException] {
    }
    catch {
        $cleanupFailure = "Failed to stop owned process '$($OwnedProcess.Name)': $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $current) {
            $current.Dispose()
        }
    }

    $OwnedProcess.Process.Refresh()
    if (-not $OwnedProcess.Process.HasExited -and -not $OwnedProcess.Process.WaitForExit(5000)) {
        $cleanupFailure = "Owned process '$($OwnedProcess.Name)' is still running after cleanup."
    }

    if ($null -ne $cleanupFailure) {
        $OwnedProcess.StdoutStream.Dispose()
        $OwnedProcess.StderrStream.Dispose()
        $OwnedProcess.CaptureCompleted = $true
        $OwnedProcess.Process.Dispose()
        throw $cleanupFailure
    }

    Complete-ProcessCapture -OwnedProcess $OwnedProcess
    $OwnedProcess.Process.Dispose()
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Path,
        [int]$Depth = 100
    )

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $json = $Value | ConvertTo-Json -Depth $Depth
    [System.IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
}

function Add-ManifestProcess {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Manifest,
        [Parameter(Mandatory = $true)]$OwnedProcess,
        [Parameter(Mandatory = $true)][string]$ManifestPath
    )

    $Manifest["processes"] = @($Manifest["processes"]) + @([ordered]@{
        name = $OwnedProcess.Name
        pid = $OwnedProcess.Pid
        startedAtUtcTicks = $OwnedProcess.StartedAtUtcTicks
        stdout = $OwnedProcess.StdoutPath
        stderr = $OwnedProcess.StderrPath
    })
    Write-JsonFile -Value $Manifest -Path $ManifestPath
}

function Get-Sha256Hex {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][byte[]]$Bytes)

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        [byte[]]$digest = $sha256.ComputeHash($Bytes)
    }
    finally {
        $sha256.Dispose()
    }

    return [System.BitConverter]::ToString($digest).Replace("-", "").ToLowerInvariant()
}

function Get-FileEvidence {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Evidence file is missing: $fullPath"
    }

    [byte[]]$bytes = [System.IO.File]::ReadAllBytes($fullPath)
    return [ordered]@{
        path = $fullPath
        length = $bytes.Length
        sha256 = Get-Sha256Hex -Bytes $bytes
    }
}

function Get-StringSha256 {
    param([Parameter(Mandatory = $true)][string]$Value)

    return Get-Sha256Hex -Bytes ([System.Text.Encoding]::UTF8.GetBytes($Value))
}

function Resolve-ArtifactChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$ArtifactDirectory,
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$Owner
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [System.IO.Path]::IsPathRooted($RelativePath)) {
        throw "$Owner must be a non-empty path relative to the acceptance artifact directory."
    }

    $artifactPrefix = [System.IO.Path]::GetFullPath($ArtifactDirectory).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $ArtifactDirectory $RelativePath))
    if (-not $fullPath.StartsWith($artifactPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Owner resolves outside the acceptance artifact directory: $fullPath"
    }
    return $fullPath
}

function New-ClientScreenshotCapture {
    param(
        [Parameter(Mandatory = $true)][string]$ProcessName,
        [Parameter(Mandatory = $true)]$Configuration,
        [Parameter(Mandatory = $true)][string]$ArtifactDirectory
    )

    if ($null -eq $Configuration) {
        throw "Screenshot configuration is required for '$ProcessName'."
    }
    $targetPath = Resolve-ArtifactChildPath -ArtifactDirectory $ArtifactDirectory `
        -RelativePath ([string]$Configuration.path) -Owner "$ProcessName screenshot path"
    $milestones = @($Configuration.milestones | ForEach-Object { [string]$_ })
    if ($milestones.Count -eq 0) {
        throw "Screenshot milestones for '$ProcessName' must not be empty."
    }
    $seenMilestones = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($milestone in $milestones) {
        if ($milestone -cnotmatch '^[A-Za-z0-9._-]+$') {
            throw "Screenshot milestone '$milestone' for '$ProcessName' must be a non-empty ASCII identifier."
        }
        if (-not $seenMilestones.Add($milestone)) {
            throw "Screenshot milestone '$milestone' for '$ProcessName' is duplicated."
        }
    }

    $extension = [System.IO.Path]::GetExtension($targetPath)
    if ([string]::IsNullOrWhiteSpace($extension) -or $extension -cne ".png") {
        throw "Screenshot path for '$ProcessName' must use the .png extension."
    }
    $directory = [System.IO.Path]::GetDirectoryName($targetPath)
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($targetPath)
    if ([string]::IsNullOrWhiteSpace($baseName)) {
        throw "Screenshot path for '$ProcessName' must include a file name."
    }

    $diagnosticPath = Resolve-ArtifactChildPath -ArtifactDirectory $ArtifactDirectory `
        -RelativePath (Join-Path $ProcessName "raylib-diagnostic.log") `
        -Owner "$ProcessName Raylib diagnostic path"

    $files = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $milestones.Count; $index++) {
        $fileName = "{0}_{1:000}_{2}{3}" -f $baseName, ($index + 1), $milestones[$index], $extension
        $files.Add([pscustomobject]@{
            ProcessName = $ProcessName
            Milestone = $milestones[$index]
            MilestoneIndex = $index
            Path = [System.IO.Path]::Combine($directory, $fileName)
            EvidencePath = [System.IO.Path]::ChangeExtension(
                [System.IO.Path]::Combine($directory, $fileName),
                ".evidence.json")
            DiagnosticPath = $diagnosticPath
        })
    }

    return [pscustomobject]@{
        ProcessName = $ProcessName
        TargetPath = $targetPath
        DiagnosticPath = $diagnosticPath
        Milestones = $milestones
        Files = @($files)
        EnvironmentVariables = [ordered]@{
            LUDOTS_TAKE_SCREENSHOT_PATH = $targetPath
            LUDOTS_TAKE_SCREENSHOT_MILESTONES = ($milestones -join ",")
            LUDOTS_RAYLIB_DIAGNOSTIC_PATH = $diagnosticPath
        }
    }
}

function Get-RequiredDiagnosticCount {
    param(
        [Parameter(Mandatory = $true)][string]$Line,
        [Parameter(Mandatory = $true)][string]$Field,
        [Parameter(Mandatory = $true)][string]$ProcessName,
        [Parameter(Mandatory = $true)][string]$Milestone
    )

    $pattern = "(?:^|\s)$([regex]::Escape($Field))=(?<value>\d+)(?:\s|$)"
    $match = [regex]::Match($Line, $pattern, [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $match.Success) {
        throw "Client '$ProcessName' Raylib diagnostic for screenshot milestone '$Milestone' lacks integer field '$Field'."
    }

    return [int]::Parse($match.Groups["value"].Value, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Read-ClientPresentationEvidence {
    param(
        [Parameter(Mandatory = $true)]$Capture,
        [Parameter(Mandatory = $true)]$Minimums,
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$RequiredReceipts
    )

    $diagnosticPath = [string]$Capture.DiagnosticPath
    if (-not (Test-Path -LiteralPath $diagnosticPath -PathType Leaf)) {
        throw "Client '$($Capture.ProcessName)' did not write its Raylib diagnostic: $diagnosticPath"
    }

    $lines = @([System.IO.File]::ReadAllLines($diagnosticPath))
    if ($lines.Count -eq 0) {
        throw "Client '$($Capture.ProcessName)' wrote an empty Raylib diagnostic: $diagnosticPath"
    }

    $expectedMilestones = @($Capture.Milestones | ForEach-Object { [string]$_ })
    $completionByMilestone = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    foreach ($line in $lines) {
        if ($line -notmatch '(?:^|\s)screenshot-complete(?:\s|$)') {
            continue
        }
        $completionMatch = [regex]::Match(
            $line,
            '^\[[^\]]+\]\s+screenshot-complete milestone=(?<milestone>[A-Za-z0-9._-]+) milestoneOrder=(?<order>\d+) milestoneRevision=(?<revision>\d+) frame=(?<frame>\d+) file=(?<file>[^\s]+) evidence=(?<evidence>[^\s]+)\s*$',
            [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
        if (-not $completionMatch.Success) {
            throw "Client '$($Capture.ProcessName)' wrote a malformed screenshot completion diagnostic."
        }
        $milestone = $completionMatch.Groups["milestone"].Value
        if (-not ($expectedMilestones -ccontains $milestone)) {
            throw "Client '$($Capture.ProcessName)' wrote an unexpected screenshot completion for milestone '$milestone'."
        }
        if ($completionByMilestone.ContainsKey($milestone)) {
            throw "Client '$($Capture.ProcessName)' wrote duplicate screenshot completion diagnostics for milestone '$milestone'."
        }
        $target = @($Capture.Files | Where-Object { [string]$_.Milestone -ceq $milestone })
        if ($target.Count -ne 1) {
            throw "Client '$($Capture.ProcessName)' has no unique screenshot target for milestone '$milestone'."
        }
        $expectedFile = [System.IO.Path]::GetFileName([string]$target[0].Path)
        if ($completionMatch.Groups["file"].Value -cne $expectedFile) {
            throw "Client '$($Capture.ProcessName)' milestone '$milestone' completion names file '$($completionMatch.Groups["file"].Value)' instead of '$expectedFile'."
        }
        $expectedEvidenceFile = [System.IO.Path]::GetFileName([string]$target[0].EvidencePath)
        if ($completionMatch.Groups["evidence"].Value -cne $expectedEvidenceFile) {
            throw "Client '$($Capture.ProcessName)' milestone '$milestone' completion names evidence '$($completionMatch.Groups["evidence"].Value)' instead of '$expectedEvidenceFile'."
        }
        $completionByMilestone[$milestone] = [ordered]@{
            milestone = $milestone
            milestoneOrder = [int]::Parse($completionMatch.Groups["order"].Value, [System.Globalization.CultureInfo]::InvariantCulture)
            milestoneRevision = [uint32]::Parse($completionMatch.Groups["revision"].Value, [System.Globalization.CultureInfo]::InvariantCulture)
            hostFrame = [int]::Parse($completionMatch.Groups["frame"].Value, [System.Globalization.CultureInfo]::InvariantCulture)
            fileName = $expectedFile
            evidenceFileName = $expectedEvidenceFile
            evidencePath = [string]$target[0].EvidencePath
        }
    }

    $recordsByMilestone = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    $nextExpectedIndex = 0
    $previousOrder = -1
    for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
        $screenshotMatch = [regex]::Match(
            $lines[$lineIndex],
            "(?:^|\s)screenshot milestone=(?<milestone>[A-Za-z0-9._-]+) milestoneOrder=(?<order>\d+) milestoneRevision=(?<revision>\d+) frame=(?<frame>\d+)(?:\s|$)",
            [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
        if (-not $screenshotMatch.Success) {
            continue
        }

        $milestone = $screenshotMatch.Groups["milestone"].Value
        if ($nextExpectedIndex -ge $expectedMilestones.Count -or
            $milestone -cne $expectedMilestones[$nextExpectedIndex]) {
            $expected = if ($nextExpectedIndex -lt $expectedMilestones.Count) { $expectedMilestones[$nextExpectedIndex] } else { "<none>" }
            throw "Client '$($Capture.ProcessName)' wrote screenshot milestone '$milestone' out of order; expected '$expected'."
        }
        if ($recordsByMilestone.ContainsKey($milestone)) {
            throw "Client '$($Capture.ProcessName)' wrote duplicate Raylib diagnostics for screenshot milestone '$milestone'."
        }
        $milestoneOrder = [int]::Parse(
            $screenshotMatch.Groups["order"].Value,
            [System.Globalization.CultureInfo]::InvariantCulture)
        $milestoneRevision = [uint32]::Parse(
            $screenshotMatch.Groups["revision"].Value,
            [System.Globalization.CultureInfo]::InvariantCulture)
        $frame = [int]::Parse(
            $screenshotMatch.Groups["frame"].Value,
            [System.Globalization.CultureInfo]::InvariantCulture)
        if ($milestoneOrder -le $previousOrder) {
            throw "Client '$($Capture.ProcessName)' screenshot milestone '$milestone' has non-increasing order $milestoneOrder."
        }
        if (-not $completionByMilestone.ContainsKey($milestone)) {
            throw "Client '$($Capture.ProcessName)' screenshot milestone '$milestone' has no completion diagnostic."
        }
        $completion = $completionByMilestone[$milestone]
        if ([int]$completion.milestoneOrder -ne $milestoneOrder -or
            [uint32]$completion.milestoneRevision -ne $milestoneRevision -or
            [int]$completion.hostFrame -ne $frame) {
            throw "Client '$($Capture.ProcessName)' screenshot milestone '$milestone' start and completion metadata do not match."
        }

        $timingLine = $null
        $visualCountLine = $null
        $receiptLines = [System.Collections.Generic.List[string]]::new()
        for ($detailIndex = $lineIndex + 1; $detailIndex -lt $lines.Count; $detailIndex++) {
            if ($lines[$detailIndex] -match '(?:^|\s)screenshot milestone=') {
                break
            }
            if ($null -eq $timingLine -and $lines[$detailIndex] -match '(?:^|\s)timing frame=') {
                $timingLine = $lines[$detailIndex]
                continue
            }
            if ($null -eq $visualCountLine -and $lines[$detailIndex] -match '(?:^|\s)typed-visual-counts lastFrame\(') {
                $visualCountLine = $lines[$detailIndex]
                continue
            }
            if ($lines[$detailIndex] -match '(?:^|\s)presentation-receipt(?:\s|$)') {
                $receiptLines.Add($lines[$detailIndex])
            }
        }
        if ($null -eq $timingLine -or $null -eq $visualCountLine) {
            throw "Client '$($Capture.ProcessName)' screenshot milestone '$milestone' lacks its complete Raylib presentation diagnostics."
        }

        $visibleEntities = Get-RequiredDiagnosticCount -Line $timingLine -Field "visibleEntities" `
            -ProcessName $Capture.ProcessName -Milestone $milestone
        $performerActive = Get-RequiredDiagnosticCount -Line $timingLine -Field "presenterActive" `
            -ProcessName $Capture.ProcessName -Milestone $milestone
        $primitiveRaw = Get-RequiredDiagnosticCount -Line $timingLine -Field "primitiveRaw" `
            -ProcessName $Capture.ProcessName -Milestone $milestone
        $primitiveInstances = Get-RequiredDiagnosticCount -Line $timingLine -Field "primInstances" `
            -ProcessName $Capture.ProcessName -Milestone $milestone
        $primitiveBatches = Get-RequiredDiagnosticCount -Line $timingLine -Field "primBatches" `
            -ProcessName $Capture.ProcessName -Milestone $milestone

        $visualMatch = [regex]::Match(
            $visualCountLine,
            'typed-visual-counts lastFrame\(mesh=(?<mesh>\d+),decal=(?<decal>\d+),vfx=(?<vfx>\d+),surface=(?<surface>\d+)\)',
            [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
        if (-not $visualMatch.Success) {
            throw "Client '$($Capture.ProcessName)' screenshot milestone '$milestone' has malformed prefab visual diagnostics."
        }
        $prefabVisuals = 0
        foreach ($kind in @("mesh", "decal", "vfx", "surface")) {
            $prefabVisuals += [int]::Parse(
                $visualMatch.Groups[$kind].Value,
                [System.Globalization.CultureInfo]::InvariantCulture)
        }

        $observed = [ordered]@{
            milestone = $milestone
            milestoneOrder = $milestoneOrder
            milestoneRevision = $milestoneRevision
            hostFrame = $frame
            visibleEntities = $visibleEntities
            activePerformers = $performerActive
            authoredPrimitives = $primitiveRaw
            submittedPrimitiveInstances = $primitiveInstances
            submittedPrimitiveBatches = $primitiveBatches
            prefabVisuals = $prefabVisuals
        }
        foreach ($requirement in @(
            [pscustomobject]@{ Name = "visible entities"; Actual = $visibleEntities; Minimum = [int]$Minimums.minimumVisibleEntities }
            [pscustomobject]@{ Name = "active performers"; Actual = $performerActive; Minimum = [int]$Minimums.minimumActivePerformers }
            [pscustomobject]@{ Name = "authored primitives"; Actual = $primitiveRaw; Minimum = [int]$Minimums.minimumAuthoredPrimitives }
            [pscustomobject]@{ Name = "submitted primitive instances"; Actual = $primitiveInstances; Minimum = [int]$Minimums.minimumSubmittedPrimitiveInstances }
            [pscustomobject]@{ Name = "submitted primitive batches"; Actual = $primitiveBatches; Minimum = [int]$Minimums.minimumSubmittedPrimitiveBatches }
            [pscustomobject]@{ Name = "prefab visuals"; Actual = $prefabVisuals; Minimum = [int]$Minimums.minimumPrefabVisuals }
        )) {
            if ($requirement.Actual -lt $requirement.Minimum) {
                throw "Client '$($Capture.ProcessName)' screenshot milestone '$milestone' has $($requirement.Actual) $($requirement.Name); expected at least $($requirement.Minimum). HUD-only screenshots are not accepted."
            }
        }

        $receiptsByTemplate = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
        foreach ($receiptLine in $receiptLines) {
            $receiptMatch = [regex]::Match(
                $receiptLine,
                '^\[[^\]]+\]\s+presentation-receipt template=(?<template>[A-Za-z0-9._:-]+) templateId=(?<templateId>[1-9]\d*) submitted=(?<submitted>[1-9]\d*) onscreen=(?<onscreen>\d+) minShortEdgePx=(?<shortEdge>\d+(?:\.\d+)?) minAreaPx2=(?<area>\d+(?:\.\d+)?) stateSha256=(?<stateSha256>[0-9A-Fa-f]{64})\s*$',
                [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
            if (-not $receiptMatch.Success) {
                throw "Client '$($Capture.ProcessName)' screenshot milestone '$milestone' has malformed presentation receipt diagnostics."
            }

            $template = $receiptMatch.Groups["template"].Value
            if ($receiptsByTemplate.ContainsKey($template)) {
                throw "Client '$($Capture.ProcessName)' screenshot milestone '$milestone' has duplicate presentation receipts for template '$template'."
            }

            $receiptsByTemplate[$template] = [ordered]@{
                template = $template
                templateId = [int]::Parse($receiptMatch.Groups["templateId"].Value, [System.Globalization.CultureInfo]::InvariantCulture)
                submitted = [int]::Parse($receiptMatch.Groups["submitted"].Value, [System.Globalization.CultureInfo]::InvariantCulture)
                onscreen = [int]::Parse($receiptMatch.Groups["onscreen"].Value, [System.Globalization.CultureInfo]::InvariantCulture)
                minimumShortEdgePx = [double]::Parse($receiptMatch.Groups["shortEdge"].Value, [System.Globalization.CultureInfo]::InvariantCulture)
                minimumAreaPx2 = [double]::Parse($receiptMatch.Groups["area"].Value, [System.Globalization.CultureInfo]::InvariantCulture)
                stateSha256 = $receiptMatch.Groups["stateSha256"].Value.ToLowerInvariant()
            }
        }

        $requiredForMilestone = @($RequiredReceipts | Where-Object { @($_.milestones | ForEach-Object { [string]$_ }) -ccontains $milestone })
        $observedRequiredReceipts = [System.Collections.Generic.List[object]]::new()
        foreach ($requirement in $requiredForMilestone) {
            $template = [string]$requirement.template
            if (-not $receiptsByTemplate.ContainsKey($template)) {
                throw "Client '$($Capture.ProcessName)' screenshot milestone '$milestone' has no submitted presentation receipt for role '$($requirement.role)' (template '$template')."
            }

            $receipt = $receiptsByTemplate[$template]
            foreach ($threshold in @(
                [pscustomobject]@{ Name = "submitted instances"; Actual = [int]$receipt.submitted; Minimum = [int]$requirement.minimumSubmitted }
                [pscustomobject]@{ Name = "onscreen instances"; Actual = [int]$receipt.onscreen; Minimum = [int]$requirement.minimumOnscreen }
                [pscustomobject]@{ Name = "minimum short edge pixels"; Actual = [double]$receipt.minimumShortEdgePx; Minimum = [double]$requirement.minimumShortEdgePx }
                [pscustomobject]@{ Name = "minimum projected area pixels"; Actual = [double]$receipt.minimumAreaPx2; Minimum = [double]$requirement.minimumAreaPx2 }
            )) {
                if ($threshold.Actual -lt $threshold.Minimum) {
                    throw "Client '$($Capture.ProcessName)' screenshot milestone '$milestone' role '$($requirement.role)' has $($threshold.Actual) $($threshold.Name); expected at least $($threshold.Minimum)."
                }
            }

            $observedRequiredReceipts.Add([ordered]@{
                role = [string]$requirement.role
                template = $template
                templateId = [int]$receipt.templateId
                submitted = [int]$receipt.submitted
                onscreen = [int]$receipt.onscreen
                minimumShortEdgePx = [double]$receipt.minimumShortEdgePx
                minimumAreaPx2 = [double]$receipt.minimumAreaPx2
                stateSha256 = [string]$receipt.stateSha256
            })
        }
        $observed["presentationReceipts"] = @($observedRequiredReceipts)

        $sidecarPath = [string]$completion.evidencePath
        if (-not (Test-Path -LiteralPath $sidecarPath -PathType Leaf) -or
            (Get-Item -LiteralPath $sidecarPath).Length -le 0) {
            throw "Client '$($Capture.ProcessName)' screenshot milestone '$milestone' has no non-empty presentation evidence sidecar: $sidecarPath"
        }
        try {
            $sidecar = Get-Content -LiteralPath $sidecarPath -Raw | ConvertFrom-Json
        }
        catch {
            throw "Client '$($Capture.ProcessName)' screenshot milestone '$milestone' presentation evidence is not valid JSON: $sidecarPath. $($_.Exception.Message)"
        }
        if ([int]$sidecar.schemaVersion -ne 2 -or
            [string]$sidecar.milestone -cne $milestone -or
            [int]$sidecar.milestoneOrder -ne $milestoneOrder -or
            [uint32]$sidecar.milestoneRevision -ne $milestoneRevision -or
            [int]$sidecar.hostFrame -ne $frame) {
            throw "Client '$($Capture.ProcessName)' screenshot milestone '$milestone' sidecar does not describe the same captured presentation frame."
        }
        if ($null -eq $sidecar.PSObject.Properties["cameraTargetXCm"] -or
            $null -eq $sidecar.PSObject.Properties["cameraTargetYCm"] -or
            $null -eq $sidecar.PSObject.Properties["viewportWidthPx"] -or
            $null -eq $sidecar.PSObject.Properties["viewportHeightPx"] -or
            $null -eq $sidecar.PSObject.Properties["instances"]) {
            throw "Client '$($Capture.ProcessName)' screenshot milestone '$milestone' sidecar lacks camera or instance evidence."
        }
        $viewportWidthPx = [double]$sidecar.viewportWidthPx
        $viewportHeightPx = [double]$sidecar.viewportHeightPx
        if ($viewportWidthPx -le 0 -or $viewportHeightPx -le 0) {
            throw "Client '$($Capture.ProcessName)' screenshot milestone '$milestone' sidecar has an invalid viewport."
        }
        $seenInstanceKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($instance in @($sidecar.instances)) {
            foreach ($fieldName in @(
                "ownerStableId", "visualStableId", "templateId", "template", "worldXCm", "worldYCm",
                "screenLeftPx", "screenTopPx", "screenRightPx", "screenBottomPx",
                "shortEdgePx", "areaPx2")) {
                if ($null -eq $instance.PSObject.Properties[$fieldName]) {
                    throw "Client '$($Capture.ProcessName)' screenshot milestone '$milestone' sidecar instance lacks '$fieldName'."
                }
            }
            if ([int]$instance.ownerStableId -le 0 -or [int]$instance.visualStableId -le 0 -or
                [int]$instance.templateId -le 0 -or
                [string]::IsNullOrWhiteSpace([string]$instance.template)) {
                throw "Client '$($Capture.ProcessName)' screenshot milestone '$milestone' sidecar contains an invalid presentation instance identity."
            }
            $instanceKey = "$([int]$instance.templateId):$([int]$instance.visualStableId)"
            if (-not $seenInstanceKeys.Add($instanceKey)) {
                throw "Client '$($Capture.ProcessName)' screenshot milestone '$milestone' sidecar duplicates presentation instance '$instanceKey'."
            }
            $screenWidth = [double]$instance.screenRightPx - [double]$instance.screenLeftPx
            $screenHeight = [double]$instance.screenBottomPx - [double]$instance.screenTopPx
            $expectedShortEdgePx = [Math]::Min($screenWidth, $screenHeight)
            $expectedAreaPx2 = $screenWidth * $screenHeight
            if ($screenWidth -le 0 -or $screenHeight -le 0 -or
                [double]$instance.screenLeftPx -lt 0 -or [double]$instance.screenTopPx -lt 0 -or
                [double]$instance.screenRightPx -gt $viewportWidthPx -or
                [double]$instance.screenBottomPx -gt $viewportHeightPx -or
                [Math]::Abs([double]$instance.shortEdgePx - $expectedShortEdgePx) -gt 0.01 -or
                [Math]::Abs([double]$instance.areaPx2 - $expectedAreaPx2) -gt 0.1) {
                throw "Client '$($Capture.ProcessName)' screenshot milestone '$milestone' sidecar instance '$instanceKey' is not actually on screen."
            }
        }
        $observed["worldEvidenceFile"] = Get-FileEvidence -Path $sidecarPath
        $observed["worldEvidence"] = $sidecar

        $recordsByMilestone[$milestone] = $observed
        $previousOrder = $milestoneOrder
        $nextExpectedIndex++
    }

    foreach ($expectedMilestone in $expectedMilestones) {
        if (-not $recordsByMilestone.ContainsKey($expectedMilestone)) {
            throw "Client '$($Capture.ProcessName)' lacks Raylib presentation evidence for screenshot milestone '$expectedMilestone'."
        }
    }

    return [ordered]@{
        process = $Capture.ProcessName
        diagnostic = Get-FileEvidence -Path $diagnosticPath
        milestones = @($expectedMilestones | ForEach-Object { $recordsByMilestone[$_] })
    }
}

function Read-ClientFramebufferPixelEvidence {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$Screenshots,
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$PresentationItems,
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$GameplayItems,
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$Requirements,
        [Parameter(Mandatory = $true)][string]$DotnetPath,
        [Parameter(Mandatory = $true)][string]$LauncherAssemblyPath,
        [Parameter(Mandatory = $true)][string]$ArtifactDirectory,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    $allRequirements = @($Requirements)
    if ($allRequirements.Count -eq 0) {
        throw "Framebuffer pixel evidence requires at least one profile rule."
    }
    if (-not (Test-Path -LiteralPath $LauncherAssemblyPath -PathType Leaf)) {
        throw "Framebuffer pixel evidence launcher assembly is missing: $LauncherAssemblyPath"
    }

    $outputDirectory = Join-Path $ArtifactDirectory "framebuffer-pixel-evidence"
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    $results = [System.Collections.Generic.List[object]]::new()
    foreach ($screenshot in @($Screenshots)) {
        $processName = [string]$screenshot.ProcessName
        $milestone = [string]$screenshot.Milestone
        $presentationMatches = @($PresentationItems | Where-Object { [string]$_.process -ceq $processName })
        if ($presentationMatches.Count -ne 1) {
            throw "Client '$processName' has no unique presentation evidence for framebuffer milestone '$milestone'."
        }
        $gameplayMatches = @($GameplayItems | Where-Object { [string]$_.Name -ceq $processName })
        if ($gameplayMatches.Count -ne 1) {
            throw "Client '$processName' has no unique gameplay evidence for framebuffer milestone '$milestone'."
        }
        $isWinner = [int]$gameplayMatches[0].Value.seatSlot -eq
            [int]$gameplayMatches[0].Value.gameplay.winningSideIndex
        $milestoneMatches = @($presentationMatches[0].milestones | Where-Object {
            [string]$_.milestone -ceq $milestone
        })
        if ($milestoneMatches.Count -ne 1) {
            throw "Client '$processName' has no unique same-frame sidecar for framebuffer milestone '$milestone'."
        }

        $worldEvidence = $milestoneMatches[0].worldEvidence
        $milestoneRequirements = @($allRequirements | Where-Object {
            $targetsMilestone = @($_.milestones | ForEach-Object { [string]$_ }) -ccontains $milestone
            $perspective = [string]$_.perspective
            $targetsPerspective = switch -CaseSensitive ($perspective) {
                "all" { $true; break }
                "winner" { $isWinner; break }
                "loser" { -not $isWinner; break }
                default { throw "Framebuffer role '$([string]$_.role)' uses unsupported perspective '$perspective'." }
            }
            $targetsMilestone -and $targetsPerspective
        })
        if ($milestoneRequirements.Count -eq 0) {
            throw "Client '$processName' framebuffer milestone '$milestone' has no required player-visible role."
        }

        $inspectionRequirements = [System.Collections.Generic.List[object]]::new()
        foreach ($requirement in $milestoneRequirements) {
            $role = [string]$requirement.role
            $template = [string]$requirement.presentationTemplate
            $matchingInstances = @($worldEvidence.instances | Where-Object { [string]$_.template -ceq $template })
            if ($matchingInstances.Count -eq 0) {
                throw "Client '$processName' framebuffer milestone '$milestone' has no '$role' presentation region for template '$template'."
            }

            $marginRatio = [double]$requirement.regionMarginRatio
            $regions = [System.Collections.Generic.List[object]]::new()
            foreach ($instance in $matchingInstances) {
                $instanceWidth = [double]$instance.screenRightPx - [double]$instance.screenLeftPx
                $instanceHeight = [double]$instance.screenBottomPx - [double]$instance.screenTopPx
                $horizontalMargin = [int][Math]::Ceiling($instanceWidth * $marginRatio)
                $verticalMargin = [int][Math]::Ceiling($instanceHeight * $marginRatio)
                $left = [Math]::Max(0, [int][Math]::Floor([double]$instance.screenLeftPx) - $horizontalMargin)
                $top = [Math]::Max(0, [int][Math]::Floor([double]$instance.screenTopPx) - $verticalMargin)
                $right = [Math]::Min([int]$worldEvidence.viewportWidthPx, [int][Math]::Ceiling([double]$instance.screenRightPx) + $horizontalMargin)
                $bottom = [Math]::Min([int]$worldEvidence.viewportHeightPx, [int][Math]::Ceiling([double]$instance.screenBottomPx) + $verticalMargin)
                if ($right -le $left -or $bottom -le $top) {
                    throw "Client '$processName' framebuffer milestone '$milestone' role '$role' produced an empty pixel region."
                }
                [void]$regions.Add([ordered]@{
                    id = "$([int]$instance.templateId):$([int]$instance.visualStableId)"
                    x = $left
                    y = $top
                    width = $right - $left
                    height = $bottom - $top
                })
            }

            [void]$inspectionRequirements.Add([ordered]@{
                role = $role
                presentationTemplate = $template
                maximumChannelDifference = [int]$requirement.maximumChannelDifference
                minimumPixelsPerRegion = [int]$requirement.minimumPixelsPerInstance
                minimumPassingRegions = [int]$requirement.minimumPassingInstances
                acceptedColors = @($requirement.acceptedColors | ForEach-Object {
                    [ordered]@{
                        red = [byte]$_.red
                        green = [byte]$_.green
                        blue = [byte]$_.blue
                    }
                })
                regions = @($regions)
            })
        }

        $request = [ordered]@{
            schemaVersion = 1
            imagePath = [System.IO.Path]::GetFullPath([string]$screenshot.Path)
            expectedWidth = [int]$worldEvidence.viewportWidthPx
            expectedHeight = [int]$worldEvidence.viewportHeightPx
            requirements = @($inspectionRequirements)
        }
        $artifactBaseName = "$processName-$milestone"
        $requestPath = Join-Path $outputDirectory "$artifactBaseName.request.json"
        $resultPath = Join-Path $outputDirectory "$artifactBaseName.result.json"
        Write-JsonFile -Value $request -Path $requestPath
        $inspectionJson = Invoke-NativeTextCommand -Name "inspect-framebuffer-$processName-$milestone" `
            -FilePath $DotnetPath `
            -Arguments @("exec", "--roll-forward", "Major", $LauncherAssemblyPath,
                "evidence", "inspect-framebuffer", $requestPath) `
            -WorkingDirectory $WorkingDirectory
        try {
            $inspection = $inspectionJson | ConvertFrom-Json
        }
        catch {
            throw "Client '$processName' framebuffer milestone '$milestone' inspector returned invalid JSON: $($_.Exception.Message)"
        }
        $returnedRequirements = @($inspection.requirements)
        if ([int]$inspection.schemaVersion -ne 1) {
            throw "Client '$processName' framebuffer milestone '$milestone' inspector returned unsupported schema '$($inspection.schemaVersion)'."
        }
        if ([int]$inspection.width -ne [int]$worldEvidence.viewportWidthPx -or
            [int]$inspection.height -ne [int]$worldEvidence.viewportHeightPx) {
            throw "Client '$processName' framebuffer milestone '$milestone' inspector returned dimensions $($inspection.width)x$($inspection.height), expected $($worldEvidence.viewportWidthPx)x$($worldEvidence.viewportHeightPx)."
        }
        if ($returnedRequirements.Count -ne $inspectionRequirements.Count) {
            throw "Client '$processName' framebuffer milestone '$milestone' inspector returned $($returnedRequirements.Count) requirements, expected $($inspectionRequirements.Count)."
        }
        Write-JsonFile -Value $inspection -Path $resultPath

        [void]$results.Add([ordered]@{
            process = $processName
            milestone = $milestone
            screenshot = Get-FileEvidence -Path ([string]$screenshot.Path)
            request = Get-FileEvidence -Path $requestPath
            result = Get-FileEvidence -Path $resultPath
            width = [int]$inspection.width
            height = [int]$inspection.height
            passed = [bool]$inspection.passed
            requirements = $returnedRequirements
        })
    }

    return @($results)
}

function Assert-ClientFramebufferPixelEvidencePassed {
    param([Parameter(Mandatory = $true)][System.Collections.IEnumerable]$Items)

    $all = @($Items)
    if ($all.Count -eq 0) {
        throw "No client framebuffer pixel evidence was inspected."
    }

    $failures = [System.Collections.Generic.List[string]]::new()
    foreach ($item in $all) {
        if (-not [bool]$item.passed) {
            [void]$failures.Add(
                "client '$([string]$item.process)' milestone '$([string]$item.milestone)' inspector reported passed=false")
        }
        foreach ($requirement in @($item.requirements | Where-Object { -not [bool]$_.passed })) {
            [void]$failures.Add(
                "client '$([string]$item.process)' milestone '$([string]$item.milestone)' role '$([string]$requirement.role)' " +
                "has $([int]$requirement.passingRegions)/$([int]$requirement.minimumPassingRegions) passing instances " +
                "and $([int]$requirement.matchingPixels) matching pixels")
        }
    }
    if ($failures.Count -ne 0) {
        throw "Framebuffer PNG does not visibly contain every required role: $($failures -join '; ')."
    }
}

function Get-ScreenshotCompletionRecord {
    param([Parameter(Mandatory = $true)]$Target)

    $diagnosticPath = [string]$Target.DiagnosticPath
    if (-not (Test-Path -LiteralPath $diagnosticPath -PathType Leaf)) {
        return $null
    }

    $milestone = [string]$Target.Milestone
    $expectedFile = [System.IO.Path]::GetFileName([string]$Target.Path)
    $completionMatches = [System.Collections.Generic.List[System.Text.RegularExpressions.Match]]::new()
    foreach ($line in [System.IO.File]::ReadAllLines($diagnosticPath)) {
        $isCompletionLine = $line -match '(?:^|\s)screenshot-complete(?:\s|$)'
        $match = [regex]::Match(
            $line,
            '^\[[^\]]+\]\s+screenshot-complete milestone=(?<milestone>[A-Za-z0-9._-]+) milestoneOrder=(?<order>\d+) milestoneRevision=(?<revision>\d+) frame=(?<frame>\d+) file=(?<file>[^\s]+) evidence=(?<evidence>[^\s]+)\s*$',
            [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
        if ($isCompletionLine -and -not $match.Success) {
            throw "Client '$($Target.ProcessName)' wrote a malformed screenshot completion diagnostic."
        }
        if ($match.Success -and $match.Groups["milestone"].Value -ceq $milestone) {
            $completionMatches.Add($match)
        }
    }
    if ($completionMatches.Count -eq 0) {
        return $null
    }
    if ($completionMatches.Count -ne 1) {
        throw "Client '$($Target.ProcessName)' wrote duplicate screenshot completion diagnostics for milestone '$milestone'."
    }
    $completion = $completionMatches[0]
    if ($completion.Groups["file"].Value -cne $expectedFile) {
        throw "Client '$($Target.ProcessName)' milestone '$milestone' completion names file '$($completion.Groups["file"].Value)' instead of '$expectedFile'."
    }
    $expectedEvidencePath = if ($null -ne $Target.PSObject.Properties["EvidencePath"]) {
        [string]$Target.EvidencePath
    }
    else {
        [System.IO.Path]::ChangeExtension([string]$Target.Path, ".evidence.json")
    }
    $expectedEvidenceFile = [System.IO.Path]::GetFileName($expectedEvidencePath)
    if ($completion.Groups["evidence"].Value -cne $expectedEvidenceFile) {
        throw "Client '$($Target.ProcessName)' milestone '$milestone' completion names evidence '$($completion.Groups["evidence"].Value)' instead of '$expectedEvidenceFile'."
    }

    return [pscustomobject]@{
        ProcessName = [string]$Target.ProcessName
        Milestone = $milestone
        MilestoneOrder = [int]::Parse($completion.Groups["order"].Value, [System.Globalization.CultureInfo]::InvariantCulture)
        MilestoneRevision = [uint32]::Parse($completion.Groups["revision"].Value, [System.Globalization.CultureInfo]::InvariantCulture)
        HostFrame = [int]::Parse($completion.Groups["frame"].Value, [System.Globalization.CultureInfo]::InvariantCulture)
        Path = [string]$Target.Path
        EvidencePath = $expectedEvidencePath
        DiagnosticPath = $diagnosticPath
    }
}

function Wait-ForScreenshotEvidence {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$Targets,
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$OwnedProcesses,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds,
        [Parameter(Mandatory = $true)][int]$PollMilliseconds
    )

    $all = @($Targets)
    if ($all.Count -eq 0) { throw "At least one screenshot target is required." }
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        Assert-OwnedProcessesAlive -OwnedProcesses $OwnedProcesses
        $ready = $true
        $completionRecords = [System.Collections.Generic.List[object]]::new()
        foreach ($target in $all) {
            if (-not (Test-Path -LiteralPath $target.Path -PathType Leaf) -or
                (Get-Item -LiteralPath $target.Path).Length -le 0 -or
                -not (Test-Path -LiteralPath $target.EvidencePath -PathType Leaf) -or
                (Get-Item -LiteralPath $target.EvidencePath).Length -le 0) {
                $ready = $false
                break
            }
            $completion = Get-ScreenshotCompletionRecord -Target $target
            if ($null -eq $completion) {
                $ready = $false
                break
            }
            $completionRecords.Add($completion)
        }
        if ($ready) { return @($completionRecords) }
        Start-Sleep -Milliseconds $PollMilliseconds
    }

    $missing = @($all | Where-Object {
        -not (Test-Path -LiteralPath $_.Path -PathType Leaf) -or
        (Get-Item -LiteralPath $_.Path).Length -le 0 -or
        -not (Test-Path -LiteralPath $_.EvidencePath -PathType Leaf) -or
        (Get-Item -LiteralPath $_.EvidencePath).Length -le 0
    } | ForEach-Object { "$($_.ProcessName):$($_.Milestone)" })
    $missingCompletions = @($all | Where-Object {
        $null -eq (Get-ScreenshotCompletionRecord -Target $_)
    } | ForEach-Object { "$($_.ProcessName):$($_.Milestone)-completion" })
    $missingEvidence = @(@($missing) + @($missingCompletions) | Select-Object -Unique)
    throw "Timed out after $TimeoutSeconds seconds waiting for client screenshots. Missing or empty: $($missingEvidence -join ', ')."
}

function Invoke-NativeTextCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.Arguments = (($Arguments | ForEach-Object { ConvertTo-NativeArgument -Value $_ }) -join " ")

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Failed to start process '$Name'."
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "Process '$Name' failed with exit code $($process.ExitCode): $($stderr.Trim())"
        }

        return $stdout.Trim()
    }
    finally {
        $process.Dispose()
    }
}

function Get-GitEvidence {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $git = Get-Command "git" -CommandType Application -ErrorAction Stop | Select-Object -First 1
    $commit = Invoke-NativeTextCommand -Name "git-rev-parse" -FilePath ([string]$git.Source) `
        -Arguments @("-C", $RepositoryRoot, "rev-parse", "HEAD") -WorkingDirectory $RepositoryRoot
    $status = Invoke-NativeTextCommand -Name "git-status" -FilePath ([string]$git.Source) `
        -Arguments @("-C", $RepositoryRoot, "status", "--porcelain=v1", "--untracked-files=all") `
        -WorkingDirectory $RepositoryRoot

    return [ordered]@{
        commit = $commit
        dirty = -not [string]::IsNullOrWhiteSpace($status)
    }
}

function Assert-RuntimeStderrEmpty {
    param([Parameter(Mandatory = $true)][System.Collections.IEnumerable]$OwnedProcesses)

    foreach ($owned in $OwnedProcesses) {
        $stderr = Get-Item -LiteralPath $owned.StderrPath -ErrorAction Stop
        if ($stderr.Length -ne 0) {
            throw "Process '$($owned.Name)' wrote $($stderr.Length) bytes to stderr. See $($owned.StderrPath)."
        }
    }
}

function Remove-ClientCredential {
    param(
        [Parameter(Mandatory = $true)][int]$ClientInstanceId,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ArtifactDirectory
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $artifactPrefix = [System.IO.Path]::GetFullPath($ArtifactDirectory).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($artifactPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Credential path is outside this acceptance artifact directory: $fullPath"
    }
    $credentialDirectory = [System.IO.Path]::GetDirectoryName($fullPath)
    $credentialFileName = [System.IO.Path]::GetFileName($fullPath)
    $temporaryEvidence = [System.Collections.Generic.List[object]]::new()
    $cleanupFailures = [System.Collections.Generic.List[string]]::new()
    $finalEvidence = [ordered]@{
        clientInstanceId = $ClientInstanceId
        present = $false
        valid = $false
        length = 0
        sha256 = $null
        deleted = $false
        temporaryCredentials = @()
    }

    if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
        try {
            $finalEvidence.present = $true
            $finalEvidence.valid = Test-ClientCredentialFile -Path $fullPath
            [byte[]]$bytes = [System.IO.File]::ReadAllBytes($fullPath)
            $finalEvidence.length = $bytes.Length
            $finalEvidence.sha256 = Get-Sha256Hex -Bytes $bytes
            [System.IO.File]::Delete($fullPath)
            $finalEvidence.deleted = -not (Test-Path -LiteralPath $fullPath)
            if (-not $finalEvidence.deleted) {
                throw "Credential secret was not deleted: $fullPath"
            }
        }
        catch {
            $cleanupFailures.Add($_.Exception.Message)
        }
    }

    if (Test-Path -LiteralPath $credentialDirectory -PathType Container) {
        $temporaryPaths = [System.IO.Directory]::GetFiles(
            $credentialDirectory,
            ".$credentialFileName.*.tmp",
            [System.IO.SearchOption]::TopDirectoryOnly)
        foreach ($temporaryPath in $temporaryPaths) {
            try {
                $temporaryFullPath = [System.IO.Path]::GetFullPath($temporaryPath)
                if (-not $temporaryFullPath.StartsWith($artifactPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                    throw "Temporary credential path is outside this acceptance artifact directory: $temporaryFullPath"
                }
                [byte[]]$temporaryBytes = [System.IO.File]::ReadAllBytes($temporaryFullPath)
                $temporary = [ordered]@{
                    length = $temporaryBytes.Length
                    sha256 = Get-Sha256Hex -Bytes $temporaryBytes
                    deleted = $false
                }
                [System.IO.File]::Delete($temporaryFullPath)
                $temporary.deleted = -not (Test-Path -LiteralPath $temporaryFullPath)
                $temporaryEvidence.Add($temporary)
                if (-not $temporary.deleted) {
                    throw "Temporary credential secret was not deleted: $temporaryFullPath"
                }
            }
            catch {
                $cleanupFailures.Add($_.Exception.Message)
            }
        }
    }

    $finalEvidence.temporaryCredentials = @($temporaryEvidence)
    if ($cleanupFailures.Count -ne 0) {
        throw "Credential cleanup failed for clientInstanceId ${ClientInstanceId}: $($cleanupFailures -join '; ')"
    }

    return $finalEvidence
}

function New-RoleArtifacts {
    param(
        [Parameter(Mandatory = $true)][string]$RoleDirectory,
        [Parameter(Mandatory = $true)][string]$ProcessRole,
        [Parameter(Mandatory = $true)][int]$ClientInstanceId,
        [Parameter(Mandatory = $true)][int]$FaultSeed,
        [AllowEmptyString()][Parameter(Mandatory = $true)][string]$CredentialPath,
        [Parameter(Mandatory = $true)][string]$SourceGraphPath,
        [Parameter(Mandatory = $true)]$Plan
    )

    New-Item -ItemType Directory -Path $RoleDirectory -Force | Out-Null
    $graphPath = Join-Path $RoleDirectory "launcher.graph.json"
    $bootstrapPath = Join-Path $RoleDirectory "launcher.runtime.json"
    $graph = Get-Content -LiteralPath $SourceGraphPath -Raw | ConvertFrom-Json
    $graph.selectors = @($graph.selectors)
    $graph.rootModIds = @($graph.rootModIds)
    $graph.orderedModIds = @($graph.orderedModIds)
    $graph.plannedMods = @($graph.plannedMods)
    foreach ($plannedMod in $graph.plannedMods) {
        $plannedMod.bindingNames = @($plannedMod.bindingNames)
    }
    $graph.diagnostics.settings = @($graph.diagnostics.settings)
    $graph.diagnostics.warnings = @($graph.diagnostics.warnings)
    foreach ($setting in $graph.diagnostics.settings) {
        $setting.contributions = @($setting.contributions)
    }
    if ($null -ne $graph.browserRuntime) {
        $graph.browserRuntime.processSharedAssemblyNamePrefixes = @($graph.browserRuntime.processSharedAssemblyNamePrefixes)
    }
    $graph.runtimeArtifacts.graphArtifactPath = $graphPath
    $graph.runtimeArtifacts.bootstrapArtifactPath = $bootstrapPath
    Write-JsonFile -Value $graph -Path $graphPath

    $networkHost = [ordered]@{
        ProcessRole = $ProcessRole
        Host = if ($ProcessRole -eq "replicatedClient") { $script:HostAddressValue } else { "" }
        Port = $script:PortValue
        ConnectionKey = $script:ConnectionKeyValue
        ClientInstanceId = $ClientInstanceId
        CredentialPath = $CredentialPath
        FaultProfile = $script:FaultProfileValue
        FaultSeed = $FaultSeed
    }
    $bootstrap = [ordered]@{
        LaunchGraphPath = "launcher.graph.json"
        LaunchGraphFullPath = $graphPath
        PlanSelectors = @($Plan.Selectors)
        PlanRootModIds = @($Plan.RootModIds)
        PlanOrderedModIds = @($Plan.OrderedModIds)
        PlanFingerprint = $Plan.PlanFingerprint
        PlanSchemaVersion = $Plan.SchemaVersion
        PlanGeneratedAtUtc = $Plan.GeneratedAtUtc
        BrowserRuntime = $Plan.BrowserRuntime
        NetworkHost = $networkHost
    }
    Write-JsonFile -Value $bootstrap -Path $bootstrapPath

    return [pscustomobject]@{
        ProcessRole = $ProcessRole
        GraphPath = $graphPath
        BootstrapPath = $bootstrapPath
        CredentialPath = $CredentialPath
    }
}

function Wait-ForCredentialEvidence {
    param(
        [Parameter(Mandatory = $true)][string[]]$CredentialPaths,
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$OwnedProcesses,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds,
        [Parameter(Mandatory = $true)][int]$PollMilliseconds
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        Assert-OwnedProcessesAlive -OwnedProcesses $OwnedProcesses
        $ready = $true
        foreach ($path in $CredentialPaths) {
            if (-not (Test-ClientCredentialFile -Path $path)) {
                $ready = $false
                break
            }
        }

        if ($ready) {
            return
        }

        Start-Sleep -Milliseconds $PollMilliseconds
    }

    throw "Timed out waiting for both clients to receive independent session credentials."
}

function Test-ClientCredentialFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    try {
        [byte[]]$bytes = [System.IO.File]::ReadAllBytes($Path)
    }
    catch [System.IO.IOException] {
        return $false
    }
    if ($bytes.Length -ne 64) {
        return $false
    }

    [byte[]]$magic = [System.Text.Encoding]::ASCII.GetBytes("LUDCRD01")
    for ($index = 0; $index -lt $magic.Length; $index++) {
        if ($bytes[$index] -ne $magic[$index]) {
            return $false
        }
    }

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        [byte[]]$digest = $sha256.ComputeHash($bytes, 0, 32)
    }
    finally {
        $sha256.Dispose()
    }
    for ($index = 0; $index -lt $digest.Length; $index++) {
        if ($digest[$index] -ne $bytes[32 + $index]) {
            return $false
        }
    }

    $epoch = [System.BitConverter]::ToUInt64($bytes, 8)
    $tokenLow = [System.BitConverter]::ToUInt64($bytes, 16)
    $tokenHigh = [System.BitConverter]::ToUInt64($bytes, 24)
    return $epoch -ne 0 -and ($tokenLow -ne 0 -or $tokenHigh -ne 0)
}

function Read-GameplayEvidence {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }
    try {
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    catch {
        throw "Gameplay evidence is not valid JSON: $Path. $($_.Exception.Message)"
    }
}

function Wait-ForGameplayEvidence {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$Targets,
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$OwnedProcesses,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds,
        [Parameter(Mandatory = $true)][int]$PollMilliseconds
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $completed = [System.Collections.Generic.List[object]]::new()
        foreach ($target in $Targets) {
            $evidence = Read-GameplayEvidence -Path $target.Path
            if ($null -eq $evidence) {
                continue
            }
            if ([string]$evidence.status -ceq "failed") {
                throw "Gameplay acceptance failed in '$($target.Name)': $($evidence.failure). Evidence: $($target.Path)"
            }
            if ([string]$evidence.status -ceq "running") {
                continue
            }
            if ([string]$evidence.status -cne "passed") {
                throw "Gameplay evidence '$($target.Name)' has terminal file with unsupported status '$($evidence.status)'."
            }
            $completed.Add([pscustomobject]@{ Name = $target.Name; Path = $target.Path; Value = $evidence })
        }
        if ($completed.Count -eq @($Targets).Count) {
            Assert-OwnedProcessesAlive -OwnedProcesses $OwnedProcesses
            return @($completed)
        }

        foreach ($owned in $OwnedProcesses) {
            $owned.Process.Refresh()
            if ($owned.Process.HasExited) {
                throw "Process '$($owned.Name)' exited with code $($owned.Process.ExitCode) before all gameplay evidence passed."
            }
        }
        Start-Sleep -Milliseconds $PollMilliseconds
    }

    $missing = @($Targets | Where-Object { -not (Test-Path -LiteralPath $_.Path -PathType Leaf) } | ForEach-Object { $_.Name })
    throw "Timed out after $TimeoutSeconds seconds waiting for gameplay evidence. Missing: $($missing -join ', ')."
}

function Assert-NetworkFaultInjectionEvidence {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$Items,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$ExpectedByProcess,
        [Parameter(Mandatory = $true)][string]$FaultProfile
    )

    $all = @($Items)
    if ($ExpectedByProcess.Count -ne $all.Count) {
        throw "Fault injection expectations must cover exactly $($all.Count) processes."
    }

    [int64]$clientReorderedStateDatagrams = 0
    foreach ($item in $all) {
        if (-not $ExpectedByProcess.Contains([string]$item.Name)) {
            throw "Fault injection expectation is missing process '$($item.Name)'."
        }
        $expected = $ExpectedByProcess[[string]$item.Name]
        $evidence = $item.Value
        if ($null -eq $evidence.PSObject.Properties["networkFaultInjection"]) {
            throw "Evidence '$($item.Name)' lacks networkFaultInjection."
        }
        $observed = $evidence.networkFaultInjection
        if ($null -eq $observed -or $null -eq $observed.PSObject.Properties["configuration"]) {
            throw "Evidence '$($item.Name)' lacks the effective fault injection configuration."
        }
        foreach ($counterProperty in @(
            "delayedInboundPacketCount",
            "droppedInboundPacketCount",
            "reorderedInboundStateDatagramCount")) {
            if ($null -eq $observed.PSObject.Properties[$counterProperty]) {
                throw "Evidence '$($item.Name)' lacks network fault counter '$counterProperty'."
            }
        }
        $configuration = $observed.configuration
        if ([string]$observed.role -cne [string]$expected.Role -or
            [string]$configuration.transportIdentity -cne [string]$expected.TransportIdentity -or
            [string]$configuration.profileId -cne [string]$expected.ProfileId -or
            [int]$configuration.seed -ne [int]$expected.Seed -or
            [int]$configuration.roundTripLatencyMilliseconds -ne [int]$expected.RoundTripLatencyMilliseconds -or
            [int]$configuration.jitterMilliseconds -ne [int]$expected.JitterMilliseconds -or
            [int]$configuration.packetLossPermille -ne [int]$expected.PacketLossPermille -or
            [int]$configuration.stateReorderPermille -ne [int]$expected.StateReorderPermille -or
            [bool]$configuration.isEnabled -ne [bool]$expected.IsEnabled) {
            throw "Evidence '$($item.Name)' effective fault injection configuration differs from its launch configuration."
        }

        [int64]$delayed = [int64]$observed.delayedInboundPacketCount
        [int64]$dropped = [int64]$observed.droppedInboundPacketCount
        [int64]$reordered = [int64]$observed.reorderedInboundStateDatagramCount
        if ($delayed -lt 0 -or $dropped -lt 0 -or $reordered -lt 0) {
            throw "Evidence '$($item.Name)' contains a negative injected-fault count."
        }

        if ($FaultProfile -ceq "normal") {
            if ($delayed -ne 0 -or $dropped -ne 0 -or $reordered -ne 0) {
                throw "Normal-profile evidence '$($item.Name)' reported an injected network fault."
            }
        }
        elseif ($FaultProfile -ceq "unstable") {
            if ($delayed -le 0 -or $dropped -le 0) {
                throw "Unstable-profile evidence '$($item.Name)' did not observe both delayed and dropped inbound packets."
            }
            if ([string]$expected.Role -ceq "replicatedClient") {
                $clientReorderedStateDatagrams += $reordered
            }
        }
        else {
            throw "Unsupported fault profile '$FaultProfile'."
        }
    }

    if ($FaultProfile -ceq "unstable" -and $clientReorderedStateDatagrams -le 0) {
        throw "Unstable-profile client evidence did not observe an actually reordered inbound state datagram."
    }
}

function Assert-ClientCommandAdmissionEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$ClientName,
        [Parameter(Mandatory = $true)]$Command,
        [Parameter(Mandatory = $true)][string]$ExpectedAction,
        [Parameter(Mandatory = $true)][uint64]$ExpectedSequence
    )

    if ([string]$Command.action -cne $ExpectedAction -or
        [uint64]$Command.clientBatchSequence -ne $ExpectedSequence -or
        [string]$Command.admissionStage -cne "Terminal" -or
        [string]$Command.admissionResult -cne "TerminalCompleted") {
        throw "Client evidence '$ClientName' command sequence $ExpectedSequence has an unexpected action or final admission."
    }
    if ([int]$Command.actorCount -le 0 -or
        @($Command.actorHandles).Count -ne [int]$Command.actorCount -or
        @($Command.actorAdmissions).Count -ne [int]$Command.actorCount) {
        throw "Client evidence '$ClientName' command '$($Command.action)' has inconsistent actor evidence."
    }

    $uniqueActorHandles = @($Command.actorHandles |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
        Sort-Object -Unique)
    if ($uniqueActorHandles.Count -ne [int]$Command.actorCount) {
        throw "Client evidence '$ClientName' command '$($Command.action)' has blank or duplicate actor handles."
    }

    $isQueuedTraining = [string]$Command.action -ceq "QueueTrainInfantry"
    $expectedActorResult = if ($isQueuedTraining) { "Queued" } else { "Activated" }
    foreach ($actor in @($Command.actorAdmissions)) {
        if ([string]$actor.stage -cne "EntityIntake" -or
            [string]$actor.result -cne $expectedActorResult) {
            throw "Client evidence '$ClientName' command '$($Command.action)' actor admission " +
                "expected EntityIntake/$expectedActorResult."
        }
    }

    $actorIndexes = @($Command.actorAdmissions | ForEach-Object { [int]$_.batchIndex } | Sort-Object -Unique)
    if ($actorIndexes.Count -ne [int]$Command.actorCount) {
        throw "Client evidence '$ClientName' command '$($Command.action)' has duplicate actor admission indexes."
    }
    for ($actorIndex = 0; $actorIndex -lt [int]$Command.actorCount; $actorIndex++) {
        if ($actorIndexes[$actorIndex] -ne $actorIndex) {
            throw "Client evidence '$ClientName' command '$($Command.action)' does not cover actor index $actorIndex."
        }
    }

    $history = @($Command.admissionHistory)
    $networkTransitionIndex = -1
    $queuedTransitionIndex = -1
    $activatedTransitionIndex = -1
    $terminalTransitionIndex = -1
    $networkTransitionCount = 0
    $queuedTransitionCount = 0
    $activatedTransitionCount = 0
    $terminalTransitionCount = 0
    for ($historyIndex = 0; $historyIndex -lt $history.Count; $historyIndex++) {
        if ([string]$history[$historyIndex].stage -ceq "NetworkIntake" -and
            [string]$history[$historyIndex].result -ceq "NetworkScheduled") {
            $networkTransitionCount++
            if ($networkTransitionIndex -lt 0) {
                $networkTransitionIndex = $historyIndex
            }
        }
        if ([string]$history[$historyIndex].stage -ceq "EntityIntake" -and
            [string]$history[$historyIndex].result -ceq "Queued") {
            $queuedTransitionCount++
            if ($queuedTransitionIndex -lt 0) {
                $queuedTransitionIndex = $historyIndex
            }
        }
        if ([string]$history[$historyIndex].stage -ceq "EntityIntake" -and
            [string]$history[$historyIndex].result -ceq "Activated") {
            $activatedTransitionCount++
            if ($activatedTransitionIndex -lt 0) {
                $activatedTransitionIndex = $historyIndex
            }
        }
        if ([string]$history[$historyIndex].stage -ceq "Terminal" -and
            [string]$history[$historyIndex].result -ceq "TerminalCompleted") {
            $terminalTransitionCount++
            if ($terminalTransitionIndex -lt 0) {
                $terminalTransitionIndex = $historyIndex
            }
        }
    }

    if ($isQueuedTraining) {
        if ($activatedTransitionIndex -ge 0) {
            throw "Client evidence '$ClientName' queued training must not contain EntityIntake/Activated."
        }
        if ($networkTransitionCount -ne 1 -or $queuedTransitionCount -ne 1 -or
            $terminalTransitionCount -ne 1) {
            throw "Client evidence '$ClientName' queued training must contain exactly one scheduled, queued, and terminal transition."
        }
        if ($networkTransitionIndex -lt 0 -or
            $queuedTransitionIndex -le $networkTransitionIndex -or
            $terminalTransitionIndex -le $queuedTransitionIndex) {
            throw "Client evidence '$ClientName' command '$($Command.action)' lacks its ordered network-to-terminal admission history."
        }
    }
    else {
        if ($networkTransitionCount -ne 1 -or $activatedTransitionCount -ne 1 -or
            $terminalTransitionCount -ne 1) {
            throw "Client evidence '$ClientName' command '$($Command.action)' must contain exactly one scheduled, activated, and terminal transition."
        }
        if ($networkTransitionIndex -lt 0 -or
            $activatedTransitionIndex -le $networkTransitionIndex -or
            $terminalTransitionIndex -le $activatedTransitionIndex) {
            throw "Client evidence '$ClientName' command '$($Command.action)' lacks its ordered network-to-terminal admission history."
        }
        if ([string]$Command.action -ceq "TrainInfantry" -and $queuedTransitionIndex -ge 0) {
            throw "Client evidence '$ClientName' first training command unexpectedly entered the entity queue."
        }
    }

    return [pscustomobject]@{
        NetworkTransitionIndex = $networkTransitionIndex
        QueuedTransitionIndex = $queuedTransitionIndex
        ActivatedTransitionIndex = $activatedTransitionIndex
        TerminalTransitionIndex = $terminalTransitionIndex
    }
}

function Assert-MeetingBarrierCommandCausality {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$ClientItems
    )

    $clients = @($ClientItems)
    if ($clients.Count -ne 2) {
        throw "Meeting-barrier causality requires exactly two replicated client artifacts."
    }

    foreach ($item in $clients) {
        $gameplay = $item.Value.gameplay
        if ($null -eq $gameplay.PSObject.Properties["meetingBarrierCommittedTick"]) {
            throw "Client evidence '$($item.Name)' lacks meetingBarrierCommittedTick."
        }
        $barrierTick = [int]$gameplay.meetingBarrierCommittedTick
        if ($barrierTick -le 0) {
            throw "Client evidence '$($item.Name)' has a non-positive meeting barrier tick."
        }
        $attackCommands = @($item.Value.commands | Where-Object {
            [string]$_.action -ceq "AttackEnemyInfantry" -or
            [string]$_.action -ceq "AttackEnemyCore"
        })
        if ($attackCommands.Count -ne 1) {
            throw "Client evidence '$($item.Name)' must contain exactly one attack command."
        }
        if ($null -eq $attackCommands[0].PSObject.Properties["issuedInputRevision"] -or
            [long]$attackCommands[0].issuedInputRevision -le 0 -or
            $null -eq $attackCommands[0].PSObject.Properties["issuedCommittedTick"]) {
            throw "Client evidence '$($item.Name)' attack command lacks positive client issue-time evidence."
        }
        $issuedTick = [int]$attackCommands[0].issuedCommittedTick
        if ($issuedTick -le 0) {
            throw "Client evidence '$($item.Name)' attack command has a non-positive client issue tick."
        }
        $scheduledTransitions = @($attackCommands[0].admissionHistory | Where-Object {
            [string]$_.stage -ceq "NetworkIntake" -and
            [string]$_.result -ceq "NetworkScheduled"
        })
        if ($scheduledTransitions.Count -ne 1 -or
            $null -eq $scheduledTransitions[0].PSObject.Properties["authoritativeCommittedTick"]) {
            throw "Client evidence '$($item.Name)' attack command lacks one authoritative NetworkIntake/NetworkScheduled transition."
        }
        $attackTick = [int]$scheduledTransitions[0].authoritativeCommittedTick
        if ($attackTick -le 0) {
            throw "Client evidence '$($item.Name)' attack command has a non-positive authoritative scheduled tick."
        }
        if ($attackTick -lt $issuedTick) {
            throw "Client evidence '$($item.Name)' attack was scheduled at tick $attackTick before the client issued it at replicated tick $issuedTick."
        }
        if ($issuedTick -lt $barrierTick) {
            throw "Client evidence '$($item.Name)' issued its attack at replicated tick $issuedTick before its local meeting barrier at tick $barrierTick."
        }
    }
}

function Assert-GameplayEvidence {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$Items,
        [Parameter(Mandatory = $true)]$ExpectedPlan,
        [Parameter(Mandatory = $true)]$FrontlineConfig,
        [Parameter(Mandatory = $true)][string]$PlanFingerprint,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$ExpectedFaultInjectionByProcess,
        [Parameter(Mandatory = $true)][string]$FaultProfile
    )

    $all = @($Items)
    if ($all.Count -ne 3) { throw "Exactly three gameplay evidence files are required; observed $($all.Count)." }
    $servers = @($all | Where-Object { [string]$_.Value.role -ceq "authoritativeServer" })
    $clients = @($all | Where-Object { [string]$_.Value.role -ceq "replicatedClient" })
    if ($servers.Count -ne 1 -or $clients.Count -ne 2) {
        throw "Gameplay evidence must contain one authoritative server and two replicated clients."
    }
    if ([string]$servers[0].Name -cne "authoritative-server") {
        throw "Only the authoritative-server artifact may provide authoritative server evidence."
    }
    foreach ($clientItem in $clients) {
        if ([string]$clientItem.Name -cne "client-a" -and [string]$clientItem.Name -cne "client-b") {
            throw "Replicated client evidence came from an unexpected role artifact '$($clientItem.Name)'."
        }
    }

    $epochs = @($all | ForEach-Object { [uint64]$_.Value.sessionEpoch } | Select-Object -Unique)
    $contentFingerprints = @($all | ForEach-Object { [string]$_.Value.contentFingerprint } | Select-Object -Unique)
    $committedTicks = @($all | ForEach-Object { [int]$_.Value.gameplay.committedTick } | Select-Object -Unique)
    if ($epochs.Count -ne 1 -or $epochs[0] -eq 0) { throw "All roles must report the same non-zero session epoch." }
    if ($contentFingerprints.Count -ne 1 -or [string]::IsNullOrWhiteSpace($contentFingerprints[0])) {
        throw "All roles must report the same non-empty content fingerprint."
    }
    if ($committedTicks.Count -ne 1 -or $committedTicks[0] -le 0) {
        throw "All roles must report the same positive authoritative Frontline committed tick."
    }

    $winningSide = [int]$ExpectedPlan.expected.winningSideIndex
    $expectedOutcome = if ($winningSide -eq 0) { "SideOneVictory" } elseif ($winningSide -eq 1) { "SideTwoVictory" } else { throw "Unsupported expected winning side $winningSide." }
    $expectedEvidenceSchemaVersion = [int]$ExpectedPlan.evidenceSchemaVersion
    if ($expectedEvidenceSchemaVersion -le 0) {
        throw "Acceptance plan requires a positive evidenceSchemaVersion."
    }
    foreach ($item in $all) {
        $evidence = $item.Value
        if ([int]$evidence.schemaVersion -ne $expectedEvidenceSchemaVersion -or [string]$evidence.status -cne "passed") {
            throw "Evidence '$($item.Name)' did not pass schema version $expectedEvidenceSchemaVersion."
        }
        if ([int]$evidence.faultCount -ne 0) { throw "Evidence '$($item.Name)' reported $($evidence.faultCount) network faults." }
        if ([string]$evidence.planFingerprint -cne $PlanFingerprint) {
            throw "Evidence '$($item.Name)' plan fingerprint differs from the launcher plan."
        }
        if (@($evidence.seats).Count -ne 2) { throw "Evidence '$($item.Name)' does not contain exactly two seats." }
        for ($seatIndex = 0; $seatIndex -lt 2; $seatIndex++) {
            $seat = @($evidence.seats | Where-Object { [int]$_.seatSlot -eq $seatIndex })
            if ($seat.Count -ne 1 -or [int]$seat[0].playerId -ne ($seatIndex + 1) -or
                [string]$seat[0].connectionState -cne "Connected") {
                throw "Evidence '$($item.Name)' seat $seatIndex is not the expected connected player seat."
            }
        }
        [string[]]$expectedStepNames = if ([string]$evidence.role -ceq "authoritativeServer") {
            @("AuthoritativeMatch")
        }
        else {
            @("Connecting", "Ready", "Gathering", "Training", "Advancing", "Engaging", "WaitingForOutcome")
        }
        $steps = @($evidence.steps)
        if ($steps.Count -ne $expectedStepNames.Count) {
            throw "Evidence '$($item.Name)' contains $($steps.Count) gameplay steps; expected $($expectedStepNames.Count)."
        }
        for ($stepIndex = 0; $stepIndex -lt $expectedStepNames.Count; $stepIndex++) {
            if ([string]$steps[$stepIndex].name -cne $expectedStepNames[$stepIndex] -or
                [string]$steps[$stepIndex].status -cne "passed") {
                throw "Evidence '$($item.Name)' gameplay step $stepIndex is not passed '$($expectedStepNames[$stepIndex])'."
            }
        }
        if ([string]$evidence.gameplay.matchPhase -cne "Completed" -or
            [string]$evidence.gameplay.outcome -cne $expectedOutcome -or
            [int]$evidence.gameplay.winningSideIndex -ne $winningSide) {
            throw "Evidence '$($item.Name)' reports an unexpected final match result."
        }
    }
    Assert-NetworkFaultInjectionEvidence -Items $all `
        -ExpectedByProcess $ExpectedFaultInjectionByProcess -FaultProfile $FaultProfile

    $server = $servers[0].Value
    if ([string]$server.gameplay.outcomeSource -cne "authoritative-frontline-runtime-snapshot" -or
        [string]$server.gameplay.committedTickSource -cne "authoritative-frontline-runtime-snapshot") {
        throw "Server evidence did not come from the authoritative Frontline runtime snapshot."
    }
    if ($null -eq $server.gameplay.PSObject.Properties["firstTrainedInfantrySpawnCommittedTickBySide"]) {
        throw "Server evidence lacks first-trained-infantry authoritative spawn ticks."
    }
    $serverFirstTrainedInfantrySpawnTicks = @($server.gameplay.firstTrainedInfantrySpawnCommittedTickBySide)
    if ($serverFirstTrainedInfantrySpawnTicks.Count -ne 2 -or
        [int]$serverFirstTrainedInfantrySpawnTicks[0] -le 0 -or
        [int]$serverFirstTrainedInfantrySpawnTicks[1] -le 0) {
        throw "Server evidence does not contain two positive first-trained-infantry authoritative spawn ticks."
    }
    if ($null -eq $server.gameplay.PSObject.Properties["secondTrainedInfantrySpawnCommittedTickBySide"]) {
        throw "Server evidence lacks second-trained-infantry authoritative spawn ticks."
    }
    $serverSecondTrainedInfantrySpawnTicks = @($server.gameplay.secondTrainedInfantrySpawnCommittedTickBySide)
    if ($serverSecondTrainedInfantrySpawnTicks.Count -ne 2 -or
        [int]$serverSecondTrainedInfantrySpawnTicks[0] -le 0 -or
        [int]$serverSecondTrainedInfantrySpawnTicks[1] -le 0) {
        throw "Server evidence does not contain two positive second-trained-infantry authoritative spawn ticks."
    }
    $losingSide = if ($winningSide -eq 0) { 1 } else { 0 }
    $serverCoreHealth = @($server.gameplay.observedCoreHealthBySide)
    if ($serverCoreHealth.Count -ne 2 -or $null -eq $serverCoreHealth[0] -or $null -eq $serverCoreHealth[1]) {
        throw "Server evidence does not contain two authoritative core-health values."
    }
    $losingCoreHealth = [double]$serverCoreHealth[$losingSide]
    if ([double]::IsNaN($losingCoreHealth) -or [double]::IsInfinity($losingCoreHealth) -or $losingCoreHealth -ne 0.0) {
        throw "Server evidence does not show the losing command core at zero health."
    }

    $clientSeats = @($clients | ForEach-Object { [int]$_.Value.seatSlot } | Sort-Object -Unique)
    $clientPlayers = @($clients | ForEach-Object { [int]$_.Value.playerId } | Sort-Object -Unique)
    if ($clientSeats.Count -ne 2 -or $clientSeats[0] -ne 0 -or $clientSeats[1] -ne 1 -or
        $clientPlayers.Count -ne 2 -or $clientPlayers[0] -ne 1 -or $clientPlayers[1] -ne 2) {
        throw "Client evidence does not represent two distinct player seats."
    }

    $winnerClientCount = 0
    $loserClientCount = 0
    foreach ($item in $clients) {
        $client = $item.Value
        if ([int]$client.playerId -ne ([int]$client.seatSlot + 1)) {
            throw "Client evidence '$($item.Name)' player $($client.playerId) is not bound to seat $($client.seatSlot)."
        }
        if ([string]$client.gameplay.outcomeSource -cne "replicated-match-state" -or
            [string]$client.gameplay.committedTickSource -cne "replicated-match-state") {
            throw "Client evidence '$($item.Name)' did not use the replicated match-state result."
        }
        if ([int]$client.gameplay.initialCrystals -ne [int]$ExpectedPlan.expected.initialCrystals -or
            [int]$client.gameplay.harvestedCrystals -ne [int]$ExpectedPlan.expected.harvestedCrystals -or
            [int]$client.gameplay.postTrainingCrystals -ne [int]$ExpectedPlan.expected.postTrainingCrystals -or
            [int]$client.gameplay.initialInfantryCount -ne [int]$ExpectedPlan.expected.initialInfantryCount -or
            [int]$client.gameplay.trainedInfantryCount -ne [int]$ExpectedPlan.expected.trainedInfantryCount) {
            throw "Client evidence '$($item.Name)' does not prove the configured gather-and-train economy."
        }
        if ($null -eq $client.gameplay.PSObject.Properties["firstTrainedInfantryObservedCommittedTick"] -or
            $null -eq $client.gameplay.PSObject.Properties["firstTrainedInfantryObservedCount"]) {
            throw "Client evidence '$($item.Name)' lacks first-trained-infantry replicated observation evidence."
        }
        $firstTrainedInfantryObservedTick = [int]$client.gameplay.firstTrainedInfantryObservedCommittedTick
        $expectedFirstTrainedInfantryCount = [int]$ExpectedPlan.expected.initialInfantryCount + 1
        if ($firstTrainedInfantryObservedTick -le 0 -or
            [int]$client.gameplay.firstTrainedInfantryObservedCount -ne $expectedFirstTrainedInfantryCount) {
            throw "Client evidence '$($item.Name)' did not observe exactly the first trained infantry at a positive authoritative tick."
        }
        $serverSpawnTick = [int]$serverFirstTrainedInfantrySpawnTicks[[int]$client.seatSlot]
        if ($firstTrainedInfantryObservedTick -lt $serverSpawnTick) {
            throw "Client evidence '$($item.Name)' observed the first trained infantry before its authoritative spawn tick."
        }
        if ($null -eq $client.gameplay.PSObject.Properties["secondTrainedInfantryObservedCommittedTick"] -or
            $null -eq $client.gameplay.PSObject.Properties["secondTrainedInfantryObservedCount"]) {
            throw "Client evidence '$($item.Name)' lacks second-trained-infantry replicated observation evidence."
        }
        $secondTrainedInfantryObservedTick = [int]$client.gameplay.secondTrainedInfantryObservedCommittedTick
        if ($secondTrainedInfantryObservedTick -le 0 -or
            [int]$client.gameplay.secondTrainedInfantryObservedCount -ne [int]$ExpectedPlan.expected.trainedInfantryCount) {
            throw "Client evidence '$($item.Name)' did not observe exactly the completed infantry training at a positive authoritative tick."
        }
        $serverSecondSpawnTick = [int]$serverSecondTrainedInfantrySpawnTicks[[int]$client.seatSlot]
        if ($secondTrainedInfantryObservedTick -lt $serverSecondSpawnTick) {
            throw "Client evidence '$($item.Name)' observed the second trained infantry before its authoritative spawn tick."
        }
        if ([double]$client.gameplay.attackTargetHealthBefore -le [double]$client.gameplay.attackTargetHealthAfter -or
            [double]$client.gameplay.attackTargetHealthAfter -lt 0) {
            throw "Client evidence '$($item.Name)' does not prove observed attack damage."
        }
        if ([int]$client.gameplay.initialVisibleEnemyInfantryCount -ne 0 -or
            [int]$client.gameplay.initialVisibleEnemyCoreCount -ne 0 -or
            -not [bool]$client.gameplay.enemyInfantryEnteredVision) {
            throw "Client evidence '$($item.Name)' does not prove initial fog concealment followed by enemy infantry disclosure."
        }
        if ($null -eq $client.gameplay.meetingPoint -or $null -eq $client.gameplay.siegePoint) {
            throw "Client evidence '$($item.Name)' lacks its data-derived meeting and siege points."
        }
        if ($null -eq $client.gameplay.attackTargetPositionBefore -or
            [int]$client.gameplay.attackTargetPositionBefore.presentationStableId -le 0) {
            throw "Client evidence '$($item.Name)' lacks the attacked entity's positive presentation stable id and position."
        }
        if ($null -eq $client.gameplay.defeatedCoreLastPosition -or
            [int]$client.gameplay.defeatedCoreLastPosition.presentationStableId -le 0) {
            throw "Client evidence '$($item.Name)' lacks the defeated core's last visible position and stable id."
        }
        if ($null -eq $client.gameplay.completedCameraTarget -or
            [int]$client.gameplay.completedLosingCoreCount -ne 0 -or
            [int]$client.gameplay.completedWinnerInfantryNearDefeatedCoreCount -le 0 -or
            [int]$client.gameplay.completedPresentationFrameId -le 0) {
            throw "Client evidence '$($item.Name)' lacks the verified post-destruction world and camera state."
        }
        $completedWitnesses = @($client.gameplay.completedWinnerInfantryNearDefeatedCorePositions)
        if ($completedWitnesses.Count -ne [int]$client.gameplay.completedWinnerInfantryNearDefeatedCoreCount -or
            @($completedWitnesses | Where-Object {
                $null -eq $_ -or
                $null -eq $_.PSObject.Properties["presentationStableId"] -or
                [int]$_.presentationStableId -le 0
            }).Count -ne 0 -or
            @($completedWitnesses.presentationStableId | Sort-Object -Unique).Count -ne $completedWitnesses.Count) {
            throw "Client evidence '$($item.Name)' does not bind each completion witness to a unique presentation stable id."
        }

        if ($null -eq $client.gameplay.harvesterStartPosition -or $null -eq $client.gameplay.harvesterEndPosition) {
            throw "Client evidence '$($item.Name)' lacks harvester movement positions."
        }
        $harvesterDx = [int64]$client.gameplay.harvesterEndPosition.xCm - [int64]$client.gameplay.harvesterStartPosition.xCm
        $harvesterDy = [int64]$client.gameplay.harvesterEndPosition.yCm - [int64]$client.gameplay.harvesterStartPosition.yCm
        $minimumMoveSquared = [int64]$ExpectedPlan.battle.minimumObservedMoveCm * [int64]$ExpectedPlan.battle.minimumObservedMoveCm
        if (($harvesterDx * $harvesterDx) + ($harvesterDy * $harvesterDy) -lt $minimumMoveSquared) {
            throw "Client evidence '$($item.Name)' does not prove harvester travel during gathering."
        }

        $commands = @($client.commands)
        $isWinner = @($commands | Where-Object { [string]$_.action -ceq "AttackEnemyCore" }).Count -eq 1
        if ($isWinner -ne ([int]$client.seatSlot -eq $winningSide)) {
            throw "Client evidence '$($item.Name)' attack role does not match the authoritative winning side."
        }
        $gatherCommandCount = 0
        while ($gatherCommandCount -lt $commands.Count -and
            [string]$commands[$gatherCommandCount].action -ceq "Gather") {
            $gatherCommandCount++
        }
        if ($gatherCommandCount -eq 0) {
            throw "Client evidence '$($item.Name)' did not begin with a gather command."
        }
        $roleActions = if ($isWinner) {
            $winnerClientCount++
            if (-not [bool]$client.gameplay.enemyCoreEnteredVision) {
                throw "Winning client evidence '$($item.Name)' does not prove the opposing core entered vision before attack."
            }
            @("TrainInfantry", "QueueTrainInfantry", "MoveToMeeting", "MoveToSiege", "AttackEnemyCore")
        }
        else {
            $loserClientCount++
            @("TrainInfantry", "QueueTrainInfantry", "MoveToMeeting", "AttackEnemyInfantry")
        }
        $expectedActions = @(@("Gather") * $gatherCommandCount) + @($roleActions)
        if ($commands.Count -ne $expectedActions.Count) {
            throw "Client evidence '$($item.Name)' has $($commands.Count) command batches; expected $($expectedActions.Count)."
        }
        for ($commandIndex = 0; $commandIndex -lt $commands.Count; $commandIndex++) {
            $command = $commands[$commandIndex]
            [void](Assert-ClientCommandAdmissionEvidence `
                -ClientName ([string]$item.Name) `
                -Command $command `
                -ExpectedAction ([string]$expectedActions[$commandIndex]) `
                -ExpectedSequence ([uint64]($commandIndex + 1)))
        }

        $producedInfantry = [int]$client.gameplay.trainedInfantryCount - [int]$client.gameplay.initialInfantryCount
        $trainingCommands = @($commands | Where-Object {
            [string]$_.action -ceq "TrainInfantry" -or [string]$_.action -ceq "QueueTrainInfantry"
        })
        $trainingCommandCount = $trainingCommands.Count
        $spentCrystals = [int]$client.gameplay.harvestedCrystals - [int]$client.gameplay.postTrainingCrystals
        $configuredTrainingSpend = $trainingCommandCount * [int]$FrontlineConfig.trainCostCrystals
        if ($trainingCommandCount -ne 2 -or $producedInfantry -ne $trainingCommandCount -or
            $spentCrystals -ne $configuredTrainingSpend) {
            throw "Client evidence '$($item.Name)' does not prove two training commands with exact configured cost and output."
        }
        if (@($trainingCommands | Where-Object { [int]$_.actorCount -ne 1 }).Count -ne 0) {
            throw "Client evidence '$($item.Name)' training commands must each come from exactly one selected command core."
        }
        $immediateTrainingCommands = @($trainingCommands | Where-Object { [string]$_.action -ceq "TrainInfantry" })
        if ($immediateTrainingCommands.Count -ne 1) {
            throw "Client evidence '$($item.Name)' must contain exactly one immediate infantry training command."
        }
        $immediateTrainingActivations = @($immediateTrainingCommands[0].admissionHistory | Where-Object {
            [string]$_.stage -ceq "EntityIntake" -and [string]$_.result -ceq "Activated"
        })
        if ($immediateTrainingActivations.Count -ne 1 -or
            $null -eq $immediateTrainingActivations[0].PSObject.Properties["authoritativeCommittedTick"]) {
            throw "Client evidence '$($item.Name)' immediate training lacks one authoritative EntityIntake:Activated transition."
        }
        $immediateTrainingTerminals = @($immediateTrainingCommands[0].admissionHistory | Where-Object {
            [string]$_.stage -ceq "Terminal" -and [string]$_.result -ceq "TerminalCompleted"
        })
        if ($immediateTrainingTerminals.Count -ne 1 -or
            $null -eq $immediateTrainingTerminals[0].PSObject.Properties["authoritativeCommittedTick"]) {
            throw "Client evidence '$($item.Name)' immediate training lacks one authoritative terminal transition."
        }
        $immediateActivatedAuthoritativeTick = [int]$immediateTrainingActivations[0].authoritativeCommittedTick
        $immediateTerminalAuthoritativeTick = [int]$immediateTrainingTerminals[0].authoritativeCommittedTick
        $queuedTrainingCommands = @($trainingCommands | Where-Object { [string]$_.action -ceq "QueueTrainInfantry" })
        if ($queuedTrainingCommands.Count -ne 1) {
            throw "Client evidence '$($item.Name)' must contain exactly one queued infantry training command."
        }
        $queuedTrainingAdmissions = @($queuedTrainingCommands[0].admissionHistory | Where-Object {
            [string]$_.stage -ceq "EntityIntake" -and [string]$_.result -ceq "Queued"
        })
        $queuedTrainingTerminals = @($queuedTrainingCommands[0].admissionHistory | Where-Object {
            [string]$_.stage -ceq "Terminal" -and [string]$_.result -ceq "TerminalCompleted"
        })
        if ($queuedTrainingAdmissions.Count -ne 1 -or $queuedTrainingTerminals.Count -ne 1) {
            throw "Client evidence '$($item.Name)' queued training must contain one queued admission and one terminal transition."
        }
        if ($null -eq $queuedTrainingAdmissions[0].PSObject.Properties["authoritativeCommittedTick"] -or
            $null -eq $queuedTrainingTerminals[0].PSObject.Properties["authoritativeCommittedTick"]) {
            throw "Client evidence '$($item.Name)' queued training lacks authoritative admission or terminal ticks."
        }
        $queuedAuthoritativeTick = [int]$queuedTrainingAdmissions[0].authoritativeCommittedTick
        $queuedTerminalAuthoritativeTick = [int]$queuedTrainingTerminals[0].authoritativeCommittedTick
        if ($immediateActivatedAuthoritativeTick -le 0 -or
            $queuedAuthoritativeTick -le $immediateActivatedAuthoritativeTick -or
            $queuedAuthoritativeTick -ge $immediateTerminalAuthoritativeTick -or
            $immediateTerminalAuthoritativeTick -gt $serverSpawnTick -or
            $serverSpawnTick -gt $firstTrainedInfantryObservedTick -or
            $queuedTerminalAuthoritativeTick -le $immediateTerminalAuthoritativeTick -or
            $queuedTerminalAuthoritativeTick -gt $serverSecondSpawnTick -or
            $serverSecondSpawnTick -gt $secondTrainedInfantryObservedTick) {
            throw "Client evidence '$($item.Name)' queued training admission, ordered completion, spawn, and observation are not causally ordered."
        }

        $meetingCommands = @($commands | Where-Object { [string]$_.action -ceq "MoveToMeeting" })
        if ($meetingCommands.Count -ne 1) {
            throw "Client evidence '$($item.Name)' must contain exactly one MoveToMeeting command."
        }
        $meetingCommand = $meetingCommands[0]
        $meetingActorSet = @($meetingCommand.actorHandles | Sort-Object) -join "|"
        $selectedInfantryHandles = @($client.gameplay.selectedInfantryHandles)
        $selectedInfantrySet = @($selectedInfantryHandles | Sort-Object -Unique) -join "|"
        if (@($selectedInfantryHandles | Sort-Object -Unique).Count -ne $selectedInfantryHandles.Count) {
            throw "Client evidence '$($item.Name)' contains duplicate selected infantry handles."
        }
        if ($isWinner) {
            $siegeCommands = @($commands | Where-Object { [string]$_.action -ceq "MoveToSiege" })
            $coreAttackCommands = @($commands | Where-Object { [string]$_.action -ceq "AttackEnemyCore" })
            $minimumWinnerActors = [int]$ExpectedPlan.expected.winnerMinimumAttackers
            $expectedWinnerCasualties = [int]$ExpectedPlan.expected.winnerCasualtiesBeforeSiege
            if ([int]$meetingCommand.actorCount -lt $minimumWinnerActors -or
                $siegeCommands.Count -ne 1 -or [int]$siegeCommands[0].actorCount -lt $minimumWinnerActors -or
                $coreAttackCommands.Count -ne 1 -or [int]$coreAttackCommands[0].actorCount -lt $minimumWinnerActors) {
                throw "Winning client evidence '$($item.Name)' does not prove multi-unit advance, siege move, and core attack."
            }
            if ([int]$meetingCommand.actorCount - [int]$siegeCommands[0].actorCount -ne $expectedWinnerCasualties) {
                throw "Winning client evidence '$($item.Name)' does not prove the configured casualties were replicated before the surviving infantry siege command."
            }
            $coreAttackActorSet = @($coreAttackCommands[0].actorHandles | Sort-Object) -join "|"
            foreach ($siegeHandle in @($siegeCommands[0].actorHandles)) {
                if (-not (@($meetingCommand.actorHandles) -ccontains [string]$siegeHandle)) {
                    throw "Winning client evidence '$($item.Name)' added an unrelated actor to the siege move."
                }
            }
            foreach ($coreAttackHandle in @($coreAttackCommands[0].actorHandles)) {
                if (-not (@($siegeCommands[0].actorHandles) -ccontains [string]$coreAttackHandle)) {
                    throw "Winning client evidence '$($item.Name)' added an unrelated actor to the core attack."
                }
            }
            if ($selectedInfantryHandles.Count -ne [int]$coreAttackCommands[0].actorCount -or
                $selectedInfantrySet -cne $coreAttackActorSet) {
                throw "Winning client evidence '$($item.Name)' final selected infantry do not match the core attack actors."
            }
        }
        else {
            if ([int]$meetingCommand.actorCount -ne [int]$ExpectedPlan.expected.loserAttackers) {
                throw "Losing client evidence '$($item.Name)' moved an unexpected number of infantry."
            }
            if ($selectedInfantryHandles.Count -ne [int]$meetingCommand.actorCount -or
                $selectedInfantrySet -cne $meetingActorSet) {
                throw "Losing client evidence '$($item.Name)' selected infantry do not match the MoveToMeeting actors."
            }
        }

        $startPositions = @($client.gameplay.moveStartPositions)
        $endPositions = @($client.gameplay.moveEndPositions)
        if ($startPositions.Count -ne [int]$meetingCommand.actorCount -or
            $endPositions.Count -ne [int]$meetingCommand.actorCount) {
            throw "Client evidence '$($item.Name)' lacks one start and end position per MoveToMeeting actor."
        }
        $uniqueStartHandles = @($startPositions | ForEach-Object { [string]$_.handle } | Sort-Object -Unique)
        $uniqueEndHandles = @($endPositions | ForEach-Object { [string]$_.handle } | Sort-Object -Unique)
        if ($uniqueStartHandles.Count -ne $startPositions.Count -or $uniqueEndHandles.Count -ne $endPositions.Count) {
            throw "Client evidence '$($item.Name)' contains duplicate movement position handles."
        }
        $uniqueStartStableIds = @($startPositions | ForEach-Object { [int]$_.presentationStableId } | Sort-Object -Unique)
        $uniqueEndStableIds = @($endPositions | ForEach-Object { [int]$_.presentationStableId } | Sort-Object -Unique)
        if ($uniqueStartStableIds.Count -ne $startPositions.Count -or
            $uniqueEndStableIds.Count -ne $endPositions.Count -or
            @($uniqueStartStableIds | Where-Object { $_ -le 0 }).Count -ne 0 -or
            @($uniqueEndStableIds | Where-Object { $_ -le 0 }).Count -ne 0) {
            throw "Client evidence '$($item.Name)' contains invalid or duplicate movement presentation stable ids."
        }
        $minimumSquared = [int64]$ExpectedPlan.battle.minimumObservedMoveCm * [int64]$ExpectedPlan.battle.minimumObservedMoveCm
        foreach ($actorHandle in @($meetingCommand.actorHandles)) {
            $start = @($startPositions | Where-Object { [string]$_.handle -ceq [string]$actorHandle })
            $end = @($endPositions | Where-Object { [string]$_.handle -ceq [string]$actorHandle })
            if ($start.Count -ne 1 -or $end.Count -ne 1) {
                throw "Client evidence '$($item.Name)' cannot correlate movement positions for actor '$actorHandle'."
            }
            if ([int]$start[0].presentationStableId -ne [int]$end[0].presentationStableId) {
                throw "Client evidence '$($item.Name)' actor '$actorHandle' changed presentation stable identity while moving."
            }
            $dx = [int64]$end[0].xCm - [int64]$start[0].xCm
            $dy = [int64]$end[0].yCm - [int64]$start[0].yCm
            if (($dx * $dx) + ($dy * $dy) -lt $minimumSquared) {
                throw "Client evidence '$($item.Name)' actor '$actorHandle' did not move the configured minimum distance."
            }
        }
    }
    if ($winnerClientCount -ne 1 -or $loserClientCount -ne 1) {
        throw "Client evidence must prove one core attack and one opposing-infantry attack."
    }
    [void](Assert-MeetingBarrierCommandCausality -ClientItems $clients)
}

function Get-WorldEvidenceDistanceSquared {
    param(
        [Parameter(Mandatory = $true)][int64]$LeftX,
        [Parameter(Mandatory = $true)][int64]$LeftY,
        [Parameter(Mandatory = $true)][int64]$RightX,
        [Parameter(Mandatory = $true)][int64]$RightY
    )

    $dx = $LeftX - $RightX
    $dy = $LeftY - $RightY
    return ($dx * $dx) + ($dy * $dy)
}

function Get-GameplayWorldAnchor {
    param(
        [Parameter(Mandatory = $true)]$Gameplay,
        [Parameter(Mandatory = $true)][string]$Anchor,
        [Parameter(Mandatory = $true)][string]$ProcessName,
        [Parameter(Mandatory = $true)][string]$Milestone
    )

    $point = switch -CaseSensitive ($Anchor) {
        "meeting" { $Gameplay.gameplay.meetingPoint; break }
        "siege" { $Gameplay.gameplay.siegePoint; break }
        "defeatedCore" { $Gameplay.gameplay.defeatedCoreLastPosition; break }
        default { throw "World evidence '$Milestone' for '$ProcessName' uses unsupported anchor '$Anchor'." }
    }
    if ($null -eq $point -or $null -eq $point.PSObject.Properties["xCm"] -or
        $null -eq $point.PSObject.Properties["yCm"]) {
        throw "Gameplay evidence '$ProcessName' lacks '$Anchor' coordinates for screenshot milestone '$Milestone'."
    }
    return [pscustomobject]@{ xCm = [int]$point.xCm; yCm = [int]$point.yCm }
}

function Get-RequiredPresentationStableIds {
    param(
        [Parameter(Mandatory = $true)]$Gameplay,
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$ProcessName,
        [Parameter(Mandatory = $true)][string]$Milestone,
        [Parameter(Mandatory = $true)][string]$Role
    )

    $positions = @(switch -CaseSensitive ($Source) {
        "selectedInfantry" { @($Gameplay.gameplay.moveStartPositions); break }
        "attackTarget" { @($Gameplay.gameplay.attackTargetPositionBefore); break }
        "defeatedCore" { @($Gameplay.gameplay.defeatedCoreLastPosition); break }
        "completedWinnerInfantry" { @($Gameplay.gameplay.completedWinnerInfantryNearDefeatedCorePositions); break }
        default { throw "World evidence role '$Role' for '${ProcessName}:$Milestone' uses unsupported source '$Source'." }
    })
    if ($positions.Count -eq 0 -or @($positions | Where-Object { $null -eq $_ }).Count -ne 0) {
        throw "Gameplay evidence '$ProcessName' lacks stable identity source '$Source' for role '$Role' at '$Milestone'."
    }
    $ids = @($positions | ForEach-Object {
        if ($null -eq $_.PSObject.Properties["presentationStableId"] -or [int]$_.presentationStableId -le 0) {
            throw "Gameplay evidence '$ProcessName' has an invalid presentation stable id in '$Source'."
        }
        [int]$_.presentationStableId
    } | Sort-Object -Unique)
    if ($ids.Count -ne $positions.Count) {
        throw "Gameplay evidence '$ProcessName' has duplicate presentation stable ids in '$Source'."
    }
    return @($ids)
}

function Get-DistinctEntityLayoutSources {
    param(
        [Parameter(Mandatory = $true)]$Layout
    )

    if ($null -eq $Layout.PSObject.Properties["sources"] -or $null -eq $Layout.sources) {
        return
    }

    $Layout.sources | ForEach-Object { [string]$_ }
}

function Resolve-GroupMoveTargetLayoutEvidence {
    param(
        [Parameter(Mandatory = $true)]$SourceGraph
    )

    $rtsDemoMods = @($SourceGraph.plannedMods | Where-Object { [string]$_.id -ceq "RtsDemoMod" })
    if ($rtsDemoMods.Count -ne 1) {
        throw "Launcher graph must contain exactly one RtsDemoMod for group-move layout evidence; observed $($rtsDemoMods.Count)."
    }

    $mappingPath = [System.IO.Path]::GetFullPath((Join-Path `
        ([string]$rtsDemoMods[0].rootPath) "assets\Input\input_order_mappings.json"))
    if (-not (Test-Path -LiteralPath $mappingPath -PathType Leaf)) {
        throw "Formal RTS input mapping is missing: $mappingPath"
    }
    try {
        $mapping = Get-Content -LiteralPath $mappingPath -Raw | ConvertFrom-Json
    }
    catch {
        throw "Formal RTS input mapping is not valid JSON: $mappingPath. $($_.Exception.Message)"
    }
    if ($null -eq $mapping.PSObject.Properties["groupMoveTargetLayout"] -or
        $null -eq $mapping.groupMoveTargetLayout) {
        throw "Formal RTS input mapping lacks groupMoveTargetLayout."
    }

    $layout = $mapping.groupMoveTargetLayout
    $orderTypeKeys = @($layout.orderTypeKeys | ForEach-Object { [string]$_ })
    $uniqueOrderTypeKeys = @($orderTypeKeys | Sort-Object -Unique -CaseSensitive)
    if ([string]$layout.mode -cne "Grid" -or
        $orderTypeKeys.Count -ne $uniqueOrderTypeKeys.Count -or
        -not ($orderTypeKeys -ccontains "moveTo")) {
        throw "Formal RTS groupMoveTargetLayout must be Grid and contain moveTo exactly once."
    }
    if ([string]$layout.assignment -cne "PreserveRelative") {
        throw "Formal RTS groupMoveTargetLayout.assignment must be PreserveRelative."
    }
    $spacingCm = [double]$layout.spacingCm
    if ([double]::IsNaN($spacingCm) -or [double]::IsInfinity($spacingCm) -or
        $spacingCm -le 0 -or $spacingCm -ne [Math]::Floor($spacingCm)) {
        throw "Formal RTS groupMoveTargetLayout.spacingCm must be a positive finite integer."
    }

    return [pscustomobject]@{
        source = "groupMoveTargetLayout.spacingCm"
        modId = "RtsDemoMod"
        mode = [string]$layout.mode
        assignment = [string]$layout.assignment
        orderTypeKeys = $orderTypeKeys
        spacingCm = [int64]$spacingCm
        config = Get-FileEvidence -Path $mappingPath
    }
}

function Assert-ClientWorldPresentationEvidence {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$PresentationItems,
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$GameplayItems,
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$Requirements,
        [Parameter(Mandatory = $true)]$GroupMoveLayoutEvidence
    )

    $presentations = @($PresentationItems)
    $clients = @($GameplayItems | Where-Object { [string]$_.Value.role -ceq "replicatedClient" })
    $rules = @($Requirements)
    if ($presentations.Count -ne 2 -or $clients.Count -ne 2) {
        throw "World presentation evidence requires exactly two client presentation artifacts and two client gameplay artifacts."
    }
    if ($rules.Count -eq 0) {
        throw "World presentation evidence has no configured requirements."
    }

    $verified = [System.Collections.Generic.List[object]]::new()
    foreach ($rule in $rules) {
        $milestone = [string]$rule.milestone
        $perspective = [string]$rule.perspective
        $anchorName = [string]$rule.anchor
        $positionToleranceCm = [int64]$rule.positionToleranceCm
        $cameraToleranceCm = [int64]$rule.cameraToleranceCm
        $positionToleranceSquared = $positionToleranceCm * $positionToleranceCm
        $cameraToleranceSquared = $cameraToleranceCm * $cameraToleranceCm
        $matchedPerspectiveCount = 0

        foreach ($clientItem in $clients) {
            $processName = [string]$clientItem.Name
            $gameplay = $clientItem.Value
            $isWinner = [int]$gameplay.seatSlot -eq [int]$gameplay.gameplay.winningSideIndex
            $matchesPerspective = switch -CaseSensitive ($perspective) {
                "all" { $true; break }
                "winner" { $isWinner; break }
                "loser" { -not $isWinner; break }
                default { throw "World evidence '$milestone' uses unsupported perspective '$perspective'." }
            }
            if (-not $matchesPerspective) {
                continue
            }
            $matchedPerspectiveCount++

            $presentation = @($presentations | Where-Object { [string]$_.process -ceq $processName })
            if ($presentation.Count -ne 1) {
                throw "Client '$processName' has no unique presentation artifact."
            }
            $milestoneRecord = @($presentation[0].milestones | Where-Object { [string]$_.milestone -ceq $milestone })
            if ($milestoneRecord.Count -ne 1 -or $null -eq $milestoneRecord[0].worldEvidence) {
                throw "Client '$processName' has no unique same-frame world sidecar for milestone '$milestone'."
            }
            $document = $milestoneRecord[0].worldEvidence
            $anchor = Get-GameplayWorldAnchor -Gameplay $gameplay -Anchor $anchorName `
                -ProcessName $processName -Milestone $milestone
            $cameraDistanceSquared = Get-WorldEvidenceDistanceSquared `
                -LeftX ([int64]$document.cameraTargetXCm) -LeftY ([int64]$document.cameraTargetYCm) `
                -RightX ([int64]$anchor.xCm) -RightY ([int64]$anchor.yCm)
            if ($cameraDistanceSquared -gt $cameraToleranceSquared) {
                throw "Client '$processName' screenshot milestone '$milestone' camera is outside the '$anchorName' tolerance."
            }

            $roleResults = [System.Collections.Generic.List[object]]::new()
            foreach ($roleRequirement in @($rule.requiredRoles)) {
                $role = [string]$roleRequirement.role
                $template = [string]$roleRequirement.template
                $source = [string]$roleRequirement.source
                $stableIds = @(Get-RequiredPresentationStableIds -Gameplay $gameplay -Source $source `
                    -ProcessName $processName -Milestone $milestone -Role $role)
                $matching = @($document.instances | Where-Object {
                    [string]$_.template -ceq $template -and
                    ($stableIds.Count -eq 0 -or $stableIds -contains [int]$_.ownerStableId)
                })
                if ($matching.Count -eq 0) {
                    throw "Client '$processName' screenshot milestone '$milestone' has no same-frame stable entity for role '$role' (template '$template')."
                }
                $nearAndReadable = @($matching | Where-Object {
                    $distanceSquared = Get-WorldEvidenceDistanceSquared `
                        -LeftX ([int64]$_.worldXCm) -LeftY ([int64]$_.worldYCm) `
                        -RightX ([int64]$anchor.xCm) -RightY ([int64]$anchor.yCm)
                    $distanceSquared -le $positionToleranceSquared -and
                        [double]$_.shortEdgePx -ge [double]$roleRequirement.minimumShortEdgePx -and
                        [double]$_.areaPx2 -ge [double]$roleRequirement.minimumAreaPx2
                })
                if ($nearAndReadable.Count -lt [int]$roleRequirement.minimumOnscreen) {
                    throw "Client '$processName' screenshot milestone '$milestone' role '$role' is missing, unreadable, or in the wrong '$anchorName' region."
                }
                $roleResults.Add([ordered]@{
                    role = $role
                    template = $template
                    source = $source
                    onscreenNearAnchor = $nearAndReadable.Count
                })
            }

            $distinctLayoutResult = $null
            if ($null -ne $rule.PSObject.Properties["distinctEntityLayout"] -and
                $null -ne $rule.distinctEntityLayout) {
                $layout = $rule.distinctEntityLayout
                $layoutTemplate = [string]$layout.template
                $templateInstances = @($document.instances | Where-Object {
                    [string]$_.template -ceq $layoutTemplate
                })
                $layoutScope = [string]$layout.scope
                $layoutRegion = [string]$layout.region
                $layoutInstances = if ($layoutScope -ceq "allVisibleTemplate") {
                    @($templateInstances)
                }
                elseif ($layoutScope -ceq "stableEntitySources") {
                    $layoutSources = @(Get-DistinctEntityLayoutSources -Layout $layout)
                    $layoutStableIds = @($layoutSources | ForEach-Object {
                        Get-RequiredPresentationStableIds -Gameplay $gameplay -Source ([string]$_) `
                            -ProcessName $processName -Milestone $milestone -Role "distinctEntityLayout"
                    } | Sort-Object -Unique)
                    foreach ($stableId in $layoutStableIds) {
                        $stableMatches = @($templateInstances | Where-Object { [int]$_.ownerStableId -eq [int]$stableId })
                        if ($stableMatches.Count -ne 1) {
                            throw "Client '$processName' screenshot milestone '$milestone' cannot bind distinct-layout stable entity '$stableId' exactly once."
                        }
                    }
                    @($templateInstances | Where-Object { $layoutStableIds -contains [int]$_.ownerStableId })
                }
                else {
                    throw "Client '$processName' screenshot milestone '$milestone' uses unsupported distinct layout scope '$layoutScope'."
                }
                $layoutInstances = @($layoutInstances)
                if ($layoutRegion -ceq "anchor") {
                    $layoutInstances = @($layoutInstances | Where-Object {
                        (Get-WorldEvidenceDistanceSquared `
                            -LeftX ([int64]$_.worldXCm) -LeftY ([int64]$_.worldYCm) `
                            -RightX ([int64]$anchor.xCm) -RightY ([int64]$anchor.yCm)) -le $positionToleranceSquared
                    })
                }
                elseif ($layoutRegion -cne "screen") {
                    throw "Client '$processName' screenshot milestone '$milestone' uses unsupported distinct layout region '$layoutRegion'."
                }
                $minimumInstances = [int]$layout.minimumInstances
                if ($layoutInstances.Count -lt $minimumInstances) {
                    throw "Client '$processName' screenshot milestone '$milestone' has $($layoutInstances.Count) " +
                        "'$layoutTemplate' entities; distinct layout requires at least $minimumInstances."
                }

                $explicitMinimumWorldSeparationCm = [int64]0
                $hasExplicitMinimumWorldSeparation = $null -ne $layout.PSObject.Properties["minimumWorldSeparationCm"]
                if ($hasExplicitMinimumWorldSeparation) {
                    $explicitMinimumWorldSeparationCm = [int64]$layout.minimumWorldSeparationCm
                    if ($explicitMinimumWorldSeparationCm -le 0) {
                        throw "Client '$processName' screenshot milestone '$milestone' distinct layout explicit minimum world separation must be positive."
                    }
                }
                elseif ([string]$layout.minimumWorldSeparationSource -cne [string]$GroupMoveLayoutEvidence.source) {
                    throw "Client '$processName' screenshot milestone '$milestone' distinct layout does not use the formal group-move spacing source."
                }
                if ($hasExplicitMinimumWorldSeparation) {
                    $minimumWorldSeparationCm = $explicitMinimumWorldSeparationCm
                }
                else {
                    $minimumWorldSeparationCm = [int64]$GroupMoveLayoutEvidence.spacingCm
                }
                $minimumWorldSeparationSquared = $minimumWorldSeparationCm * $minimumWorldSeparationCm
                $maximumScreenOverlapRatio = [double]$layout.maximumScreenOverlapRatio
                foreach ($instance in $layoutInstances) {
                    $screenLeft = [double]$instance.screenLeftPx
                    $screenTop = [double]$instance.screenTopPx
                    $screenRight = [double]$instance.screenRightPx
                    $screenBottom = [double]$instance.screenBottomPx
                    if ([double]::IsNaN($screenLeft) -or [double]::IsInfinity($screenLeft) -or
                        [double]::IsNaN($screenTop) -or [double]::IsInfinity($screenTop) -or
                        [double]::IsNaN($screenRight) -or [double]::IsInfinity($screenRight) -or
                        [double]::IsNaN($screenBottom) -or [double]::IsInfinity($screenBottom) -or
                        $screenRight -le $screenLeft -or $screenBottom -le $screenTop) {
                        throw "Client '$processName' screenshot milestone '$milestone' distinct layout contains a non-finite or empty screen box."
                    }
                }
                for ($leftIndex = 0; $leftIndex -lt $layoutInstances.Count; $leftIndex++) {
                    $left = $layoutInstances[$leftIndex]
                    for ($rightIndex = $leftIndex + 1; $rightIndex -lt $layoutInstances.Count; $rightIndex++) {
                        $right = $layoutInstances[$rightIndex]
                        if ([int]$left.ownerStableId -eq [int]$right.ownerStableId) {
                            throw "Client '$processName' screenshot milestone '$milestone' duplicates owner stable id '$([int]$left.ownerStableId)' for '$layoutTemplate'."
                        }

                        $worldSeparationSquared = Get-WorldEvidenceDistanceSquared `
                            -LeftX ([int64]$left.worldXCm) -LeftY ([int64]$left.worldYCm) `
                            -RightX ([int64]$right.worldXCm) -RightY ([int64]$right.worldYCm)
                        if ($worldSeparationSquared -lt $minimumWorldSeparationSquared) {
                            throw "Client '$processName' screenshot milestone '$milestone' overlaps '$layoutTemplate' entities " +
                                "'$([int]$left.ownerStableId)' and '$([int]$right.ownerStableId)' in the world."
                        }

                        $intersectionWidth = [Math]::Max(0.0,
                            [Math]::Min([double]$left.screenRightPx, [double]$right.screenRightPx) -
                            [Math]::Max([double]$left.screenLeftPx, [double]$right.screenLeftPx))
                        $intersectionHeight = [Math]::Max(0.0,
                            [Math]::Min([double]$left.screenBottomPx, [double]$right.screenBottomPx) -
                            [Math]::Max([double]$left.screenTopPx, [double]$right.screenTopPx))
                        $intersectionArea = $intersectionWidth * $intersectionHeight
                        $leftArea = ([double]$left.screenRightPx - [double]$left.screenLeftPx) *
                            ([double]$left.screenBottomPx - [double]$left.screenTopPx)
                        $rightArea = ([double]$right.screenRightPx - [double]$right.screenLeftPx) *
                            ([double]$right.screenBottomPx - [double]$right.screenTopPx)
                        $smallerArea = [Math]::Min($leftArea, $rightArea)
                        if ($smallerArea -le 0 -or [double]::IsNaN($smallerArea) -or [double]::IsInfinity($smallerArea)) {
                            throw "Client '$processName' screenshot milestone '$milestone' distinct layout has an invalid overlap denominator."
                        }
                        $screenOverlapRatio = $intersectionArea / $smallerArea
                        if ([double]::IsNaN($screenOverlapRatio) -or [double]::IsInfinity($screenOverlapRatio)) {
                            throw "Client '$processName' screenshot milestone '$milestone' distinct layout produced a non-finite screen overlap ratio."
                        }
                        if ($screenOverlapRatio -gt $maximumScreenOverlapRatio) {
                            throw "Client '$processName' screenshot milestone '$milestone' overlaps '$layoutTemplate' entities " +
                                "'$([int]$left.ownerStableId)' and '$([int]$right.ownerStableId)' on screen " +
                                "(ratio=$([Math]::Round($screenOverlapRatio, 4)))."
                        }
                    }
                }

                $minimumWorldSeparationSourceLabel = [string]$GroupMoveLayoutEvidence.source
                if ($hasExplicitMinimumWorldSeparation) {
                    $minimumWorldSeparationSourceLabel = "explicit"
                }
                $distinctLayoutResult = [ordered]@{
                    template = $layoutTemplate
                    scope = $layoutScope
                    region = $layoutRegion
                    instanceCount = $layoutInstances.Count
                    minimumWorldSeparationSource = $minimumWorldSeparationSourceLabel
                    minimumWorldSeparationCm = $minimumWorldSeparationCm
                    maximumScreenOverlapRatio = $maximumScreenOverlapRatio
                }
            }

            foreach ($forbidden in @($rule.forbiddenRoles)) {
                $role = [string]$forbidden.role
                $template = [string]$forbidden.template
                $forbiddenInstances = @($document.instances | Where-Object { [string]$_.template -ceq $template })
                if ([string]$forbidden.scope -ceq "anchor") {
                    $forbiddenInstances = @($forbiddenInstances | Where-Object {
                        (Get-WorldEvidenceDistanceSquared `
                            -LeftX ([int64]$_.worldXCm) -LeftY ([int64]$_.worldYCm) `
                            -RightX ([int64]$anchor.xCm) -RightY ([int64]$anchor.yCm)) -le $positionToleranceSquared
                    })
                }
                elseif ([string]$forbidden.scope -cne "screen") {
                    throw "World evidence forbidden role '$role' uses unsupported scope '$($forbidden.scope)'."
                }
                if ($forbiddenInstances.Count -ne 0) {
                    throw "Client '$processName' screenshot milestone '$milestone' still shows forbidden role '$role' (template '$template')."
                }
            }

            if ($null -ne $rule.PSObject.Properties["stableEntityMotion"] -and
                $null -ne $rule.stableEntityMotion) {
                $motion = $rule.stableEntityMotion
                $starts = @($gameplay.gameplay.moveStartPositions)
                if ($starts.Count -eq 0) {
                    throw "Client '$processName' has no movement starts for '$milestone'."
                }
                $minimumMoveCm = [int64]$motion.minimumObservedMoveCm
                $minimumMoveSquared = $minimumMoveCm * $minimumMoveCm
                foreach ($start in $starts) {
                    $stableId = [int]$start.presentationStableId
                    $instances = @($document.instances | Where-Object {
                        [int]$_.ownerStableId -eq $stableId -and
                        [string]$_.template -ceq [string]$motion.template
                    })
                    if ($instances.Count -ne 1) {
                        throw "Client '$processName' screenshot milestone '$milestone' cannot bind selected stable entity '$stableId' to its visible infantry template."
                    }
                    $movedSquared = Get-WorldEvidenceDistanceSquared `
                        -LeftX ([int64]$instances[0].worldXCm) -LeftY ([int64]$instances[0].worldYCm) `
                        -RightX ([int64]$start.xCm) -RightY ([int64]$start.yCm)
                    if ($movedSquared -lt $minimumMoveSquared) {
                        throw "Client '$processName' screenshot milestone '$milestone' selected stable entity '$stableId' did not visibly move the configured minimum distance."
                    }
                }
            }

            if ($null -ne $rule.PSObject.Properties["requireObservedDamage"] -and
                [bool]$rule.requireObservedDamage -and
                ([double]$gameplay.gameplay.attackTargetHealthBefore -le [double]$gameplay.gameplay.attackTargetHealthAfter -or
                 [double]$gameplay.gameplay.attackTargetHealthAfter -lt 0)) {
                throw "Client '$processName' screenshot milestone '$milestone' is not backed by observed attack damage."
            }
            if ($null -ne $rule.PSObject.Properties["requireCompletedWorldState"] -and
                [bool]$rule.requireCompletedWorldState) {
                if ([int]$gameplay.gameplay.completedLosingCoreCount -ne 0) {
                    throw "Client '$processName' completed gameplay evidence still contains the losing core mirror."
                }
                if ([int]$gameplay.gameplay.completedWinnerInfantryNearDefeatedCoreCount -le 0) {
                    throw "Client '$processName' completed gameplay evidence has no winning infantry near the defeated core."
                }
                if ([int]$gameplay.gameplay.completedPresentationFrameId -le 0 -or
                    [int]$document.hostFrame -lt [int]$gameplay.gameplay.completedPresentationFrameId) {
                    throw "Client '$processName' completed screenshot predates the verified post-destruction presentation frame."
                }
                $completedCamera = $gameplay.gameplay.completedCameraTarget
                if ($null -eq $completedCamera -or
                    (Get-WorldEvidenceDistanceSquared `
                        -LeftX ([int64]$completedCamera.xCm) -LeftY ([int64]$completedCamera.yCm) `
                        -RightX ([int64]$anchor.xCm) -RightY ([int64]$anchor.yCm)) -gt $cameraToleranceSquared) {
                    throw "Client '$processName' completed gameplay camera does not frame the defeated core position."
                }
            }

            $verified.Add([ordered]@{
                process = $processName
                milestone = $milestone
                perspective = if ($isWinner) { "winner" } else { "loser" }
                anchor = $anchorName
                cameraTargetXCm = [int]$document.cameraTargetXCm
                cameraTargetYCm = [int]$document.cameraTargetYCm
                roles = @($roleResults)
                distinctEntityLayout = $distinctLayoutResult
            })
        }

        $expectedPerspectiveCount = if ($perspective -ceq "all") { 2 } else { 1 }
        if ($matchedPerspectiveCount -ne $expectedPerspectiveCount) {
            throw "World evidence '$milestone' perspective '$perspective' matched $matchedPerspectiveCount clients; expected $expectedPerspectiveCount."
        }
    }

    return @($verified)
}

function Assert-UdpPortAvailable {
    param([Parameter(Mandatory = $true)][int]$Port)

    $udp = [System.Net.Sockets.UdpClient]::new()
    try {
        $udp.ExclusiveAddressUse = $true
        $udp.Client.Bind([System.Net.IPEndPoint]::new([System.Net.IPAddress]::Any, $Port))
    }
    catch {
        throw "UDP port $Port is unavailable: $($_.Exception.Message)"
    }
    finally {
        $udp.Dispose()
    }
}

function Assert-DistinctClientMilestoneScreenshots {
    param([Parameter(Mandatory = $true)][System.Collections.IEnumerable]$Screenshots)

    $items = @($Screenshots)
    foreach ($processName in @($items | ForEach-Object { [string]$_.process } | Select-Object -Unique)) {
        $processItems = @($items | Where-Object { [string]$_.process -ceq $processName })
        $duplicateGroups = @($processItems |
            Group-Object -Property { [string]$_.file.sha256 } |
            Where-Object { $_.Count -gt 1 })
        if ($duplicateGroups.Count -eq 0) {
            continue
        }

        $duplicates = @($duplicateGroups | ForEach-Object {
            $milestones = @($_.Group | ForEach-Object { [string]$_.milestone })
            "[$($milestones -join ', ')]"
        })
        throw "Client '$processName' duplicates pixel evidence across gameplay milestones: $($duplicates -join '; ')."
    }
}

if ($LoadPresentationEvidenceFunctionsOnly) {
    return
}

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$profileFullPath = [System.IO.Path]::GetFullPath($ProfilePath)
if (-not (Test-Path -LiteralPath $profileFullPath)) {
    throw "Acceptance profile not found: $profileFullPath"
}

$profile = Get-Content -LiteralPath $profileFullPath -Raw | ConvertFrom-Json
if ($profile.schemaVersion -ne 8) {
    throw "Unsupported acceptance profile schemaVersion '$($profile.schemaVersion)'."
}

$script:HostAddressValue = if ([string]::IsNullOrWhiteSpace($HostAddress)) { [string]$profile.host } else { $HostAddress }
$script:PortValue = if ($Port -gt 0) { $Port } else { [int]$profile.port }
$script:ConnectionKeyValue = if ([string]::IsNullOrWhiteSpace($ConnectionKey)) { [string]$profile.connectionKey } else { $ConnectionKey }
$credentialTimeoutValue = if ($CredentialTimeoutSeconds -gt 0) { $CredentialTimeoutSeconds } else { [int]$profile.credentialTimeoutSeconds }
$runSecondsValue = if ($RunSeconds -ge 0) { $RunSeconds } else { [int]$profile.runSeconds }
$pollMilliseconds = [int]$profile.monitorIntervalMilliseconds
$script:FaultProfileValue = if ([string]::IsNullOrWhiteSpace($FaultProfile)) { [string]$profile.faultProfile } else { $FaultProfile }
$serverFaultSeedValue = [int]$profile.faultSeeds.server
$clientOneFaultSeedValue = [int]$profile.faultSeeds.clientOne
$clientTwoFaultSeedValue = [int]$profile.faultSeeds.clientTwo
if ($script:PortValue -lt 1 -or $script:PortValue -gt 65535) { throw "Port must be between 1 and 65535." }
if ([string]::IsNullOrWhiteSpace($script:ConnectionKeyValue)) { throw "ConnectionKey is required." }
if ($credentialTimeoutValue -le 0) { throw "CredentialTimeoutSeconds must be positive." }
if ($runSecondsValue -le 0) { throw "RunSeconds must be positive." }
if ($pollMilliseconds -le 0) { throw "monitorIntervalMilliseconds must be positive." }
foreach ($minimumProperty in @(
    "minimumVisibleEntities",
    "minimumActivePerformers",
    "minimumAuthoredPrimitives",
    "minimumSubmittedPrimitiveInstances",
    "minimumSubmittedPrimitiveBatches",
    "minimumPrefabVisuals"
)) {
    if ($null -eq $profile.clientPresentation -or
        $null -eq $profile.clientPresentation.PSObject.Properties[$minimumProperty] -or
        [int]$profile.clientPresentation.$minimumProperty -le 0) {
        throw "clientPresentation.$minimumProperty must be positive."
    }
}
$requiredPresentationReceipts = @($profile.requiredPresentationReceipts)
if ($requiredPresentationReceipts.Count -eq 0) {
    throw "requiredPresentationReceipts must declare at least one role-specific presentation requirement."
}
$configuredScreenshotMilestones = @(
    @($profile.clientScreenshots.clientOne.milestones) + @($profile.clientScreenshots.clientTwo.milestones) |
        ForEach-Object { [string]$_ } |
        Sort-Object -Unique -CaseSensitive)
$requiredFramebufferEvidence = @($profile.requiredFramebufferEvidence)
if ($requiredFramebufferEvidence.Count -eq 0) {
    throw "requiredFramebufferEvidence must declare player-visible entity colors for every screenshot milestone."
}
$framebufferRequirementKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$framebufferCoveredMilestones = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($requirement in $requiredFramebufferEvidence) {
    $role = [string]$requirement.role
    $template = [string]$requirement.presentationTemplate
    $perspective = [string]$requirement.perspective
    $milestones = @($requirement.milestones | ForEach-Object { [string]$_ })
    if ([string]::IsNullOrWhiteSpace($role) -or [string]::IsNullOrWhiteSpace($template) -or
        ($perspective -cne "all" -and $perspective -cne "winner" -and $perspective -cne "loser")) {
        throw "Every requiredFramebufferEvidence entry must declare non-empty role and presentationTemplate values plus a supported perspective."
    }
    if ($milestones.Count -eq 0 -or @($milestones | Where-Object {
        $_ -cnotmatch '^[A-Za-z0-9._-]+$' -or -not ($configuredScreenshotMilestones -ccontains $_)
    }).Count -ne 0) {
        throw "requiredFramebufferEvidence role '$role' must target configured screenshot milestones."
    }
    foreach ($thresholdName in @("maximumChannelDifference", "minimumPixelsPerInstance", "minimumPassingInstances", "regionMarginRatio")) {
        if ($null -eq $requirement.PSObject.Properties[$thresholdName]) {
            throw "requiredFramebufferEvidence role '$role' must declare $thresholdName."
        }
    }
    if ([int]$requirement.maximumChannelDifference -lt 0 -or [int]$requirement.maximumChannelDifference -gt 255) {
        throw "requiredFramebufferEvidence role '$role' maximumChannelDifference must be between 0 and 255."
    }
    if ([int]$requirement.minimumPixelsPerInstance -le 0 -or [int]$requirement.minimumPassingInstances -le 0) {
        throw "requiredFramebufferEvidence role '$role' instance minimums must be positive."
    }
    if ([double]$requirement.regionMarginRatio -lt 0 -or [double]$requirement.regionMarginRatio -gt 1) {
        throw "requiredFramebufferEvidence role '$role' regionMarginRatio must be between 0 and 1."
    }
    $acceptedColors = @($requirement.acceptedColors)
    if ($acceptedColors.Count -eq 0) {
        throw "requiredFramebufferEvidence role '$role' must declare at least one accepted color."
    }
    foreach ($color in $acceptedColors) {
        foreach ($channel in @("red", "green", "blue")) {
            if ($null -eq $color.PSObject.Properties[$channel] -or
                [int]$color.$channel -lt 0 -or [int]$color.$channel -gt 255) {
                throw "requiredFramebufferEvidence role '$role' has an invalid '$channel' color channel."
            }
        }
    }
    foreach ($milestone in $milestones) {
        if (-not $framebufferRequirementKeys.Add("$milestone`n$perspective`n$role")) {
            throw "requiredFramebufferEvidence duplicates role '$role' for screenshot milestone '$milestone' perspective '$perspective'."
        }
        [void]$framebufferCoveredMilestones.Add($milestone)
    }
}
foreach ($milestone in $configuredScreenshotMilestones) {
    if (-not $framebufferCoveredMilestones.Contains($milestone)) {
        throw "requiredFramebufferEvidence has no player-visible role for screenshot milestone '$milestone'."
    }
}
$requiredReceiptKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($requirement in $requiredPresentationReceipts) {
    $role = [string]$requirement.role
    $template = [string]$requirement.template
    $milestones = @($requirement.milestones | ForEach-Object { [string]$_ })
    if ([string]::IsNullOrWhiteSpace($role) -or [string]::IsNullOrWhiteSpace($template)) {
        throw "Every requiredPresentationReceipts entry must declare non-empty role and template values."
    }
    if ($milestones.Count -eq 0 -or @($milestones | Where-Object {
        $_ -cnotmatch '^[A-Za-z0-9._-]+$' -or -not ($configuredScreenshotMilestones -ccontains $_)
    }).Count -ne 0) {
        throw "requiredPresentationReceipts role '$role' must target configured screenshot milestones."
    }
    foreach ($thresholdName in @("minimumSubmitted", "minimumOnscreen", "minimumShortEdgePx", "minimumAreaPx2")) {
        if ($null -eq $requirement.PSObject.Properties[$thresholdName] -or [double]$requirement.$thresholdName -le 0) {
            throw "requiredPresentationReceipts role '$role' must declare positive $thresholdName."
        }
    }
    foreach ($milestone in $milestones) {
        $key = "$milestone`n$template"
        if (-not $requiredReceiptKeys.Add($key)) {
            throw "requiredPresentationReceipts duplicates template '$template' for screenshot milestone '$milestone'."
        }
    }
}
$requiredWorldEvidence = @($profile.requiredWorldEvidence)
if ($requiredWorldEvidence.Count -eq 0) {
    throw "requiredWorldEvidence must declare the player-visible world states for the battle milestones."
}
$worldEvidenceRuleKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($requirement in $requiredWorldEvidence) {
    $milestone = [string]$requirement.milestone
    $perspective = [string]$requirement.perspective
    $anchor = [string]$requirement.anchor
    if (-not ($configuredScreenshotMilestones -ccontains $milestone)) {
        throw "requiredWorldEvidence milestone '$milestone' is not configured for screenshots."
    }
    if ($perspective -cne "all" -and $perspective -cne "winner" -and $perspective -cne "loser") {
        throw "requiredWorldEvidence '$milestone' perspective must be winner, loser, or all."
    }
    if ($anchor -cne "meeting" -and $anchor -cne "siege" -and $anchor -cne "defeatedCore") {
        throw "requiredWorldEvidence '$milestone' anchor must be meeting, siege, or defeatedCore."
    }
    foreach ($toleranceName in @("positionToleranceCm", "cameraToleranceCm")) {
        if ($null -eq $requirement.PSObject.Properties[$toleranceName] -or [int]$requirement.$toleranceName -le 0) {
            throw "requiredWorldEvidence '$milestone/$perspective' must declare positive $toleranceName."
        }
    }
    if (-not $worldEvidenceRuleKeys.Add("$milestone`n$perspective")) {
        throw "requiredWorldEvidence duplicates milestone '$milestone' perspective '$perspective'."
    }
    $requiredRoles = @($requirement.requiredRoles)
    if ($requiredRoles.Count -eq 0) {
        throw "requiredWorldEvidence '$milestone/$perspective' must declare at least one required role."
    }
    $roleKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($roleRequirement in $requiredRoles) {
        $role = [string]$roleRequirement.role
        $template = [string]$roleRequirement.template
        $source = [string]$roleRequirement.source
        if ([string]::IsNullOrWhiteSpace($role) -or [string]::IsNullOrWhiteSpace($template)) {
            throw "requiredWorldEvidence '$milestone/$perspective' has a required role without role and template."
        }
        if ($source -cne "selectedInfantry" -and $source -cne "attackTarget" -and
            $source -cne "defeatedCore" -and $source -cne "completedWinnerInfantry") {
            throw "requiredWorldEvidence role '$role' uses unsupported stable entity source '$source'."
        }
        foreach ($thresholdName in @("minimumOnscreen", "minimumShortEdgePx", "minimumAreaPx2")) {
            if ($null -eq $roleRequirement.PSObject.Properties[$thresholdName] -or
                [double]$roleRequirement.$thresholdName -le 0) {
                throw "requiredWorldEvidence role '$role' must declare positive $thresholdName."
            }
        }
        if (-not $roleKeys.Add("$role`n$template`n$source")) {
            throw "requiredWorldEvidence '$milestone/$perspective' duplicates role '$role'."
        }
    }
    foreach ($forbiddenRole in @($requirement.forbiddenRoles)) {
        if ([string]::IsNullOrWhiteSpace([string]$forbiddenRole.role) -or
            [string]::IsNullOrWhiteSpace([string]$forbiddenRole.template) -or
            ([string]$forbiddenRole.scope -cne "screen" -and [string]$forbiddenRole.scope -cne "anchor")) {
            throw "requiredWorldEvidence '$milestone/$perspective' has an invalid forbidden role."
        }
    }
    if ($null -ne $requirement.PSObject.Properties["stableEntityMotion"] -and
        $null -ne $requirement.stableEntityMotion) {
        if ([string]::IsNullOrWhiteSpace([string]$requirement.stableEntityMotion.template) -or
            [int]$requirement.stableEntityMotion.minimumObservedMoveCm -le 0) {
            throw "requiredWorldEvidence '$milestone/$perspective' has invalid stableEntityMotion."
        }
    }
    if ($null -ne $requirement.PSObject.Properties["distinctEntityLayout"] -and
        $null -ne $requirement.distinctEntityLayout) {
        $layout = $requirement.distinctEntityLayout
        $layoutScope = [string]$layout.scope
        $layoutRegion = [string]$layout.region
        $maximumScreenOverlapRatio = [double]$layout.maximumScreenOverlapRatio
        $layoutSources = @(Get-DistinctEntityLayoutSources -Layout $layout)
        $hasSeparationSource = $null -ne $layout.PSObject.Properties["minimumWorldSeparationSource"] -and
            -not [string]::IsNullOrWhiteSpace([string]$layout.minimumWorldSeparationSource)
        $hasExplicitSeparation = $null -ne $layout.PSObject.Properties["minimumWorldSeparationCm"]
        if ([string]::IsNullOrWhiteSpace([string]$layout.template) -or
            [int]$layout.minimumInstances -lt 2 -or
            ($hasSeparationSource -eq $hasExplicitSeparation) -or
            ($hasSeparationSource -and [string]$layout.minimumWorldSeparationSource -cne "groupMoveTargetLayout.spacingCm") -or
            ($hasExplicitSeparation -and [int64]$layout.minimumWorldSeparationCm -le 0) -or
            ($layoutScope -cne "allVisibleTemplate" -and $layoutScope -cne "stableEntitySources") -or
            ($layoutRegion -cne "screen" -and $layoutRegion -cne "anchor") -or
            ($layoutScope -ceq "allVisibleTemplate" -and $layoutSources.Count -ne 0) -or
            ($layoutScope -ceq "stableEntitySources" -and $layoutSources.Count -eq 0) -or
            @($layoutSources | Where-Object {
                $_ -cne "selectedInfantry" -and $_ -cne "attackTarget" -and
                $_ -cne "defeatedCore" -and $_ -cne "completedWinnerInfantry"
            }).Count -ne 0 -or
            [double]::IsNaN($maximumScreenOverlapRatio) -or
            [double]::IsInfinity($maximumScreenOverlapRatio) -or
            $maximumScreenOverlapRatio -lt 0 -or
            $maximumScreenOverlapRatio -ge 1) {
            throw "requiredWorldEvidence '$milestone/$perspective' has invalid distinctEntityLayout."
        }
    }
}
foreach ($requiredRuleKey in @("advancing`nall", "engaging`nwinner", "engaging`nloser", "completed`nall")) {
    if (-not $worldEvidenceRuleKeys.Contains($requiredRuleKey)) {
        throw "requiredWorldEvidence lacks mandatory battle view '$($requiredRuleKey.Replace("`n", "/"))'."
    }
}
if ($script:FaultProfileValue -cne "normal" -and $script:FaultProfileValue -cne "unstable") {
    throw "faultProfile must be 'normal' or 'unstable'."
}
$faultSeeds = @($serverFaultSeedValue, $clientOneFaultSeedValue, $clientTwoFaultSeedValue)
if (@($faultSeeds | Where-Object { $_ -le 0 }).Count -ne 0) {
    throw "All faultSeeds must be positive."
}
if (@($faultSeeds | Select-Object -Unique).Count -ne 3) {
    throw "Server and client faultSeeds must be distinct."
}
if ($profile.clientInstanceIds.Count -ne 2 -or $profile.clientInstanceIds[0] -eq $profile.clientInstanceIds[1]) {
    throw "The profile must declare exactly two distinct clientInstanceIds."
}
$repositoryEvidence = Get-GitEvidence -RepositoryRoot $script:RepoRoot
if ([bool]$repositoryEvidence.dirty) {
    throw "Final three-process acceptance requires a clean worktree."
}

$artifactRoot = Resolve-RepoPath -Path ([string]$profile.artifactRoot)
if ([string]::IsNullOrWhiteSpace($ArtifactDirectory)) {
    $runId = [DateTime]::UtcNow.ToString("yyyyMMddTHHmmssfffZ")
    $artifactDirectoryValue = Join-Path $artifactRoot $runId
}
else {
    $artifactDirectoryValue = Resolve-RepoPath -Path $ArtifactDirectory
}
if (Test-Path -LiteralPath $artifactDirectoryValue) {
    if (@(Get-ChildItem -LiteralPath $artifactDirectoryValue -Force).Count -gt 0) {
        throw "ArtifactDirectory must be new or empty: $artifactDirectoryValue"
    }
}
New-Item -ItemType Directory -Path $artifactDirectoryValue -Force | Out-Null
$clientAScreenshotCapture = New-ClientScreenshotCapture -ProcessName "client-a" `
    -Configuration $profile.clientScreenshots.clientOne -ArtifactDirectory $artifactDirectoryValue
$clientBScreenshotCapture = New-ClientScreenshotCapture -ProcessName "client-b" `
    -Configuration $profile.clientScreenshots.clientTwo -ArtifactDirectory $artifactDirectoryValue
$screenshotTargets = @($clientAScreenshotCapture.Files) + @($clientBScreenshotCapture.Files)
$screenshotPaths = @($screenshotTargets | ForEach-Object { [System.IO.Path]::GetFullPath($_.Path) })
if (@($screenshotPaths | Select-Object -Unique).Count -ne $screenshotPaths.Count) {
    throw "Client screenshot configurations must resolve to distinct output files."
}

$manifestPath = Join-Path $artifactDirectoryValue "run-manifest.json"
$logsDirectory = Join-Path $artifactDirectoryValue "logs"
New-Item -ItemType Directory -Path $logsDirectory -Force | Out-Null
$ownedProcesses = [System.Collections.Generic.List[object]]::new()
$credentialTargets = [System.Collections.Generic.List[object]]::new()
$exitCode = 0
$failureMessage = $null
$verificationReached = $false
$manifest = [ordered]@{
    schemaVersion = 11
    acceptanceScope = "three-process-player-input-to-authoritative-frontline-outcome"
    status = "preparing"
    startedAtUtc = [DateTime]::UtcNow.ToString("O")
    completedAtUtc = $null
    artifactDirectory = $artifactDirectoryValue
    repository = $repositoryEvidence
    inputs = [ordered]@{
        profile = Get-FileEvidence -Path $profileFullPath
        acceptancePlan = $null
        frontlineConfig = $null
        groupMoveLayoutConfig = $null
        networkConfig = $null
        sourceLaunchGraph = $null
        roleArtifacts = @()
        assemblies = @()
    }
    effectiveParameters = [ordered]@{
        host = $script:HostAddressValue
        port = $script:PortValue
        connectionKeySha256 = Get-StringSha256 -Value $script:ConnectionKeyValue
        credentialTimeoutSeconds = $credentialTimeoutValue
        runSeconds = $runSecondsValue
        monitorIntervalMilliseconds = $pollMilliseconds
        faultProfile = $script:FaultProfileValue
        faultSeeds = [ordered]@{
            server = $serverFaultSeedValue
            clientOne = $clientOneFaultSeedValue
            clientTwo = $clientTwoFaultSeedValue
        }
        faultConfiguration = $null
        groupMoveTargetLayout = $null
        clientScreenshots = @(
            [ordered]@{
                process = $clientAScreenshotCapture.ProcessName
                targetPath = $clientAScreenshotCapture.TargetPath
                diagnosticPath = $clientAScreenshotCapture.DiagnosticPath
                milestones = @($clientAScreenshotCapture.Milestones)
            }
            [ordered]@{
                process = $clientBScreenshotCapture.ProcessName
                targetPath = $clientBScreenshotCapture.TargetPath
                diagnosticPath = $clientBScreenshotCapture.DiagnosticPath
                milestones = @($clientBScreenshotCapture.Milestones)
            }
        )
        clientPresentation = $profile.clientPresentation
        requiredFramebufferEvidence = $requiredFramebufferEvidence
    }
    planFingerprint = $null
    orderedModIds = @()
    processes = @()
    clientCredentials = @()
    gameplayEvidence = @()
    screenshots = @()
    clientFramebufferEvidence = @()
    clientPresentation = @()
    clientWorldEvidence = @()
    runtimeLogs = @()
    credentialValidation = "LUDCRD01/64-byte/SHA256/non-empty/distinct"
    runtimeErrorEvidence = [ordered]@{
        stderrPolicy = "must-be-empty"
        runtimeFaultPort = "gameplayEvidence[].faultCount"
        structuredFaultPort = "gameplayEvidence[].networkFaultInjection"
    }
    failure = $null
    cleanup = "pending"
}
Write-JsonFile -Value $manifest -Path $manifestPath

try {
    Assert-UdpPortAvailable -Port $script:PortValue
    $dotnet = Get-DotnetCommand
    $launcherProject = Resolve-RepoPath -Path ([string]$profile.launcherCliProject)
    $serverProject = Resolve-RepoPath -Path ([string]$profile.dedicatedServerProject)
    $serverAssembly = Resolve-RepoPath -Path ([string]$profile.dedicatedServerAssembly)
    $acceptancePlanPath = Resolve-RepoPath -Path ([string]$profile.acceptancePlan)
    $expectedAcceptancePlan = Get-Content -LiteralPath $acceptancePlanPath -Raw | ConvertFrom-Json
    $manifest.inputs.acceptancePlan = Get-FileEvidence -Path $acceptancePlanPath
    $launcherAssembly = Join-Path (Split-Path -Parent $launcherProject) "bin\Release\net8.0\Ludots.Launcher.Cli.dll"
    $selector = "preset:$($profile.preset)"

    Invoke-CheckedProcess -Name "build-launcher-cli" -FilePath $dotnet `
        -Arguments @("build", $launcherProject, "-c", "Release", "-m:1", "-nologo", "-clp:ErrorsOnly") `
        -WorkingDirectory $script:RepoRoot -StdoutPath (Join-Path $logsDirectory "build-launcher.stdout.log") `
        -StderrPath (Join-Path $logsDirectory "build-launcher.stderr.log")
    Invoke-CheckedProcess -Name "build-networked-mod-plan" -FilePath $dotnet `
        -Arguments @("exec", "--roll-forward", "Major", $launcherAssembly, "build", $selector, "--adapter", [string]$profile.adapterId, "--build", [string]$profile.buildMode) `
        -WorkingDirectory $script:RepoRoot -StdoutPath (Join-Path $logsDirectory "build-mods.stdout.log") `
        -StderrPath (Join-Path $logsDirectory "build-mods.stderr.log")
    Invoke-CheckedProcess -Name "build-raylib-app" -FilePath $dotnet `
        -Arguments @("exec", "--roll-forward", "Major", $launcherAssembly, "build", "app", "--adapter", [string]$profile.adapterId) `
        -WorkingDirectory $script:RepoRoot -StdoutPath (Join-Path $logsDirectory "build-raylib.stdout.log") `
        -StderrPath (Join-Path $logsDirectory "build-raylib.stderr.log")
    Invoke-CheckedProcess -Name "build-dedicated-server" -FilePath $dotnet `
        -Arguments @("build", $serverProject, "-c", "Release", "-m:1", "-nologo", "-clp:ErrorsOnly") `
        -WorkingDirectory $script:RepoRoot -StdoutPath (Join-Path $logsDirectory "build-server.stdout.log") `
        -StderrPath (Join-Path $logsDirectory "build-server.stderr.log")

    $resolveJsonPath = Join-Path $artifactDirectoryValue "launcher-resolve.json"
    Invoke-CheckedProcess -Name "resolve-launch-plan" -FilePath $dotnet `
        -Arguments @("exec", "--roll-forward", "Major", $launcherAssembly, "resolve", $selector, "--adapter", [string]$profile.adapterId, "--build", "never", "--json") `
        -WorkingDirectory $script:RepoRoot -StdoutPath $resolveJsonPath `
        -StderrPath (Join-Path $logsDirectory "resolve.stderr.log")
    $resolve = Get-Content -LiteralPath $resolveJsonPath -Raw | ConvertFrom-Json
    $plan = $resolve.Plan
    if ($plan.AdapterId -ne $profile.adapterId) { throw "Launcher resolved unexpected adapter '$($plan.AdapterId)'." }
    if ([string]::IsNullOrWhiteSpace([string]$plan.PlanFingerprint)) { throw "Launcher resolved an empty PlanFingerprint." }
    if ($null -ne $plan.BrowserRuntime) { throw "RTS three-process acceptance must not require BrowserRuntime." }
    $sourceGraphPath = [System.IO.Path]::GetFullPath([string]$plan.GraphArtifactPath)
    if (-not (Test-Path -LiteralPath $sourceGraphPath)) { throw "Launcher did not write its launch graph: $sourceGraphPath" }
    if (-not (Test-Path -LiteralPath ([string]$plan.AppAssemblyPath))) { throw "Raylib app assembly is missing: $($plan.AppAssemblyPath)" }
    if (-not (Test-Path -LiteralPath $serverAssembly)) { throw "Dedicated server assembly is missing: $serverAssembly" }

    $sourceGraphEvidencePath = Join-Path $artifactDirectoryValue "launcher-resolved.graph.json"
    Copy-Item -LiteralPath $sourceGraphPath -Destination $sourceGraphEvidencePath
    $sourceGraph = Get-Content -LiteralPath $sourceGraphPath -Raw | ConvertFrom-Json
    $groupMoveLayoutEvidence = Resolve-GroupMoveTargetLayoutEvidence -SourceGraph $sourceGraph
    $manifest.inputs.groupMoveLayoutConfig = $groupMoveLayoutEvidence.config
    $manifest.effectiveParameters.groupMoveTargetLayout = [ordered]@{
        source = [string]$groupMoveLayoutEvidence.source
        modId = [string]$groupMoveLayoutEvidence.modId
        mode = [string]$groupMoveLayoutEvidence.mode
        assignment = [string]$groupMoveLayoutEvidence.assignment
        orderTypeKeys = @($groupMoveLayoutEvidence.orderTypeKeys)
        spacingCm = [int64]$groupMoveLayoutEvidence.spacingCm
    }
    $frontlineMods = @($sourceGraph.plannedMods | Where-Object { [string]$_.id -ceq "RtsMultiplayerFrontlineMod" })
    if ($frontlineMods.Count -ne 1) {
        throw "Launcher graph must contain exactly one RtsMultiplayerFrontlineMod; observed $($frontlineMods.Count)."
    }
    $frontlineConfigPath = [System.IO.Path]::GetFullPath((Join-Path `
        ([string]$frontlineMods[0].rootPath) "assets\RtsMultiplayerFrontlineConfig.json"))
    if (-not (Test-Path -LiteralPath $frontlineConfigPath -PathType Leaf)) {
        throw "Formal Frontline gameplay config is missing: $frontlineConfigPath"
    }
    $frontlineConfig = Get-Content -LiteralPath $frontlineConfigPath -Raw | ConvertFrom-Json
    if ([int]$frontlineConfig.trainCostCrystals -le 0) {
        throw "Formal Frontline gameplay config has an invalid training price."
    }
    $networkedMods = @($sourceGraph.plannedMods | Where-Object { [string]$_.id -ceq "RtsMultiplayerFrontlineNetworkedMod" })
    if ($networkedMods.Count -ne 1) {
        throw "Launcher graph must contain exactly one RtsMultiplayerFrontlineNetworkedMod; observed $($networkedMods.Count)."
    }
    $networkConfigPath = [System.IO.Path]::GetFullPath((Join-Path ([string]$networkedMods[0].rootPath) "assets\game.json"))
    if (-not (Test-Path -LiteralPath $networkConfigPath -PathType Leaf)) {
        throw "Formal Frontline network config is missing: $networkConfigPath"
    }
    $networkGameConfig = Get-Content -LiteralPath $networkConfigPath -Raw | ConvertFrom-Json
    $networkConfig = $networkGameConfig.networking
    if ($null -eq $networkConfig -or [string]::IsNullOrWhiteSpace([string]$networkConfig.referenceTransport)) {
        throw "Formal Frontline network config does not declare a reference transport."
    }
    $faultProfileConfig = if ($script:FaultProfileValue -ceq "normal") {
        $networkConfig.normalConnection
    }
    else {
        $networkConfig.unstableConnection
    }
    if ($null -eq $faultProfileConfig) {
        throw "Formal Frontline network config does not declare fault profile '$($script:FaultProfileValue)'."
    }
    $faultInjectionEnabled = [int]$faultProfileConfig.roundTripLatencyMs -ne 0 -or
        [int]$faultProfileConfig.jitterMs -ne 0 -or
        [int]$faultProfileConfig.packetLossPermille -ne 0 -or
        [int]$faultProfileConfig.reorderPermille -ne 0
    $manifest.effectiveParameters.faultConfiguration = [ordered]@{
        transportIdentity = [string]$networkConfig.referenceTransport
        profileId = $script:FaultProfileValue
        roundTripLatencyMilliseconds = [int]$faultProfileConfig.roundTripLatencyMs
        jitterMilliseconds = [int]$faultProfileConfig.jitterMs
        packetLossPermille = [int]$faultProfileConfig.packetLossPermille
        stateReorderPermille = [int]$faultProfileConfig.reorderPermille
        isEnabled = $faultInjectionEnabled
    }
    $manifest.planFingerprint = [string]$plan.PlanFingerprint
    $manifest.orderedModIds = @($plan.OrderedModIds)
    $manifest.inputs.frontlineConfig = Get-FileEvidence -Path $frontlineConfigPath
    $manifest.inputs.networkConfig = Get-FileEvidence -Path $networkConfigPath
    $manifest.inputs.sourceLaunchGraph = Get-FileEvidence -Path $sourceGraphEvidencePath
    $manifest.inputs.assemblies = @(
        Get-FileEvidence -Path $launcherAssembly
        Get-FileEvidence -Path $serverAssembly
        Get-FileEvidence -Path ([string]$plan.AppAssemblyPath)
    )

    $serverRole = New-RoleArtifacts -RoleDirectory (Join-Path $artifactDirectoryValue "server") `
        -ProcessRole "authoritativeServer" -ClientInstanceId 0 -FaultSeed $serverFaultSeedValue -CredentialPath "" `
        -SourceGraphPath $sourceGraphEvidencePath -Plan $plan
    $clientACredential = Join-Path $artifactDirectoryValue "client-a\session.credential"
    $clientARole = New-RoleArtifacts -RoleDirectory (Join-Path $artifactDirectoryValue "client-a") `
        -ProcessRole "replicatedClient" -ClientInstanceId ([int]$profile.clientInstanceIds[0]) `
        -FaultSeed $clientOneFaultSeedValue `
        -CredentialPath $clientACredential -SourceGraphPath $sourceGraphEvidencePath -Plan $plan
    $clientBCredential = Join-Path $artifactDirectoryValue "client-b\session.credential"
    $clientBRole = New-RoleArtifacts -RoleDirectory (Join-Path $artifactDirectoryValue "client-b") `
        -ProcessRole "replicatedClient" -ClientInstanceId ([int]$profile.clientInstanceIds[1]) `
        -FaultSeed $clientTwoFaultSeedValue `
        -CredentialPath $clientBCredential -SourceGraphPath $sourceGraphEvidencePath -Plan $plan
    $expectedFaultInjectionByProcess = [ordered]@{}
    foreach ($processLaunch in @(
        [pscustomobject]@{ Name = "authoritative-server"; RoleArtifact = $serverRole; ExpectedRole = "authoritativeServer"; ExpectedSeed = $serverFaultSeedValue }
        [pscustomobject]@{ Name = "client-a"; RoleArtifact = $clientARole; ExpectedRole = "replicatedClient"; ExpectedSeed = $clientOneFaultSeedValue }
        [pscustomobject]@{ Name = "client-b"; RoleArtifact = $clientBRole; ExpectedRole = "replicatedClient"; ExpectedSeed = $clientTwoFaultSeedValue }
    )) {
        $bootstrap = Get-Content -LiteralPath $processLaunch.RoleArtifact.BootstrapPath -Raw | ConvertFrom-Json
        $networkHost = $bootstrap.NetworkHost
        if ($null -eq $networkHost -or
            [string]$networkHost.ProcessRole -cne [string]$processLaunch.ExpectedRole -or
            [string]$networkHost.FaultProfile -cne $script:FaultProfileValue -or
            [int]$networkHost.FaultSeed -ne [int]$processLaunch.ExpectedSeed) {
            throw "Generated bootstrap for '$($processLaunch.Name)' differs from the requested role or fault launch parameters."
        }
        $expectedFaultInjectionByProcess[$processLaunch.Name] = [pscustomobject]@{
            Role = [string]$networkHost.ProcessRole
            TransportIdentity = [string]$networkConfig.referenceTransport
            ProfileId = [string]$networkHost.FaultProfile
            Seed = [int]$networkHost.FaultSeed
            RoundTripLatencyMilliseconds = [int]$faultProfileConfig.roundTripLatencyMs
            JitterMilliseconds = [int]$faultProfileConfig.jitterMs
            PacketLossPermille = [int]$faultProfileConfig.packetLossPermille
            StateReorderPermille = [int]$faultProfileConfig.reorderPermille
            IsEnabled = $faultInjectionEnabled
        }
    }
    if ([System.IO.Path]::GetFullPath($clientACredential) -eq [System.IO.Path]::GetFullPath($clientBCredential)) {
        throw "Client credential paths must be distinct."
    }
    $credentialTargets.Add([pscustomobject]@{
        ClientInstanceId = [int]$profile.clientInstanceIds[0]
        Path = $clientACredential
    })
    $credentialTargets.Add([pscustomobject]@{
        ClientInstanceId = [int]$profile.clientInstanceIds[1]
        Path = $clientBCredential
    })
    $manifest.inputs.roleArtifacts = @(@($serverRole, $clientARole, $clientBRole) | ForEach-Object {
        [ordered]@{
            processRole = $_.ProcessRole
            graph = Get-FileEvidence -Path $_.GraphPath
            bootstrap = Get-FileEvidence -Path $_.BootstrapPath
        }
    })
    Write-JsonFile -Value $manifest -Path $manifestPath

    $server = Start-CapturedProcess -Name "authoritative-server" -FilePath $dotnet `
        -Arguments @("exec", "--roll-forward", "Major", $serverAssembly, $serverRole.BootstrapPath) `
        -WorkingDirectory (Join-Path $artifactDirectoryValue "server") `
        -StdoutPath (Join-Path $artifactDirectoryValue "server\stdout.log") `
        -StderrPath (Join-Path $artifactDirectoryValue "server\stderr.log")
    $ownedProcesses.Add($server)
    Add-ManifestProcess -Manifest $manifest -OwnedProcess $server -ManifestPath $manifestPath
    Start-Sleep -Milliseconds ([int]$profile.interProcessDelayMilliseconds)
    Assert-OwnedProcessesAlive -OwnedProcesses $ownedProcesses

    $clientA = Start-CapturedProcess -Name "client-a" -FilePath $dotnet `
        -Arguments @("exec", "--roll-forward", "Major", [string]$plan.AppAssemblyPath, $clientARole.BootstrapPath) `
        -WorkingDirectory (Join-Path $artifactDirectoryValue "client-a") `
        -StdoutPath (Join-Path $artifactDirectoryValue "client-a\stdout.log") `
        -StderrPath (Join-Path $artifactDirectoryValue "client-a\stderr.log") `
        -EnvironmentVariables $clientAScreenshotCapture.EnvironmentVariables
    $ownedProcesses.Add($clientA)
    Add-ManifestProcess -Manifest $manifest -OwnedProcess $clientA -ManifestPath $manifestPath
    Start-Sleep -Milliseconds ([int]$profile.interProcessDelayMilliseconds)
    Assert-OwnedProcessesAlive -OwnedProcesses $ownedProcesses

    $clientB = Start-CapturedProcess -Name "client-b" -FilePath $dotnet `
        -Arguments @("exec", "--roll-forward", "Major", [string]$plan.AppAssemblyPath, $clientBRole.BootstrapPath) `
        -WorkingDirectory (Join-Path $artifactDirectoryValue "client-b") `
        -StdoutPath (Join-Path $artifactDirectoryValue "client-b\stdout.log") `
        -StderrPath (Join-Path $artifactDirectoryValue "client-b\stderr.log") `
        -EnvironmentVariables $clientBScreenshotCapture.EnvironmentVariables
    $ownedProcesses.Add($clientB)
    Add-ManifestProcess -Manifest $manifest -OwnedProcess $clientB -ManifestPath $manifestPath
    Start-Sleep -Milliseconds ([int]$profile.interProcessDelayMilliseconds)
    Assert-OwnedProcessesAlive -OwnedProcesses $ownedProcesses
    if ($clientA.Process.HasExited) { throw "Client A was replaced when client B started." }

    $manifest.status = "waiting-for-client-session-credentials"
    Write-JsonFile -Value $manifest -Path $manifestPath
    Wait-ForCredentialEvidence -CredentialPaths @($clientACredential, $clientBCredential) `
        -OwnedProcesses $ownedProcesses -TimeoutSeconds $credentialTimeoutValue -PollMilliseconds $pollMilliseconds
    [byte[]]$clientABytes = [System.IO.File]::ReadAllBytes($clientACredential)
    [byte[]]$clientBBytes = [System.IO.File]::ReadAllBytes($clientBCredential)
    if ([System.Convert]::ToBase64String($clientABytes) -eq [System.Convert]::ToBase64String($clientBBytes)) {
        throw "Clients received identical session credentials."
    }
    $manifest.status = "waiting-for-complete-gameplay-evidence"
    Write-JsonFile -Value $manifest -Path $manifestPath

    $gameplayTargets = @(
        [pscustomobject]@{ Name = "authoritative-server"; Path = (Join-Path $artifactDirectoryValue "server\gameplay-evidence.json") }
        [pscustomobject]@{ Name = "client-a"; Path = (Join-Path $artifactDirectoryValue "client-a\gameplay-evidence.json") }
        [pscustomobject]@{ Name = "client-b"; Path = (Join-Path $artifactDirectoryValue "client-b\gameplay-evidence.json") }
    )
    $gameplayItems = @(Wait-ForGameplayEvidence -Targets $gameplayTargets -OwnedProcesses $ownedProcesses `
        -TimeoutSeconds $runSecondsValue -PollMilliseconds $pollMilliseconds)
    Assert-GameplayEvidence -Items $gameplayItems -ExpectedPlan $expectedAcceptancePlan `
        -FrontlineConfig $frontlineConfig `
        -PlanFingerprint ([string]$plan.PlanFingerprint) `
        -ExpectedFaultInjectionByProcess $expectedFaultInjectionByProcess `
        -FaultProfile $script:FaultProfileValue
    $manifest.gameplayEvidence = @($gameplayItems | ForEach-Object {
        [ordered]@{
            name = $_.Name
            file = Get-FileEvidence -Path $_.Path
            role = [string]$_.Value.role
            playerId = [int]$_.Value.playerId
            seatSlot = [int]$_.Value.seatSlot
            sessionEpoch = [uint64]$_.Value.sessionEpoch
            contentFingerprint = [string]$_.Value.contentFingerprint
            committedTick = [int]$_.Value.gameplay.committedTick
            outcome = [string]$_.Value.gameplay.outcome
            networkFaultInjection = $_.Value.networkFaultInjection
        }
    })
    $manifest.status = "waiting-for-client-screenshots"
    Write-JsonFile -Value $manifest -Path $manifestPath
    $screenshotItems = @(Wait-ForScreenshotEvidence -Targets $screenshotTargets `
        -OwnedProcesses $ownedProcesses -TimeoutSeconds $runSecondsValue -PollMilliseconds $pollMilliseconds)
    $manifest.screenshots = @($screenshotItems | ForEach-Object {
        [ordered]@{
            process = $_.ProcessName
            milestone = [string]$_.Milestone
            milestoneOrder = [int]$_.MilestoneOrder
            milestoneRevision = [uint32]$_.MilestoneRevision
            hostFrame = [int]$_.HostFrame
            file = Get-FileEvidence -Path $_.Path
            evidence = Get-FileEvidence -Path $_.EvidencePath
        }
    })
    Assert-DistinctClientMilestoneScreenshots -Screenshots $manifest.screenshots
    $clientPresentationItems = @(
        Read-ClientPresentationEvidence -Capture $clientAScreenshotCapture -Minimums $profile.clientPresentation `
            -RequiredReceipts $requiredPresentationReceipts
        Read-ClientPresentationEvidence -Capture $clientBScreenshotCapture -Minimums $profile.clientPresentation `
            -RequiredReceipts $requiredPresentationReceipts
    )
    $manifest.clientPresentation = $clientPresentationItems
    $clientFramebufferEvidence = @(Read-ClientFramebufferPixelEvidence `
        -Screenshots $screenshotItems `
        -PresentationItems $clientPresentationItems `
        -GameplayItems $gameplayItems `
        -Requirements $requiredFramebufferEvidence `
        -DotnetPath $dotnet `
        -LauncherAssemblyPath $launcherAssembly `
        -ArtifactDirectory $artifactDirectoryValue `
        -WorkingDirectory $script:RepoRoot)
    $manifest.clientFramebufferEvidence = $clientFramebufferEvidence
    Write-JsonFile -Value $manifest -Path $manifestPath
    Assert-ClientFramebufferPixelEvidencePassed -Items $clientFramebufferEvidence
    $manifest.clientWorldEvidence = @(
        Assert-ClientWorldPresentationEvidence -PresentationItems $clientPresentationItems `
            -GameplayItems $gameplayItems -Requirements $requiredWorldEvidence `
            -GroupMoveLayoutEvidence $groupMoveLayoutEvidence
    )
    $manifest.status = "verification-complete"
    $verificationReached = $true
}
catch {
    $exitCode = 1
    $failureMessage = $_.Exception.Message
    $manifest.status = "failed"
    $manifest.failure = $failureMessage
}
finally {
    for ($index = $ownedProcesses.Count - 1; $index -ge 0; $index--) {
        try {
            Stop-OwnedProcess -OwnedProcess $ownedProcesses[$index]
        }
        catch {
            $exitCode = 1
            if ($null -eq $failureMessage) {
                $failureMessage = "Failed to clean up owned process '$($ownedProcesses[$index].Name)': $($_.Exception.Message)"
                $manifest.status = "failed"
                $manifest.failure = $failureMessage
            }
        }
    }

    try {
        $manifest.runtimeLogs = @($ownedProcesses | ForEach-Object {
            [ordered]@{
                name = $_.Name
                stdout = Get-FileEvidence -Path $_.StdoutPath
                stderr = Get-FileEvidence -Path $_.StderrPath
            }
        })
    }
    catch {
        $exitCode = 1
        if ($null -eq $failureMessage) {
            $failureMessage = "Failed to capture runtime log evidence: $($_.Exception.Message)"
            $manifest.failure = $failureMessage
        }
    }

    try {
        Assert-RuntimeStderrEmpty -OwnedProcesses $ownedProcesses
    }
    catch {
        $exitCode = 1
        if ($null -eq $failureMessage) {
            $failureMessage = $_.Exception.Message
            $manifest.failure = $failureMessage
        }
    }

    try {
        $credentialEvidence = [System.Collections.Generic.List[object]]::new()
        $credentialCleanupFailures = [System.Collections.Generic.List[string]]::new()
        foreach ($target in $credentialTargets) {
            try {
                $credentialEvidence.Add((Remove-ClientCredential -ClientInstanceId $target.ClientInstanceId `
                    -Path $target.Path -ArtifactDirectory $artifactDirectoryValue))
            }
            catch {
                $credentialCleanupFailures.Add($_.Exception.Message)
            }
        }
        $manifest.clientCredentials = @($credentialEvidence)
        if ($credentialCleanupFailures.Count -ne 0) {
            throw ($credentialCleanupFailures -join '; ')
        }
        if ($verificationReached) {
            if ($manifest.clientCredentials.Count -ne 2 -or
                @($manifest.clientCredentials | Where-Object { -not $_.present -or -not $_.valid -or -not $_.deleted }).Count -ne 0) {
                throw "Two valid client credential hashes were not retained after secret deletion."
            }
            if ($manifest.clientCredentials[0].sha256 -eq $manifest.clientCredentials[1].sha256) {
                throw "Clients received identical session credentials."
            }
        }
    }
    catch {
        $exitCode = 1
        if ($null -eq $failureMessage) {
            $failureMessage = $_.Exception.Message
            $manifest.failure = $failureMessage
        }
    }

    if ($exitCode -eq 0 -and -not $verificationReached) {
        $exitCode = 1
        $failureMessage = "Acceptance ended before the complete three-process gameplay verification finished."
        $manifest.failure = $failureMessage
    }
    $manifest.cleanup = if ($exitCode -eq 0) { "owned-processes-stopped" } else { "attempted" }
    $manifest.status = if ($exitCode -eq 0 -and $verificationReached) { "passed" } else { "failed" }
    $manifest.completedAtUtc = [DateTime]::UtcNow.ToString("O")
    Write-JsonFile -Value $manifest -Path $manifestPath
}

if ($exitCode -ne 0) {
    Write-Error "$failureMessage Evidence: $manifestPath"
    exit $exitCode
}

Write-Host "[PASS] Two clients completed the player-input RTS flow and agreed with the authoritative server outcome."
Write-Host "Evidence: $artifactDirectoryValue"
Write-Host "Scope: gather, train, multi-select move, opposing-unit attack, core destruction, per-actor admission, and final replicated outcome."
