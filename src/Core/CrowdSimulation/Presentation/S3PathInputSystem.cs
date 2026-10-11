using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// S3 交互:动作边沿只入队,路径服务在随后的仿真 tick 消费。
/// </summary>
internal sealed class S3PathInputSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly S3PathDemoRuntime _runtime;
    private readonly CrowdPathBindings _bindings;
    private bool _prevStart, _prevGoal, _prevContext, _prevThreads, _prevSlow, _prevView;

    public S3PathInputSystem(GameEngine engine, S3PathDemoRuntime runtime, CrowdPathBindings bindings)
    {
        _engine = engine;
        _runtime = runtime;
        _bindings = bindings;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!_runtime.Active) return;
        var actions = _engine.GetService(CoreServiceKeys.InputHandler)
            ?? throw new InvalidOperationException("群体路径演示需要 InputHandler。");
        int n = _runtime.Nav.CellCount;
        float csCm = _runtime.Config.NavCellSizeCm;
        int hovered = -1;
        if (CrowdPointerGround.TryPick(
                _engine, actions,
                S3PathDemoPresentationSystem.HudPanelX,
                S3PathDemoPresentationSystem.HudPanelY,
                S3PathDemoPresentationSystem.HudPanelW,
                S3PathDemoPresentationSystem.HudPanelH,
                out double xCm, out double yCm))
        {
            int cx = (int)MathF.Floor((float)xCm / csCm);
            int cy = (int)MathF.Floor((float)yCm / csCm);
            if (cx >= 0 && cy >= 0 && cx < n && cy < n) hovered = cy * n + cx;
        }

        _runtime.HoveredCell = hovered;
        bool start = actions.IsDown(_bindings.SetStart);
        bool goal = actions.IsDown(_bindings.SetGoal);
        bool context = actions.IsDown(_bindings.CycleContext);
        bool threads = actions.IsDown(_bindings.ToggleThreads);
        bool slow = actions.IsDown(_bindings.ToggleSlow);
        bool view = actions.IsDown(_bindings.CycleView);
        if (start && !_prevStart && hovered >= 0) _runtime.EnqueueSetStart(hovered);
        if (goal && !_prevGoal && hovered >= 0) _runtime.EnqueueSetGoal(hovered);
        if (context && !_prevContext) _runtime.EnqueueCycleContext();
        if (threads && !_prevThreads) _runtime.EnqueueCycleThreads();
        if (slow && !_prevSlow) _runtime.EnqueueToggleSlow();
        if (view && !_prevView) _runtime.EnqueueCycleView();
        _prevStart = start;
        _prevGoal = goal;
        _prevContext = context;
        _prevThreads = threads;
        _prevSlow = slow;
        _prevView = view;
    }
}

internal sealed class CrowdPathBindings
{
    public required string SetStart { get; init; }
    public required string SetGoal { get; init; }
    public required string CycleContext { get; init; }
    public required string ToggleThreads { get; init; }
    public required string ToggleSlow { get; init; }
    public required string CycleView { get; init; }
}
