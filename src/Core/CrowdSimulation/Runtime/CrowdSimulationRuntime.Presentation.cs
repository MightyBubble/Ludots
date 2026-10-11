using System;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Fields;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.CrowdSimulation.Runtime;

public sealed partial class CrowdSimulationRuntime
{
    private bool _pathPresentationInstalled;
    private bool _deployPresentationInstalled;
    private S3PathDemoRuntime? _pathDemo;
    private S4DeployDemoRuntime? _deployDemo;

    public bool ReleasePathPresentation(GameEngine engine, MapId mapId)
    {
        if (_pathDemo == null || _configDto == null || _debug == null) return false;
        if (!string.Equals(mapId.Value, _configDto.MapId, StringComparison.Ordinal)) return false;
        if (PresentationKind(_debug) != "path") return false;
        _pathDemo.Active = false;
        _pathDemo.Dispose();
        _pathDemo = null;
        _pathPresentationInstalled = false;
        return true;
    }

    private void InstallPathPresentation(GameEngine engine, MapConfig mapConfig)
    {
        if (_pathPresentationInstalled) return;
        var presentation = RequirePresentation(_debug!);
        var bindings = new CrowdPathBindings
        {
            SetStart = RequireAction(presentation, "setStart"),
            SetGoal = RequireAction(presentation, "setGoal"),
            CycleContext = RequireAction(presentation, "cycleContext"),
            ToggleThreads = RequireAction(presentation, "toggleThreads"),
            ToggleSlow = RequireAction(presentation, "toggleSlow"),
            CycleView = RequireAction(presentation, "cycleView"),
        };
        string truth = presentation["truthAsset"]?.GetValue<string>()
            ?? throw new InvalidOperationException("CrowdSimulationDebug.json presentation.truthAsset 必填(path)。");
        string title = RequireTitle(presentation);
        var (registry, routes) = EnsureVisualServices(engine);
        var demo = S3PathDemoRuntime.Load(engine, _configDto!, mapConfig, truth);
        var flow = new CrowdFlowFieldVisualSource
        {
            CellCount = demo.Nav.CellCount,
            CellSizeCm = demo.Config.NavCellSizeCm,
        };
        demo.FlowVisual = flow;
        registry.Register(new DemoViewProjector(
            demo,
            new CrowdFlowFieldVisualProjector(flow),
            new CrowdWalkableVisualProjector(flow, () => demo.Nav)));
        var overlay = RequireOverlay(engine);
        engine.RegisterSystem(new S3PathInputSystem(engine, demo, bindings), SystemGroup.PostMovement);
        engine.RegisterSystem(new S3PathDemoSimulationSystem(demo), SystemGroup.PostMovement);
        engine.RegisterPresentationSystem(new S3PathDemoPresentationSystem(demo, routes, overlay, title));
        _pathDemo = demo;
        _pathPresentationInstalled = true;
    }

