param(
    [string]$OutputRoot = "",
    [int]$WidthChunks = 4,
    [int]$HeightChunks = 4,
    [string]$Preset = "flat",
    [string]$MapId = "mass_nav_bake_flat",
    [string]$Layer = "Ground",
    [string]$Profile = "GroundLight",
    [ValidateSet("vtxm", "vhtm", "lhtm")]
    [string]$BakeSource = "vtxm",
    [string]$ModRoot = "",
    [string]$ExpectedSourceOriginKind = "",
    [switch]$ApplyEditorPatch,
    [switch]$InteractiveWorkbench
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputRoot = Join-Path $repoRoot "artifacts\acceptance\navmesh-bake-raylib-$stamp"
}
else {
    $OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
}

$toolProject = Join-Path $repoRoot "src\Tools\Ludots.Tool\Ludots.Tool.csproj"
$viewerProject = Join-Path $repoRoot "src\Tools\Ludots.NavBake.Raylib\Ludots.NavBake.Raylib.csproj"
if ([string]::IsNullOrWhiteSpace($ModRoot)) {
    $ModRoot = Join-Path $repoRoot "mods\capabilities\navigation\MassNavigationMod"
}
else {
    $ModRoot = [System.IO.Path]::GetFullPath($ModRoot)
}

if ([string]::IsNullOrWhiteSpace($ExpectedSourceOriginKind)) {
    $ExpectedSourceOriginKind = $BakeSource
}

if (-not (Test-Path $ModRoot)) {
    throw "ModRoot not found: $ModRoot"
}

$artifactRepo = Join-Path $OutputRoot "repo"
$screens = Join-Path $OutputRoot "screens"
$vtxm = Join-Path $OutputRoot "$MapId.vtxm"
$vhtm = Join-Path $OutputRoot "$MapId.vhtm"
$lhtm = Join-Path $OutputRoot "$MapId.lhtm"

function Resolve-ViewerLayer {
    param(
        [string]$LayerId,
        [string]$NavigationConfigPath
    )

    if ($LayerId -match '^\d+$') {
        return [int]$LayerId
    }

    if (-not (Test-Path $NavigationConfigPath)) {
        throw "Navigation navmesh config not found: $NavigationConfigPath"
    }

    $navConfig = Get-Content -Path $NavigationConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $layerConfig = @($navConfig.layers | Where-Object { $_.id -eq $LayerId }) | Select-Object -First 1
    if ($null -eq $layerConfig) {
        $known = @($navConfig.layers | ForEach-Object { $_.id }) -join ", "
        throw "Unknown viewer layer '$LayerId'. Known layers from ${NavigationConfigPath}: $known"
    }

    return [int]$layerConfig.layer
}

function Invoke-CheckedDotnet {
    param(
        [string[]]$Arguments,
        [string]$Label
    )

    & dotnet @Arguments
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        throw "$Label failed with exit code $code"
    }
}

$navmeshConfigPath = Join-Path $ModRoot "assets\Configs\Navigation\navmesh.json"
$viewerLayer = Resolve-ViewerLayer -LayerId $Layer -NavigationConfigPath $navmeshConfigPath
$navmeshConfig = Get-Content -Path $navmeshConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
$profileConfig = @($navmeshConfig.profiles | Where-Object { $_.id -eq $Profile }) | Select-Object -First 1
if ($null -eq $profileConfig) {
    $knownProfiles = @($navmeshConfig.profiles | ForEach-Object { $_.id }) -join ", "
    throw "Unknown profile '$Profile'. Known profiles from ${navmeshConfigPath}: $knownProfiles"
}

New-Item -ItemType Directory -Force -Path (Join-Path $artifactRepo "assets") | Out-Null
New-Item -ItemType Directory -Force -Path $screens | Out-Null
Copy-Item -Path (Join-Path $repoRoot "assets\Configs") -Destination (Join-Path $artifactRepo "assets\Configs") -Recurse -Force
New-Item -ItemType Directory -Force -Path (Join-Path $artifactRepo "assets\Data") | Out-Null

