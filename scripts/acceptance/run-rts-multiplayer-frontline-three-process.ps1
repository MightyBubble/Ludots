param(
    [string]$ProfilePath = (Join-Path $PSScriptRoot "rts-multiplayer-frontline-three-process.profile.json"),
    [string]$ArtifactDirectory = "",
    [string]$HostAddress = "",
    [int]$Port = 0,
    [string]$ConnectionKey = "",
    [string]$FaultProfile = "",
    [int]$CredentialTimeoutSeconds = 0,
    [int]$RunSeconds = -1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$script:AcceptanceContaminatingEnvironmentVariables = @(
    "LUDOTS_AUTO_EXIT_FRAME",
    "LUDOTS_MIN_RUNTIME_MS_BEFORE_SCREENSHOT",
    "LUDOTS_TAKE_SCREENSHOT_PATH",
    "LUDOTS_TAKE_SCREENSHOT_FRAME",
    "LUDOTS_TAKE_SCREENSHOT_FRAMES",
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
    $frames = @($Configuration.frames | ForEach-Object { [int]$_ })
    if ($frames.Count -eq 0 -or @($frames | Where-Object { $_ -le 0 }).Count -ne 0) {
        throw "Screenshot frames for '$ProcessName' must contain positive frame numbers."
    }
    for ($index = 1; $index -lt $frames.Count; $index++) {
        if ($frames[$index] -le $frames[$index - 1]) {
            throw "Screenshot frames for '$ProcessName' must be strictly increasing."
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

    $files = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $frames.Count; $index++) {
        $fileName = "{0}_{1:000}_f{2:0000}{3}" -f $baseName, ($index + 1), $frames[$index], $extension
        $files.Add([pscustomobject]@{
            ProcessName = $ProcessName
            Frame = $frames[$index]
            Path = [System.IO.Path]::Combine($directory, $fileName)
        })
    }

    return [pscustomobject]@{
        ProcessName = $ProcessName
        TargetPath = $targetPath
        Frames = $frames
        Files = @($files)
        EnvironmentVariables = [ordered]@{
            LUDOTS_TAKE_SCREENSHOT_PATH = $targetPath
            LUDOTS_TAKE_SCREENSHOT_FRAMES = ($frames -join ",")
        }
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
        foreach ($target in $all) {
            if (-not (Test-Path -LiteralPath $target.Path -PathType Leaf) -or
                (Get-Item -LiteralPath $target.Path).Length -le 0) {
                $ready = $false
                break
            }
        }
        if ($ready) { return $all }
        Start-Sleep -Milliseconds $PollMilliseconds
    }

    $missing = @($all | Where-Object {
        -not (Test-Path -LiteralPath $_.Path -PathType Leaf) -or (Get-Item -LiteralPath $_.Path).Length -le 0
    } | ForEach-Object { "$($_.ProcessName):frame-$($_.Frame)" })
    throw "Timed out after $TimeoutSeconds seconds waiting for client screenshots. Missing or empty: $($missing -join ', ')."
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
    foreach ($item in $all) {
        $evidence = $item.Value
        if ([int]$evidence.schemaVersion -ne 5 -or [string]$evidence.status -cne "passed") {
            throw "Evidence '$($item.Name)' did not pass schema version 5."
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
    if ([double]::IsNaN($losingCoreHealth) -or [double]::IsInfinity($losingCoreHealth) -or $losingCoreHealth -gt 0) {
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
            if ([string]$command.action -cne $expectedActions[$commandIndex] -or
                [uint64]$command.clientBatchSequence -ne [uint64]($commandIndex + 1) -or
                [string]$command.admissionStage -cne "EntityIntake" -or
                [string]$command.admissionResult -cne "Activated") {
                throw "Client evidence '$($item.Name)' command $commandIndex has an unexpected action, sequence, or final admission."
            }
            if ([int]$command.actorCount -le 0 -or
                @($command.actorHandles).Count -ne [int]$command.actorCount -or
                @($command.actorAdmissions).Count -ne [int]$command.actorCount) {
                throw "Client evidence '$($item.Name)' command '$($command.action)' has inconsistent actor evidence."
            }
            $uniqueActorHandles = @($command.actorHandles |
                Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
                Sort-Object -Unique)
            if ($uniqueActorHandles.Count -ne [int]$command.actorCount) {
                throw "Client evidence '$($item.Name)' command '$($command.action)' has blank or duplicate actor handles."
            }
            foreach ($actor in @($command.actorAdmissions)) {
                if ([string]$actor.stage -cne "EntityIntake" -or [string]$actor.result -cne "Activated") {
                    throw "Client evidence '$($item.Name)' command '$($command.action)' contains a non-final actor admission."
                }
            }
            $actorIndexes = @($command.actorAdmissions | ForEach-Object { [int]$_.batchIndex } | Sort-Object -Unique)
            if ($actorIndexes.Count -ne [int]$command.actorCount) {
                throw "Client evidence '$($item.Name)' command '$($command.action)' has duplicate actor admission indexes."
            }
            for ($actorIndex = 0; $actorIndex -lt [int]$command.actorCount; $actorIndex++) {
                if ($actorIndexes[$actorIndex] -ne $actorIndex) {
                    throw "Client evidence '$($item.Name)' command '$($command.action)' does not cover actor index $actorIndex."
                }
            }

            $history = @($command.admissionHistory)
            $networkTransitionIndex = -1
            $queuedTransitionIndex = -1
            $entityTransitionIndex = -1
            for ($historyIndex = 0; $historyIndex -lt $history.Count; $historyIndex++) {
                if ($networkTransitionIndex -lt 0 -and [string]$history[$historyIndex].stage -ceq "NetworkIntake" -and
                    [string]$history[$historyIndex].result -ceq "NetworkScheduled") {
                    $networkTransitionIndex = $historyIndex
                }
                if ($queuedTransitionIndex -lt 0 -and [string]$history[$historyIndex].stage -ceq "EntityIntake" -and
                    [string]$history[$historyIndex].result -ceq "Queued") {
                    $queuedTransitionIndex = $historyIndex
                }
                if ($entityTransitionIndex -lt 0 -and [string]$history[$historyIndex].stage -ceq "EntityIntake" -and
                    [string]$history[$historyIndex].result -ceq "Activated") {
                    $entityTransitionIndex = $historyIndex
                }
            }
            if ($networkTransitionIndex -lt 0 -or $entityTransitionIndex -le $networkTransitionIndex) {
                throw "Client evidence '$($item.Name)' command '$($command.action)' lacks its network-to-entity admission history."
            }
            if ([string]$command.action -ceq "QueueTrainInfantry") {
                if ($queuedTransitionIndex -le $networkTransitionIndex -or
                    $entityTransitionIndex -le $queuedTransitionIndex) {
                    throw "Client evidence '$($item.Name)' queued training did not wait in EntityIntake before activation."
                }
            }
            elseif ([string]$command.action -ceq "TrainInfantry" -and $queuedTransitionIndex -ge 0) {
                throw "Client evidence '$($item.Name)' first training command unexpectedly entered the entity queue."
            }
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
        $immediateActivatedAuthoritativeTick = [int]$immediateTrainingActivations[0].authoritativeCommittedTick
        if ($immediateActivatedAuthoritativeTick -le 0 -or
            $immediateActivatedAuthoritativeTick -gt $serverSpawnTick -or
            $serverSpawnTick -gt $firstTrainedInfantryObservedTick) {
            throw "Client evidence '$($item.Name)' immediate training activation, spawn, and observation are not causally ordered."
        }
        $queuedTrainingCommands = @($trainingCommands | Where-Object { [string]$_.action -ceq "QueueTrainInfantry" })
        if ($queuedTrainingCommands.Count -ne 1) {
            throw "Client evidence '$($item.Name)' must contain exactly one queued infantry training command."
        }
        $queuedTrainingActivations = @($queuedTrainingCommands[0].admissionHistory | Where-Object {
            [string]$_.stage -ceq "EntityIntake" -and [string]$_.result -ceq "Activated"
        })
        if ($queuedTrainingActivations.Count -ne 1) {
            throw "Client evidence '$($item.Name)' queued training must contain exactly one EntityIntake:Activated transition."
        }
        if ($null -eq $queuedTrainingActivations[0].PSObject.Properties["authoritativeCommittedTick"]) {
            throw "Client evidence '$($item.Name)' queued training activation lacks an authoritative committed tick."
        }
        $queueActivatedAuthoritativeTick = [int]$queuedTrainingActivations[0].authoritativeCommittedTick
        if ($queueActivatedAuthoritativeTick -lt $serverSpawnTick -or
            $queueActivatedAuthoritativeTick -gt $serverSecondSpawnTick -or
            $serverSecondSpawnTick -gt $secondTrainedInfantryObservedTick) {
            throw "Client evidence '$($item.Name)' queued training activation, second spawn, and observation are not causally ordered."
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
            if ([int]$meetingCommand.actorCount -lt $minimumWinnerActors -or
                $siegeCommands.Count -ne 1 -or [int]$siegeCommands[0].actorCount -lt $minimumWinnerActors -or
                $coreAttackCommands.Count -ne 1 -or [int]$coreAttackCommands[0].actorCount -lt $minimumWinnerActors) {
                throw "Winning client evidence '$($item.Name)' does not prove multi-unit advance, siege move, and core attack."
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
        $minimumSquared = [int64]$ExpectedPlan.battle.minimumObservedMoveCm * [int64]$ExpectedPlan.battle.minimumObservedMoveCm
        foreach ($actorHandle in @($meetingCommand.actorHandles)) {
            $start = @($startPositions | Where-Object { [string]$_.handle -ceq [string]$actorHandle })
            $end = @($endPositions | Where-Object { [string]$_.handle -ceq [string]$actorHandle })
            if ($start.Count -ne 1 -or $end.Count -ne 1) {
                throw "Client evidence '$($item.Name)' cannot correlate movement positions for actor '$actorHandle'."
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

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$profileFullPath = [System.IO.Path]::GetFullPath($ProfilePath)
if (-not (Test-Path -LiteralPath $profileFullPath)) {
    throw "Acceptance profile not found: $profileFullPath"
}

$profile = Get-Content -LiteralPath $profileFullPath -Raw | ConvertFrom-Json
if ($profile.schemaVersion -ne 2) {
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
    schemaVersion = 5
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
        clientScreenshots = @(
            [ordered]@{
                process = $clientAScreenshotCapture.ProcessName
                targetPath = $clientAScreenshotCapture.TargetPath
                frames = @($clientAScreenshotCapture.Frames)
            }
            [ordered]@{
                process = $clientBScreenshotCapture.ProcessName
                targetPath = $clientBScreenshotCapture.TargetPath
                frames = @($clientBScreenshotCapture.Frames)
            }
        )
    }
    planFingerprint = $null
    orderedModIds = @()
    processes = @()
    clientCredentials = @()
    gameplayEvidence = @()
    screenshots = @()
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
            frame = [int]$_.Frame
            file = Get-FileEvidence -Path $_.Path
        }
    })
    foreach ($processName in @($screenshotTargets | ForEach-Object { $_.ProcessName } | Select-Object -Unique)) {
        $processHashes = @($manifest.screenshots |
            Where-Object { $_.process -eq $processName } |
            ForEach-Object { $_.file.sha256 } |
            Select-Object -Unique)
        $processScreenshotCount = @($manifest.screenshots |
            Where-Object { $_.process -eq $processName }).Count
        if ($processHashes.Count -ne $processScreenshotCount) {
            throw "Client '$processName' screenshots must be visually distinct across every configured gameplay stage."
        }
    }
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
