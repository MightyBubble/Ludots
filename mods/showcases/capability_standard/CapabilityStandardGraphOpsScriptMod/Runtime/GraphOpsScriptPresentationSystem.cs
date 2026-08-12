using System.Numerics;
using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphOpsScriptMod.Runtime;

internal sealed class GraphOpsScriptPresentationSystem : ISystem<float>
{
    private static readonly Vector2[] PatrolStops =
    {
        new(-4f, -2f), new(4f, -2f), new(4f, 2f), new(-4f, 2f)
    };

    private readonly GraphOpsScriptRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;

    public GraphOpsScriptPresentationSystem(GraphOpsScriptRuntime runtime, DebugDrawCommandBuffer debugDraw)
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
        GraphShowcaseStagePresenter.DrawPolyline(_debugDraw, PatrolStops, GraphShowcaseStagePresenter.PathColor);
        DrawDrinkCup();
        DrawPatrolMarker();
        DrawConstValue();
        GraphShowcaseStagePresenter.DrawBudgetBar(_debugDraw, _runtime.Metrics.LastThinkMs);
    }

    private void DrawDrinkCup()
    {
        GraphShowcaseStagePresenter.DrawActor(_debugDraw, -6f, 0f, 2.0f, DebugDrawColor.Gray, 0.12f);
        for (int i = 0; i < _runtime.DrinkLimit; i++)
        {
            bool filled = i < _runtime.Water;
            _debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(-6f, -1.1f + i * 0.5f),
                HalfWidth = 1.0f,
                HalfHeight = 0.2f,
                Thickness = 0.1f,
                Color = filled
                    ? (_runtime.AllPhasesComplete || _runtime.Water >= _runtime.DrinkLimit
                        ? DebugDrawColor.Green
                        : DebugDrawColor.Cyan)
                    : DebugDrawColor.Gray
            });
        }
    }

    private void DrawPatrolMarker()
    {
        int step = Math.Clamp(_runtime.PatrolStep, 0, PatrolStops.Length - 1);
        Vector2 pos = PatrolStops[step];
        GraphShowcaseStagePresenter.DrawActor(_debugDraw, pos.X, pos.Y, 0.55f, GraphShowcaseStagePresenter.GuardColor);
    }

    private void DrawConstValue()
    {
        if (_runtime.ConstValue <= 0) return;
        for (int i = 0; i < _runtime.ConstValue; i++)
        {
            _debugDraw.Circles.Add(new DebugDrawCircle2D
            {
                Center = new Vector2(6f - i * 0.55f, 0f),
                Radius = 0.22f,
                Thickness = 0.1f,
                Color = DebugDrawColor.Yellow
            });
        }
    }
}
