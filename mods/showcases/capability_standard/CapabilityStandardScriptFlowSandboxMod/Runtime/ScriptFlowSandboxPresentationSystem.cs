using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardScriptFlowSandboxMod.Runtime;

internal sealed class ScriptFlowSandboxPresentationSystem : ISystem<float>
{
    private readonly ScriptFlowSandboxRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;

    public ScriptFlowSandboxPresentationSystem(ScriptFlowSandboxRuntime runtime, DebugDrawCommandBuffer debugDraw)
    {
        _runtime = runtime;
        _debugDraw = debugDraw;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        GraphShowcaseStagePresenter.Clear(_debugDraw);
        // Cup outline
        GraphShowcaseStagePresenter.DrawActor(_debugDraw, 0f, 0f, 2.2f, DebugDrawColor.Gray, 0.12f);
        // Water fill as stacked pips
        for (int i = 0; i < _runtime.Limit; i++)
        {
            bool filled = i < _runtime.Water;
            _debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new System.Numerics.Vector2(0f, -1.2f + i * 0.55f),
                HalfWidth = 1.2f,
                HalfHeight = 0.22f,
                Thickness = 0.1f,
                Color = filled
                    ? (_runtime.Halted ? DebugDrawColor.Green : DebugDrawColor.Cyan)
                    : DebugDrawColor.Gray
            });
        }

        GraphShowcaseStagePresenter.DrawBudgetBar(_debugDraw, _runtime.Metrics.LastThinkMs);
    }
}
