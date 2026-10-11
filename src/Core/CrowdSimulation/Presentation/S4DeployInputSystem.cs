using System;
using Arch.System;
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

    public S4DeployInputSystem(GameEngine engine, S4DeployDemoRuntime demo, CrowdDeployBindings bindings, CrowdHudPanel hud)
    {
        _engine = engine;
        _demo = demo;
        _bindings = bindings;
        _hud = hud;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!_demo.Active) return;
        var actions = CrowdAuthoritativeInput.Require(_engine);
        bool picked = CrowdPointerGround.TryPick(_engine, actions, _hud.X, _hud.Y, _hud.W, _hud.H, out double xCm, out double yCm);
        _demo.HoverCm = picked ? (xCm, yCm) : null;

        if (actions.PressedThisTick(_bindings.CycleView)) _demo.EnqueueCycleView();
        if (actions.PressedThisTick(_bindings.ToggleBuild)) _demo.EnqueueToggleBuild();
        for (int i = 0; i < _demo.StructureCount; i++)
        {
            if (actions.PressedThisTick(_demo.SelectActionAt(i))) _demo.EnqueueSelectTemplate(i);
        }

        if (actions.PressedThisTick(_bindings.Place) && _demo.BuildMode && picked)
        {
            _demo.EnqueuePlace((int)Math.Floor(xCm), (int)Math.Floor(yCm));
        }

        if (actions.PressedThisTick(_bindings.Remove) && picked)
        {
            _demo.EnqueueRemove((int)Math.Floor(xCm), (int)Math.Floor(yCm));
        }

        _demo.Tick();
    }
}

internal sealed class CrowdDeployBindings
{
    public required string CycleView { get; init; }
    public required string ToggleBuild { get; init; }
    public required string Remove { get; init; }
    public required string Place { get; init; }
}