    private void InstallDeployPresentation(GameEngine engine)
    {
        if (_deployPresentationInstalled) return;
        var presentation = RequirePresentation(_debug!);
        var bindings = new CrowdDeployBindings
        {
            CycleView = RequireAction(presentation, "cycleView"),
            ToggleBuild = RequireAction(presentation, "toggleBuild"),
            Remove = RequireAction(presentation, "remove"),
            Place = RequireAction(presentation, "place"),
            Template1 = RequireAction(presentation, "template1"),
            Template2 = RequireAction(presentation, "template2"),
            Template3 = RequireAction(presentation, "template3"),
        };
        var structures = presentation["structures"]?.AsArray()
            ?? throw new InvalidOperationException("CrowdSimulationDebug.json presentation.structures 必填(deploy)。");
        if (structures.Count != 3)
        {
            throw new InvalidOperationException("CrowdSimulationDebug.json presentation.structures 需要 3 条,与模板动作一一对应。");
        }

        var choices = new S4DeployDemoRuntime.StructureChoice[3];
        for (int i = 0; i < 3; i++)
        {
            var item = structures[i]!.AsObject();
            choices[i] = new S4DeployDemoRuntime.StructureChoice(
                item["id"]?.GetValue<string>() ?? throw new InvalidOperationException("presentation.structures.id 必填。"),
                item["label"]?.GetValue<string>() ?? throw new InvalidOperationException("presentation.structures.label 必填。"),
                item["sizeCm"]?.GetValue<int>() ?? throw new InvalidOperationException("presentation.structures.sizeCm 必填。"),
                item["roadLengthCm"]?.GetValue<int>() ?? 0);
        }

        string title = RequireTitle(presentation);
        var (registry, routes) = EnsureVisualServices(engine);
        var demo = new S4DeployDemoRuntime(engine);
        demo.UseStructures(choices);
        registry.Register(new S4DeployDemoViewProjector(
            demo,
            () => engine.TryGetService(CoreServiceKeys.CrowdSimulationSession, out var session) ? session : null));
        var overlay = RequireOverlay(engine);
        engine.InsertSystemBeforeRequired<CrowdSimulationSessionTickSystem>(
            new S4DeployInputSystem(engine, demo, bindings), SystemGroup.PostMovement);
        engine.RegisterPresentationSystem(new S4DeployDemoPresentationSystem(
            demo,
            routes,
            overlay,
            () => engine.TryGetService(CoreServiceKeys.CrowdSimulationRuntime, out var runtime) ? runtime : null,
            () => engine.TryGetService(CoreServiceKeys.CrowdSimulationSession, out var session) ? session : null,
            title));
        _deployDemo = demo;
        _deployPresentationInstalled = true;
    }

    private static (GlobalFieldVisualProjectorRegistry Registry, RouteVisualBuffer Routes) EnsureVisualServices(GameEngine engine)
    {
        var registry = engine.GetService(CoreServiceKeys.GlobalFieldVisualProjectorRegistry);
        if (registry == null)
        {
            registry = new GlobalFieldVisualProjectorRegistry();
            engine.SetService(CoreServiceKeys.GlobalFieldVisualProjectorRegistry, registry);
        }

        var routes = engine.GetService(CoreServiceKeys.RouteVisualBuffer);
        if (routes == null)
        {
            routes = new RouteVisualBuffer();
            engine.SetService(CoreServiceKeys.RouteVisualBuffer, routes);
        }

        return (registry, routes);
    }

    private static ScreenOverlayBuffer RequireOverlay(GameEngine engine) =>
        engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
        ?? throw new InvalidOperationException("群体演示需要 ScreenOverlayBuffer。");

    private static string? PresentationKind(JsonObject debug)
    {
        var presentation = debug["presentation"] as JsonObject;
        if (presentation == null) return null;
        string kind = presentation["kind"]?.GetValue<string>()
            ?? throw new InvalidOperationException("CrowdSimulationDebug.json presentation.kind 必填。");
        if (kind is not ("path" or "deploy"))
        {
            throw new InvalidOperationException($"CrowdSimulationDebug.json presentation.kind \"{kind}\" 需为 path / deploy。");
        }

        return kind;
    }

    private static JsonObject RequirePresentation(JsonObject debug) =>
        debug["presentation"] as JsonObject
        ?? throw new InvalidOperationException("CrowdSimulationDebug.json 缺少 presentation。");

    private static string RequireTitle(JsonObject presentation) =>
        presentation["title"]?.GetValue<string>()
        ?? throw new InvalidOperationException("CrowdSimulationDebug.json presentation.title 必填。");

    private static string RequireAction(JsonObject presentation, string name)
    {
        var actions = presentation["actions"] as JsonObject
            ?? throw new InvalidOperationException("CrowdSimulationDebug.json presentation.actions 必填。");
        return actions[name]?.GetValue<string>()
            ?? throw new InvalidOperationException($"CrowdSimulationDebug.json presentation.actions.{name} 必填。");
    }
}
