param(
    [string]$OutputRoot = "",
    [int]$WidthChunks = 8,
    [int]$HeightChunks = 8,
    [string]$Preset = "mountainRiver",
    [ValidateSet("raylib", "web")]
    [string]$Adapter = "raylib",
    [switch]$StopOnFailure
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\acceptance\mass-navigation-showcase-current"
}
else {
    $OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
}

$navBakeScript = Join-Path $PSScriptRoot "run-navmesh-bake-raylib-acceptance.ps1"
$largeWorldScript = Join-Path $PSScriptRoot "run-mass-navigation-large-world-uat.ps1"
if (-not (Test-Path $navBakeScript)) { throw "NavMesh bake acceptance script not found: $navBakeScript" }
if (-not (Test-Path $largeWorldScript)) { throw "MassNavigation large-world UAT script not found: $largeWorldScript" }

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$reportPath = Join-Path $OutputRoot "mass-navigation-showcase-acceptance-report.md"
$summaryPath = Join-Path $OutputRoot "mass-navigation-showcase-acceptance-summary.json"
$summaryAliasPath = Join-Path $OutputRoot "summary.json"

function Read-JsonFile {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        throw "Expected JSON file not found: $Path"
    }

    return Get-Content -Path $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Get-ShowcaseEvidenceHash {
    param([string]$Path)

    return (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToLowerInvariant()
}

function Normalize-ShowcaseEvidencePath {
    param([string]$Path)

    return $Path.Replace("\", "/").TrimStart("/")
}

function Resolve-ShowcaseEvidencePath {
    param(
        [string]$SuiteRoot,
        [string]$RunRoot,
        [string]$RelativePath
    )

    $normalized = Normalize-ShowcaseEvidencePath -Path $RelativePath
    if ([System.IO.Path]::IsPathRooted($normalized)) {
        return $normalized
    }

    $local = $normalized.Replace("/", [System.IO.Path]::DirectorySeparatorChar)
    $runPath = Join-Path $RunRoot $local
    if (Test-Path $runPath) {
        return $runPath
    }

    $suitePath = Join-Path $SuiteRoot $local
    if (Test-Path $suitePath) {
        return $suitePath
    }

    return $runPath
}

function Assert-UseCaseEvidenceFiles {
    param(
        [object]$Summary,
        [string]$SuiteRoot,
        [string]$RunRoot
    )

    $manifest = @($Summary.evidence_manifest)
    foreach ($case in @($Summary.use_case_statuses)) {
        foreach ($file in @($case.player_visible_evidence_files)) {
            $normalized = Normalize-ShowcaseEvidencePath -Path ([string]$file)
            $resolved = Resolve-ShowcaseEvidencePath -SuiteRoot $SuiteRoot -RunRoot $RunRoot -RelativePath $normalized
            if (-not (Test-Path $resolved)) {
                throw "Use case $($case.id) evidence file does not exist: $normalized resolved=$resolved"
            }

            $entry = @($manifest | Where-Object { (Normalize-ShowcaseEvidencePath -Path ([string]$_.file)) -eq $normalized }) | Select-Object -First 1
            if ($null -eq $entry) {
                throw "Use case $($case.id) evidence file is missing from evidence_manifest: $normalized"
            }

            if ($entry.PSObject.Properties.Name -contains "exists" -and $entry.exists -ne $true) {
                throw "Use case $($case.id) evidence manifest marks file missing: $normalized"
            }

            if ([string]::IsNullOrWhiteSpace([string]$entry.sha256)) {
                throw "Use case $($case.id) evidence manifest is missing sha256: $normalized"
            }
        }
    }
}

function New-ShowcaseEvidenceEntry {
    param(
        [string]$SuiteRoot,
        [string]$RunRoot,
        [string]$RelativePath,
        [string]$EvidenceKind,
        [string]$UseCaseId,
        [string]$Description
    )

    $normalized = Normalize-ShowcaseEvidencePath -Path $RelativePath
    $resolved = Resolve-ShowcaseEvidencePath -SuiteRoot $SuiteRoot -RunRoot $RunRoot -RelativePath $normalized
    if (-not (Test-Path $resolved)) {
        throw "Showcase evidence file does not exist: $normalized resolved=$resolved"
    }

    return [pscustomobject]@{
        file = $normalized
        kind = $EvidenceKind
        use_case_id = $UseCaseId
        description = $Description
        absolute_path = $resolved
        bytes = (Get-Item $resolved).Length
        sha256 = Get-ShowcaseEvidenceHash -Path $resolved
    }
}

function Build-ShowcaseEvidenceManifest {
    param(
        [object[]]$BakeRows,
        [object]$LargeSummary,
        [string]$SuiteRoot,
        [string]$RunRoot
    )

    $entries = New-Object System.Collections.Generic.List[object]
    $largeWorldKeyframes = @(
        @("screens/000_boot.png", "U11", "Boot overview"),
        @("screens/001_selection_order.png", "U12", "Initial selection order"),
        @("screens/002_remote_minimap_jump.png", "U11", "Remote minimap jump"),
        @("screens/003_return_original_area.png", "U11", "Return to original area"),
        @("screens/004_bake_hpa_overlay.png", "U5/U11", "Bake and HPA overlay"),
        @("screens/005_path_strategy_inspector.png", "U6", "Path strategy inspector"),
        @("screens/006_order_reuse_target_allocation.png", "U7/U8", "Reuse and target allocation overview"),
        @("screens/006a_runtime_u1_visual_heightmap_bake.png", "U1", "Playable runtime guide: VisualHeightmap bake"),
        @("screens/006b_runtime_u2_logic_heightmap_bake.png", "U2", "Playable runtime guide: LogicHeightmap unification"),
        @("screens/006c_runtime_u3_layer_area_editor.png", "U3", "Playable runtime guide: layer area editor"),
        @("screens/006d_runtime_u4_path_only.png", "U4", "Playable runtime guide: path-only query"),
        @("screens/006e_runtime_u5_world_hpa.png", "U5", "Playable runtime guide: world HPA"),
        @("screens/006f_runtime_u6_strategy_switch.png", "U6", "Playable runtime guide: strategy switch"),
        @("screens/006g_runtime_u7_order_reuse.png", "U7", "Playable runtime guide: order reuse"),
        @("screens/006h_runtime_u8_target_allocation.png", "U8", "Playable runtime guide: target allocation"),
        @("screens/006i_runtime_u9_layer_costs.png", "U9", "Playable runtime guide: layer costs"),
        @("screens/006j_runtime_u10_waypoint_authoring.png", "U10", "Playable runtime guide: waypoint authoring"),
        @("screens/006k_runtime_u11_large_world.png", "U11", "Playable runtime guide: large world"),
        @("screens/006l_runtime_u12_10k_flow.png", "U12", "Playable runtime guide: 10k flow"),
        @("screens/006m_runtime_u13_static_obstacles.png", "U13", "Playable runtime guide: static obstacles"),
        @("screens/006n_runtime_u14_fps_scope.png", "U14", "Playable runtime guide: FPS scope"),
        @("screens/006o_runtime_u15_debug_budget.png", "U15", "Playable runtime guide: debug budget"),
        @("screens/006p_runtime_u16_bake_tool.png", "U16", "Playable runtime guide: bake tool"),
        @("screens/007_10k_commanded_flow_probe.png", "U12", "10k commanded flow operation evidence"),
        @("screens/008_acceptance_gate_matrix.png", "U1-U16", "Acceptance gate matrix"),
        @("screens/009_raylib_frame_benchmark.png", "U14/U15", "Raylib framebuffer benchmark"),
        @("screens/010_path_only_pick_before.png", "U4", "Path-only pick before query"),
        @("screens/011_path_only_pick_result_no_order.png", "U4", "Path-only highlighted result"),
        @("screens/012_path_only_unreachable_failure.png", "U4/U9", "Path-only failure drilldown"),
        @("screens/013_hpa_active_window_portal_route.png", "U5/U11", "HPA active-window portal route"),
        @("screens/014_graph_navmesh_hybrid_same_query_compare.png", "U6", "Graph/NavMesh/Hybrid comparison"),
        @("screens/015_layer_cost_ground_water_air_mountain_compare.png", "U3/U9", "Layer cost comparison"),
        @("screens/016_noflyzone_blocked_query.png", "U9", "NoFlyZone blocked query"),
        @("screens/017_order_reuse_first_order.png", "U7", "Order reuse first order"),
        @("screens/018_order_reuse_same_point_cache_hit.png", "U7", "Same-point cache hit"),
        @("screens/019_order_reuse_near_point_cache_hit.png", "U7", "Near-point cache hit"),
        @("screens/020_target_allocation_10k_slots_zoom.png", "U8", "10k target allocation slots"),
        @("screens/021_10k_move_t0.png", "U12", "10k move t0"),
        @("screens/022_10k_move_tN_avoidance.png", "U12", "10k move avoidance"),
        @("screens/023_10k_arrival_or_stuck_breakdown.png", "U12", "Arrival or stuck breakdown"),
        @("screens/024_40k_obstacle_distribution_gap.png", "U13", "40k obstacle distribution"),
        @("screens/025_raylib_micro_fps_debug_off.png", "U14/U15", "Raylib micro FPS debug off"),
        @("screens/026_raylib_micro_fps_debug_on.png", "U14/U15", "Raylib micro FPS debug on"),
        @("screens/027_navmesh_failure_drilldown_tile.png", "U1/U11/U16", "NavMesh failure drilldown tile"),
        @("screens/028_bake_tool_interactive_query.png", "U16", "Bake tool interactive query"),
        @("screens/029_waypoint_edit_before.png", "U10", "Waypoint edit before"),
        @("screens/030_waypoint_edit_after_pathpoints_regenerated.png", "U10", "Waypoint edit after pathpoints regenerated")
    )
    foreach ($frame in $largeWorldKeyframes) {
        $entries.Add((New-ShowcaseEvidenceEntry -SuiteRoot $SuiteRoot -RunRoot $RunRoot -RelativePath $frame[0] -EvidenceKind "large_world_step_keyframe" -UseCaseId $frame[1] -Description $frame[2]))
    }

    foreach ($frame in @($LargeSummary.screenshot_keyframes)) {
        $entries.Add((New-ShowcaseEvidenceEntry -SuiteRoot $SuiteRoot -RunRoot $RunRoot -RelativePath ([string]$frame.file) -EvidenceKind "large_world_keyframe" -UseCaseId ([string]$frame.use_case_id) -Description ([string]$frame.use_case_name)))
    }

    $runtimeFiles = @(
        @("battle-report.md", "large_world_report", "", "Large-world battle report"),
        @("summary.json", "large_world_summary", "", "Large-world machine summary"),
        @("visible-checklist.md", "large_world_checklist", "", "Visible UAT checklist"),
        @("trace.jsonl", "large_world_trace", "", "Large-world trace events"),
        @("path.mmd", "large_world_path_diagram", "", "Navigation architecture path diagram"),
        @("screens/timeline.png", "large_world_timeline", "", "Timeline keyframe sheet"),
        @("screens/raylib-frame-benchmark.json", "large_world_benchmark_json", "U14/U15", "Raylib framebuffer benchmark data")
    )
    foreach ($item in $runtimeFiles) {
        $entries.Add((New-ShowcaseEvidenceEntry -SuiteRoot $SuiteRoot -RunRoot $RunRoot -RelativePath $item[0] -EvidenceKind $item[1] -UseCaseId $item[2] -Description $item[3]))
    }

    foreach ($row in $BakeRows) {
        foreach ($screen in @("001_navmesh_bake_coverage.png", "002_navmesh_tile_detail.png", "003_path_only_query.png", "004_hpa_macro_overlay.png", "005_layer_area_editor.png", "nav-bake-raylib-result.json")) {
            $relative = "$($row.name)/screens/$screen"
            $entries.Add((New-ShowcaseEvidenceEntry -SuiteRoot $SuiteRoot -RunRoot $RunRoot -RelativePath $relative -EvidenceKind "navmesh_bake_viewer" -UseCaseId "U1/U2/U3/U16" -Description "$($row.name) $screen"))
        }
    }

    return @($entries | Sort-Object file,kind,use_case_id)
}

function Assert-NavBakeArtifact {
    param(
        [string]$Name,
        [string]$Root,
        [string]$ExpectedOrigin,
        [int]$ExpectedTiles
    )

    $resultPath = Join-Path $Root "screens\nav-bake-raylib-result.json"
    $result = Read-JsonFile -Path $resultPath
    $requiredScreens = @(
        "001_navmesh_bake_coverage.png",
        "002_navmesh_tile_detail.png",
        "003_path_only_query.png",
        "004_hpa_macro_overlay.png",
        "005_layer_area_editor.png"
    )

    $missing = @($requiredScreens | Where-Object { -not (Test-Path (Join-Path $Root "screens\$_")) })
    if ($missing.Count -gt 0) {
        throw "$Name missing Raylib bake screenshots: $($missing -join ', ')"
    }

    if ($result.success -ne $true) { throw "$Name result.success must be true" }
    if ($result.sourceKind -ne "lhtm") { throw "$Name sourceKind must be lhtm, got $($result.sourceKind)" }
    if ($result.sourceOriginKind -ne $ExpectedOrigin) { throw "$Name sourceOriginKind must be $ExpectedOrigin, got $($result.sourceOriginKind)" }
    if ([int]$result.totalExpectedTileBakes -ne $ExpectedTiles) { throw "$Name expected tile count mismatch" }
    if ([int]$result.totalBakedTiles -ne $ExpectedTiles) { throw "$Name baked tile count mismatch" }
    if ([int]$result.totalFailedTiles -ne 0) { throw "$Name failed tiles must be 0" }
    if ([double]$result.coveragePercent -ne 100) { throw "$Name coverage must be 100" }
    if ($result.pathStatus -ne "Ok") { throw "$Name pathStatus must be Ok" }
    if ($result.layerEditorSource -ne "logic_heightmap_sampled_view") { throw "$Name layer editor must read LogicHeightmap semantics" }
    if ($result.logicSemanticAvailable -ne $true) { throw "$Name logic semantic data must be available" }
    if ($result.logicSemanticHasMountainRiverSignals -ne $true) { throw "$Name mountain/river logic signals must be visible" }

    return [pscustomobject]@{
        name = $Name
        root = $Root
        success = [bool]$result.success
        source_kind = $result.sourceKind
        source_origin_kind = $result.sourceOriginKind
        total_baked_tiles = [int]$result.totalBakedTiles
        total_expected_tile_bakes = [int]$result.totalExpectedTileBakes
        coverage_percent = [double]$result.coveragePercent
        path_status = $result.pathStatus
        layer_editor_source = $result.layerEditorSource
        distinct_area_count = [int]$result.logicSemanticDistinctAreaCount
        water_like_cells = [int]$result.logicSemanticWaterLikeCells
        height_range_cm = [int]$result.logicSemanticHeightRangeCm
    }
}

function Assert-LargeWorldArtifact {
    param(
        [string]$RunRoot,
        [string]$SuiteRoot
    )

    $summary = Read-JsonFile -Path (Join-Path $RunRoot "summary.json")

    if ($summary.scene_smoke_success -ne $true) { throw "Large-world scene_smoke_success must be true" }
    if (@($summary.use_case_statuses).Count -ne 16) { throw "Large-world use_case_statuses must contain U1-U16" }
    if ($summary.playable_guided_showcase.runtime_overlay_sampled -ne $true) { throw "Playable guided showcase must sample runtime overlays" }
    $requiredUseCases = @("U1","U2","U3","U4","U5","U6","U7","U8","U9","U10","U11","U12","U13","U14","U15","U16")
    $sampledUseCases = @($summary.playable_guided_showcase.sampled_use_cases)
    $missingUseCases = @($summary.playable_guided_showcase.missing_use_cases)
    if ($missingUseCases.Count -ne 0) { throw "Playable guided showcase missing use cases: $($missingUseCases -join ', ')" }
    foreach ($id in $requiredUseCases) {
        if (-not ($sampledUseCases -contains $id)) {
            throw "Playable guided showcase sampled_use_cases missing $id"
        }
    }
    $operationEvidenceSamples = @($summary.runtime_guide_keyframes)
    if ($operationEvidenceSamples.Count -gt 0) {
        foreach ($sample in $operationEvidenceSamples) {
            if ([string]::IsNullOrWhiteSpace([string]$sample.file)) { throw "Operation evidence sample missing file" }
            $samplePath = Resolve-ShowcaseEvidencePath -SuiteRoot $SuiteRoot -RunRoot $RunRoot -RelativePath ([string]$sample.file)
            if (-not (Test-Path $samplePath)) { throw "Operation evidence sample file missing: $($sample.file)" }
        }
    }
    if ($summary.frame_timing.renderer_scope -ne "raylib_framebuffer_micro_benchmark") { throw "Renderer scope must be raylib_framebuffer_micro_benchmark" }
    if ($summary.frame_timing.fps_production_passed -ne $true) { throw "FPS production gate must be true" }
    if ($summary.frame_timing.full_game_renderer_loaded_data_measured -ne $true) { throw "Full renderer loaded-data flag must be true" }
    if ($summary.path_only_query.NoOrderSubmitted -ne $true) { throw "Path-only query must not submit orders" }
    if ($summary.path_only_query.PreviewMode -ne "path_preview") { throw "Path-only query must expose path_preview mode" }
    if ($summary.path_only_query.InputContract -ne "pick_start_world_point_then_goal_world_point") { throw "Path-only query must expose the start/goal point-pick input contract" }
    if ($summary.path_only_query.RoutePreviewState -ne "highlighted_route_ready") { throw "Path-only query must expose a highlighted route-ready state" }
    if ($summary.path_only_query.HighlightRouteVisible -ne $true) { throw "Path-only query must mark the highlighted route visible" }
    if ($summary.path_only_query.PathPointContract -ne "immutable_query_result") { throw "Path-only query must mark pathpoints as immutable query results" }
    if ($summary.path_only_query.WaypointContract -ne "editable_order_intent") { throw "Path-only query must mark waypoints as editable order intent" }
    if ([int]$summary.target_allocation.SlotCount -lt 10000) { throw "Target allocation must produce at least 10000 slots" }
    if ([int]$summary.target_allocation.reachable_slot_count -lt 10000) { throw "Target allocation must expose at least 10000 reachable slots" }
    if ($summary.target_allocation.ReachabilityProbeStatus -ne "Ok") { throw "Target allocation reachability probe must be Ok" }
    $targetReachabilitySource = [string]$summary.target_allocation.ReachabilitySource
    if (-not ($targetReachabilitySource.Contains("path_only_route_reachability_smoke") -or $targetReachabilitySource.Contains("active_window_navmesh_query"))) {
        throw "Target allocation reachability source must cite path_only_route_reachability_smoke or active_window_navmesh_query"
    }
    if ([int]$summary.target_allocation.AllocationRouteId -le 0) { throw "Target allocation must carry AllocationRouteId" }
    if ([string]::IsNullOrWhiteSpace([string]$summary.target_allocation.AllocationRouteReuseKey)) { throw "Target allocation must carry AllocationRouteReuseKey" }
    if ([string]::IsNullOrWhiteSpace([string]$summary.order_reuse.ReuseScope)) { throw "Order reuse must carry ReuseScope" }
    if ([string]$summary.order_reuse.PathRouteSignature -eq "not_available" -or [string]::IsNullOrWhiteSpace([string]$summary.order_reuse.PathRouteSignature)) { throw "Order reuse must carry PathRouteSignature" }
    if ([string]$summary.order_reuse.MeshRouteSignature -eq "not_available" -or [string]::IsNullOrWhiteSpace([string]$summary.order_reuse.MeshRouteSignature)) { throw "Order reuse must carry MeshRouteSignature" }
    if ([int]$summary.static_obstacle_world_diagnostics.PlannedWorldObstacleCount -lt 40000) { throw "40k obstacle world distribution smoke missing" }
    if ($summary.static_obstacle_world_diagnostics.DataSource -ne "static_obstacle_world_asset") { throw "40k obstacle world asset smoke missing" }
    if ([int]$summary.obstacle_diagnostics.AuthoredStaticObstacleCount -lt 40000) { throw "40k obstacle authored world asset count missing" }
    if ([int]$summary.obstacle_diagnostics.BakedStaticObstacleCount -lt 40000) { throw "40k obstacle baked world asset count missing" }
    if ([int]$summary.obstacle_diagnostics.LoadedStaticObstacleCount -lt 40000) { throw "40k obstacle loaded world asset count missing" }
    if ([int]$summary.navmesh_baked_tiles -lt 1) { throw "Large-world active-window NavMesh bake/load smoke must load real tiles" }
    $expectedNavMeshTotalTiles = [int]$summary.macro_chunk_count * [int]$summary.navmesh_layer_count * [int]$summary.navmesh_profile_count
    if ([int]$summary.navmesh_total_tiles -ne $expectedNavMeshTotalTiles) { throw "Large-world NavMesh total tiles must equal macro_chunk_count * navmesh_layer_count * navmesh_profile_count" }
    if ([int]$summary.loaded_chunk_count -lt 1) { throw "S1 loaded_chunk_count must be present and positive" }
    if ($summary.boundary_click_result -ne "inside_edge_accepted_outside_edge_clamped") { throw "S1 boundary_click_result must prove inside edge accepted and outside edge clamped" }
    if ($summary.ground_picking_result -ne "inside_ground_pick_accepted_outside_ground_pick_clamped") { throw "S1 ground_picking_result must prove inside ground pick accepted and outside ground pick clamped" }
    if ($summary.world_boundary_diagnostics.Available -ne $true) { throw "S1 world boundary diagnostics must be available" }
    if ($summary.world_boundary_diagnostics.CameraInBounds -ne $true) { throw "S1 camera target must remain inside world bounds" }
    if ($summary.world_boundary_diagnostics.MinimapBoundaryClickInBounds -ne $true -or $summary.world_boundary_diagnostics.MinimapBoundaryClickClamped -ne $true) { throw "S1 minimap boundary click probe must resolve in bounds and clamp" }
    if ($summary.world_boundary_diagnostics.GroundPickingInsideAccepted -ne $true -or $summary.world_boundary_diagnostics.GroundPickingOutsideClamped -ne $true) { throw "S1 ground picking probe must accept inside point and clamp outside point" }
    if ([int]$summary.navmesh_not_loaded_tiles -ne ([int]$summary.navmesh_total_tiles - [int]$summary.navmesh_baked_tiles)) {
        throw "Large-world NavMesh notLoaded must equal total-baked for the multi-layer active-window smoke"
    }
    if ($summary.hpa_graph_diagnostics.Available -ne $true) { throw "HPA active-window graph diagnostics must be available" }
    if ([int]$summary.hpa_graph_diagnostics.LoadedTileCount -lt 1) { throw "HPA active-window graph diagnostics must load real NavTiles" }
    if ([int]$summary.hpa_graph_diagnostics.GraphNodeCount -lt 1) { throw "HPA active-window graph diagnostics must expose portal graph nodes" }
    if ([int]$summary.hpa_graph_diagnostics.GraphEdgeCount -lt 1) { throw "HPA active-window graph diagnostics must expose graph edges" }
    if ($summary.hpa_graph_diagnostics.ActiveWindowRouteAvailable -ne $true) { throw "HPA active-window graph diagnostics must expose a real portal route sample" }
    if ([int]$summary.hpa_graph_diagnostics.ActiveWindowRoutePortalCount -lt 2) { throw "HPA active-window route sample must include at least two portals" }
    if ([int]$summary.hpa_graph_diagnostics.ActiveWindowRouteCrossTileStepCount -lt 1) { throw "HPA active-window route sample must cross at least one tile boundary" }
    $activeWindowMeshRows = @($summary.strategy_switch_diagnostics | Where-Object {
        $_.MeshQueryAvailable -eq $true -and
        $_.MeshStatus -eq "Ok" -and
        $_.MeshQuerySource -eq "active_window_navmesh_query" -and
        [int]$_.MeshTouchedTileCount -gt 0
    })
    if ($activeWindowMeshRows.Count -lt 1) { throw "Strategy switch diagnostics must include at least one real active-window NavMesh query row" }
    foreach ($agentType in @("Infantry", "Mountain", "Naval", "Air")) {
        $mesh = @($summary.strategy_switch_diagnostics | Where-Object { $_.AgentTypeId -eq $agentType }) | Select-Object -First 1
        if ($null -eq $mesh -or $mesh.MeshQueryAvailable -ne $true -or $mesh.MeshStatus -ne "Ok" -or $mesh.MeshQuerySource -ne "active_window_navmesh_query" -or [int]$mesh.MeshTouchedTileCount -le 0) {
            throw "$agentType strategy row must use active_window_navmesh_query with MeshStatus Ok after multi-layer active-window bake"
        }
    }

    $requiredScreens = @(
        "000_boot.png",
        "001_selection_order.png",
        "002_remote_minimap_jump.png",
        "003_return_original_area.png",
        "004_bake_hpa_overlay.png",
        "005_path_strategy_inspector.png",
        "006_order_reuse_target_allocation.png",
        "006a_runtime_u1_visual_heightmap_bake.png",
        "006b_runtime_u2_logic_heightmap_bake.png",
        "006c_runtime_u3_layer_area_editor.png",
        "006d_runtime_u4_path_only.png",
        "006e_runtime_u5_world_hpa.png",
        "006f_runtime_u6_strategy_switch.png",
        "006g_runtime_u7_order_reuse.png",
        "006h_runtime_u8_target_allocation.png",
        "006i_runtime_u9_layer_costs.png",
        "006j_runtime_u10_waypoint_authoring.png",
        "006k_runtime_u11_large_world.png",
        "006l_runtime_u12_10k_flow.png",
        "006m_runtime_u13_static_obstacles.png",
        "006n_runtime_u14_fps_scope.png",
        "006o_runtime_u15_debug_budget.png",
        "006p_runtime_u16_bake_tool.png",
        "007_10k_commanded_flow_probe.png",
        "008_acceptance_gate_matrix.png",
        "009_raylib_frame_benchmark.png",
        "010_path_only_pick_before.png",
        "011_path_only_pick_result_no_order.png",
        "012_path_only_unreachable_failure.png",
        "013_hpa_active_window_portal_route.png",
        "014_graph_navmesh_hybrid_same_query_compare.png",
        "015_layer_cost_ground_water_air_mountain_compare.png",
        "016_noflyzone_blocked_query.png",
        "017_order_reuse_first_order.png",
        "018_order_reuse_same_point_cache_hit.png",
        "019_order_reuse_near_point_cache_hit.png",
        "020_target_allocation_10k_slots_zoom.png",
        "021_10k_move_t0.png",
        "022_10k_move_tN_avoidance.png",
        "023_10k_arrival_or_stuck_breakdown.png",
        "024_40k_obstacle_distribution_gap.png",
        "025_raylib_micro_fps_debug_off.png",
        "026_raylib_micro_fps_debug_on.png",
        "027_navmesh_failure_drilldown_tile.png",
        "028_bake_tool_interactive_query.png",
        "029_waypoint_edit_before.png",
        "030_waypoint_edit_after_pathpoints_regenerated.png"
    )

    $missing = @($requiredScreens | Where-Object { -not (Test-Path (Join-Path $RunRoot "screens\$_")) })
    if ($missing.Count -gt 0) {
        throw "Large-world UAT missing screenshots: $($missing -join ', ')"
    }

    foreach ($id in @("U1","U2","U3","U4","U5","U6","U7","U8","U9","U10","U11","U12","U13","U14","U15","U16")) {
        $case = @($summary.use_case_statuses | Where-Object { $_.id -eq $id }) | Select-Object -First 1
        if ($null -eq $case) { throw "Large-world UAT missing use case $id" }
        if ([string]::IsNullOrWhiteSpace($case.evidence)) { throw "Use case $id missing evidence text" }
        if ([string]::IsNullOrWhiteSpace($case.acceptance_proof)) { throw "Use case $id missing acceptance_proof" }
        if ($case.production_status -eq "PASS" -and $summary.manual_uat_accepted -ne $true) { throw "Use case $id must not report production_status PASS without manual_uat_accepted=true" }
        if ($case.production_status -notin @("PASS", "NEEDS_MANUAL_UAT", "BLOCKED")) { throw "Use case $id production_status must be PASS, NEEDS_MANUAL_UAT, or BLOCKED" }
        if ([string]::IsNullOrWhiteSpace($case.player_story_status)) { throw "Use case $id missing player story status" }
        if (@($case.player_visible_evidence_files).Count -lt 1) { throw "Use case $id missing player-visible evidence files" }
    }

    Assert-UseCaseEvidenceFiles -Summary $summary -SuiteRoot $SuiteRoot -RunRoot $RunRoot

    return $summary
}

function Get-ShowcaseSuiteStatus {
    param(
        [object]$LargeSummary,
        [object[]]$BakeRows
    )

    $bakeFailures = @($BakeRows | Where-Object { $_.success -ne $true })
    $productionFailures = @($LargeSummary.production_gate_failed_checks)
    if ($LargeSummary.production_gate_success -eq $true -and
        $LargeSummary.manual_uat_accepted -eq $true -and
        $LargeSummary.scene_smoke_success -eq $true -and
        @($LargeSummary.showcase_incomplete_use_cases).Count -eq 0 -and
        @($LargeSummary.production_blocked_use_cases).Count -eq 0 -and
        $productionFailures.Count -eq 0 -and
        $bakeFailures.Count -eq 0) {
        return "PASS / PRODUCTION_READY"
    }

    return "IN_PROGRESS / NEEDS_REVALIDATION"
}

function Get-UseCasePlayerGuide {
    param([string]$Id)

    switch ($Id) {
        "U1" { return @("Run the VisualHeightmap bake preset and open the bake viewer.", "Coverage, tile detail, sampled layer view and pathStatus=Ok are visible.", "Machine gate passes when active-window NavMesh bake has real tiles and no failed/missing/dirty tiles.") }
        "U2" { return @("Run the VertexMap and LogicHeightmap bake presets.", "Both sources converge to LogicHeightmap and bake 64/64 tiles.", "Machine gate passes when source/profile contracts are present.") }
        "U3" { return @("Open the mountain/river layer editor evidence.", "Mountain, river and area/layer cost overlays are visible.", "Machine gate passes when the multi-layer active-window query matrix is complete.") }
        "U4" { return @("Enter path_preview and pick start plus goal points.", "A highlighted route appears, units do not move, and order_delta=0.", "Machine gate passes when path preview exposes immutable pathpoints and editable waypoint intent.") }
        "U5" { return @("Inspect the 64km HPA overlay.", "Numbered route cells show crossed chunks, portal route and active-window graph nodes/edges.", "Machine gate passes when the active-window HPA route has portals and crosses tile boundaries.") }
        "U6" { return @("Run the same start/goal query across Road/NavMesh/Hybrid profiles.", "Each profile reports strategy choice, mesh source and touched tiles.", "Machine gate passes when graph and active-window NavMesh evidence both exist.") }
        "U7" { return @("Issue same-point and near-point order/path requests.", "The second request shows cache hit, same/near reuse, fanout and route signatures.", "Machine gate passes when near-order reuse has a cache hit and route signatures.") }
        "U8" { return @("Box-select 10k units and click one destination area.", "The system allocates 10k target slots with blocked/fallback at 0.", "Machine gate passes when 10k slots are reachable and share a route id.") }
        "U9" { return @("Switch Infantry/Naval/Air/Mountain profiles and inspect the NoFlyZone case.", "Different layer/cost rows and active-window mesh queries are visible.", "Machine gate passes when ground, water, air and mountain rows have active-window NavMesh evidence.") }
        "U10" { return @("Generate a planned route from path results, then edit a waypoint.", "Waypoints remain editable and pathpoints regenerate as query results.", "Machine gate passes when waypoint/pathpoint ownership flags are true.") }
        "U11" { return @("Open the 64km/256x256 world overview.", "World size, active window and navmesh loaded/notLoaded streaming contract are visible.", "Machine gate passes when the 64km world, active-window NavMesh and HPA route contracts all pass.") }
        "U12" { return @("Select 10k units and right-click one destination.", "Commanded, moving, avoidance and target slot breakdown are visible.", "Machine gate passes when 10k agents are commanded, moving/settled, and flow is enabled.") }
        "U13" { return @("Open the 40k obstacle distribution view.", "authored/baked/loaded=40000 and solver active subset are visible.", "Machine gate passes when world data and active-window solver subset are both valid.") }
        "U14" { return @("Run the Raylib framebuffer benchmark.", "Debug off/on p95/p99, FPS and overlay timing are visible.", "Machine gate passes when p95/p99/overlay thresholds pass.") }
        "U15" { return @("Open debug visual, timeline and report evidence.", "UAT trace, timeline, overlay A/B and low overlay draw cost are visible.", "Machine gate passes when runtime overlay writes are zero and benchmark overlay budget passes.") }
        "U16" { return @("Open the Raylib bake validator.", "Coverage, tile detail, path-only, HPA and layer screenshots are visible.", "Machine gate passes when bake, path query and HPA validator evidence are linked.") }
        default { return @("Run the matching showcase.", "Inspect the linked screenshots and summary fields.", "Machine gates and manual UAT signoff are reported separately.") }
    }
}

function Write-ShowcaseReport {
    param(
        [object[]]$BakeRows,
        [object]$LargeSummary,
        [string]$SuiteStatus,
        [string]$LargeRunRoot,
        [object[]]$EvidenceManifest,
        [string]$Path
    )

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# Mass Navigation Showcase Acceptance Suite")
    $lines.Add("")
    $lines.Add("## Verdict")
    $lines.Add("- Status: ``$SuiteStatus``")
    $lines.Add("- Showcase body: real interactive playable/editor entry points. Screenshots and frame captures are operation evidence only.")
    $lines.Add("- Scene smoke: ``$($LargeSummary.scene_smoke_success)``")
    $lines.Add("- Machine production evidence: ``$($LargeSummary.machine_production_evidence_success)``")
    $lines.Add("- Manual UAT accepted: ``$($LargeSummary.manual_uat_accepted)``")
    $lines.Add("- Production gate: ``$($LargeSummary.production_gate_success)``")
    if ($LargeSummary.manual_uat_accepted -ne $true) {
        $lines.Add("- Production claim blocker: ``$($LargeSummary.manual_uat_blocker)``")
    }
    $lines.Add("- Showcase incomplete use cases: ``$(@($LargeSummary.showcase_incomplete_use_cases).Count)``")
    $lines.Add("- Production blocked use cases: ``$(@($LargeSummary.production_blocked_use_cases).Count)``")
    $lines.Add("")
    $lines.Add("## Bake/Data Sources")
    $lines.Add("| Source | Result | Origin | Baked | Coverage | Path | Layer/area semantics |")
    $lines.Add("| --- | --- | --- | ---: | ---: | --- | --- |")
    foreach ($row in $BakeRows) {
        $lines.Add("| ``$($row.name)`` | ``$($row.success)`` | ``$($row.source_origin_kind)`` | ``$($row.total_baked_tiles)/$($row.total_expected_tile_bakes)`` | ``$($row.coverage_percent)%`` | ``$($row.path_status)`` | distinctAreas=``$($row.distinct_area_count)`` waterCells=``$($row.water_like_cells)`` heightRangeCm=``$($row.height_range_cm)`` |")
    }

    $lines.Add("")
    $lines.Add("## Large-World Smoke")
    $lines.Add("- Run root: ``$LargeRunRoot``")
    $lines.Add("- World: ``$($LargeSummary.world_width_cm)x$($LargeSummary.world_height_cm)cm``")
    $lines.Add("- Macro chunks: ``$($LargeSummary.macro_chunk_columns)x$($LargeSummary.macro_chunk_rows)``")
    $lines.Add("- S1 boundary/ground picking: loadedChunks=``$($LargeSummary.loaded_chunk_count)`` boundary=``$($LargeSummary.boundary_click_result)`` ground=``$($LargeSummary.ground_picking_result)`` source=``$($LargeSummary.world_boundary_diagnostics.Source)``")
    $lines.Add("- NavMesh active-window smoke: baked=``$($LargeSummary.navmesh_baked_tiles)`` notLoaded=``$($LargeSummary.navmesh_not_loaded_tiles)`` total=``$($LargeSummary.navmesh_total_tiles)``")
    $lines.Add("- HPA active-window graph smoke: loadedTiles=``$($LargeSummary.hpa_graph_diagnostics.LoadedTileCount)/$($LargeSummary.hpa_graph_diagnostics.ActiveWindowChunkCount)`` nodes=``$($LargeSummary.hpa_graph_diagnostics.GraphNodeCount)`` edges=``$($LargeSummary.hpa_graph_diagnostics.GraphEdgeCount)`` routePortals=``$($LargeSummary.hpa_graph_diagnostics.ActiveWindowRoutePortalCount)`` routeCrossTileSteps=``$($LargeSummary.hpa_graph_diagnostics.ActiveWindowRouteCrossTileStepCount)`` source=``$($LargeSummary.hpa_graph_diagnostics.Source)``")
    $lines.Add("- HPA readable visual contract: ``Numbered route cells show the crossed chunks``; report and operation evidence must also list ``Route chunks:`` so the route is not a mysterious line.")
    $lines.Add("- NavMesh visual contract: walkable triangles, blocked/high-cost source cells, corridor portals, mesh/off-mesh links, portal clearance, and agent radius must be visible before evidence is accepted.")
    $meshRows = @($LargeSummary.strategy_switch_diagnostics | Where-Object { $_.MeshQuerySource -eq "active_window_navmesh_query" })
    $meshStatuses = @($LargeSummary.strategy_switch_diagnostics | ForEach-Object { "{0}:{1}/{2}/{3}" -f $_.AgentTypeId, $_.MeshStatus, $_.MeshQuerySource, $_.MeshTouchedTileCount }) -join ","
    $lines.Add("- Strategy/NavMesh active-window query smoke: rows=``$($meshRows.Count)``; activeWindowMeshRows=``$((@($meshRows | Where-Object { $_.MeshQueryAvailable -eq $true })).Count)``; statuses=``$meshStatuses``")
    $lines.Add("- Agents: ``$($LargeSummary.agent_count)`` commanded=``$($LargeSummary.commanded_agents)`` moving=``$($LargeSummary.moving_agents)``")
    $lines.Add("- Target allocation: selected=``$($LargeSummary.target_allocation.SelectedCount)`` slots=``$($LargeSummary.target_allocation.SlotCount)`` reachable=``$($LargeSummary.target_allocation.reachable_slot_count)`` reachability=``$($LargeSummary.target_allocation.ReachabilityProbeStatus)`` source=``$($LargeSummary.target_allocation.ReachabilitySource)`` routeId=``$($LargeSummary.target_allocation.AllocationRouteId)`` mesh=``$($LargeSummary.target_allocation.MeshReachabilityStatus)/$($LargeSummary.target_allocation.MeshReachabilitySource)`` blocked=``$($LargeSummary.target_allocation.BlockedSlotCount)`` fallback=``$($LargeSummary.target_allocation.FallbackSlotCount)``")
    $lines.Add("- Path preview: mode=``$($LargeSummary.path_only_query.PreviewMode)`` input=``$($LargeSummary.path_only_query.InputContract)`` state=``$($LargeSummary.path_only_query.RoutePreviewState)`` highlight=``$($LargeSummary.path_only_query.HighlightRouteVisible)`` status=``$($LargeSummary.path_only_query.Status)`` noOrder=``$($LargeSummary.path_only_query.NoOrderSubmitted)`` pathpoints=``$($LargeSummary.path_only_query.PathPointCount)`` provenance=``$($LargeSummary.path_only_query.RouteProvenance)``")
    $lines.Add("- Reuse: cacheHit=``$($LargeSummary.order_reuse.CacheHit)`` fanout=``$($LargeSummary.order_reuse.FanoutCount)`` same=``$($LargeSummary.order_reuse.SamePointReuseCount)`` near=``$($LargeSummary.order_reuse.NearPointReuseCount)`` scope=``$($LargeSummary.order_reuse.ReuseScope)`` pathSig=``$($LargeSummary.order_reuse.PathRouteSignature)`` meshSig=``$($LargeSummary.order_reuse.MeshRouteSignature)``")
    $lines.Add("- 40k obstacles: planned=``$($LargeSummary.static_obstacle_world_diagnostics.PlannedWorldObstacleCount)`` loaded=``$($LargeSummary.loaded_static_obstacle_count)`` solver=``$($LargeSummary.solver_active_static_obstacle_count)``")
    $lines.Add("- Raylib micro: p95=``$($LargeSummary.frame_timing.raylib_frame_ms_p95)ms`` fpsP95=``$($LargeSummary.frame_timing.raylib_fps_p95)`` production=``$($LargeSummary.frame_timing.fps_production_passed)``")
    $lines.Add("")
    $lines.Add("## Runtime Operation Evidence")
    $lines.Add("These files are evidence captured after runtime/editor operations. They do not replace the playable mod window or the Raylib editor workbench.")
    $lines.Add("")
    $lines.Add("- Runtime overlay sampled: ``$($LargeSummary.playable_guided_showcase.runtime_overlay_sampled)``")
    $lines.Add("- Sampled use cases: ``$((@($LargeSummary.playable_guided_showcase.sampled_use_cases) -join ', '))``")
    $lines.Add("- Missing use cases: ``$((@($LargeSummary.playable_guided_showcase.missing_use_cases) -join ', '))``")
    $lines.Add("")
    $lines.Add("| Case | Evidence file | Debug presentation |")
    $lines.Add("| --- | --- | --- |")
    foreach ($frame in @($LargeSummary.runtime_guide_keyframes)) {
        $lines.Add("| ``$($frame.use_case_id)`` | ``$($frame.file)`` | $($frame.debug_presentation) |")
    }
    $lines.Add("")
    $lines.Add("## Operation Evidence Manifest")
    $lines.Add("Every listed file is existence-checked and SHA256-hashed by the suite before this report is written.")
    $lines.Add("")
    $lines.Add("| File | Kind | Use case | Bytes | SHA256 |")
    $lines.Add("| --- | --- | --- | ---: | --- |")
    foreach ($entry in $EvidenceManifest) {
        $lines.Add("| ``$($entry.file)`` | ``$($entry.kind)`` | ``$($entry.use_case_id)`` | $($entry.bytes) | ``$($entry.sha256)`` |")
    }
    $lines.Add("")
    $lines.Add("## Player-Readable Showcase Guide")
    $lines.Add("Every case in this table is backed by machine checks and linked player-visible evidence, but this is not a human-operated production signoff.")
    $lines.Add("")
    $lines.Add("| Case | Input | Expected output | Production proof |")
    $lines.Add("| --- | --- | --- | --- |")
    foreach ($case in $LargeSummary.use_case_statuses) {
        $guide = Get-UseCasePlayerGuide -Id $case.id
        $lines.Add("| ``$($case.id)`` $($case.name) | $($guide[0]) | $($guide[1]) | $($guide[2]) |")
    }
    $lines.Add("")
    $lines.Add("## U1-U16")
    $lines.Add("| Case | Showcase | Production | Player story | Evidence | Acceptance proof |")
    $lines.Add("| --- | --- | --- | --- | --- | --- |")
    foreach ($case in $LargeSummary.use_case_statuses) {
        $lines.Add("| ``$($case.id)`` $($case.name) | ``$($case.showcase_status)`` | ``$($case.production_status)`` | ``$($case.player_story_status)`` | $($case.evidence) | $($case.acceptance_proof) |")
    }

    $lines.Add("")
    $lines.Add("## Required Evidence")
    $lines.Add("- ``battle-report.md``")
    $lines.Add("- ``summary.json``")
    $lines.Add("- ``visible-checklist.md``")
    $lines.Add("- ``trace.jsonl``")
    $lines.Add("- ``path.mmd``")
    $lines.Add("- Runtime/editor operation evidence, including ``screens/006a_runtime_u1_visual_heightmap_bake.png`` through ``screens/006p_runtime_u16_bake_tool.png``")
    $lines.Add("- Large-world operation evidence, including ``screens/000_boot.png`` through ``screens/030_waypoint_edit_after_pathpoints_regenerated.png``")
    $lines.Add("- ``screens/raylib-frame-benchmark.json`` and ``screens/timeline.png``")
    $lines.Add("")
    $lines.Add("## Production Gate Checks")
    if (@($LargeSummary.production_gate_failed_checks).Count -eq 0) {
        $lines.Add("- PASS: no failed production checks.")
    }
    else {
        foreach ($failure in $LargeSummary.production_gate_failed_checks) {
            $lines.Add("- $failure")
        }
    }

    Set-Content -Path $Path -Value $lines -Encoding UTF8
}

$expectedTiles = $WidthChunks * $HeightChunks
$bakeSpecs = @(
    [pscustomobject]@{
        Name = "navmesh-layer-editor-current"
        MapId = "mass_nav_layer_editor_mountain_river"
        Source = "vtxm"
        ApplyEditorPatch = $true
    },
    [pscustomobject]@{
        Name = "navmesh-visual-heightmap-current"
        MapId = "mass_nav_vhtm_mountain_river"
        Source = "vhtm"
        ApplyEditorPatch = $false
    },
    [pscustomobject]@{
        Name = "navmesh-logic-heightmap-current"
        MapId = "mass_nav_logic_mountain_river"
        Source = "lhtm"
        ApplyEditorPatch = $false
    }
)

$bakeRows = New-Object System.Collections.Generic.List[object]
foreach ($spec in $bakeSpecs) {
    $artifactRoot = Join-Path $OutputRoot $spec.Name
    $navBakeArgs = @(
        "-OutputRoot", $artifactRoot,
        "-WidthChunks", "$WidthChunks",
        "-HeightChunks", "$HeightChunks",
        "-Preset", $Preset,
        "-MapId", $spec.MapId,
        "-Layer", "Ground",
        "-Profile", "GroundLight",
        "-BakeSource", $spec.Source
    )
    if ($spec.ApplyEditorPatch -eq $true) {
        $navBakeArgs += "-ApplyEditorPatch"
    }

    & $navBakeScript @navBakeArgs
    if ($LASTEXITCODE -ne 0) {
        throw "NavMesh bake $($spec.Source) failed with exit code $LASTEXITCODE"
    }

    $bakeRows.Add((Assert-NavBakeArtifact -Name $spec.Name -Root $artifactRoot -ExpectedOrigin $spec.Source -ExpectedTiles $expectedTiles))
}

$largeWorldRoot = Join-Path $OutputRoot "mass-navigation-large-world-current"
& $largeWorldScript `
    -OutputRoot $largeWorldRoot `
    -Iterations 1 `
    -Adapter $Adapter
if ($LASTEXITCODE -ne 0) {
    throw "MassNavigation large-world UAT failed with exit code $LASTEXITCODE"
}

$largeRunRoot = Join-Path $largeWorldRoot "run-0001"
$largeSummary = Assert-LargeWorldArtifact -RunRoot $largeRunRoot -SuiteRoot $OutputRoot
$bakeArtifactRows = @()
foreach ($row in $bakeRows) {
    $bakeArtifactRows += $row
}
$largeWorldSummaryPath = Join-Path $largeRunRoot "summary.json"
$largeWorldBattleReportPath = Join-Path $largeRunRoot "battle-report.md"
$largeWorldScreensPath = Join-Path $largeRunRoot "screens"
$showcaseIncompleteUseCaseCount = @($largeSummary.showcase_incomplete_use_cases).Count
$productionBlockedUseCaseCount = @($largeSummary.production_blocked_use_cases).Count
$showcaseEvidenceManifest = Build-ShowcaseEvidenceManifest -BakeRows $bakeArtifactRows -LargeSummary $largeSummary -SuiteRoot $OutputRoot -RunRoot $largeRunRoot
$suiteStatus = Get-ShowcaseSuiteStatus -LargeSummary $largeSummary -BakeRows $bakeArtifactRows

$suiteSummary = [ordered]@{
    status = $suiteStatus
    showcase_body_contract = "interactive playable/editor entry points; screenshots are operation evidence only"
    output_root = $OutputRoot
    summary = $summaryPath
    summary_alias = $summaryAliasPath
    report = $reportPath
    bake_artifacts = $bakeArtifactRows
    large_world_run_root = $largeRunRoot
    scene_smoke_success = [bool]$largeSummary.scene_smoke_success
    machine_production_evidence_success = [bool]$largeSummary.machine_production_evidence_success
    manual_uat_required = [bool]$largeSummary.manual_uat_required
    manual_uat_accepted = [bool]$largeSummary.manual_uat_accepted
    manual_uat_evidence_path = $largeSummary.manual_uat_evidence_path
    manual_uat_blocker = $largeSummary.manual_uat_blocker
    production_gate_success = [bool]$largeSummary.production_gate_success
    showcase_incomplete_use_case_count = $showcaseIncompleteUseCaseCount
    production_blocked_use_case_count = $productionBlockedUseCaseCount
    large_world_summary = $largeWorldSummaryPath
    large_world_battle_report = $largeWorldBattleReportPath
    large_world_screens = $largeWorldScreensPath
    large_world_navmesh_baked_tiles = [int]$largeSummary.navmesh_baked_tiles
    large_world_navmesh_not_loaded_tiles = [int]$largeSummary.navmesh_not_loaded_tiles
    large_world_navmesh_total_tiles = [int]$largeSummary.navmesh_total_tiles
    large_world_hpa_graph_loaded_tiles = [int]$largeSummary.hpa_graph_diagnostics.LoadedTileCount
    large_world_hpa_graph_nodes = [int]$largeSummary.hpa_graph_diagnostics.GraphNodeCount
    large_world_hpa_graph_edges = [int]$largeSummary.hpa_graph_diagnostics.GraphEdgeCount
    large_world_hpa_graph_route_portals = [int]$largeSummary.hpa_graph_diagnostics.ActiveWindowRoutePortalCount
    large_world_hpa_graph_route_cross_tile_steps = [int]$largeSummary.hpa_graph_diagnostics.ActiveWindowRouteCrossTileStepCount
    playable_guided_showcase = $largeSummary.playable_guided_showcase
    runtime_operation_evidence = $largeSummary.runtime_guide_keyframes
    operation_evidence_manifest = $showcaseEvidenceManifest
}

$suiteSummaryJson = $suiteSummary | ConvertTo-Json -Depth 12
$suiteSummaryJson | Set-Content -Path $summaryPath -Encoding UTF8
$suiteSummaryJson | Set-Content -Path $summaryAliasPath -Encoding UTF8
Write-ShowcaseReport -BakeRows $bakeArtifactRows -LargeSummary $largeSummary -SuiteStatus $suiteStatus -LargeRunRoot $largeRunRoot -EvidenceManifest $showcaseEvidenceManifest -Path $reportPath

if ($StopOnFailure -and ($largeSummary.production_gate_success -ne $true -or $largeSummary.manual_uat_accepted -ne $true)) {
    throw "Production gate did not pass. Review production_gate_failed_checks and manual_uat_blocker before accepting this suite."
}

Write-Host "MassNavigation showcase acceptance suite complete."
Write-Host "status=$suiteStatus"
Write-Host "output=$OutputRoot"
Write-Host "report=$reportPath"
Write-Host "summary=$summaryPath"
Write-Host "summary_alias=$summaryAliasPath"
