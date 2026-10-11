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
    private bool _prevView, _prevBuild, _prevRemove, _prevPlace, _prevT1, _prevT2, _prevT3;

    public S4DeployInputSystem(GameEngine engine, S4DeployDemoRuntime demo, CrowdDeployBindings bindings)
    {
        _engine = engine;
        _demo = demo;
        _bindings = bindings;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!_demo.Active) return;
        var actions = _engine.GetService(CoreServiceKeys.InputHandler)
            ?? throw new InvalidOperationException("群体部署演示需要 InputHandler。");
        bool picked = CrowdPointerGround.TryPick(
            _engine, actions,
            S4DeployDemoPresentationSystem.HudPanelX,
            S4DeployDemoPresentationSystem.HudPanelY,
            S4DeployDemoPresentationSystem.HudPanelW,
            S4DeployDemoPresentationSystem.HudPanelH,
            out double xCm, out double yCm);
        _demo.HoverCm = picked ? (xCm, yCm) : null;

        bool view = actions.IsDown(_bindings.CycleView);
        bool build = actions.IsDown(_bindings.ToggleBuild);
        bool remove = actions.IsDown(_bindings.Remove);
        bool place = actions.IsDown(_bindings.Place);
        bool t1 = actions.IsDown(_bindings.Template1);
        bool t2 = actions.IsDown(_bindings.Template2);
        bool t3 = actions.IsDown(_bindings.Template3);
        if (view && !_prevView) _demo.EnqueueCycleView();
        if (build && !_prevBuild) _demo.EnqueueToggleBuild();
        if (t1 && !_prevT1) _demo.EnqueueSelectTemplate(0);
        if (t2 && !_prevT2) _demo.EnqueueSelectTemplate(1);
        if (t3 && !_prevT3) _demo.EnqueueSelectTemplate(2);
        if (place && !_prevPlace && _demo.BuildMode && picked)
        {
            _demo.EnqueuePlace((int)Math.Floor(xCm), (int)Math.Floor(yCm));
        }

        if (remove && !_prevRemove && picked)
        {
            _demo.EnqueueRemove((int)Math.Floor(xCm), (int)Math.Floor(yCm));
        }

        _prevView = view;
        _prevBuild = build;
        _prevRemove = remove;
        _prevPlace = place;
        _prevT1 = t1;
        _prevT2 = t2;
        _prevT3 = t3;
        _demo.Tick();
    }
}

internal sealed class CrowdDeployBindings
{
    public required string CycleView { get; init; }
    public required string ToggleBuild { get; init; }
    public required string Remove { get; init; }
    public required string Place { get; init; }
    public required string Template1 { get; init; }
    public required string Template2 { get; init; }
    public required string Template3 { get; init; }
}