Push-Location $repoRoot
try {
    if ($BakeSource -eq "vhtm") {
        if (Test-Path $vtxm) { Remove-Item -LiteralPath $vtxm -Force }
        Invoke-CheckedDotnet -Label "map gen-vhtm" -Arguments @("run", "--project", $toolProject, "--", "map", "gen-vhtm", "--out", $vhtm, "--widthChunks", "$WidthChunks", "--heightChunks", "$HeightChunks", "--preset", $Preset, "--overwrite")
        Invoke-CheckedDotnet -Label "map to-lhtm vhtm" -Arguments @("run", "--project", $toolProject, "--", "map", "to-lhtm", "--sourceKind", "vhtm", "--in", $vhtm, "--out", $lhtm, "--overwrite")
    }
    elseif ($BakeSource -eq "lhtm") {
        if (Test-Path $vtxm) { Remove-Item -LiteralPath $vtxm -Force }
        Invoke-CheckedDotnet -Label "map gen-lhtm" -Arguments @("run", "--project", $toolProject, "--", "map", "gen-lhtm", "--out", $lhtm, "--widthChunks", "$WidthChunks", "--heightChunks", "$HeightChunks", "--preset", $Preset, "--overwrite")
    }
    else {
        Invoke-CheckedDotnet -Label "map gen-vtxm" -Arguments @("run", "--project", $toolProject, "--", "map", "gen-vtxm", "--out", $vtxm, "--widthChunks", "$WidthChunks", "--heightChunks", "$HeightChunks", "--preset", $Preset, "--overwrite")
        Invoke-CheckedDotnet -Label "map to-lhtm vtxm" -Arguments @("run", "--project", $toolProject, "--", "map", "to-lhtm", "--sourceKind", "vtxm", "--in", $vtxm, "--out", $lhtm, "--heightScale", "2", "--overwrite")
    }
    Invoke-CheckedDotnet -Label "nav bake-recast-lhtm" -Arguments @("run", "--project", $toolProject, "--", "nav", "bake-recast-lhtm", "--mapId", $MapId, "--in", $lhtm, "--repoRoot", $artifactRepo, "--modRoot", $ModRoot, "--layer", $Layer, "--profile", $Profile, "--parallel", "false", "--artifact", "true")
    $sourceForViewer = if ($BakeSource -eq "vhtm") { $vhtm } elseif ($BakeSource -eq "lhtm") { $lhtm } else { $vtxm }
    $editPatch = Join-Path $screens "logic-heightmap-edit-patch.json"
    $dirtyOut = Join-Path $screens "dirty-chunks.json"
    $patchedLhtm = Join-Path $OutputRoot "$MapId.edited.lhtm"
    foreach ($staleEditorArtifact in @($editPatch, $dirtyOut, $patchedLhtm)) {
        if (Test-Path $staleEditorArtifact) {
            Remove-Item -LiteralPath $staleEditorArtifact -Force
        }
    }

    $viewerArgs = @("run", "--project", $viewerProject, "--", "--lhtm", $lhtm, "--mapId", $MapId, "--repoRoot", $artifactRepo, "--profile", $Profile, "--layer", "$viewerLayer", "--out", $screens, "--targetFps", "100", "--maxSampleTiles", "32", "--macroColumns", "256", "--macroRows", "256", "--targetStaticObstacles", "40000", "--sourceOriginKind", $BakeSource, "--sourceOriginPath", $sourceForViewer, "--editPatch", $editPatch, "--dirtyOut", $dirtyOut, "--patchedLhtm", $patchedLhtm)
    if ($InteractiveWorkbench) {
        $viewerArgs += "--interactive"
    }
    else {
        $viewerArgs += "--failOnInvalid"
    }
    if ($ApplyEditorPatch) {
        $viewerArgs += "--autoEditorPatch"
    }

    Invoke-CheckedDotnet -Label "Ludots.NavBake.Raylib" -Arguments $viewerArgs
    if ($ApplyEditorPatch) {
        Invoke-CheckedDotnet -Label "map patch-lhtm" -Arguments @("run", "--project", $toolProject, "--", "map", "patch-lhtm", "--in", $lhtm, "--patch", $editPatch, "--out", $patchedLhtm, "--dirtyOut", $dirtyOut, "--overwrite")
        Invoke-CheckedDotnet -Label "nav bake-recast-lhtm dirty" -Arguments @("run", "--project", $toolProject, "--", "nav", "bake-recast-lhtm", "--mapId", "$MapId-edited", "--in", $patchedLhtm, "--dirty", $dirtyOut, "--repoRoot", $artifactRepo, "--modRoot", $ModRoot, "--layer", $Layer, "--profile", $Profile, "--parallel", "false", "--artifact", "true")
    }
}
finally {
    Pop-Location
}

