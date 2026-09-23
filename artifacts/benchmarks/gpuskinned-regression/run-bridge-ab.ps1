param(
    [Parameter(Mandatory)]
    [ValidateSet('baseline', 'no-effect', 'no-presenter', 'no-grounding', 'no-animator', 'no-minimap', 'no-hud', 'nav-1hz', 'paused')]
    [string]$Profile,
    [int]$Rounds = 3,
    [int]$Port = 47947,
    [int]$WarmupSeconds = 8,
    [int]$SampleSeconds = 4,
    [int]$WindowsPerRound = 4
)

$ErrorActionPreference = 'Stop'
$benchRoot = $PSScriptRoot
$repoRoot = [IO.Path]::GetFullPath((Join-Path $benchRoot '../../..'))
$appRoot = Join-Path $repoRoot 'src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net9.0'
$graphPath = Join-Path $repoRoot 'artifacts/launcher/raylib.launch.graph.json'
$presenterPath = Join-Path $repoRoot 'mods/capabilities/navigation/MassNavigationMod/assets/Presentation/presenters.json'
$templatePath = Join-Path $repoRoot 'mods/showcases/capability_standard/CapabilityStandardMassNavigationLargeWorld10kMod/assets/Entities/templates.json'
$navigationConfigPath = Join-Path $repoRoot 'mods/capabilities/navigation/MassNavigationMod/assets/MassNavigationConfig.json'
$outputPath = Join-Path $benchRoot "ab-$Profile.json"

function Invoke-BridgeRpc {
    param([string]$Method, [hashtable]$Params = @{})
    $body = @{
        jsonrpc = '2.0'
        id = 1
        method = $Method
        params = $Params
    } | ConvertTo-Json -Depth 10
    $response = Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$Port/rpc" -ContentType 'application/json' -Body $body
    if ($null -ne $response.error) {
        throw "Bridge RPC $Method failed: $($response.error | ConvertTo-Json -Compress -Depth 10)"
    }
    return $response.result
}

function Set-AbProfile {
    param([string]$Name)

    if ($Name -eq 'no-effect') {
        $templates = Get-Content -LiteralPath $templatePath -Raw | ConvertFrom-Json
        foreach ($template in $templates) {
            $template.PSObject.Properties.Remove('onSpawnEffect')
        }
        $templates | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $templatePath -Encoding utf8NoBOM
        return
    }

    if ($Name -eq 'nav-1hz') {
        $config = Get-Content -LiteralPath $navigationConfigPath -Raw | ConvertFrom-Json
        $config.cadence.simulationHz = 1
        $config.cadence.targetUpdateHz = 0
        $config.cadence.flowStepHz = 0
        $config.cadence.flowCrowdStampHz = 0
        $config.cadence.flowObstacleStampHz = 0
        $config.cadence.hardResolveHz = 0
        $config.cadence.entitySyncHz = 0
        $config | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $navigationConfigPath -Encoding utf8NoBOM
        return
    }

    if ($Name -notin @('no-presenter', 'no-grounding', 'no-animator', 'no-minimap', 'no-hud')) {
        return
    }

    $definitions = Get-Content -LiteralPath $presenterPath -Raw | ConvertFrom-Json
    if ($Name -eq 'no-presenter') {
        foreach ($definition in $definitions) {
            if ($null -eq $definition.rules) {
                continue
            }
            $createsPresenter = $false
            foreach ($rule in $definition.rules) {
                if ($rule.command.kind -eq 'CreatePresenter') {
                    $createsPresenter = $true
                    break
                }
            }
            if ($createsPresenter) {
                $definition.rules = @($definition.rules | Where-Object { $_.command.kind -ne 'CreatePresenter' -and $_.command.kind -ne 'DestroyPresenterScope' })
            }
        }
    }
    else {
        foreach ($definition in $definitions) {
            if ($definition.id -notin @('mass_navigation_agent_light', 'mass_navigation_agent_heavy')) {
                continue
            }
            if ($Name -eq 'no-hud') {
                $definition.children = @()
                continue
            }
            $kind = switch ($Name) {
                'no-grounding' { 'Grounding' }
                'no-animator' { 'Animator' }
                'no-minimap' { 'MinimapMarker' }
            }
            $definition.behaviors = @($definition.behaviors | Where-Object { $_.kind -ne $kind })
        }
    }
    $definitions | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $presenterPath -Encoding utf8NoBOM
}

function Wait-BridgeReady {
    param([Diagnostics.Process]$Process)
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            throw "Raylib exited during startup with code $($Process.ExitCode)."
        }
        try {
            $health = Invoke-RestMethod "http://127.0.0.1:$Port/health"
            if ($health.ok -and $health.pumpCount -gt 0) {
                return $health
            }
        }
        catch {
        }
        Start-Sleep -Milliseconds 250
    }
    throw "Agent Bridge did not become ready on port $Port."
}

$presenterBytes = [IO.File]::ReadAllBytes($presenterPath)
$templateBytes = [IO.File]::ReadAllBytes($templatePath)
$navigationConfigBytes = [IO.File]::ReadAllBytes($navigationConfigPath)
$samples = [Collections.Generic.List[object]]::new()

