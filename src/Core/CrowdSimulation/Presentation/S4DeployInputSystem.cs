using System;
using Arch.System;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// S4 交互:动作边沿进演示队列,放置/拆除进群体指令队列。
/// 排在会话步进之前,本 tick 的现场指令在步进前落地。
/// </summary>
internal sealed class S4DeployInputSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly S4DeployDemoRuntime _demo;
    private readonly CrowdDeployBindings _bindings;
    private readonly CrowdHudPanel _hud;
    private CrowdDebugViewFrame? _debugView;

    public S4DeployInputSystem(GameEngine engine, S4DeployDemoRuntime demo, CrowdDeployBindings bindings, CrowdHudPanel hud)
    {
        _engine = engine;
        _demo = demo;
        _bindings = bindings;
        _hud = hud;
    }

    internal void UseDebugView(CrowdDebugViewFrame frame) => _debugView = frame;

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!_demo.Active) return;
        var actions = CrowdAuthoritativeInput.Require(_engine);
        if (_engine.TryGetService(CoreServiceKeys.CrowdSimulationSession, out CrowdSimSession? session))
        {
            _demo.ArmSession(session);
        }

        bool picked = CrowdPointerGround.TryPick(_engine, actions, _hud.OriginXPx, _hud.OriginYPx, _hud.WidthPx, _hud.HeightPx, out double xCm, out double yCm);
        _demo.HoverCm = picked ? (xCm, yCm) : null;

        if (actions.PressedThisTick(_bindings.CycleView)) _demo.EnqueueCycleView();
        if (actions.PressedThisTick(_bindings.ToggleBuild)) _demo.EnqueueToggleBuild();
        if (actions.PressedThisTick(_bindings.ToolOff)) _demo.EnqueueTool(CrowdClickTool.Off);
        if (actions.PressedThisTick(_bindings.ToolBoom)) _demo.EnqueueTool(CrowdClickTool.Boom);
        if (actions.PressedThisTick(_bindings.ToolSpawn)) _demo.EnqueueTool(CrowdClickTool.Spawn);
        if (actions.PressedThisTick(_bindings.ToolStructure)) _demo.EnqueueTool(CrowdClickTool.Structure);
        if (actions.PressedThisTick(_bindings.ToolDemolish)) _demo.EnqueueTool(CrowdClickTool.Demolish);
        if (actions.PressedThisTick(_bindings.Deploy)) _demo.EnqueueDeploy();
        if (actions.PressedThisTick(_bindings.Scatter)) _demo.EnqueueScatter();
        if (actions.PressedThisTick(_bindings.Stress)) _demo.EnqueueStress();
        if (actions.PressedThisTick(_bindings.ClearCaches)) _demo.EnqueueClearCaches();
        if (actions.PressedThisTick(_bindings.AvoidDown)) _demo.EnqueueAvoid(-1);
        if (actions.PressedThisTick(_bindings.AvoidUp)) _demo.EnqueueAvoid(1);
        if (actions.PressedThisTick(_bindings.SliceDown)) _demo.EnqueueSlices(-1);
        if (actions.PressedThisTick(_bindings.SliceUp)) _demo.EnqueueSlices(1);
        if (actions.PressedThisTick(_bindings.Pause)) _demo.EnqueuePause();
        if (actions.PressedThisTick(_bindings.SpeedDown)) _demo.EnqueueSpeed(-1);
        if (actions.PressedThisTick(_bindings.SpeedUp)) _demo.EnqueueSpeed(1);
        for (int i = 0; i < _demo.StructureCount; i++)
        {
            if (actions.PressedThisTick(_demo.SelectActionAt(i))) _demo.EnqueueSelectTemplate(i);
        }

        if (actions.PressedThisTick(_bindings.Place) && picked)
        {
            _demo.EnqueueMapClick((int)Math.Floor(xCm), (int)Math.Floor(yCm));
        }

        if (actions.PressedThisTick(_bindings.Remove) && picked)
        {
            _demo.EnqueueRemove((int)Math.Floor(xCm), (int)Math.Floor(yCm));
        }

        _debugView?.ApplyInput(actions, _engine);
        _demo.Tick();
    }
}

internal sealed class CrowdDeployBindings
{
    public required string CycleView { get; init; }
    public required string ToggleBuild { get; init; }
    public required string Remove { get; init; }
    public required string Place { get; init; }
    public required string ToolOff { get; init; }
    public required string ToolBoom { get; init; }
    public required string ToolSpawn { get; init; }
    public required string ToolStructure { get; init; }
    public required string ToolDemolish { get; init; }
    public required string Deploy { get; init; }
    public required string Scatter { get; init; }
    public required string Stress { get; init; }
    public required string ClearCaches { get; init; }
    public required string AvoidDown { get; init; }
    public required string AvoidUp { get; init; }
    public required string SliceDown { get; init; }
    public required string SliceUp { get; init; }
    public required string Pause { get; init; }
    public required string SpeedDown { get; init; }
    public required string SpeedUp { get; init; }
}
