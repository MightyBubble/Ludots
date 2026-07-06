using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.Input.Config;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

[TestFixture]
public sealed class LiveMapEditorLauncherContractTests
{
    [Test]
    public void LauncherPreset_StacksUatMapAndLiveEditorCapabilityWithHostCefRuntime()
    {
        string repoRoot = FindRepoRoot();
        JsonObject presets = ReadObject(Path.Combine(repoRoot, "launcher.presets.json"));
        JsonArray presetArray = presets["presets"] as JsonArray
            ?? throw new InvalidDataException("launcher.presets.json must contain a presets array.");
        JsonObject preset = presetArray
            .OfType<JsonObject>()
            .Single(obj => string.Equals(obj["id"]?.GetValue<string>(), "live_map_editor_nav_grid_cef_raylib", StringComparison.Ordinal));
        string[] selectors = (preset["selectors"] as JsonArray
                ?? throw new InvalidDataException("live map editor preset must declare selectors."))
            .Select(node => node?.GetValue<string>() ?? string.Empty)
            .ToArray();

        Assert.That(selectors, Is.EqualTo(new[]
        {
            "$live_map_editor_nav_grid_uat",
            "$live_map_editor"
        }));
        Assert.That(preset["adapterId"]?.GetValue<string>(), Is.EqualTo("raylib"));
        AssertHostCefRuntime(preset);
    }

    [Test]
    public void LauncherPreset_StacksIntegratedTerrainNavTransportUatAndLiveEditorCapability()
    {
        string repoRoot = FindRepoRoot();
        JsonObject presets = ReadObject(Path.Combine(repoRoot, "launcher.presets.json"));
        JsonArray presetArray = presets["presets"] as JsonArray
            ?? throw new InvalidDataException("launcher.presets.json must contain a presets array.");
        JsonObject preset = presetArray
            .OfType<JsonObject>()
            .Single(obj => string.Equals(obj["id"]?.GetValue<string>(), "live_map_editor_integrated_nav_transport_cef_raylib", StringComparison.Ordinal));
        string[] selectors = (preset["selectors"] as JsonArray
                ?? throw new InvalidDataException("integrated live map editor preset must declare selectors."))
            .Select(node => node?.GetValue<string>() ?? string.Empty)
            .ToArray();

        Assert.That(selectors, Is.EqualTo(new[]
        {
            "$live_map_editor_integrated_nav_transport_uat",
            "$live_map_editor"
        }));
        Assert.That(preset["adapterId"]?.GetValue<string>(), Is.EqualTo("raylib"));
        AssertHostCefRuntime(preset);
    }

    [Test]
    public void LauncherPreset_KeepsTransportNetworkEntryAsDebugOnly()
    {
        string repoRoot = FindRepoRoot();
        JsonObject presets = ReadObject(Path.Combine(repoRoot, "launcher.presets.json"));
        JsonArray presetArray = presets["presets"] as JsonArray
            ?? throw new InvalidDataException("launcher.presets.json must contain a presets array.");
        JsonObject preset = presetArray
            .OfType<JsonObject>()
            .Single(obj => string.Equals(obj["id"]?.GetValue<string>(), "live_map_editor_transport_network_cef_raylib", StringComparison.Ordinal));
        string[] selectors = (preset["selectors"] as JsonArray
                ?? throw new InvalidDataException("live map editor transport preset must declare selectors."))
            .Select(node => node?.GetValue<string>() ?? string.Empty)
            .ToArray();

        Assert.That(selectors, Is.EqualTo(new[]
        {
            "$capability_standard_transport_network",
            "$live_map_editor"
        }));
        Assert.That(preset["name"]?.GetValue<string>(), Does.Contain("Debug Only"));
        Assert.That(preset["adapterId"]?.GetValue<string>(), Is.EqualTo("raylib"));
        AssertHostCefRuntime(preset);
    }

    [Test]
    public void LauncherConfig_RegistersLiveMapEditorModsByStrictPath()
    {
        string repoRoot = FindRepoRoot();
        JsonObject config = ReadObject(Path.Combine(repoRoot, "launcher.config.json"));
        JsonArray bindings = config["bindings"] as JsonArray
            ?? throw new InvalidDataException("launcher.config.json must contain a bindings array.");

        AssertModPath(
            bindings,
            "live_map_editor",
            "mods/capabilities/live_map_editor/LiveMapEditorMod",
            "LiveMapEditorMod.csproj");
        AssertModPath(
            bindings,
            "live_map_editor_nav_grid_uat",
            "mods/capabilities/live_map_editor/LiveMapEditorNavGridUatMod",
            expectedProjectPath: null);
        AssertModPath(
            bindings,
            "live_map_editor_integrated_nav_transport_uat",
            "mods/capabilities/live_map_editor/LiveMapEditorIntegratedUatMod",
            expectedProjectPath: null);
    }

    [Test]
    public void UatMapFragment_DeclaresExplicitLiveEditorSaveTarget()
    {
        string repoRoot = FindRepoRoot();
        JsonObject map = ReadObject(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorNavGridUatMod",
            "assets",
            "Maps",
            "live_editor_nav_grid.json"));