try {
    Set-AbProfile $Profile
    for ($round = 1; $round -le $Rounds; $round++) {
        if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
            throw "Bridge port $Port is already in use."
        }

        $plan = Get-Content -LiteralPath $graphPath -Raw | ConvertFrom-Json
        @{
            LaunchGraphPath = $graphPath
            LaunchGraphFullPath = $graphPath
            PlanSelectors = $plan.selectors
            PlanRootModIds = $plan.rootModIds
            PlanOrderedModIds = $plan.orderedModIds
            PlanFingerprint = $plan.planFingerprint
            PlanSchemaVersion = $plan.schemaVersion
            PlanGeneratedAtUtc = $plan.generatedAtUtc
            BrowserRuntime = $plan.browserRuntime
        } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $appRoot 'launcher.runtime.json') -Encoding utf8NoBOM

        $env:LUDOTS_AGENT_BRIDGE_PORT = [string]$Port
        $env:LUDOTS_RAYLIB_DRAW_SHADOWS = 'false'
        $env:LUDOTS_RAYLIB_TIMING_LOG_INTERVAL_FRAMES = '0'
        $env:LUDOTS_RAYLIB_SYSTEM_BREAKDOWN = 'false'
        $env:LUDOTS_AUTO_EXIT_FRAME = '0'
        $env:LUDOTS_RAYLIB_DIAGNOSTIC_PATH = $null
        $env:LUDOTS_TAKE_SCREENSHOT_PATH = $null
        $stdoutPath = Join-Path $benchRoot "ab-$Profile-r$round-stdout.log"
        $stderrPath = Join-Path $benchRoot "ab-$Profile-r$round-stderr.log"
        $process = Start-Process -FilePath (Join-Path $appRoot 'Ludots.App.Raylib.exe') `
            -ArgumentList 'launcher.runtime.json' -WorkingDirectory $appRoot -WindowStyle Hidden `
            -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru

        try {
            $ready = Wait-BridgeReady $process
            $entityDeadline = [DateTime]::UtcNow.AddSeconds(45)
            do {
                $entitySnapshot = Invoke-BridgeRpc 'ludots.entities.query' @{ limit = 1 }
                if ($entitySnapshot.totalMatched -ge 10000) {
                    break
                }
                if ([DateTime]::UtcNow -ge $entityDeadline) {
                    throw "10k entity population did not become ready; totalMatched=$($entitySnapshot.totalMatched)."
                }
                Start-Sleep -Milliseconds 500
            } while (-not $process.HasExited)

            Start-Sleep -Seconds $WarmupSeconds
            if ($Profile -eq 'paused') {
                Invoke-BridgeRpc 'ludots.time.control' @{ action = 'pause' } | Out-Null
                Start-Sleep -Seconds 2
            }

            $roundSamples = [Collections.Generic.List[object]]::new()
            for ($window = 1; $window -le $WindowsPerRound; $window++) {
                $startTime = [DateTime]::UtcNow
                $startHealth = Invoke-RestMethod "http://127.0.0.1:$Port/health"
                $startCpu = $process.TotalProcessorTime.TotalMilliseconds
                Start-Sleep -Seconds $SampleSeconds
                $endHealth = Invoke-RestMethod "http://127.0.0.1:$Port/health"
                $endCpu = $process.TotalProcessorTime.TotalMilliseconds
                $elapsed = ([DateTime]::UtcNow - $startTime).TotalSeconds
                $pumpDelta = [double]($endHealth.pumpCount - $startHealth.pumpCount)
                $windowSample = [pscustomobject]@{
                    round = $round
                    window = $window
                    elapsedSeconds = $elapsed
                    frames = $pumpDelta
                    fps = $pumpDelta / $elapsed
                    processCpuMs = $endCpu - $startCpu
                    workingSetBytes = $process.WorkingSet64
                    pumpStart = $startHealth.pumpCount
                    pumpEnd = $endHealth.pumpCount
                }
                $roundSamples.Add($windowSample)
                $samples.Add($windowSample)
            }

            $session = Invoke-BridgeRpc 'ludots.session.info'
            $roundFps = @($roundSamples | ForEach-Object fps | Sort-Object)
            [pscustomobject]@{
                profile = $Profile
                round = $round
                fpsMedian = $roundFps[[int][Math]::Floor($roundFps.Count / 2)]
                tick = $session.tick
                entities = $entitySnapshot.totalMatched
                pid = $process.Id
            } | ConvertTo-Json -Compress | Write-Output
        }
        finally {
            if ($null -ne $process -and -not $process.HasExited) {
                Stop-Process -Id $process.Id -Force
                $process.WaitForExit()
            }
        }
    }
}
finally {
    [IO.File]::WriteAllBytes($presenterPath, $presenterBytes)
    [IO.File]::WriteAllBytes($templatePath, $templateBytes)
    [IO.File]::WriteAllBytes($navigationConfigPath, $navigationConfigBytes)
}

$orderedFps = @($samples | ForEach-Object fps | Sort-Object)
$medianFps = if ($orderedFps.Count -eq 0) { 0 } else { $orderedFps[[int][Math]::Floor($orderedFps.Count / 2)] }
$result = [pscustomobject]@{
    profile = $Profile
    commit = (git -C $repoRoot rev-parse HEAD)
    rounds = $Rounds
    warmupSeconds = $WarmupSeconds
    sampleSeconds = $SampleSeconds
    windowsPerRound = $WindowsPerRound
    medianFps = $medianFps
    samples = $samples
}
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $outputPath -Encoding utf8NoBOM
$result | ConvertTo-Json -Depth 4