$diagnosticsPath = Join-Path $artifactRepo "assets\Data\Nav\$MapId\nav-bake-diagnostics.json"
if (-not (Test-Path $diagnosticsPath)) {
    throw "Missing diagnostics: $diagnosticsPath"
}

$sourceManifest = [ordered]@{
    schemaVersion = 1
    bakeSource = $BakeSource
    mapId = $MapId
    preset = $Preset
    widthChunks = $WidthChunks
    heightChunks = $HeightChunks
    layer = $Layer
    viewerLayer = $viewerLayer
    profile = $Profile
    sourcePath = if ($BakeSource -eq "vhtm") { $vhtm } elseif ($BakeSource -eq "lhtm") { $lhtm } else { $vtxm }
    sourceKind = $BakeSource
    viewerReferenceMapPath = $null
    vtxmPath = if ($BakeSource -eq "vtxm") { $vtxm } else { $null }
    vhtmPath = if ($BakeSource -eq "vhtm") { $vhtm } else { $null }
    logicHeightmapPath = $lhtm
    artifactRepo = $artifactRepo
    modRoot = $ModRoot
    navmeshConfigPath = $navmeshConfigPath
    diagnosticsPath = $diagnosticsPath
    screensPath = $screens
    editorPatchPath = $editPatch
    dirtyChunksPath = $dirtyOut
    patchedLogicHeightmapPath = if ($ApplyEditorPatch) { $patchedLhtm } else { $null }
    editorPatchApplied = [bool]$ApplyEditorPatch
    interactiveWorkbench = [bool]$InteractiveWorkbench
    bakeCommand = if ($BakeSource -eq "vhtm") { "map to-lhtm --sourceKind vhtm -> nav bake-recast-lhtm" } elseif ($BakeSource -eq "lhtm") { "map gen-lhtm -> nav bake-recast-lhtm" } else { "map to-lhtm --sourceKind vtxm -> nav bake-recast-lhtm" }
}
$sourceManifestPath = Join-Path $OutputRoot "nav-bake-source-manifest.json"
$sourceManifest | ConvertTo-Json -Depth 8 | Set-Content -Path $sourceManifestPath -Encoding UTF8

if ($InteractiveWorkbench) {
    Write-Host "NavMesh Raylib interactive workbench closed."
    Write-Host "output=$OutputRoot"
    Write-Host "screens=$screens"
    Write-Host "diagnostics=$diagnosticsPath"
    Write-Host "source_manifest=$sourceManifestPath"
    Write-Host "operation=Use 1-5 to switch coverage/tile/path/HPA/layer views; left/right click path endpoints; Q/W/E/R/B paint layer edits; S saves patch + dirty chunks."
    exit 0
}

$required = @(
    "001_navmesh_bake_coverage.png",
    "002_navmesh_tile_detail.png",
    "003_path_only_query.png",
    "004_hpa_macro_overlay.png",
    "005_layer_area_editor.png",
    "nav-bake-raylib-report.md",
    "nav-bake-raylib-result.json"
)
if ($ApplyEditorPatch) {
    $required += "logic-heightmap-edit-patch.json"
    $required += "dirty-chunks.json"
}

$missing = @($required | Where-Object { -not (Test-Path (Join-Path $screens $_)) })
if ($missing.Count -gt 0) {
    throw "Missing nav bake Raylib evidence: $($missing -join ', ')"
}

