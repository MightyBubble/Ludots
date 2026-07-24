param(
    [string]$ProfilePath = (Join-Path $PSScriptRoot "rts-multiplayer-frontline-three-process.profile.json"),
    [string]$ArtifactDirectory = "",
    [string]$HostAddress = "",
    [int]$Port = 0,
    [string]$ConnectionKey = "",
    [int]$CredentialTimeoutSeconds = 0,
    [int]$RunSeconds = -1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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
        [Parameter(Mandatory = $true)][string]$StderrPath
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

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Failed to start process '$Name'."
    }

    $stdoutStream = [System.IO.File]::Create($StdoutPath)
    $stderrStream = [System.IO.File]::Create($StderrPath)
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

function New-RoleArtifacts {
    param(
        [Parameter(Mandatory = $true)][string]$RoleDirectory,
        [Parameter(Mandatory = $true)][string]$ProcessRole,
        [Parameter(Mandatory = $true)][int]$ClientInstanceId,
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
if ($profile.schemaVersion -ne 1) {
    throw "Unsupported acceptance profile schemaVersion '$($profile.schemaVersion)'."
}

$script:HostAddressValue = if ([string]::IsNullOrWhiteSpace($HostAddress)) { [string]$profile.host } else { $HostAddress }
$script:PortValue = if ($Port -gt 0) { $Port } else { [int]$profile.port }
$script:ConnectionKeyValue = if ([string]::IsNullOrWhiteSpace($ConnectionKey)) { [string]$profile.connectionKey } else { $ConnectionKey }
$credentialTimeoutValue = if ($CredentialTimeoutSeconds -gt 0) { $CredentialTimeoutSeconds } else { [int]$profile.credentialTimeoutSeconds }
$runSecondsValue = if ($RunSeconds -ge 0) { $RunSeconds } else { [int]$profile.runSeconds }
$pollMilliseconds = [int]$profile.monitorIntervalMilliseconds
if ($script:PortValue -lt 1 -or $script:PortValue -gt 65535) { throw "Port must be between 1 and 65535." }
if ([string]::IsNullOrWhiteSpace($script:ConnectionKeyValue)) { throw "ConnectionKey is required." }
if ($credentialTimeoutValue -le 0) { throw "CredentialTimeoutSeconds must be positive." }
if ($runSecondsValue -lt 0) { throw "RunSeconds cannot be negative." }
if ($pollMilliseconds -le 0) { throw "monitorIntervalMilliseconds must be positive." }
if ($profile.clientInstanceIds.Count -ne 2 -or $profile.clientInstanceIds[0] -eq $profile.clientInstanceIds[1]) {
    throw "The profile must declare exactly two distinct clientInstanceIds."
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

$manifestPath = Join-Path $artifactDirectoryValue "run-manifest.json"
$logsDirectory = Join-Path $artifactDirectoryValue "logs"
New-Item -ItemType Directory -Path $logsDirectory -Force | Out-Null
$ownedProcesses = [System.Collections.Generic.List[object]]::new()
$exitCode = 0
$failureMessage = $null
$manifest = [ordered]@{
    schemaVersion = 1
    acceptanceScope = "three-process-session-establishment"
    status = "preparing"
    startedAtUtc = [DateTime]::UtcNow.ToString("O")
    completedAtUtc = $null
    profilePath = $profileFullPath
    artifactDirectory = $artifactDirectoryValue
    host = $script:HostAddressValue
    port = $script:PortValue
    planFingerprint = $null
    orderedModIds = @()
    sourceLaunchGraph = $null
    roleArtifacts = @()
    processes = @()
    clientCredentials = @()
    credentialValidation = "LUDCRD01/64-byte/SHA256/non-empty/distinct"
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
    $manifest.planFingerprint = [string]$plan.PlanFingerprint
    $manifest.orderedModIds = @($plan.OrderedModIds)
    $manifest.sourceLaunchGraph = $sourceGraphEvidencePath

    $serverRole = New-RoleArtifacts -RoleDirectory (Join-Path $artifactDirectoryValue "server") `
        -ProcessRole "authoritativeServer" -ClientInstanceId 0 -CredentialPath "" `
        -SourceGraphPath $sourceGraphEvidencePath -Plan $plan
    $clientACredential = Join-Path $artifactDirectoryValue "client-a\session.credential"
    $clientARole = New-RoleArtifacts -RoleDirectory (Join-Path $artifactDirectoryValue "client-a") `
        -ProcessRole "replicatedClient" -ClientInstanceId ([int]$profile.clientInstanceIds[0]) `
        -CredentialPath $clientACredential -SourceGraphPath $sourceGraphEvidencePath -Plan $plan
    $clientBCredential = Join-Path $artifactDirectoryValue "client-b\session.credential"
    $clientBRole = New-RoleArtifacts -RoleDirectory (Join-Path $artifactDirectoryValue "client-b") `
        -ProcessRole "replicatedClient" -ClientInstanceId ([int]$profile.clientInstanceIds[1]) `
        -CredentialPath $clientBCredential -SourceGraphPath $sourceGraphEvidencePath -Plan $plan
    if ([System.IO.Path]::GetFullPath($clientACredential) -eq [System.IO.Path]::GetFullPath($clientBCredential)) {
        throw "Client credential paths must be distinct."
    }
    $manifest.roleArtifacts = @($serverRole, $clientARole, $clientBRole)
    Write-JsonFile -Value $manifest -Path $manifestPath

    $server = Start-CapturedProcess -Name "authoritative-server" -FilePath $dotnet `
        -Arguments @("exec", "--roll-forward", "Major", $serverAssembly, $serverRole.BootstrapPath) `
        -WorkingDirectory (Split-Path -Parent $serverAssembly) `
        -StdoutPath (Join-Path $artifactDirectoryValue "server\stdout.log") `
        -StderrPath (Join-Path $artifactDirectoryValue "server\stderr.log")
    $ownedProcesses.Add($server)
    Start-Sleep -Milliseconds ([int]$profile.interProcessDelayMilliseconds)
    Assert-OwnedProcessesAlive -OwnedProcesses $ownedProcesses

    $clientA = Start-CapturedProcess -Name "client-a" -FilePath $dotnet `
        -Arguments @("exec", "--roll-forward", "Major", [string]$plan.AppAssemblyPath, $clientARole.BootstrapPath) `
        -WorkingDirectory ([string]$plan.AppOutputDirectory) `
        -StdoutPath (Join-Path $artifactDirectoryValue "client-a\stdout.log") `
        -StderrPath (Join-Path $artifactDirectoryValue "client-a\stderr.log")
    $ownedProcesses.Add($clientA)
    Start-Sleep -Milliseconds ([int]$profile.interProcessDelayMilliseconds)
    Assert-OwnedProcessesAlive -OwnedProcesses $ownedProcesses

    $clientB = Start-CapturedProcess -Name "client-b" -FilePath $dotnet `
        -Arguments @("exec", "--roll-forward", "Major", [string]$plan.AppAssemblyPath, $clientBRole.BootstrapPath) `
        -WorkingDirectory ([string]$plan.AppOutputDirectory) `
        -StdoutPath (Join-Path $artifactDirectoryValue "client-b\stdout.log") `
        -StderrPath (Join-Path $artifactDirectoryValue "client-b\stderr.log")
    $ownedProcesses.Add($clientB)
    Start-Sleep -Milliseconds ([int]$profile.interProcessDelayMilliseconds)
    Assert-OwnedProcessesAlive -OwnedProcesses $ownedProcesses
    if ($clientA.Process.HasExited) { throw "Client A was replaced when client B started." }

    $manifest.processes = @($ownedProcesses | ForEach-Object {
        [ordered]@{ name = $_.Name; pid = $_.Pid; startedAtUtcTicks = $_.StartedAtUtcTicks; stdout = $_.StdoutPath; stderr = $_.StderrPath }
    })
    $manifest.status = "waiting-for-client-session-credentials"
    Write-JsonFile -Value $manifest -Path $manifestPath
    Wait-ForCredentialEvidence -CredentialPaths @($clientACredential, $clientBCredential) `
        -OwnedProcesses $ownedProcesses -TimeoutSeconds $credentialTimeoutValue -PollMilliseconds $pollMilliseconds
    [byte[]]$clientABytes = [System.IO.File]::ReadAllBytes($clientACredential)
    [byte[]]$clientBBytes = [System.IO.File]::ReadAllBytes($clientBCredential)
    if ([System.Convert]::ToBase64String($clientABytes) -eq [System.Convert]::ToBase64String($clientBBytes)) {
        throw "Clients received identical session credentials."
    }
    $manifest.clientCredentials = @($clientACredential, $clientBCredential)
    $manifest.status = "session-established"
    Write-JsonFile -Value $manifest -Path $manifestPath

    $survivalDeadline = [DateTime]::UtcNow.AddSeconds($runSecondsValue)
    while ([DateTime]::UtcNow -lt $survivalDeadline) {
        Assert-OwnedProcessesAlive -OwnedProcesses $ownedProcesses
        Start-Sleep -Milliseconds $pollMilliseconds
    }
    Assert-OwnedProcessesAlive -OwnedProcesses $ownedProcesses
    $manifest.status = "passed"
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

    $manifest.cleanup = if ($exitCode -eq 0) { "owned-processes-stopped" } else { "attempted" }
    $manifest.completedAtUtc = [DateTime]::UtcNow.ToString("O")
    Write-JsonFile -Value $manifest -Path $manifestPath
}

if ($exitCode -ne 0) {
    Write-Error "$failureMessage Evidence: $manifestPath"
    exit $exitCode
}

Write-Host "[PASS] Dedicated server and two clients established independent sessions."
Write-Host "Evidence: $artifactDirectoryValue"
Write-Host "Scope: process/session establishment only; gameplay UAT is separate."