        Assert.That(map["Metadata"]?["liveMapEditor"]?["saveTarget"]?.GetValue<bool>(), Is.True);
        Assert.That(map["ParentId"]?.GetValue<string>(), Is.EqualTo("nav_editor_grid"));
        Assert.That(map["DefaultCamera"]?["VirtualCameraId"]?.GetValue<string>(), Is.EqualTo("LiveMapEditor.Camera.AuthoringGrid"));
        Assert.That((map["Tags"] as JsonArray)?.Select(node => node?.GetValue<string>()).ToArray(), Does.Not.Contain("Raylib.Background:Deep"));
        Assert.That(map["Boards"] as JsonArray, Is.Not.Null.And.Count.GreaterThan(0));
        foreach (JsonObject board in (map["Boards"] as JsonArray)!.OfType<JsonObject>())
        {
            Assert.That(board["DataFile"], Is.Null);
        }
    }

    [Test]
    public void IntegratedUatMap_DeclaresGridPrimaryAndSingleNodeGraphBoard()
    {
        string repoRoot = FindRepoRoot();
        string modRoot = Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorIntegratedUatMod");
        JsonObject map = ReadObject(Path.Combine(
            modRoot,
            "assets",
            "Maps",
            "live_editor_integrated_nav_transport.json"));
        JsonObject pathing = ReadObject(Path.Combine(
            modRoot,
            "assets",
            "Configs",
            "Navigation",
            "pathing.json"));
        JsonObject navmesh = ReadObject(Path.Combine(
            modRoot,
            "assets",
            "Configs",
            "Navigation",
            "navmesh.json"));
        JsonObject transport = ReadObject(Path.Combine(
            modRoot,
            "assets",
            "TransportNetwork",
            "transport_network.json"));

        Assert.That(map["Metadata"]?["liveMapEditor"]?["saveTarget"]?.GetValue<bool>(), Is.True);
        Assert.That(map["ParentId"], Is.Null);
        Assert.That((map["Tags"] as JsonArray)?.Select(node => node?.GetValue<string>()).ToArray(), Does.Contain("Feature.NavMesh:On"));

        JsonArray boards = map["Boards"] as JsonArray
            ?? throw new InvalidDataException("integrated UAT map must declare boards.");
        Assert.That(boards, Has.Count.EqualTo(2));
        Assert.That(boards[0]?["Name"]?.GetValue<string>(), Is.EqualTo("default"));
        Assert.That(boards[0]?["SpatialType"]?.GetValue<string>(), Is.EqualTo("Grid"));
        Assert.That(boards[0]?["NavigationEnabled"]?.GetValue<bool>(), Is.True);
        Assert.That(boards[1]?["Name"]?.GetValue<string>(), Is.EqualTo("transport"));
        Assert.That(boards[1]?["SpatialType"]?.GetValue<string>(), Is.EqualTo("NodeGraph"));
        Assert.That(boards.OfType<JsonObject>().Count(board => string.Equals(board["SpatialType"]?.GetValue<string>(), "NodeGraph", StringComparison.Ordinal)), Is.EqualTo(1));

        string[] agentTypes = ((JsonArray)pathing["agentTypes"]!)
            .OfType<JsonObject>()
            .Select(agent => agent["id"]?.GetValue<string>() ?? string.Empty)
            .ToArray();
        Assert.That(agentTypes, Does.Contain("Humanoid"));
        Assert.That(agentTypes, Does.Contain("Transport.FootScout"));
        Assert.That(agentTypes, Does.Contain("Transport.ShallowBoat"));
        Assert.That(agentTypes, Does.Contain("Transport.DeepDraftShip"));
        string[] navmeshProfileIds = ((JsonArray)navmesh["profiles"]!)
            .OfType<JsonObject>()
            .Select(profile => profile["id"]?.GetValue<string>() ?? string.Empty)
            .ToArray();
        Assert.That(navmeshProfileIds, Does.Contain("Transport.FootScout"));
        Assert.That(navmeshProfileIds, Does.Contain("Transport.ShallowBoat"));
        Assert.That(navmeshProfileIds, Does.Contain("Transport.DeepDraftShip"));
        Assert.That(transport["segments"] as JsonArray, Is.Not.Null.And.Count.GreaterThan(0));
    }

    [Test]
    public void PresentationSystem_DrawsBoardAuthoringGuidesAtStartup()
    {
        string repoRoot = FindRepoRoot();
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "Systems",
            "LiveMapEditorPresentationSystem.cs"));

        Assert.That(source, Does.Contain("DrawBoardAuthoringGuides"));
        Assert.That(source, Does.Contain("DrawBoardAuthoringStatus"));
        Assert.That(source, Does.Contain("MaxBoardGuideLinesPerAxis"));
        Assert.That(source, Does.Contain("ResolveBoardGuideStepCm"));
        Assert.That(source, Does.Contain("TryResolveAuthoringSurface"));
        Assert.That(source, Does.Contain("ResolveLogicTerrainAuthoringBounds"));
        Assert.That(source, Does.Contain("terrain.WidthCells * terrain.HorizontalStepCm"));
        Assert.That(source, Does.Contain("terrain.HeightCells * terrain.VerticalStepCm"));
        Assert.That(source, Does.Contain("GroundOverlayShape.Line"));
    }

    [Test]
    public void UatCameraProfile_DisablesEdgePanForStableRaylibStartup()
    {
        string repoRoot = FindRepoRoot();
        JsonArray cameras = ReadArray(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorNavGridUatMod",
            "assets",
            "Configs",
            "Camera",
            "virtual_cameras.json"));
        JsonObject camera = cameras
            .OfType<JsonObject>()
            .Single(obj => string.Equals(obj["id"]?.GetValue<string>(), "LiveMapEditor.Camera.AuthoringGrid", StringComparison.Ordinal));

        Assert.That(camera["panMode"]?.GetValue<string>(), Is.EqualTo("Keyboard"));
        Assert.That(camera["panMode"]?.GetValue<string>(), Is.Not.EqualTo("KeyboardAndEdge"));
        Assert.That(camera["targetHeightMode"]?.GetValue<string>(), Is.EqualTo("VisualHeightmap"));
        Assert.That(camera["confineTargetToWorldBounds"]?.GetValue<bool>(), Is.True);
        Assert.That(camera["allowUserInput"]?.GetValue<bool>(), Is.True);
    }

    [Test]
    public void PanelAssetPath_UsesLowercaseMountedAssetsPath()
    {
        string repoRoot = FindRepoRoot();
        string ids = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "LiveMapEditorIds.cs"));

        Assert.That(ids, Does.Contain("LiveMapEditorMod:assets/live-map-editor-app/index.html"));
        Assert.That(ids, Does.Not.Contain("LiveMapEditorMod:Assets/"));
        Assert.That(File.Exists(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "assets",
            "live-map-editor-app",
            "index.html")), Is.True);
    }

    [Test]
    public void InputConfig_UsesKnownInputActionTypes()
    {
        string repoRoot = FindRepoRoot();
        JsonObject input = ReadObject(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "assets",
            "Input",
            "default_input.json"));

        JsonArray actions = input["actions"] as JsonArray
            ?? throw new InvalidDataException("LiveMapEditor input config must contain an actions array.");
        foreach (JsonObject action in actions.OfType<JsonObject>())
        {
            string id = action["id"]?.GetValue<string>() ?? "<missing>";
            string type = action["type"]?.GetValue<string>()
                ?? throw new InvalidDataException($"LiveMapEditor action '{id}' must declare type.");
            Assert.That(
                Enum.TryParse(type, ignoreCase: false, out InputActionType _),
                Is.True,
                $"LiveMapEditor input action '{id}' uses unknown InputActionType '{type}'.");
        }
    }

    [Test]
    public void LiveMapEditorModProject_CopiesWebUiRuntimeDependencies()
    {
        string repoRoot = FindRepoRoot();
        string project = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "LiveMapEditorMod.csproj"));

        Assert.That(project, Does.Contain("<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>"));
        AssertProjectReferenceCopiesLocal(project, "Ludots.UI.Browser");
        AssertProjectReferenceCopiesLocal(project, "Ludots.WebUI.Browser");
        AssertProjectReferenceCopiesLocal(project, "Ludots.WebUI.DataPlane");
    }

    [Test]
    public void WebPanel_DoesNotFallbackBrushNumbers()
    {
        string repoRoot = FindRepoRoot();
        string appJs = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "assets",
            "live-map-editor-app",
            "app.js"));

        Assert.That(appJs, Does.Not.Contain("fallback"));
        Assert.That(appJs, Does.Contain("checkValidity"));
        Assert.That(appJs, Does.Contain("waitForDataPlaneTransport"));
        Assert.That(appJs, Does.Not.Contain("throw new Error('window.ludotsDataplane missing')"));
    }

    [Test]
    public void WebPanel_ExposesDirectCreateAndLoadMapEntryPoint()
    {
        string repoRoot = FindRepoRoot();
        string html = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "assets",
            "live-map-editor-app",
            "index.html"));
        string appJs = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "assets",
            "live-map-editor-app",
            "app.js"));
        string handler = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "WebUi",
            "LiveMapEditorCommandHandler.cs"));
        string runtime = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "Runtime",
            "LiveMapEditorRuntime.cs"));

        Assert.That(html, Does.Contain("id=\"new-map\""));
        Assert.That(html, Does.Contain("id=\"create-map-load\""));
        Assert.That(appJs, Does.Contain("focusMapCreation"));
        Assert.That(appJs, Does.Contain("loadAfterCreate"));
        Assert.That(handler, Does.Contain("ReadBool(payload, \"loadAfterCreate\")"));
        Assert.That(runtime, Does.Contain("engine.LoadMap(result.MapId)"));
        Assert.That(runtime, Does.Contain("create_map_load_failed"));
    }

    [Test]
    public void ArchitectureDoc_CapturesEntityPlacementSpatialGeometrySsot()
    {
        string repoRoot = FindRepoRoot();
        string doc = File.ReadAllText(Path.Combine(
            repoRoot,
            "gitbook",
            "architecture",
            "live-map-editor-architecture.md"));

        Assert.That(doc, Does.Contain("Entity Placement And Spatial Geometry"));
        Assert.That(doc, Does.Contain("RuntimeEntitySpawnQueue"));
        Assert.That(doc, Does.Contain("MapEntity { MapId }"));
        Assert.That(doc, Does.Contain("SpatialFootprint2D"));
        Assert.That(doc, Does.Contain("ManifestationObstacleIntent2D"));
        Assert.That(doc, Does.Contain("CompoundObstacle2DState"));
        Assert.That(doc, Does.Contain("no `SelectionFootprint2D`"));
    }

    [Test]
    public void ArchitectureDoc_CapturesTransportNetworkEditorBoundary()
    {
        string repoRoot = FindRepoRoot();
        string doc = File.ReadAllText(Path.Combine(
            repoRoot,
            "gitbook",
            "architecture",
            "live-map-editor-architecture.md"));

        Assert.That(doc, Does.Contain("Transport Network Editing Boundary"));
        Assert.That(doc, Does.Contain("#462"));
        Assert.That(doc, Does.Contain("#415"));
        Assert.That(doc, Does.Contain("TransportNetworkAsset"));
        Assert.That(doc, Does.Contain("TransportNetworkBaker.Bake(asset, chunkSizeCm)"));
        Assert.That(doc, Does.Contain("ChunkedNodeGraphStore"));
        Assert.That(doc, Does.Contain("TransportNetworkRibbonSource"));
        Assert.That(doc, Does.Contain("SurfaceSourcePayloadRegistry"));
        Assert.That(doc, Does.Contain("RoadSplineBuffer"));
        Assert.That(doc, Does.Contain("GraphEdgeProjectionQuery"));
        Assert.That(doc, Does.Contain("AutoPathService"));
        Assert.That(doc, Does.Contain("transportRebake"));
        Assert.That(doc, Does.Contain("transportSave"));
        Assert.That(doc, Does.Contain("no private road graph"));
    }

    [Test]
    public void TransportNetworkEditorDocs_AreIndexedAndCaptureCoreBoundary()
    {
        string repoRoot = FindRepoRoot();
        string adr = File.ReadAllText(Path.Combine(
            repoRoot,
            "docs",
            "adr",
            "ADR-0004-transport-network-editor-boundary.md"));
        string gitbook = File.ReadAllText(Path.Combine(
            repoRoot,
            "gitbook",
            "architecture",
            "transport-network-editor.md"));
        string summary = File.ReadAllText(Path.Combine(repoRoot, "gitbook", "SUMMARY.md"));

        Assert.That(adr, Does.Contain("Status: Accepted"));
        Assert.That(adr, Does.Contain("TransportNetworkBaker.Bake(asset, chunkSizeCm)"));
        Assert.That(adr, Does.Contain("SurfaceSourcePayloadRegistry"));
        Assert.That(adr, Does.Contain("AutoPathService"));
        Assert.That(gitbook, Does.Contain("Tool Modes"));
        Assert.That(gitbook, Does.Contain("`node`"));
        Assert.That(gitbook, Does.Contain("`segment`"));
        Assert.That(gitbook, Does.Contain("`route`"));
        Assert.That(gitbook, Does.Contain("No JavaScript world renderer"));
        Assert.That(summary, Does.Contain("architecture/transport-network-editor.md"));
    }

    [Test]
    public void WebPanel_ExposesTransportNetworkControls()
    {
        string repoRoot = FindRepoRoot();
        string html = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "assets",
            "live-map-editor-app",
            "index.html"));
        string appJs = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "assets",
            "live-map-editor-app",
            "app.js"));

        Assert.That(html, Does.Contain("data-tool=\"transport\""));
        Assert.That(html, Does.Contain("data-transport-mode=\"node\""));
        Assert.That(html, Does.Contain("data-transport-mode=\"segment\""));
        Assert.That(html, Does.Contain("data-transport-mode=\"route\""));
        Assert.That(appJs, Does.Contain("transportUpdateNode"));
        Assert.That(appJs, Does.Contain("transportCommitSegment"));
        Assert.That(appJs, Does.Contain("transportInsertSegmentPoint"));
        Assert.That(appJs, Does.Contain("transportSetRouteAgent"));
        Assert.That(appJs, Does.Contain("transportQueryRoute"));
        Assert.That(appJs, Does.Contain("transportSave"));
    }

    [Test]
    public void Phase2ParityAdr_IsIndexedAndClassifiesTerrainSsot()
    {
        string repoRoot = FindRepoRoot();
        string adr = File.ReadAllText(Path.Combine(
            repoRoot,
            "docs",
            "adr",
            "ADR-0005-live-map-editor-phase2-parity-boundary.md"));
        string adrIndex = File.ReadAllText(Path.Combine(repoRoot, "docs", "adr", "README.md"));
        string gitbookIndex = File.ReadAllText(Path.Combine(repoRoot, "gitbook", "architecture", "README.md"));
        string architecture = File.ReadAllText(Path.Combine(
            repoRoot,
            "gitbook",
            "architecture",
            "live-map-editor-architecture.md"));
        string uat = File.ReadAllText(Path.Combine(
            repoRoot,
            "gitbook",
            "reference",
            "live-map-editor-uat.md"));

        Assert.That(adr, Does.Contain("Status: Accepted"));
        Assert.That(adr, Does.Contain("LogicTerrainCell"));
        Assert.That(adr, Does.Contain("HeightLevel"));
        Assert.That(adr, Does.Contain("WaterHeightLevel"));
        Assert.That(adr, Does.Contain("AreaId"));
        Assert.That(adr, Does.Contain("Biome"));
        Assert.That(adr, Does.Contain("Vegetation"));
        Assert.That(adr, Does.Contain("Deferred"));
        Assert.That(adr, Does.Contain("Ludots.Editor.Bridge"));
        Assert.That(adr, Does.Contain("NodeGraph"));
        Assert.That(adrIndex, Does.Contain("ADR-0005-live-map-editor-phase2-parity-boundary.md"));
        Assert.That(gitbookIndex, Does.Contain("ADR-0005-live-map-editor-phase2-parity-boundary.md"));
        Assert.That(architecture, Does.Contain("Phase 2 Parity Boundary"));
        Assert.That(architecture, Does.Contain("bucketFillWater"));
        Assert.That(architecture, Does.Contain("estimateNavBake"));
        Assert.That(architecture, Does.Contain("setPathOptions"));
        Assert.That(architecture, Does.Contain("setViewToggle"));
        Assert.That(architecture, Does.Contain("ManifestationObstacleIntent2D"));
        Assert.That(uat, Does.Contain("Phase 2 Parity Checks"));
        Assert.That(uat, Does.Contain("UiSurfaceSegment.Main"));
        Assert.That(uat, Does.Contain("EntityTemplateKeyRegistry.SnapshotMappings"));
    }

    [Test]
    public void RaylibWysiwygTerrain_UsesOptionalCoreFeatureSource()
    {
        string repoRoot = FindRepoRoot();
        string heightmapSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Core",
            "Presentation",
            "Terrain",
            "IVisualHeightmapRenderSource.cs"));
        string featureSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Core",
            "Presentation",
            "Terrain",
            "VisualTerrainRenderFeatures.cs"));
        string adapter = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Core",
            "Presentation",
            "Terrain",
            "LogicTerrainVisualHeightmapAdapter.cs"));
        string rules = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Core",
            "Presentation",
            "Rendering",
            "TerrainVisualRules.cs"));
        string renderer = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Client",
            "Ludots.Client.Raylib",
            "Rendering",
            "RaylibVisualHeightmapRenderer.cs"));

        Assert.That(heightmapSource, Does.Not.Contain("TryReadFeatureCell"));
        Assert.That(featureSource, Does.Contain("IVisualTerrainRenderFeatureSource"));
        Assert.That(featureSource, Does.Contain("VisualTerrainRenderCell"));
        Assert.That(adapter, Does.Contain("IVisualTerrainRenderFeatureSource"));
        Assert.That(adapter, Does.Contain("source.WaterHeightLevel"));
        Assert.That(adapter, Does.Contain("source.AreaId"));
        Assert.That(rules, Does.Contain("GetTerrainFeatureColor"));
        Assert.That(rules, Does.Contain("cell.IsBlocked"));
        Assert.That(renderer, Does.Contain("CreateWaterMesh"));
        Assert.That(renderer, Does.Contain("DrawFeatureEdges"));
        Assert.That(renderer, Does.Contain("IVisualTerrainRenderFeatureSource? featureSource"));
    }

    [Test]
    public void WebPanel_ExposesPhase2BrushBakePathViewAndMinimapControls()
    {
        string repoRoot = FindRepoRoot();
        string html = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "assets",
            "live-map-editor-app",
            "index.html"));
        string appJs = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "assets",
            "live-map-editor-app",
            "app.js"));

        Assert.That(html, Does.Contain("data-brush-mode=\"set\""));
        Assert.That(html, Does.Contain("data-brush-mode=\"raise\""));
        Assert.That(html, Does.Contain("data-brush-mode=\"lower\""));
        Assert.That(html, Does.Contain("id=\"brush-target\""));
        Assert.That(html, Does.Contain("id=\"brush-water-height\""));
        Assert.That(html, Does.Contain("id=\"water-bucket\""));
        Assert.That(html, Does.Contain("id=\"command-status\""));
        Assert.That(html, Does.Contain("id=\"brush-status\""));
        Assert.That(html, Does.Contain("data-view-toggle=\"navmesh\""));
        Assert.That(html, Does.Contain("data-view-toggle=\"minimap\""));
        Assert.That(html, Does.Contain("id=\"bake-scope\""));
        Assert.That(html, Does.Contain("id=\"nav-config-mode\""));
        Assert.That(html, Does.Contain("id=\"nav-agent-profiles\""));
        Assert.That(html, Does.Contain("id=\"nav-bake-profiles\""));
        Assert.That(html, Does.Contain("id=\"nav-layers\""));
        Assert.That(html, Does.Contain("id=\"nav-areas\""));
        Assert.That(html, Does.Contain("id=\"path-profile\""));
        Assert.That(html, Does.Contain("id=\"minimap\""));
        Assert.That(html, Does.Contain("id=\"transport-unavailable\""));
        Assert.That(html, Does.Contain("id=\"obstacle-shape\""));
        Assert.That(html, Does.Contain("id=\"place-obstacle\""));
        Assert.That(html, Does.Contain("id=\"entity-override-json\""));
        Assert.That(html, Does.Contain("id=\"create-map\""));
        Assert.That(html, Does.Contain("id=\"add-board\""));
        Assert.That(html, Does.Contain("id=\"board-stack\""));
        Assert.That(html, Does.Contain("id=\"update-board\""));
        Assert.That(html, Does.Contain("id=\"reload-map\""));
        Assert.That(appJs, Does.Contain("state.map?.id"));
        Assert.That(appJs, Does.Contain("isDataPlaneEnvelope"));
        Assert.That(appJs, Does.Contain("typeof value.kind === 'string'"));
        Assert.That(appJs, Does.Contain("typeof value.topic === 'string'"));
        Assert.That(appJs, Does.Contain("bucketFillWater"));
        Assert.That(appJs, Does.Contain("renderBrushPickState"));
        Assert.That(appJs, Does.Contain("formatCommandError"));
        Assert.That(appJs, Does.Contain("setBakeOptions"));
        Assert.That(appJs, Does.Contain("navConfigSave"));
        Assert.That(appJs, Does.Contain("navConfigReload"));
        Assert.That(appJs, Does.Contain("navAddProfile"));
        Assert.That(appJs, Does.Contain("navAddBakeProfile"));
        Assert.That(appJs, Does.Contain("navAddLayer"));
        Assert.That(appJs, Does.Contain("navAddArea"));
        Assert.That(appJs, Does.Contain("estimateNavBake"));
        Assert.That(appJs, Does.Contain("rebakeNav"));
        Assert.That(appJs, Does.Contain("clearNavTiles"));
        Assert.That(appJs, Does.Contain("setPathOptions"));
        Assert.That(appJs, Does.Contain("setViewToggle"));
        Assert.That(appJs, Does.Contain("cameraPanTo"));
        Assert.That(appJs, Does.Contain("drawMinimapCamera"));
        Assert.That(appJs, Does.Contain("chunk.dirty"));
        Assert.That(appJs, Does.Contain("placeObstacle"));
        Assert.That(appJs, Does.Contain("setEntityOverride"));
        Assert.That(appJs, Does.Contain("renderMinimap"));
        Assert.That(appJs, Does.Contain("previewBoardAllocation"));
        Assert.That(appJs, Does.Contain("createMap"));
        Assert.That(appJs, Does.Contain("addBoard"));
        Assert.That(appJs, Does.Contain("renderBoardStack"));
        Assert.That(appJs, Does.Contain("renderAllocationPreview"));
        Assert.That(appJs, Does.Contain("No NodeGraph board"));
        Assert.That(appJs, Does.Not.Contain("Ludots.Editor.Bridge"));
    }

    [Test]
    public void PanelController_RegistersTransportNetworkCommands()
    {
        string repoRoot = FindRepoRoot();
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "UI",
            "LiveMapEditorPanelController.cs"));

        string[] commands =
        {
            "transportSetMode",
            "transportSetRoot",
            "transportAddNode",
            "transportSelectNode",
            "transportMoveNode",
            "transportUpdateNode",
            "transportDeleteNode",
            "transportBeginSegment",
            "transportAppendSegmentPoint",
            "transportUndoSegmentPoint",
            "transportCommitSegment",
            "transportSelectSegment",
            "transportUpdateSegment",
            "transportInsertSegmentPoint",
            "transportMoveSegmentPoint",
            "transportDeleteSegmentPoint",
            "transportDeleteSegment",
            "transportRebake",
            "transportSetRouteAgent",
            "transportQueryRoute",
            "transportSave"
        };

        foreach (string command in commands)
        {
            Assert.That(source, Does.Contain($"router.Register(\"{command}\", handler);"));
        }
    }

    [Test]
    public void PanelController_RegistersPhase2ParityCommands()
    {
        string repoRoot = FindRepoRoot();
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "UI",
            "LiveMapEditorPanelController.cs"));
        string handler = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "WebUi",
            "LiveMapEditorCommandHandler.cs"));
        string runtime = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "Runtime",
            "LiveMapEditorRuntime.cs"));
        string stateProducer = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "WebUi",
            "LiveMapEditorStateTopicProducer.cs"));

        string[] commands =
        {
            "bucketFillWater",
            "setBakeOptions",
            "estimateNavBake",
            "rebakeNav",
            "clearNavTiles",
            "setPathOptions",
            "setViewToggle",
            "cameraPanTo",
            "previewBoardAllocation",
            "createMap",
            "addBoard",
            "deleteBoard",
            "updateBoard",
            "selectBoard",
            "reloadMap",
            "setObstacle",
            "placeObstacle",
            "eraseObstacle",
            "setEntityOverride",
            "deleteEntityOverride",
            "navConfigReload",
            "navConfigSave",
            "navAddProfile",
            "navDeleteProfile",
            "navAddBakeProfile",
            "navDeleteBakeProfile",
            "navAddLayer",
            "navDeleteLayer",
            "navAddArea",
            "navDeleteArea",
            "navSetMode",
            "navSetAlgorithm",
            "navSetRuntimeField"
        };

        foreach (string command in commands)
        {
            Assert.That(source, Does.Contain($"router.Register(\"{command}\", handler);"));
            Assert.That(handler, Does.Contain($"\"{command}\""));
        }

        Assert.That(runtime, Does.Contain("BucketFillWater"));
        Assert.That(runtime, Does.Contain("NormalizeBakeScope"));
        Assert.That(runtime, Does.Contain("NavTileStore"));
        Assert.That(runtime, Does.Contain("NavMeshProfileRegistry"));
        Assert.That(runtime, Does.Contain("WaterHeightLevel"));
        Assert.That(runtime, Does.Contain("LogicTerrainSurfaceFlags.Water"));
        Assert.That(runtime, Does.Contain("SetViewToggle"));
        Assert.That(runtime, Does.Contain("ManifestationObstacleIntent2D"));
        Assert.That(runtime, Does.Contain("RuntimeNavMeshStructuralObstacle"));
        Assert.That(runtime, Does.Contain("SetSelectedEntityOverride"));
        Assert.That(runtime, Does.Contain("Nav.MaxPortals"));
        Assert.That(runtime, Does.Contain("NavigationConfigAuthoringWriter"));
        Assert.That(runtime, Does.Contain("ResolveWritableMapConfigTargetModId"));
        Assert.That(runtime, Does.Contain("ReloadNavConfig"));
        Assert.That(runtime, Does.Contain("BoardAllocationPreviewCalculator"));
        Assert.That(runtime, Does.Contain("CreateMap"));
        Assert.That(runtime, Does.Contain("AddBoard"));
        Assert.That(runtime, Does.Contain("UpdateBoardSettings"));
        Assert.That(runtime, Does.Contain("ReloadCurrentMap"));
        Assert.That(stateProducer, Does.Contain("EntityTemplateKeyRegistry"));
        Assert.That(stateProducer, Does.Contain("SnapshotMappings"));
        Assert.That(stateProducer, Does.Contain("CaptureMinimap"));
        Assert.That(stateProducer, Does.Contain("CaptureCamera"));
        Assert.That(stateProducer, Does.Contain("CaptureNavConfig"));
        Assert.That(stateProducer, Does.Contain("CaptureSimulationProfile"));
        Assert.That(stateProducer, Does.Contain("maxPortals = _runtime.Nav.MaxPortals"));
        Assert.That(stateProducer, Does.Contain("dirty = IsMinimapChunkDirty"));
        Assert.That(stateProducer, Does.Contain("CaptureAgentProfiles"));
        Assert.That(stateProducer, Does.Contain("CaptureNavProfiles"));
        Assert.That(stateProducer, Does.Contain("obstacleCount"));
        Assert.That(stateProducer, Does.Contain("CaptureEntityOverrides"));
        Assert.That(stateProducer, Does.Contain("CaptureAuthoredBoards"));
        Assert.That(stateProducer, Does.Contain("CaptureAllocationPreview"));
    }

    [Test]
    public void NavigationConfigAuthoringWriter_WritesAgentProfilesAndNavMeshToMountedMod()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"ludots_nav_config_writer_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("TargetNavMod", tempRoot);
            var agents = new[]
            {
                new AgentProfileConfig
                {
                    Id = "Scout",
                    RadiusCm = 32f,
                    HeightCm = 180f,
                    ClearanceCm = 24f,
                    DraftCm = 0f,
                    BeamCm = 0f,
                    Mass = 1f,
                    Layer = 0
                }
            };
            var config = new NavMeshBakeConfig
            {
                Mode = "runtime-incremental",
                Algorithm = "cdt",
                Profiles = new()
                {
                    new NavMeshAgentProfileConfig
                    {
                        Id = "Scout",
                        MaxClimbCm = 40,
                        MaxSlopeDeg = 45f
                    }
                },
                Layers = new()
                {
                    new NavLayerConfig { Id = "Ground", Layer = 0 }
                },
                Areas = new()
                {
                    new NavAreaCostConfig { Id = "Default", AreaId = 0, Cost = 1f }
                },
                RuntimeIncremental = new NavRuntimeIncrementalConfig
                {
                    TileBudgetPerFixedTick = 4,
                    IncludeNeighborTiles = true,
                    HeightScaleMeters = 1f,
                    MinWalkableUpDot = 0.6f,
                    CliffHeightThreshold = 1
                }
            };

            NavigationConfigAuthoringSaveResult result =
                new NavigationConfigAuthoringWriter(vfs).Save("TargetNavMod", agents, config);

            Assert.That(result.AgentProfileCount, Is.EqualTo(1));
            Assert.That(result.BakeProfileCount, Is.EqualTo(1));
            Assert.That(File.Exists(result.AgentProfilesPath), Is.True);
            Assert.That(File.Exists(result.NavMeshPath), Is.True);
            JsonArray writtenAgents = ReadArray(result.AgentProfilesPath);
            JsonObject writtenNavMesh = ReadObject(result.NavMeshPath);
            Assert.That(writtenAgents[0]?["id"]?.GetValue<string>(), Is.EqualTo("Scout"));
            Assert.That(writtenNavMesh["mode"]?.GetValue<string>(), Is.EqualTo("runtime-incremental"));
            Assert.That(writtenNavMesh["algorithm"]?.GetValue<string>(), Is.EqualTo("cdt"));
            Assert.That(writtenNavMesh["runtimeIncremental"]?["tileBudgetPerFixedTick"]?.GetValue<int>(), Is.EqualTo(4));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public void LiveMapEditorMod_ProvidesDefaultObstacleTemplate()
    {
        string repoRoot = FindRepoRoot();
        JsonArray templates = ReadArray(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "assets",
            "Entities",
            "templates.json"));

        JsonObject template = templates
            .OfType<JsonObject>()
            .Single(obj => string.Equals(obj["id"]?.GetValue<string>(), "live_map_editor_obstacle", StringComparison.Ordinal));
        Assert.That(template["components"]?["Name"]?["Value"]?.GetValue<string>(), Is.EqualTo("LiveMapEditor.Obstacle"));
    }

    [Test]
    public void PresentationSystem_GatesPhase2DebugOverlaysFromViewState()
    {
        string repoRoot = FindRepoRoot();
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "Systems",
            "LiveMapEditorPresentationSystem.cs"));

        Assert.That(source, Does.Contain("_runtime.View.ShowGrid"));
        Assert.That(source, Does.Contain("_runtime.View.ShowChunks"));
        Assert.That(source, Does.Contain("_runtime.View.ShowNavMesh"));
        Assert.That(source, Does.Contain("_runtime.View.ShowPath"));
        Assert.That(source, Does.Contain("_runtime.View.ShowTransport"));
        Assert.That(source, Does.Contain("_runtime.View.ShowEntities"));
        Assert.That(source, Does.Contain("_runtime.PlaceObstacle"));
        Assert.That(source, Does.Contain("_runtime.EraseObstacleAt"));
    }

    [Test]
    public void PanelController_UsesExclusiveMainSurfaceForPhase2()
    {
        string repoRoot = FindRepoRoot();
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "UI",
            "LiveMapEditorPanelController.cs"));

        Assert.That(source, Does.Contain("UiSurfaceSegment.Main"));
        Assert.That(source, Does.Contain("exclusive: true"));
        Assert.That(source, Does.Contain("exclusive Main lease published"));
    }

    [Test]
    public void RaylibHostLoop_ProvidesRealWindowFrameCaptureForLiveEditorUat()
    {
        string repoRoot = FindRepoRoot();
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Adapters",
            "Raylib",
            "Ludots.Adapter.Raylib",
            "RaylibHostLoop.cs"));

        Assert.That(source, Does.Contain("LUDOTS_RAYLIB_FRAME_CAPTURE_DIR"));
        Assert.That(source, Does.Contain("LUDOTS_RAYLIB_FRAME_CAPTURE_START_FRAME"));
        Assert.That(source, Does.Contain("LUDOTS_RAYLIB_FRAME_CAPTURE_END_FRAME"));
        Assert.That(source, Does.Contain("SaveRaylibScreenshot"));
    }

    [Test]
    public void PresentationSystem_ProvidesAutoOpenPanelHookForRealWindowUat()
    {
        string repoRoot = FindRepoRoot();
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "Systems",
            "LiveMapEditorPresentationSystem.cs"));

        Assert.That(source, Does.Contain("LUDOTS_LIVE_MAP_EDITOR_AUTO_OPEN_FRAME"));
        Assert.That(source, Does.Contain("Auto-opening panel"));
        Assert.That(source, Does.Contain("_panelController.Show()"));
    }


    [Test]
    public void TransportShowcase_ProvidesDataDrivenRouteProfilesForCapacityValidation()
    {
        string repoRoot = FindRepoRoot();
        JsonArray profiles = ReadArray(Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            "CapabilityStandardTransportNetworkMod",
            "assets",
            "Configs",
            "Navigation",
            "agent_profiles.json"));
        JsonObject pathing = ReadObject(Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            "CapabilityStandardTransportNetworkMod",
            "assets",
            "Configs",
            "Navigation",
            "pathing.json"));

        string[] profileIds = profiles
            .OfType<JsonObject>()
            .Select(obj => obj["id"]?.GetValue<string>() ?? string.Empty)
            .ToArray();
        Assert.That(profileIds, Does.Contain("Transport.FootScout"));
        Assert.That(profileIds, Does.Contain("Transport.ShallowBoat"));
        Assert.That(profileIds, Does.Contain("Transport.DeepDraftShip"));
        Assert.That(profiles.OfType<JsonObject>()
            .Single(obj => string.Equals(obj["id"]?.GetValue<string>(), "Transport.DeepDraftShip", StringComparison.Ordinal))["draftCm"]?.GetValue<double>(), Is.EqualTo(500.0));

        JsonArray agentTypes = pathing["agentTypes"] as JsonArray
            ?? throw new InvalidDataException("Transport pathing config must declare agentTypes.");
        string[] agentTypeIds = agentTypes
            .OfType<JsonObject>()
            .Select(obj => obj["id"]?.GetValue<string>() ?? string.Empty)
            .ToArray();
        Assert.That(agentTypeIds, Does.Contain("Transport.FootScout"));
        Assert.That(agentTypeIds, Does.Contain("Transport.ShallowBoat"));
        Assert.That(agentTypeIds, Does.Contain("Transport.DeepDraftShip"));
        Assert.That(pathing.ToJsonString(), Does.Contain("Transport.Area.Water"));
        Assert.That(pathing.ToJsonString(), Does.Contain("Transport.Flow.Upstream"));
    }

    [Test]
    public void TransportRuntime_UsesCoreBakerAndNeutralNaming()
    {
        string repoRoot = FindRepoRoot();
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "Runtime",
            "LiveMapEditorTransportAuthoring.cs"));
        string ribbonSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Core",
            "TransportNetwork",
            "TransportNetworkRibbonSource.cs"));
        string showcaseRuntime = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            "CapabilityStandardTransportNetworkMod",
            "Runtime",
            "CapabilityStandardTransportNetworkRuntime.cs"));
        string gameEngine = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "Engine", "GameEngine.cs"));

        Assert.That(source, Does.Contain("TransportNetworkAssetLoader"));
        Assert.That(source, Does.Contain("TransportNetworkBaker().Bake"));
        Assert.That(source, Does.Contain("graphBoard.GraphStore.Clear"));
        Assert.That(source, Does.Contain("BoardResolution.RequireSingleNodeGraphBoard"));
        Assert.That(source, Does.Contain("TransportNetworkRibbonSource"));
        Assert.That(source, Does.Contain("TransportNetworkRibbonSource.ComposeDefaultSurfaceScopeId"));
        Assert.That(source, Does.Contain("GraphEdgeProjectionQuery"));
        Assert.That(source, Does.Contain("LoadedChunkSolvePrimer"));
        Assert.That(source, Does.Contain("PolylineGoalSnapQuery"));
        Assert.That(source, Does.Contain("PathService"));
        Assert.That(ribbonSource, Does.Contain("ComposeDefaultSurfaceScopeId"));
        Assert.That(gameEngine, Does.Contain("BoardResolution.TryGetSingleNodeGraphBoard"));
        Assert.That(gameEngine, Does.Contain("graphBoard='"));
        Assert.That(showcaseRuntime, Does.Contain("TransportNetworkRibbonSource.ComposeDefaultSurfaceScopeId"));
        Assert.That(showcaseRuntime, Does.Contain("BoardResolution.RequireSingleNodeGraphBoard"));
        Assert.That(showcaseRuntime, Does.Not.Contain("ComposeSurfaceScopeId"));
        Assert.That(source, Does.Not.Contain("ComposeSurfaceScopeId"));
        Assert.That(source, Does.Not.Contain("corridor"));
        Assert.That(source, Does.Not.Contain("fort"));
        Assert.That(source, Does.Not.Contain("private road graph"));
    }

    private static void AssertModPath(
        JsonArray mods,
        string name,
        string expectedPath,
        string? expectedProjectPath)
    {
        JsonObject mod = mods
            .OfType<JsonObject>()
            .Single(obj => string.Equals(obj["name"]?.GetValue<string>(), name, StringComparison.Ordinal));
        JsonObject target = mod["target"] as JsonObject
            ?? throw new InvalidDataException($"Launcher mod '{name}' target must be an object.");

        Assert.That(target["type"]?.GetValue<string>(), Is.EqualTo("path"));
        Assert.That(target["value"]?.GetValue<string>(), Is.EqualTo(expectedPath));
        if (expectedProjectPath == null)
        {
            Assert.That(target["projectPath"], Is.Null);
        }
        else
        {
            Assert.That(target["projectPath"]?.GetValue<string>(), Is.EqualTo(expectedProjectPath));
        }
    }

    private static void AssertHostCefRuntime(JsonObject preset)
    {
        JsonObject browserRuntime = preset["browserRuntime"] as JsonObject
            ?? throw new InvalidDataException("CEF preset must declare browserRuntime.");
        Assert.Multiple(() =>
        {
            Assert.That(browserRuntime["enabled"]?.GetValue<bool>(), Is.True);
            Assert.That(browserRuntime["required"]?.GetValue<bool>(), Is.True);
            Assert.That(browserRuntime["provider"]?.GetValue<string>(), Is.EqualTo("cef"));
        });
    }

    private static void AssertProjectReferenceCopiesLocal(string project, string projectName)
    {
        string include = $"src\\Libraries\\{projectName}\\{projectName}.csproj";
        int start = project.IndexOf(include, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Missing project reference to {projectName}.");
        int end = project.IndexOf("</ProjectReference>", start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start), $"Project reference to {projectName} must use an explicit closing tag.");
        string block = project[start..end];
        Assert.That(block, Does.Contain("<Private>true</Private>"), $"{projectName} must be copied beside the mod assembly.");
    }

    private static JsonObject ReadObject(string path)
        => JsonNode.Parse(File.ReadAllText(path)) as JsonObject
           ?? throw new InvalidDataException($"{path} must contain a JSON object.");

    private static JsonArray ReadArray(string path)
        => JsonNode.Parse(File.ReadAllText(path)) as JsonArray
           ?? throw new InvalidDataException($"{path} must contain a JSON array.");

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "launcher.config.json")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root containing launcher.config.json");
    }
}
