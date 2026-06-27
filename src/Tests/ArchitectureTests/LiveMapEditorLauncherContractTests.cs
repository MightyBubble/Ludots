using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.Input.Config;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

[TestFixture]
public sealed class LiveMapEditorLauncherContractTests
{
    [Test]
    public void LauncherPreset_StacksCefRuntimeUatMapAndLiveEditorCapability()
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
            "$browser_cef_runtime",
            "$live_map_editor_nav_grid_uat",
            "$live_map_editor"
        }));
        Assert.That(preset["adapterId"]?.GetValue<string>(), Is.EqualTo("raylib"));
    }

    [Test]
    public void LauncherPreset_StacksCefRuntimeTransportNetworkAndLiveEditorCapability()
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
            "$browser_cef_runtime",
            "$capability_standard_transport_network",
            "$live_map_editor"
        }));
        Assert.That(preset["adapterId"]?.GetValue<string>(), Is.EqualTo("raylib"));
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

        Assert.That(source, Does.Contain("TransportNetworkAssetLoader"));
        Assert.That(source, Does.Contain("TransportNetworkBaker().Bake"));
        Assert.That(source, Does.Contain("graphBoard.GraphStore.Clear"));
        Assert.That(source, Does.Contain("TransportNetworkRibbonSource"));
        Assert.That(source, Does.Contain("TransportNetworkRibbonSource.ComposeDefaultSurfaceScopeId"));
        Assert.That(source, Does.Contain("GraphEdgeProjectionQuery"));
        Assert.That(source, Does.Contain("LoadedChunkSolvePrimer"));
        Assert.That(source, Does.Contain("PolylineGoalSnapQuery"));
        Assert.That(source, Does.Contain("PathService"));
        Assert.That(ribbonSource, Does.Contain("ComposeDefaultSurfaceScopeId"));
        Assert.That(showcaseRuntime, Does.Contain("TransportNetworkRibbonSource.ComposeDefaultSurfaceScopeId"));
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