$diagnostics = Get-Content -Path $diagnosticsPath -Raw | ConvertFrom-Json
if ([int]$diagnostics.LayerCount -ne 1) {
    throw "Expected exactly one baked layer for this acceptance run, got LayerCount=$($diagnostics.LayerCount)"
}

if ([int]$diagnostics.ProfileCount -ne 1) {
    throw "Expected exactly one baked profile for this acceptance run, got ProfileCount=$($diagnostics.ProfileCount)"
}

$expectedTiles = $WidthChunks * $HeightChunks
if ([int]$diagnostics.TotalExpectedTileBakes -ne $expectedTiles) {
    throw "Unexpected expected tile bakes: expected=$expectedTiles actual=$($diagnostics.TotalExpectedTileBakes)"
}

if ($diagnostics.TotalFailedTiles -ne 0) {
    throw "Nav bake failed tiles: $($diagnostics.TotalFailedTiles)"
}

if ($diagnostics.TotalBakedTiles -ne $diagnostics.TotalExpectedTileBakes) {
    throw "Nav bake coverage incomplete: baked=$($diagnostics.TotalBakedTiles) expected=$($diagnostics.TotalExpectedTileBakes)"
}

$resultPath = Join-Path $screens "nav-bake-raylib-result.json"
$result = Get-Content -Path $resultPath -Raw | ConvertFrom-Json
if ($result.success -ne $true) {
    throw "Raylib nav bake validation failed: $($result.errors -join '; ')"
}

if ($result.sourceKind -ne "lhtm") {
    throw "Expected Raylib sourceKind=lhtm, got '$($result.sourceKind)'"
}

if ($result.sourceOriginKind -ne $ExpectedSourceOriginKind) {
    throw "Expected sourceOriginKind=$ExpectedSourceOriginKind, got '$($result.sourceOriginKind)'"
}

if ($result.profileId -ne $Profile) {
    throw "Expected profileId=$Profile, got '$($result.profileId)'"
}

if ([int]$result.layer -ne $viewerLayer) {
    throw "Expected viewer layer=$viewerLayer, got '$($result.layer)'"
}

if ($result.diagnosticsLoaded -ne $true) {
    throw "Raylib diagnosticsLoaded=false"
}

if ([int]$result.totalExpectedTileBakes -ne $expectedTiles) {
    throw "Unexpected Raylib expected tile bakes: expected=$expectedTiles actual=$($result.totalExpectedTileBakes)"
}

if ([int]$result.totalBakedTiles -ne $expectedTiles) {
    throw "Unexpected Raylib baked tiles: expected=$expectedTiles actual=$($result.totalBakedTiles)"
}

if ([int]$result.totalFailedTiles -ne 0) {
    throw "Unexpected Raylib failed tiles: $($result.totalFailedTiles)"
}

if ([double]$result.coveragePercent -ne 100) {
    throw "Expected Raylib coveragePercent=100, got '$($result.coveragePercent)'"
}

if ($result.pathStatus -ne "Ok") {
    throw "Expected pathStatus=Ok, got '$($result.pathStatus)'"
}

if ([int]$result.pathPoints -lt 2) {
    throw "Expected at least 2 path points, got '$($result.pathPoints)'"
}

if ($result.layerEditorSource -ne "logic_heightmap_sampled_view") {
    throw "Expected layerEditorSource=logic_heightmap_sampled_view, got '$($result.layerEditorSource)'"
}

