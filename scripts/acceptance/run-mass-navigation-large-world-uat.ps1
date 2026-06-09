param(
    [string]$OutputRoot = "",
    [int]$Iterations = 1,
    [string]$UntilLocalTime = "",
    [ValidateSet("raylib", "web")]
    [string]$Adapter = "raylib",
    [string]$Build = "",
    [int]$NavMeshActiveChunkMinX = 126,
    [int]$NavMeshActiveChunkMinY = 126,
    [int]$NavMeshActiveChunkMaxX = 130,
    [int]$NavMeshActiveChunkMaxY = 130,
    [switch]$SkipNavMeshActiveWindowBake,
    [switch]$StopOnFailure,
    [switch]$AllowFailures
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$launcher = Join-Path $repoRoot "scripts\run-mod-launcher.cmd"
$toolProject = Join-Path $repoRoot "src\Tools\Ludots.Tool\Ludots.Tool.csproj"
$massNavigationModRoot = Join-Path $repoRoot "mods\capabilities\navigation\MassNavigationMod"
if (-not (Test-Path $launcher)) {
    throw "Launcher script not found: $launcher"
}

if (-not (Test-Path $toolProject)) {
    throw "Ludots.Tool project not found: $toolProject"
}

if (-not (Test-Path (Join-Path $massNavigationModRoot "mod.json"))) {
    throw "MassNavigationMod root not found: $massNavigationModRoot"
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputRoot = Join-Path $repoRoot "artifacts\acceptance\mass-navigation-large-world-soak-$stamp"
}
else {
    $OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$deadline = $null
if (-not [string]::IsNullOrWhiteSpace($UntilLocalTime)) {
    $parsed = [DateTime]::MinValue
    if (-not [DateTime]::TryParse($UntilLocalTime, [ref]$parsed)) {
        throw "UntilLocalTime must be parseable by PowerShell DateTime, for example '06:00' or '2026-04-27 06:00'."
    }

    $now = Get-Date
    if ($UntilLocalTime -match '^\d{1,2}:\d{2}$') {
        $deadline = Get-Date -Year $now.Year -Month $now.Month -Day $now.Day -Hour $parsed.Hour -Minute $parsed.Minute -Second 0
        if ($deadline -le $now) {
            $deadline = $deadline.AddDays(1)
        }
    }
    else {
        $deadline = $parsed
    }
}

if ($Iterations -lt 0) {
    throw "Iterations must be 0 or greater. Use 0 with UntilLocalTime for an overnight run."
}

if ($Iterations -eq 0 -and $null -eq $deadline) {
    throw "Iterations=0 requires UntilLocalTime so the soak has an explicit stop condition."
}

$summaryPath = Join-Path $OutputRoot "soak-summary.jsonl"
$reportPath = Join-Path $OutputRoot "soak-report.md"
$runs = New-Object System.Collections.Generic.List[object]

function Invoke-CheckedDotnet {
    param(
        [string]$Label,
        [string[]]$Arguments
    )

    $output = & dotnet @Arguments 2>&1
    $exit = $LASTEXITCODE
    $output | ForEach-Object { Write-Host "[$Label] $_" }
    if ($exit -ne 0) {
        throw "$Label failed with exit code $exit"
    }
}

function Initialize-MassNavigationActiveWindowNavMeshBake {
    if ($SkipNavMeshActiveWindowBake) {
        Write-Host "Skipping MassNavigation active-window NavMesh bake."
        return
    }

    if ($NavMeshActiveChunkMinX -gt $NavMeshActiveChunkMaxX -or $NavMeshActiveChunkMinY -gt $NavMeshActiveChunkMaxY) {
        throw "NavMesh active chunk min must be <= max."
    }

    $bakeRoot = Join-Path $OutputRoot "_navmesh-active-window-bake"
    New-Item -ItemType Directory -Force -Path $bakeRoot | Out-Null
    $logicHeightmap = Join-Path $bakeRoot "mass_navigation_active_window.lhtm"
    $dirtyChunksPath = Join-Path $bakeRoot "mass_navigation_active_window_dirty_chunks.json"

    $dirty = New-Object System.Collections.Generic.List[string]
    for ($cy = $NavMeshActiveChunkMinY; $cy -le $NavMeshActiveChunkMaxY; $cy++) {
        for ($cx = $NavMeshActiveChunkMinX; $cx -le $NavMeshActiveChunkMaxX; $cx++) {
            $dirty.Add("$cx,$cy")
        }
    }

    $dirty | ConvertTo-Json | Set-Content -Path $dirtyChunksPath -Encoding UTF8
    Invoke-CheckedDotnet -Label "mass-nav gen sparse lhtm" -Arguments @(
        "run", "--project", $toolProject, "--",
        "map", "gen-lhtm",
        "--out", $logicHeightmap,
        "--widthChunks", "256",
        "--heightChunks", "256",
        "--preset", "mountainRiver",
        "--chunkMinX", "$NavMeshActiveChunkMinX",
        "--chunkMinY", "$NavMeshActiveChunkMinY",
        "--chunkMaxX", "$NavMeshActiveChunkMaxX",
        "--chunkMaxY", "$NavMeshActiveChunkMaxY",
        "--includeNeighbors", "true",
        "--overwrite"
    )

    Invoke-CheckedDotnet -Label "mass-nav bake active-window navmesh" -Arguments @(
        "run", "--project", $toolProject, "--",
        "nav", "bake-recast-lhtm",
        "--mapId", "mass_navigation",
        "--in", $logicHeightmap,
        "--dirty", $dirtyChunksPath,
        "--includeNeighbors", "false",
        "--repoRoot", $massNavigationModRoot,
        "--modRoot", $massNavigationModRoot,
        "--parallel", "true",
        "--maxDegree", "4",
        "--artifact", "true"
    )
}

Initialize-MassNavigationActiveWindowNavMeshBake

function ConvertTo-SafeJsonLine {
    param([object]$Value)
    return ($Value | ConvertTo-Json -Depth 12 -Compress)
}

function Write-SoakReport {
    param(
        [System.Collections.Generic.List[object]]$RunRows,
        [string]$Path,
        [string]$Root,
        [object]$DeadlineValue
    )

    $passed = @($RunRows | Where-Object { $_.scene_smoke_success -eq $true }).Count
    $failed = @($RunRows | Where-Object { $_.scene_smoke_success -ne $true }).Count
    $productionPassed = @($RunRows | Where-Object { $_.production_gate_success -eq $true }).Count
    $productionFailed = @($RunRows | Where-Object { $_.production_gate_success -ne $true }).Count
    $machineReady = @($RunRows | Where-Object { $_.machine_production_evidence_success -eq $true }).Count
    $manualAccepted = @($RunRows | Where-Object { $_.manual_uat_accepted -eq $true }).Count
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# MassNavigation Large-World UAT Soak")
    $lines.Add("")
    $lines.Add("## Scope")
    $lines.Add("- Target: ``MassNavigationMod``, the high-performance MassNavigation foundation acceptance surface.")
    $lines.Add("- Launcher path: ``scripts/run-mod-launcher.cmd cli launch mass_navigation --adapter raylib --record ...``.")
    $lines.Add("- Evidence per run: ``battle-report.md``, ``trace.jsonl``, ``path.mmd``, ``summary.json``, ``visible-checklist.md``, ``screens/000_boot.png`` through ``screens/030_waypoint_edit_after_pathpoints_regenerated.png``, ``screens/raylib-frame-benchmark.json``, and ``screens/timeline.png``.")
    $lines.Add("- No fallback contract: out-of-world commands must be rejected and counted; camera/minimap may inspect any in-bounds world coordinate.")
    $lines.Add("")
    $lines.Add("## Result")
    $lines.Add("- Output root: ``$Root``")
    $lines.Add("- Deadline: ``$DeadlineValue``")
    $lines.Add("- Runs: ``$($RunRows.Count)``")
    $lines.Add("- Scene smoke passed: ``$passed``")
    $lines.Add("- Scene smoke failed: ``$failed``")
    $lines.Add("- Production gate passed: ``$productionPassed``")
    $lines.Add("- Production gate failed: ``$productionFailed``")
    $lines.Add("- Machine production evidence ready: ``$machineReady``")
    $lines.Add("- Manual UAT accepted: ``$manualAccepted``")
    $lines.Add("- Replay/smoke evidence is not a production signoff; ``manual-uat-signoff.json`` is required before reporting production PASS.")
    $lines.Add("")
    $lines.Add("## UAT Matrix")
    $lines.Add("| Case | Player-facing expectation | Machine check |")
    $lines.Add("| --- | --- | --- |")
    $lines.Add("| 64km world boot | Designer sees one standard RTS battlefield | ``world_width_cm == 6400000 && world_height_cm == 6400000`` |")
    $lines.Add("| Four dynamic teams | Scenario is not a hard-coded two-team demo | ``teams >= 4`` |")
    $lines.Add("| Full minimap | Minimap starts as the whole world | full-world half extent check |")
    $lines.Add("| Camera jumps | Clicking minimap coordinates moves camera exactly there | 12 target tolerances, including all corners and empty space |")
    $lines.Add("| Formal selection | Box-selected units are represented by Ludots selection runtime | selected count after ``SelectionRuntime`` write |")
    $lines.Add("| GAS order | Right-click move goes through order buffer | active order/group after ``massNavigationMove`` |")
    $lines.Add("| Movement | Selected group actually moves | first, second, empty-world, multi-team, and near-edge command advance thresholds |")
    $lines.Add("| Reset hygiene | Reset clears selection/groups/orders | post-reset selected/groups zero |")
    $lines.Add("| Multi-team orders | Every dynamic team can own a selected move order | per-team command advance array |")
    $lines.Add("| Edge in-bounds | World edge is still normal playable RTS space | four corners plus four side midpoints are accepted and move |")
    $lines.Add("| Boundary errors | Invalid commands are diagnosed, not clamped | eight out-of-world probes, including just-over-edge cases, increment rejects |")
    $lines.Add("| World bounds | Runtime never leaks positions outside configured board | ``agents_outside_world == 0`` and solver/work area bounds checks |")
    $lines.Add("| Streaming/working set | Large-world active chunks stay live through soak | loaded chunk count remains positive |")
    $lines.Add("| Memory stability | Long run does not steadily leak | steady managed/allocated thresholds |")
    $lines.Add("| U1-U16 acceptance matrix | Mod developer can see every requested navigation showcase case and its production gate | ``use_case_statuses`` contains U1-U16 and ``screens/008_acceptance_gate_matrix.png`` exists |")
    $lines.Add("")
    $lines.Add("## Runs")
    $lines.Add("| # | Scene | Prod | Machine | Manual | World cm | Agents cmd/move | Full select/slots | Reuse | Teams | Macro | NavMesh baked/notLoaded/total | NavMesh % | Obstacles target/auth/bake/load/solver | FPS measured | Timeline |")
    $lines.Add("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- | --- | ---: | --- | ---: | --- | --- |")
    foreach ($run in $RunRows) {
        $sceneResult = if ($run.scene_smoke_success) { "PASS" } else { "FAIL" }
        $prodResult = if ($run.production_gate_success) { "PASS" } else { "FAIL" }
        $machineResult = if ($run.machine_production_evidence_success) { "READY" } else { "BLOCKED" }
        $manualResult = if ($run.manual_uat_accepted) { "SIGNED" } else { "MISSING" }
        $timeline = if ($run.timeline) { $run.timeline } else { "n/a" }
        $world = if ($null -ne $run.world_width_cm -and $null -ne $run.world_height_cm) { "$($run.world_width_cm)x$($run.world_height_cm)" } else { "n/a" }
        $macro = if ($null -ne $run.macro_chunk_columns -and $null -ne $run.macro_chunk_rows) { "$($run.macro_chunk_columns)x$($run.macro_chunk_rows)" } else { "n/a" }
        $navmesh = "$($run.navmesh_baked_tiles)/$($run.navmesh_not_loaded_tiles)/$($run.navmesh_total_tiles)"
        $agentLoad = "$($run.agent_count)/$($run.commanded_agents)/$($run.moving_agents)"
        $fullLoad = "$($run.full_selection_agents)/$($run.full_selection_target_slots)"
        $reuse = "hit=$($run.order_reuse_cache_hit) fanout=$($run.order_reuse_fanout)"
        $obstacles = "$($run.target_static_obstacle_count)/$($run.authored_static_obstacle_count)/$($run.baked_static_obstacle_count)/$($run.loaded_static_obstacle_count)/$($run.solver_active_static_obstacle_count)"
        $fpsMeasured = if ($null -ne $run.fps_measured) { "$($run.fps_measured)" } else { "false" }
        $coverage = if ($null -ne $run.navmesh_coverage_percent) { "{0:0.##}" -f [double]$run.navmesh_coverage_percent } else { "n/a" }
        $lines.Add("| $($run.run) | $sceneResult | $prodResult | $machineResult | $manualResult | $world | $agentLoad | $fullLoad | $reuse | $($run.team_count) | $macro | $navmesh | $coverage | $obstacles | $fpsMeasured | ``$timeline`` |")
    }

    $lines.Add("")
    $lines.Add("## Failure Handling")
    $lines.Add("- If any run fails, inspect that run directory first; it keeps stdout/stderr in ``run.log`` and the last evidence screenshots.")
    $lines.Add("- Do not treat a missing service or missing summary as a fallback success. Missing evidence is a failed run.")
    $lines.Add("- Headless tick timings are separate from Raylib framebuffer timing. Use ``frame_timing.raylib_*`` and ``screens/raylib-frame-benchmark.json`` for the renderer smoke benchmark.")
    Set-Content -Path $Path -Value $lines -Encoding UTF8
}

$runIndex = 0
$hadFailure = $false

function Assert-MassNavigationLargeWorldSummary {
    param([object]$Summary)

    $failures = New-Object System.Collections.Generic.List[string]

    if ($Summary.path_only_query.Status -ne "Ok") { $failures.Add("path_only_query.Status must be Ok") }
    if ($Summary.path_only_query.NoOrderSubmitted -ne $true) { $failures.Add("path_only_query.NoOrderSubmitted must be true") }
    if ($Summary.path_only_query.PreviewMode -ne "path_preview") { $failures.Add("path_only_query.PreviewMode must be path_preview") }
    if ($Summary.path_only_query.InputContract -ne "pick_start_world_point_then_goal_world_point") { $failures.Add("path_only_query.InputContract must describe start/goal point picking") }
    if ($Summary.path_only_query.RoutePreviewState -ne "highlighted_route_ready") { $failures.Add("path_only_query.RoutePreviewState must be highlighted_route_ready") }
    if ($Summary.path_only_query.HighlightRouteVisible -ne $true) { $failures.Add("path_only_query.HighlightRouteVisible must be true") }
    if ($Summary.path_only_query.PathPointContract -ne "immutable_query_result") { $failures.Add("path_only_query.PathPointContract must be immutable_query_result") }
    if ($Summary.path_only_query.WaypointContract -ne "editable_order_intent") { $failures.Add("path_only_query.WaypointContract must be editable_order_intent") }
    if ([string]::IsNullOrWhiteSpace([string]$Summary.path_only_query.RouteProvenance)) { $failures.Add("path_only_query.RouteProvenance must be present") }
    if ([int]$Summary.path_only_query.order_delta -ne 0) { $failures.Add("path_only_query.order_delta must be 0") }
    if ([int]$Summary.path_only_query.PathPointCount -lt 2) { $failures.Add("path_only_query.PathPointCount must be >= 2") }
    if ([int]$Summary.path_only_query.MacroRouteChunkCount -lt 1) { $failures.Add("path_only_query.MacroRouteChunkCount must be >= 1") }
    if (@($Summary.strategy_switch_diagnostics).Count -lt 5) { $failures.Add("strategy_switch_diagnostics.Count must be >= 5") }
    $activeWindowMeshRows = @($Summary.strategy_switch_diagnostics | Where-Object {
        $_.MeshQueryAvailable -eq $true -and
        $_.MeshStatus -eq "Ok" -and
        $_.MeshQuerySource -eq "active_window_navmesh_query" -and
        [int]$_.MeshTouchedTileCount -gt 0
    })
    if ($activeWindowMeshRows.Count -lt 1) { $failures.Add("strategy_switch_diagnostics must include at least one real active-window NavMesh query row") }
    $infantryMesh = @($Summary.strategy_switch_diagnostics | Where-Object { $_.AgentTypeId -eq "Infantry" }) | Select-Object -First 1
    if ($null -eq $infantryMesh -or $infantryMesh.MeshQuerySource -ne "active_window_navmesh_query" -or $infantryMesh.MeshStatus -ne "Ok") {
        $failures.Add("Infantry strategy row must use active_window_navmesh_query with MeshStatus Ok")
    }
    $airMesh = @($Summary.strategy_switch_diagnostics | Where-Object { $_.AgentTypeId -eq "Air" }) | Select-Object -First 1
    foreach ($agentType in @("Infantry", "Mountain", "Naval", "Air")) {
        $mesh = @($Summary.strategy_switch_diagnostics | Where-Object { $_.AgentTypeId -eq $agentType }) | Select-Object -First 1
        if ($null -eq $mesh -or $mesh.MeshQueryAvailable -ne $true -or $mesh.MeshStatus -ne "Ok" -or $mesh.MeshQuerySource -ne "active_window_navmesh_query" -or [int]$mesh.MeshTouchedTileCount -le 0) {
            $failures.Add("$agentType strategy row must use active_window_navmesh_query with MeshStatus Ok after multi-layer active-window bake")
        }
    }
    if ($Summary.order_reuse.CacheHit -ne $true) { $failures.Add("order_reuse.CacheHit must be true") }
    if ([int]$Summary.order_reuse.FanoutCount -lt 128) { $failures.Add("order_reuse.FanoutCount must be >= 128") }
    if ([int]$Summary.order_reuse.SamePointReuseCount -lt 1) { $failures.Add("order_reuse.SamePointReuseCount must be >= 1") }
    if ([int]$Summary.order_reuse.NearPointReuseCount -lt 1) { $failures.Add("order_reuse.NearPointReuseCount must be >= 1") }
    if ([string]::IsNullOrWhiteSpace([string]$Summary.order_reuse.ReuseScope)) { $failures.Add("order_reuse.ReuseScope must be present") }
    if ([string]$Summary.order_reuse.PathRouteSignature -eq "not_available" -or [string]::IsNullOrWhiteSpace([string]$Summary.order_reuse.PathRouteSignature)) { $failures.Add("order_reuse.PathRouteSignature must be available") }
    if ([string]$Summary.order_reuse.MeshRouteSignature -eq "not_available" -or [string]::IsNullOrWhiteSpace([string]$Summary.order_reuse.MeshRouteSignature)) { $failures.Add("order_reuse.MeshRouteSignature must be available") }
    if ([int]$Summary.order_reuse.PathRoutePointCount -lt 2) { $failures.Add("order_reuse.PathRoutePointCount must be >= 2") }
    if ([int]$Summary.order_reuse.MeshRouteTouchedTileCount -lt 1) { $failures.Add("order_reuse.MeshRouteTouchedTileCount must be >= 1") }
    if ([int]$Summary.target_allocation.SlotCount -lt 10000) { $failures.Add("target_allocation.SlotCount must be >= 10000") }
    if ([int]$Summary.target_allocation.SelectedCount -lt 10000) { $failures.Add("target_allocation.SelectedCount must be >= 10000") }
    if ([int]$Summary.target_allocation.reachable_slot_count -lt 10000) { $failures.Add("target_allocation.reachable_slot_count must be >= 10000") }
    if ($Summary.target_allocation.ReachabilityProbeStatus -ne "Ok") { $failures.Add("target_allocation.ReachabilityProbeStatus must be Ok") }
    $reachabilitySource = [string]$Summary.target_allocation.ReachabilitySource
    if (-not ($reachabilitySource.Contains("path_only_route_reachability_smoke") -or $reachabilitySource.Contains("active_window_navmesh_query"))) {
        $failures.Add("target_allocation.ReachabilitySource must cite path_only_route_reachability_smoke or active_window_navmesh_query")
    }
    if ([int]$Summary.target_allocation.ReachabilityFanoutCount -lt 10000) { $failures.Add("target_allocation.ReachabilityFanoutCount must be >= 10000") }
    if ([int]$Summary.target_allocation.AllocationRouteId -le 0) { $failures.Add("target_allocation.AllocationRouteId must be > 0") }
    if ([string]::IsNullOrWhiteSpace([string]$Summary.target_allocation.AllocationRouteReuseKey)) { $failures.Add("target_allocation.AllocationRouteReuseKey must be present") }
    if ([string]::IsNullOrWhiteSpace([string]$Summary.target_allocation.MeshReachabilityStatus)) { $failures.Add("target_allocation.MeshReachabilityStatus must be present") }
    if ([string]::IsNullOrWhiteSpace([string]$Summary.target_allocation.MeshReachabilitySource)) { $failures.Add("target_allocation.MeshReachabilitySource must be present") }
    if ([int]$Summary.target_allocation.BlockedSlotCount -ne 0) { $failures.Add("target_allocation.BlockedSlotCount must be 0 for current showcase smoke") }
    if ([int]$Summary.full_selection_agents -lt 10000) { $failures.Add("full_selection_agents must be >= 10000") }
    if ([int]$Summary.commanded_agents -lt 10000) { $failures.Add("commanded_agents must be >= 10000") }
    if ($Summary.flow_enabled -ne $true) { $failures.Add("flow_enabled must be true") }
    if ([int]$Summary.macro_chunk_columns -ne 256 -or [int]$Summary.macro_chunk_rows -ne 256) { $failures.Add("macro grid must be 256x256") }
    if ([int]$Summary.macro_chunk_count -ne 65536) { $failures.Add("macro_chunk_count must be 65536") }
    if ([int]$Summary.world_width_cm -ne 6400000 -or [int]$Summary.world_height_cm -ne 6400000) { $failures.Add("world size must be 6400000x6400000cm") }
    if ([int]$Summary.loaded_chunk_count -lt 1) { $failures.Add("S1 loaded_chunk_count must be present and positive") }
    if ($Summary.boundary_click_result -ne "inside_edge_accepted_outside_edge_clamped") { $failures.Add("S1 boundary_click_result must prove inside edge accepted and outside edge clamped") }
    if ($Summary.ground_picking_result -ne "inside_ground_pick_accepted_outside_ground_pick_clamped") { $failures.Add("S1 ground_picking_result must prove inside pick accepted and outside pick clamped") }
    if ($Summary.world_boundary_diagnostics.Available -ne $true) { $failures.Add("S1 world_boundary_diagnostics.Available must be true") }
    if ($Summary.world_boundary_diagnostics.CameraInBounds -ne $true) { $failures.Add("S1 camera target must remain inside world bounds") }
    if ($Summary.world_boundary_diagnostics.MinimapBoundaryClickInBounds -ne $true -or $Summary.world_boundary_diagnostics.MinimapBoundaryClickClamped -ne $true) { $failures.Add("S1 minimap boundary click must resolve in bounds and clamp outside-screen probe") }
    if ($Summary.world_boundary_diagnostics.GroundPickingInsideAccepted -ne $true -or $Summary.world_boundary_diagnostics.GroundPickingOutsideClamped -ne $true) { $failures.Add("S1 ground picking probes must accept inside point and clamp outside point") }
    if ([int]$Summary.navmesh_baked_tiles -lt 1) { $failures.Add("large-world active-window navmesh bake must load at least one real baked tile") }
    $expectedNavMeshTotalTiles = [int]$Summary.macro_chunk_count * [int]$Summary.navmesh_layer_count * [int]$Summary.navmesh_profile_count
    if ([int]$Summary.navmesh_total_tiles -ne $expectedNavMeshTotalTiles) { $failures.Add("navmesh_total_tiles must equal macro_chunk_count * navmesh_layer_count * navmesh_profile_count") }
    if ([int]$Summary.navmesh_not_loaded_tiles -ne ([int]$Summary.navmesh_total_tiles - [int]$Summary.navmesh_baked_tiles)) { $failures.Add("navmesh_not_loaded_tiles must equal total-baked for the current multi-layer active-window bake") }
    if ([int]$Summary.navmesh_layer_count -lt 4) { $failures.Add("navmesh_layer_count must be >= 4") }
    if ([int]$Summary.navmesh_profile_count -lt 5) { $failures.Add("navmesh_profile_count must be >= 5") }
    if ([int]$Summary.navmesh_area_cost_count -lt 7) { $failures.Add("navmesh_area_cost_count must be >= 7") }
    if (@($Summary.layer_cost_diagnostics).Count -lt 5) { $failures.Add("layer_cost_diagnostics.Count must be >= 5") }
    if (@($Summary.layer_cost_query_matrix).Count -lt 5) { $failures.Add("layer_cost_query_matrix.Count must be >= 5") }
    foreach ($agentType in @("Infantry","LargeVehicle","Mountain","Naval","Air")) {
        $row = @($Summary.layer_cost_query_matrix | Where-Object { $_.agent_type_id -eq $agentType }) | Select-Object -First 1
        if ($null -eq $row) {
            $failures.Add("layer_cost_query_matrix missing $agentType")
        }
        elseif ([string]::IsNullOrWhiteSpace($row.selected_strategy) -or [string]::IsNullOrWhiteSpace($row.area_cost_samples)) {
            $failures.Add("layer_cost_query_matrix $agentType must include selected_strategy and area_cost_samples")
        }
        elseif ($agentType -eq "Infantry" -and ($row.mesh_query_source -ne "active_window_navmesh_query" -or [int]$row.mesh_touched_tile_count -lt 1)) {
            $failures.Add("layer_cost_query_matrix Infantry must include real active-window NavMesh query evidence")
        }
    }
    if ($Summary.hpa_macro_diagnostics.Available -ne $true) { $failures.Add("hpa_macro_diagnostics.Available must be true") }
    if ([int]$Summary.hpa_macro_diagnostics.MacroChunkColumns -ne 256 -or [int]$Summary.hpa_macro_diagnostics.MacroChunkRows -ne 256) { $failures.Add("hpa_macro_diagnostics macro grid must be 256x256") }
    if ([int]$Summary.hpa_macro_diagnostics.ExpectedAdjacencyEdgeCount -ne 130560) { $failures.Add("hpa_macro_diagnostics.ExpectedAdjacencyEdgeCount must be 130560") }
    if ([int]$Summary.hpa_macro_diagnostics.SampleRouteChunkCount -lt 1) { $failures.Add("hpa_macro_diagnostics.SampleRouteChunkCount must be >= 1") }
    if ([int]$Summary.hpa_macro_diagnostics.SamplePortalCount -lt 1) { $failures.Add("hpa_macro_diagnostics.SamplePortalCount must be >= 1") }
    if ($Summary.hpa_macro_diagnostics.UsesSyntheticMacroGridTarget -ne $false) { $failures.Add("hpa_macro_diagnostics.UsesSyntheticMacroGridTarget must be false; production evidence must come from active-window HPA portal graph diagnostics") }
    if ($Summary.hpa_graph_diagnostics.Available -ne $true) { $failures.Add("hpa_graph_diagnostics.Available must be true") }
    if ([int]$Summary.hpa_graph_diagnostics.LoadedTileCount -lt 1) { $failures.Add("hpa_graph_diagnostics.LoadedTileCount must be >= 1") }
    if ([int]$Summary.hpa_graph_diagnostics.GraphNodeCount -lt 1) { $failures.Add("hpa_graph_diagnostics.GraphNodeCount must be >= 1") }
    if ([int]$Summary.hpa_graph_diagnostics.GraphEdgeCount -lt 1) { $failures.Add("hpa_graph_diagnostics.GraphEdgeCount must be >= 1") }
    if ($Summary.hpa_graph_diagnostics.ActiveWindowRouteAvailable -ne $true) { $failures.Add("hpa_graph_diagnostics.ActiveWindowRouteAvailable must be true") }
    if ([int]$Summary.hpa_graph_diagnostics.ActiveWindowRoutePortalCount -lt 2) { $failures.Add("hpa_graph_diagnostics.ActiveWindowRoutePortalCount must be >= 2") }
    if ([int]$Summary.hpa_graph_diagnostics.ActiveWindowRouteCrossTileStepCount -lt 1) { $failures.Add("hpa_graph_diagnostics.ActiveWindowRouteCrossTileStepCount must be >= 1") }
    if ([int]$Summary.static_obstacle_world_diagnostics.PlannedWorldObstacleCount -lt 40000) { $failures.Add("static_obstacle_world_diagnostics.PlannedWorldObstacleCount must be >= 40000") }
    if ($Summary.static_obstacle_world_diagnostics.WorldDistributionReady -ne $true) { $failures.Add("static_obstacle_world_diagnostics.WorldDistributionReady must be true") }
    if ([int]$Summary.static_obstacle_world_diagnostics.MacroChunkCoverageCount -lt 40000) { $failures.Add("static_obstacle_world_diagnostics.MacroChunkCoverageCount must be >= 40000") }
    if ($Summary.static_obstacle_world_diagnostics.DataSource -ne "static_obstacle_world_asset") { $failures.Add("static_obstacle_world_diagnostics.DataSource must be static_obstacle_world_asset") }
    if ($Summary.static_obstacle_world_diagnostics.RuntimeActivationStrategy -ne "active_window_subset_to_mass_flow_solver") { $failures.Add("static_obstacle_world_diagnostics.RuntimeActivationStrategy must be active_window_subset_to_mass_flow_solver") }
    if ([int]$Summary.obstacle_diagnostics.AuthoredStaticObstacleCount -lt 40000) { $failures.Add("obstacle_diagnostics.AuthoredStaticObstacleCount must be >= 40000 from the world obstacle asset") }
    if ([int]$Summary.obstacle_diagnostics.BakedStaticObstacleCount -lt 40000) { $failures.Add("obstacle_diagnostics.BakedStaticObstacleCount must be >= 40000 from the world obstacle asset") }
    if ([int]$Summary.obstacle_diagnostics.LoadedStaticObstacleCount -lt 40000) { $failures.Add("obstacle_diagnostics.LoadedStaticObstacleCount must be >= 40000 from the world obstacle asset") }
    if ([int]$Summary.obstacle_diagnostics.SolverActiveStaticObstacleCount -gt [int]$Summary.obstacle_diagnostics.SolverStaticObstacleCapacity) { $failures.Add("obstacle solver active count cannot exceed solver capacity") }
    if ($Summary.debug_visual_diagnostics.Available -ne $true) { $failures.Add("debug_visual_diagnostics.Available must be true") }
    if ([int]$Summary.debug_visual_diagnostics.EvidenceOverlayItems -lt 8) { $failures.Add("debug_visual_diagnostics.EvidenceOverlayItems must be >= 8") }
    if ($Summary.movement_proof.proof_scope -ne "scene_smoke_sample_positions") { $failures.Add("movement_proof.proof_scope must be scene_smoke_sample_positions") }
    if ([int]$Summary.movement_proof.commanded_tN -lt 10000) { $failures.Add("movement_proof.commanded_tN must be >= 10000") }
    if ([int]$Summary.movement_proof.collision_or_avoidance_count -lt 1) { $failures.Add("movement_proof.collision_or_avoidance_count must be >= 1") }
    $summaryManifestEntry = @($Summary.evidence_manifest | Where-Object { $_.file -eq "summary.json" }) | Select-Object -First 1
    if ($null -ne $summaryManifestEntry) { $failures.Add("evidence_manifest must not hash summary.json from inside summary.json; suite-level complete_evidence_manifest owns final summary hash") }
    if (@($Summary.evidence_manifest).Count -lt 29) { $failures.Add("evidence_manifest must contain screenshot/report hashes") }
    if (@($Summary.screenshot_keyframes).Count -lt 30) { $failures.Add("screenshot_keyframes must bind use cases to keyframes") }
    if ($Summary.frame_timing.fps_measured -ne $true) { $failures.Add("frame_timing.fps_measured must be true after Raylib framebuffer smoke benchmark") }
    if ($Summary.frame_timing.fps_smoke_passed -ne $true) { $failures.Add("frame_timing.fps_smoke_passed must be true") }
    if ($Summary.frame_timing.renderer_scope -ne "raylib_framebuffer_micro_benchmark") { $failures.Add("frame_timing.renderer_scope must be raylib_framebuffer_micro_benchmark") }
    if ($Summary.frame_timing.full_game_renderer_loaded_data_measured -ne $true) { $failures.Add("frame_timing.full_game_renderer_loaded_data_measured must be true for production acceptance") }
    if ($Summary.frame_timing.fps_production_passed -ne $true) { $failures.Add("frame_timing.fps_production_passed must be true") }
    if ($Summary.frame_timing.micro_benchmark_production_threshold_passed -ne $true) { $failures.Add("frame_timing.micro_benchmark_production_threshold_passed must be true for current smoke: raylib p95<=10ms, p99<=12.5ms, overlay draw<=0.5ms") }
    if ([double]$Summary.frame_timing.raylib_frame_ms_p95 -le 0) { $failures.Add("frame_timing.raylib_frame_ms_p95 must be > 0") }
    if ([double]$Summary.frame_timing.raylib_frame_ms_p99 -le 0) { $failures.Add("frame_timing.raylib_frame_ms_p99 must be > 0") }
    if ([double]$Summary.frame_timing.raylib_frame_ms_p95 -gt 16.667) { $failures.Add("frame_timing.raylib_frame_ms_p95 must be <= 16.667ms for showcase smoke") }
    if ([double]$Summary.frame_timing.overlay_p95_delta_ms -gt 2) { $failures.Add("frame_timing.overlay_p95_delta_ms must be <= 2ms for debug overlay smoke") }
    if ($Summary.raylib_frame_benchmark.Available -ne $true) { $failures.Add("raylib_frame_benchmark.Available must be true") }
    if ($Summary.raylib_frame_benchmark.SmokePassed -ne $true) { $failures.Add("raylib_frame_benchmark.SmokePassed must be true") }
    if ($Summary.raylib_frame_benchmark.ProductionPassed -ne $true) { $failures.Add("raylib_frame_benchmark.ProductionPassed must be true") }
    if ($Summary.raylib_frame_benchmark.FullGameRendererLoadedDataMeasured -ne $true) { $failures.Add("raylib_frame_benchmark.FullGameRendererLoadedDataMeasured must be true") }

    $useCases = @($Summary.use_case_statuses)
    if ($useCases.Count -ne 16) {
        $failures.Add("use_case_statuses must contain exactly 16 cases")
    }

    foreach ($id in @("U1","U2","U3","U4","U5","U6","U7","U8","U9","U10","U11","U12","U13","U14","U15","U16")) {
        $case = @($useCases | Where-Object { $_.id -eq $id }) | Select-Object -First 1
        if ($null -eq $case) {
            $failures.Add("use_case_statuses missing $id")
        }
        elseif ([string]::IsNullOrWhiteSpace($case.evidence) -or [string]::IsNullOrWhiteSpace($case.acceptance_proof)) {
            $failures.Add("use_case_statuses $id must include evidence and acceptance_proof")
        }
        elseif ([string]::IsNullOrWhiteSpace($case.player_story_status) -or @($case.player_visible_evidence_files).Count -lt 1) {
            $failures.Add("use_case_statuses $id must include player_story_status and player_visible_evidence_files")
        }
        elseif ($case.production_status -eq "PASS" -and $Summary.manual_uat_accepted -ne $true) {
            $failures.Add("use_case_statuses $id must not report production_status PASS without manual_uat_accepted=true")
        }
        elseif ($case.production_status -notin @("PASS", "NEEDS_MANUAL_UAT", "BLOCKED")) {
            $failures.Add("use_case_statuses $id production_status must be PASS, NEEDS_MANUAL_UAT, or BLOCKED")
        }
    }

    if ($Summary.machine_production_evidence_success -ne $true) { $failures.Add("machine_production_evidence_success must be true before human UAT") }
    if ($Summary.manual_uat_accepted -ne $true -and [string]::IsNullOrWhiteSpace([string]$Summary.manual_uat_blocker)) { $failures.Add("manual_uat_blocker must explain missing human UAT") }
    if ($Summary.production_gate_success -eq $true -and $Summary.manual_uat_accepted -ne $true) { $failures.Add("production_gate_success cannot be true without manual_uat_accepted=true") }

    $u13 = @($useCases | Where-Object { $_.id -eq "U13" }) | Select-Object -First 1
    if ($null -ne $u13 -and $u13.showcase_status -ne "SMOKE") {
        $failures.Add("U13 showcase_status must be SMOKE for deterministic 40k world distribution smoke")
    }

    $u3 = @($useCases | Where-Object { $_.id -eq "U3" }) | Select-Object -First 1
    if ($null -ne $u3 -and $u3.showcase_status -ne "SMOKE") {
        $failures.Add("U3 showcase_status must be SMOKE for logic-heightmap sampled layer/area validator smoke")
    }

    $u5 = @($useCases | Where-Object { $_.id -eq "U5" }) | Select-Object -First 1
    if ($null -ne $u5 -and $u5.showcase_status -ne "SMOKE") {
        $failures.Add("U5 showcase_status must be SMOKE for HPA macro diagnostics smoke")
    }

    $u9 = @($useCases | Where-Object { $_.id -eq "U9" }) | Select-Object -First 1
    if ($null -ne $u9 -and $u9.showcase_status -ne "SMOKE") {
        $failures.Add("U9 showcase_status must be SMOKE for layer/cost query matrix plus active-window mesh smoke")
    }

    $u14 = @($useCases | Where-Object { $_.id -eq "U14" }) | Select-Object -First 1
    if ($null -ne $u14 -and $u14.showcase_status -ne "SMOKE") {
        $failures.Add("U14 showcase_status must be SMOKE after Raylib framebuffer benchmark")
    }

    if ($failures.Count -gt 0) {
        throw "MassNavigation large-world summary assertions failed: $($failures -join '; ')"
    }
}

while ($true) {
    if ($Iterations -gt 0 -and $runIndex -ge $Iterations) {
        break
    }

    if ($null -ne $deadline -and (Get-Date) -ge $deadline) {
        break
    }

    $runIndex++
    $runDir = Join-Path $OutputRoot ("run-{0:0000}" -f $runIndex)
    New-Item -ItemType Directory -Force -Path $runDir | Out-Null
    $logPath = Join-Path $runDir "run.log"
    $startedAt = Get-Date

    $argsList = @("cli", "launch", "mass_navigation", "--adapter", $Adapter)
    if (-not [string]::IsNullOrWhiteSpace($Build)) {
        $argsList += @("--build", $Build)
    }

    $argsList += @("--record", $runDir)

    Push-Location $repoRoot
    try {
        $output = & $launcher @argsList 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    $endedAt = Get-Date
    $output | Set-Content -Path $logPath -Encoding UTF8
    $summaryFile = Join-Path $runDir "summary.json"
    $summary = $null
    $success = $false
    if ($exitCode -eq 0 -and (Test-Path $summaryFile)) {
        $summary = Get-Content -Path $summaryFile -Raw -Encoding UTF8 | ConvertFrom-Json
        Assert-MassNavigationLargeWorldSummary -Summary $summary
        $success = [bool]$summary.scene_smoke_success
        if ([int]$summary.full_selection_agents -lt 10000 -or
            [int]$summary.full_selection_target_slots -lt 10000 -or
            [int]$summary.commanded_agents -lt 10000) {
            $success = $false
        }
    }

    $requiredEvidence = @(
        "battle-report.md",
        "trace.jsonl",
        "path.mmd",
        "summary.json",
        "visible-checklist.md",
        "screens\000_boot.png",
        "screens\001_selection_order.png",
        "screens\002_remote_minimap_jump.png",
        "screens\003_return_original_area.png",
        "screens\004_bake_hpa_overlay.png",
        "screens\005_path_strategy_inspector.png",
        "screens\006_order_reuse_target_allocation.png",
        "screens\007_10k_commanded_flow_probe.png",
        "screens\008_acceptance_gate_matrix.png",
        "screens\009_raylib_frame_benchmark.png",
        "screens\010_path_only_pick_before.png",
        "screens\011_path_only_pick_result_no_order.png",
        "screens\012_path_only_unreachable_failure.png",
        "screens\013_hpa_active_window_portal_route.png",
        "screens\014_graph_navmesh_hybrid_same_query_compare.png",
        "screens\015_layer_cost_ground_water_air_mountain_compare.png",
        "screens\016_noflyzone_blocked_query.png",
        "screens\017_order_reuse_first_order.png",
        "screens\018_order_reuse_same_point_cache_hit.png",
        "screens\019_order_reuse_near_point_cache_hit.png",
        "screens\020_target_allocation_10k_slots_zoom.png",
        "screens\021_10k_move_t0.png",
        "screens\022_10k_move_tN_avoidance.png",
        "screens\023_10k_arrival_or_stuck_breakdown.png",
        "screens\024_40k_obstacle_distribution_gap.png",
        "screens\025_raylib_micro_fps_debug_off.png",
        "screens\026_raylib_micro_fps_debug_on.png",
        "screens\027_navmesh_failure_drilldown_tile.png",
        "screens\028_bake_tool_interactive_query.png",
        "screens\029_waypoint_edit_before.png",
        "screens\030_waypoint_edit_after_pathpoints_regenerated.png",
        "screens\raylib-frame-benchmark.json",
        "screens\timeline.png"
    )
    $missingEvidence = @(
        $requiredEvidence |
            Where-Object { -not (Test-Path (Join-Path $runDir $_)) }
    )
    if ($missingEvidence.Count -gt 0) {
        $success = $false
    }

    $row = [pscustomobject]@{
        run = $runIndex
        success = $success
        scene_smoke_success = if ($summary) { $summary.scene_smoke_success } else { $false }
        machine_production_evidence_success = if ($summary) { $summary.machine_production_evidence_success } else { $false }
        manual_uat_accepted = if ($summary) { $summary.manual_uat_accepted } else { $false }
        manual_uat_blocker = if ($summary) { $summary.manual_uat_blocker } else { "missing summary.json or launcher failure" }
        production_gate_success = if ($summary) { $summary.production_gate_success } else { $false }
        exit_code = $exitCode
        started_at = $startedAt.ToString("o")
        ended_at = $endedAt.ToString("o")
        duration_seconds = [math]::Round(($endedAt - $startedAt).TotalSeconds, 3)
        output_dir = $runDir
        battle_report = (Join-Path $runDir "battle-report.md")
        trace = (Join-Path $runDir "trace.jsonl")
        summary = $summaryFile
        timeline = (Join-Path $runDir "screens\timeline.png")
        normalized_signature = if ($summary) { $summary.normalized_signature } else { $null }
        world_width_cm = if ($summary) { $summary.world_width_cm } else { $null }
        world_height_cm = if ($summary) { $summary.world_height_cm } else { $null }
        loaded_chunk_count = if ($summary) { $summary.loaded_chunk_count } else { $null }
        boundary_click_result = if ($summary) { $summary.boundary_click_result } else { $null }
        ground_picking_result = if ($summary) { $summary.ground_picking_result } else { $null }
        agent_count = if ($summary) { $summary.agent_count } else { $null }
        commanded_agents = if ($summary) { $summary.commanded_agents } else { $null }
        moving_agents = if ($summary) { $summary.moving_agents } else { $null }
        settled_agents = if ($summary) { $summary.settled_agents } else { $null }
        full_selection_agents = if ($summary) { $summary.full_selection_agents } else { $null }
        full_selection_target_slots = if ($summary) { $summary.full_selection_target_slots } else { $null }
        full_selection_blocked_slots = if ($summary) { $summary.full_selection_blocked_slots } else { $null }
        full_selection_fallback_slots = if ($summary) { $summary.full_selection_fallback_slots } else { $null }
        order_reuse_cache_hit = if ($summary) { $summary.order_reuse.CacheHit } else { $null }
        order_reuse_fanout = if ($summary) { $summary.order_reuse.FanoutCount } else { $null }
        order_reuse_route_id = if ($summary) { $summary.order_reuse.ReusedRouteId } else { $null }
        order_reuse_cache_size = if ($summary) { $summary.order_reuse.RouteCacheSize } else { $null }
        order_reuse_scope = if ($summary) { $summary.order_reuse.ReuseScope } else { $null }
        order_reuse_path_signature = if ($summary) { $summary.order_reuse.PathRouteSignature } else { $null }
        order_reuse_mesh_signature = if ($summary) { $summary.order_reuse.MeshRouteSignature } else { $null }
        flow_enabled = if ($summary) { $summary.flow_enabled } else { $null }
        team_count = if ($summary) { $summary.team_count } else { $null }
        performer_active_count = if ($summary) { $summary.performer_active_count } else { $null }
        minimap_marker_count = if ($summary) { $summary.minimap_marker_count } else { $null }
        minimap_dropped_total = if ($summary) { $summary.minimap_dropped_total } else { $null }
        bake_data_bound = if ($summary) { $summary.bake_data_bound } else { $null }
        macro_chunk_columns = if ($summary) { $summary.macro_chunk_columns } else { $null }
        macro_chunk_rows = if ($summary) { $summary.macro_chunk_rows } else { $null }
        navmesh_baked_tiles = if ($summary) { $summary.navmesh_baked_tiles } else { $null }
        navmesh_not_loaded_tiles = if ($summary) { $summary.navmesh_not_loaded_tiles } else { $null }
        navmesh_total_tiles = if ($summary) { $summary.navmesh_total_tiles } else { $null }
        navmesh_coverage_percent = if ($summary) { $summary.navmesh_coverage_percent } else { $null }
        authored_static_obstacle_count = if ($summary) { $summary.authored_static_obstacle_count } else { $null }
        target_static_obstacle_count = if ($summary) { $summary.target_static_obstacle_count } else { $null }
        baked_static_obstacle_count = if ($summary) { $summary.baked_static_obstacle_count } else { $null }
        loaded_static_obstacle_count = if ($summary) { $summary.loaded_static_obstacle_count } else { $null }
        solver_active_static_obstacle_count = if ($summary) { $summary.solver_active_static_obstacle_count } else { $null }
        solver_static_obstacle_capacity = if ($summary) { $summary.solver_static_obstacle_capacity } else { $null }
        fps_measured = if ($summary) { $summary.frame_timing.fps_measured } else { $null }
        raylib_frame_ms_p95 = if ($summary) { $summary.frame_timing.raylib_frame_ms_p95 } else { $null }
        raylib_frame_ms_p99 = if ($summary) { $summary.frame_timing.raylib_frame_ms_p99 } else { $null }
        raylib_fps_p95 = if ($summary) { $summary.frame_timing.raylib_fps_p95 } else { $null }
        overlay_p95_delta_ms = if ($summary) { $summary.frame_timing.overlay_p95_delta_ms } else { $null }
        fps_delta_percent = if ($summary) { $summary.frame_timing.fps_delta_percent } else { $null }
        first_command_advance_cm = if ($summary) { $summary.first_command_advance_cm } else { $null }
        second_command_advance_cm = if ($summary) { $summary.second_command_advance_cm } else { $null }
        empty_world_command_advance_cm = if ($summary) { $summary.empty_world_command_advance_cm } else { $null }
        edge_inside_command_advance_cm = if ($summary) { $summary.edge_inside_command_advance_cm } else { $null }
        multi_team_command_advance_cm = if ($summary) { $summary.multi_team_command_advance_cm } else { $null }
        multi_team_min_advance_cm = if ($summary) { $summary.multi_team_min_advance_cm } else { $null }
        edge_inside_min_advance_cm = if ($summary) { $summary.edge_inside_min_advance_cm } else { $null }
        final_loaded_chunks = if ($summary) { $summary.final_loaded_chunks } else { $null }
        final_rejects = if ($summary) { $summary.final_rejects } else { $null }
        boundary_rejects_added = if ($summary) { $summary.boundary_rejects_added } else { $null }
        steady_managed_growth_bytes = if ($summary) { $summary.steady_managed_growth_bytes } else { $null }
        steady_allocated_bytes = if ($summary) { $summary.steady_allocated_bytes } else { $null }
        missing_evidence = $missingEvidence
        scene_smoke_failed_checks = if ($summary) { @($summary.scene_smoke_failed_checks) + @($missingEvidence | ForEach-Object { "missing evidence: $_" }) } else { @("missing summary.json or launcher failure") + @($missingEvidence | ForEach-Object { "missing evidence: $_" }) }
        production_gate_failed_checks = if ($summary) { @($summary.production_gate_failed_checks) } else { @("missing summary.json or launcher failure") }
        failed_checks = if ($summary) { @($summary.failed_checks) + @($missingEvidence | ForEach-Object { "missing evidence: $_" }) } else { @("missing summary.json or launcher failure") + @($missingEvidence | ForEach-Object { "missing evidence: $_" }) }
        log = $logPath
    }

    $runs.Add($row)
    if (-not $success) {
        $hadFailure = $true
    }

    Add-Content -Path $summaryPath -Value (ConvertTo-SafeJsonLine $row) -Encoding UTF8
    Write-SoakReport -RunRows $runs -Path $reportPath -Root $OutputRoot -DeadlineValue $deadline

    if (-not $success -and $StopOnFailure) {
        throw "MassNavigation UAT soak failed at run $runIndex. See $runDir"
    }
}

Write-SoakReport -RunRows $runs -Path $reportPath -Root $OutputRoot -DeadlineValue $deadline
if ($hadFailure -and -not $AllowFailures) {
    throw "MassNavigation UAT completed with failed run(s). See $OutputRoot. Use -AllowFailures only for exploratory soak runs."
}

Write-Host "MassNavigation UAT soak complete."
Write-Host "output=$OutputRoot"
Write-Host "report=$reportPath"
Write-Host "summary=$summaryPath"
