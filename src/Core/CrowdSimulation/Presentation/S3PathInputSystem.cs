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
    private readonly CrowdHudPanel _hud;

    public S3PathInputSystem(GameEngine engine, S3PathDemoRuntime runtime, CrowdPathBindings bindings, CrowdHudPanel hud)
    {
        _engine = engine;
        _runtime = runtime;
        _bindings = bindings;
        _hud = hud;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!_runtime.Active) return;
        var actions = CrowdAuthoritativeInput.Require(_engine);
        int n = _runtime.Nav.CellCount;
        float csCm = _runtime.Config.NavCellSizeCm;
        int hovered = -1;
        if (CrowdPointerGround.TryPick(_engine, actions, _hud.X, _hud.Y, _hud.W, _hud.H, out double xCm, out double yCm))
        {
            int cx = (int)MathF.Floor((float)xCm / csCm);
            int cy = (int)MathF.Floor((float)yCm / csCm);
            if (cx >= 0 && cy >= 0 && cx < n && cy < n) hovered = cy * n + cx;
        }

        _runtime.HoveredCell = hovered;
        if (actions.PressedThisTick(_bindings.SetStart) && hovered >= 0) _runtime.EnqueueSetStart(hovered);
        if (actions.PressedThisTick(_bindings.SetGoal) && hovered >= 0) _runtime.EnqueueSetGoal(hovered);
        if (actions.PressedThisTick(_bindings.CycleContext)) _runtime.EnqueueCycleContext();
        if (actions.PressedThisTick(_bindings.ToggleThreads)) _runtime.EnqueueCycleThreads();
        if (actions.PressedThisTick(_bindings.ToggleSlow)) _runtime.EnqueueToggleSlow();
        if (actions.PressedThisTick(_bindings.CycleView)) _runtime.EnqueueCycleView();
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