if ($ApplyEditorPatch) {
    if ($result.editorPatchSaved -ne $true) {
        throw "Expected editorPatchSaved=true after Raylib layer editor operation"
    }

    if ([int]$result.editorPatchOperations -lt 1) {
        throw "Expected at least one editor patch operation, got '$($result.editorPatchOperations)'"
    }

    if ([int]$result.editorDirtyChunks -lt 1) {
        throw "Expected at least one editor dirty chunk, got '$($result.editorDirtyChunks)'"
    }

    if (-not (Test-Path (Join-Path $screens "logic-heightmap-edit-patch.json"))) {
        throw "Expected logic-heightmap-edit-patch.json"
    }

    if (-not (Test-Path (Join-Path $screens "dirty-chunks.json"))) {
        throw "Expected dirty-chunks.json"
    }

    if (-not (Test-Path $patchedLhtm)) {
        throw "Expected patched LogicHeightmap after ApplyEditorPatch: $patchedLhtm"
    }
}
else {
    if ($result.editorPatchSaved -ne $false) {
        throw "Expected editorPatchSaved=false when ApplyEditorPatch is not set"
    }

    if ([int]$result.editorPatchOperations -ne 0) {
        throw "Expected zero editor patch operations without ApplyEditorPatch, got '$($result.editorPatchOperations)'"
    }

    if ([int]$result.editorDirtyChunks -ne 0) {
        throw "Expected zero editor dirty chunks without ApplyEditorPatch, got '$($result.editorDirtyChunks)'"
    }
}

if ($result.logicSemanticAvailable -ne $true) {
    throw "Expected logicSemanticAvailable=true"
}

if ($result.fpsMeasured -ne $true) {
    throw "Expected Raylib capture to record frame timing samples"
}

if ([int]$result.frameSampleCount -lt 5) {
    throw "Expected at least 5 Raylib frame timing samples, got '$($result.frameSampleCount)'"
}

if ([double]$result.averageFps -le 0) {
    throw "Expected positive Raylib averageFps, got '$($result.averageFps)'"
}

if ([double]$result.frameP95Ms -le 0) {
    throw "Expected positive Raylib frameP95Ms, got '$($result.frameP95Ms)'"
}

if ([int]$result.logicSemanticSampledChunks -ne $expectedTiles) {
    throw "Expected logic semantic sampled chunks=$expectedTiles, got '$($result.logicSemanticSampledChunks)'"
}

if ([int]$result.logicSemanticSampledCells -lt ($expectedTiles * 4096)) {
    throw "Expected logic semantic sampled cells for every chunk, got '$($result.logicSemanticSampledCells)'"
}

if ([int]$result.logicSemanticDistinctAreaCount -lt 1) {
    throw "Expected logic semantic distinct areas >= 1, got '$($result.logicSemanticDistinctAreaCount)'"
}

if ($Preset -eq "mountainRiver") {
    if ([int]$result.logicSemanticDistinctAreaCount -lt 3) {
        throw "Expected mountainRiver logic semantic distinct areas >= 3, got '$($result.logicSemanticDistinctAreaCount)'"
    }

    if ([int]$result.logicSemanticHeightRangeCm -le 0) {
        throw "Expected mountainRiver logic semantic height range > 0, got '$($result.logicSemanticHeightRangeCm)'"
    }

    if ([int]$result.logicSemanticWaterLikeCells -le 0) {
        throw "Expected mountainRiver logic semantic water-like cells > 0"
    }

    if ($result.logicSemanticHasMountainRiverSignals -ne $true) {
        throw "Expected logicSemanticHasMountainRiverSignals=true for mountainRiver"
    }
}

if ([int]$result.macroColumns -ne 256 -or [int]$result.macroRows -ne 256) {
    throw "Expected macro grid 256x256, got $($result.macroColumns)x$($result.macroRows)"
}

if ([int]$result.targetStaticObstacles -lt 40000) {
    throw "Expected targetStaticObstacles>=40000, got '$($result.targetStaticObstacles)'"
}

$manifest = Get-Content -Path $sourceManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.bakeSource -ne $BakeSource) {
    throw "Manifest bakeSource mismatch: expected=$BakeSource actual=$($manifest.bakeSource)"
}

if ([int]$manifest.viewerLayer -ne $viewerLayer) {
    throw "Manifest viewerLayer mismatch: expected=$viewerLayer actual=$($manifest.viewerLayer)"
}

Write-Host "NavMesh Raylib bake acceptance complete."
Write-Host "output=$OutputRoot"
Write-Host "screens=$screens"
Write-Host "diagnostics=$diagnosticsPath"
Write-Host "source_manifest=$sourceManifestPath"
