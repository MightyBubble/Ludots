using System;
using Arch.System;
using Ludots.Core.Client;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Fields;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
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
    private CrowdPresentationConfig? _presentation;
    private CrowdFieldProjectorSlot? _projectorSlot;
    private ISystem<float>? _pathInput;
    private ISystem<float>? _pathSimulation;
    private ISystem<float>? _pathPresentation;
    private ISystem<float>? _deployInput;
    private ISystem<float>? _deployPresentation;

    internal void BindProjectorSlot(GlobalFieldVisualProjectorRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (_projectorSlot != null)
            throw new InvalidOperationException("群体场投影槽已登记。");
        _projectorSlot = new CrowdFieldProjectorSlot();
        registry.Register(_projectorSlot);
    }

    private string? PresentationKindName => _presentation?.Kind;

    public bool ReleasePathPresentation(GameEngine engine, MapId mapId)
    {
        if (_pathDemo == null || _configDto == null || _presentation == null) return false;
        if (!string.Equals(mapId.Value, _focusedCrowdMapId, StringComparison.Ordinal)) return false;
        if (_presentation.Kind != CrowdPresentationConfig.KindPath) return false;
        DetachPath(engine);
        if (_projectorSlot != null) _projectorSlot.Current = null;
        return true;
    }

    public bool ReleaseDeployPresentation(GameEngine engine, MapId mapId)
    {
        if (_deployDemo == null || _configDto == null || _presentation == null) return false;
        if (!string.Equals(mapId.Value, _focusedCrowdMapId, StringComparison.Ordinal)) return false;
        if (_presentation.Kind != CrowdPresentationConfig.KindDeploy) return false;
        DetachDeploy(engine);
        if (_projectorSlot != null) _projectorSlot.Current = null;
        return true;
    }

    private void DetachPath(GameEngine engine)
    {
        if (_pathDemo != null)
        {
            _pathDemo.Active = false;
            _pathDemo.Dispose();
            _pathDemo = null;
        }

        UnregisterFixed(engine, ref _pathInput, SystemGroup.PostMovement);
        UnregisterFixed(engine, ref _pathSimulation, SystemGroup.PostMovement);
        UnregisterPresented(engine, ref _pathPresentation);
        _pathPresentationInstalled = false;
    }

    private void DetachDeploy(GameEngine engine)
    {
        if (_deployDemo != null)
        {
            _deployDemo.ReleasePlayback();
            _deployDemo.Active = false;
        }

        _deployDemo = null;
        UnregisterFixed(engine, ref _deployInput, SystemGroup.PostMovement);
        UnregisterPresented(engine, ref _deployPresentation);
        _deployPresentationInstalled = false;
    }

    private static void UnregisterFixed(GameEngine engine, ref ISystem<float>? system, SystemGroup group)
    {
        if (system == null) return;
        if (!engine.UnregisterSystem(system, group))
            throw new InvalidOperationException($"卸载群体演示系统失败: {system.GetType().Name}。");
        system = null;
    }

    private static void UnregisterPresented(GameEngine engine, ref ISystem<float>? system)
    {
        if (system == null) return;
        if (!engine.UnregisterPresentationSystem(system))
            throw new InvalidOperationException($"卸载群体呈现系统失败: {system.GetType().Name}。");
        system = null;
    }

    private void InstallPathPresentation(GameEngine engine, MapConfig mapConfig)
    {
        if (_pathPresentationInstalled) return;
        CrowdPresentationConfig presentation = _presentation
            ?? throw new InvalidOperationException("CrowdSimulationDebug.json 缺少 presentation。");
        CrowdPathPresentation path = presentation.Path
            ?? throw new InvalidOperationException("CrowdSimulationDebug.json presentation.path 必填。");
        var bindings = new CrowdPathBindings
        {
            SetStart = path.Actions.SetStart,
            SetGoal = path.Actions.SetGoal,
            CycleContext = path.Actions.CycleContext,
            ToggleThreads = path.Actions.ToggleThreads,
            ToggleSlow = path.Actions.ToggleSlow,
            CycleView = path.Actions.CycleView,
        };
        var (routes, slot) = RequireVisualServices(engine);
        var demo = S3PathDemoRuntime.Load(engine, _configDto!, mapConfig, path.TruthAsset, RequirePlanTimeout());
        demo.UseViewLabels(presentation.ViewLabels);
        var flow = new CrowdFlowFieldVisualSource
        {
            NavCellsPerSide = demo.Nav.NavCellsPerSide,
            NavCellSizeCm = demo.Config.NavCellSizeCm,
        };
        demo.FlowVisual = flow;
        slot.Current = new DemoViewProjector(
            demo,
            new CrowdFlowFieldVisualProjector(flow),
            new CrowdWalkableVisualProjector(flow, () => demo.Nav));
        var overlay = RequireOverlay(engine);
        _pathInput = new S3PathInputSystem(engine, demo, bindings, presentation.Hud);
        _pathSimulation = new S3PathDemoSimulationSystem(demo);
        _pathPresentation = new S3PathDemoPresentationSystem(demo, routes, overlay, presentation);
        engine.RegisterSystem(_pathInput, SystemGroup.PostMovement);
        engine.RegisterSystem(_pathSimulation, SystemGroup.PostMovement);
        engine.RegisterPresentationSystem(_pathPresentation);
        _pathDemo = demo;
        _pathPresentationInstalled = true;
    }

    private void InstallDeployPresentation(GameEngine engine)
    {
        if (_deployPresentationInstalled) return;
        CrowdPresentationConfig presentation = _presentation
            ?? throw new InvalidOperationException("CrowdSimulationDebug.json 缺少 presentation。");
        CrowdDeployPresentation deploy = presentation.Deploy
            ?? throw new InvalidOperationException("CrowdSimulationDebug.json presentation.deploy 必填。");
        var bindings = new CrowdDeployBindings
        {
            CycleView = deploy.Actions.CycleView,
            ToggleBuild = deploy.Actions.ToggleBuild,
            Remove = deploy.Actions.Remove,
            Place = deploy.Actions.Place,
            Validate = deploy.Actions.Validate,
            ToolOff = deploy.Actions.ToolOff,
            ToolBoom = deploy.Actions.ToolBoom,
            ToolSpawn = deploy.Actions.ToolSpawn,
            ToolStructure = deploy.Actions.ToolStructure,
            ToolDemolish = deploy.Actions.ToolDemolish,
            Deploy = deploy.Actions.Deploy,
            Scatter = deploy.Actions.Scatter,
            Stress = deploy.Actions.Stress,
            ClearCaches = deploy.Actions.ClearCaches,
            AvoidDown = deploy.Actions.AvoidDown,
            AvoidUp = deploy.Actions.AvoidUp,
            SliceDown = deploy.Actions.SliceDown,
            SliceUp = deploy.Actions.SliceUp,
            Pause = deploy.Actions.Pause,
            SpeedDown = deploy.Actions.SpeedDown,
            SpeedUp = deploy.Actions.SpeedUp,
        };
        var choices = new S4DeployDemoRuntime.StructureChoice[deploy.Structures.Length];
        for (int i = 0; i < deploy.Structures.Length; i++)
        {
            CrowdStructureTemplateDto item = deploy.Structures[i];
            choices[i] = new S4DeployDemoRuntime.StructureChoice(
                item.Id,
                item.Label,
                item.Shape,
                item.SizeCm,
                item.RoadLengthCm ?? 0,
                item.SelectAction);
        }

        var (routes, slot) = RequireVisualServices(engine);
        var demo = new S4DeployDemoRuntime(engine);
        CrowdDebugTools tools = _debug?.Tools
            ?? throw new InvalidOperationException("部署演示缺少 tools。");
        CrowdDebugRates rates = _debug?.Rates
            ?? throw new InvalidOperationException("部署演示缺少 rates。");
        demo.UseViewLabels(presentation.ViewLabels);
        demo.UseStructures(choices);
        demo.UseDebugTools(tools, rates);
        Func<CrowdSimSession?> sessionSource = () =>
            engine.TryGetService(CoreServiceKeys.CrowdSimulationSession, out var session) ? session : null;
        var demoProjector = new S4DeployDemoViewProjector(demo, sessionSource);
        CrowdDebugViewFrame? debugView = _debugView == null ? null : new CrowdDebugViewFrame(_debugView);
        slot.Current = debugView == null
            ? demoProjector
            : new S4DeployFrameProjector(demoProjector, debugView, sessionSource);
        var overlay = RequireOverlay(engine);
        var input = new S4DeployInputSystem(engine, demo, bindings, presentation.Hud);
        var presented = new S4DeployDemoPresentationSystem(
            demo,
            routes,
            overlay,
            () => engine.TryGetService(CoreServiceKeys.CrowdSimulationRuntime, out var runtime) ? runtime : null,
            sessionSource,
            presentation,
            () => ResolveLocalSeatVisionScope(engine));
        if (debugView != null)
        {
            debugView.BindEngine(engine);
            input.UseDebugView(debugView);
            presented.UseDebugView(debugView);
        }

        _deployInput = input;
        _deployPresentation = presented;
        engine.InsertSystemBeforeRequired<CrowdSimulationSessionTickSystem>(_deployInput, SystemGroup.PostMovement);
        engine.RegisterPresentationSystem(_deployPresentation);
        _deployDemo = demo;
        _deployPresentationInstalled = true;
    }

    private static int ResolveLocalSeatVisionScope(GameEngine engine)
    {
        if (!engine.TryGetService(CoreServiceKeys.SeatVisionScope, out ISeatVisionScope? source) || source == null)
        {
            return 0;
        }

        if (!engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out ClientLocalSeatRegistry? seats) ||
            seats == null ||
            !seats.TryGetSoleSeat(out ClientLocalSeat seat))
        {
            return 0;
        }

        return source.TryGetScopeKeyId(seat.SeatId, out int scopeKeyId) ? scopeKeyId : 0;
    }

    private (RouteVisualBuffer Routes, CrowdFieldProjectorSlot Slot) RequireVisualServices(GameEngine engine)
    {
        var routes = engine.GetService(CoreServiceKeys.RouteVisualBuffer)
            ?? throw new InvalidOperationException("群体演示需要 RouteVisualBuffer。");
        CrowdFieldProjectorSlot slot = _projectorSlot
            ?? throw new InvalidOperationException("群体演示需要已登记的场投影槽。");
        if (engine.GetService(CoreServiceKeys.GlobalFieldVisualProjectorRegistry) == null)
            throw new InvalidOperationException("群体演示需要 GlobalFieldVisualProjectorRegistry。");
        return (routes, slot);
    }

    private static ScreenOverlayBuffer RequireOverlay(GameEngine engine) =>
        engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
        ?? throw new InvalidOperationException("群体演示需要 ScreenOverlayBuffer。");
}
