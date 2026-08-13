using System.Numerics;
using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;

namespace CapabilityStandardGraphOpsScriptMod.Runtime;

internal sealed class GraphOpsScriptPresentationSystem : ISystem<float>
{
    private static readonly Vector2[] PatrolStops =
    {
        new(-4f, -2f), new(4f, -2f), new(4f, 2f), new(-4f, 2f)
    };

    private readonly GraphOpsScriptRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly ScreenOverlayBuffer _overlay;

    public GraphOpsScriptPresentationSystem(
        GraphOpsScriptRuntime runtime,
        DebugDrawCommandBuffer debugDraw,
        ScreenOverlayBuffer overlay)
    {
        _runtime = runtime;
        _debugDraw = debugDraw;
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
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
        GraphShowcaseStagePresenter.DrawPlayerCaption(_overlay, GraphOpsScriptRuntime.CaptionTitle, _runtime.Metrics.Detail);
    }

    private void DrawDrinkCup()
    {
        GraphShowcaseStagePresenter.DrawActor(_debugDraw, -6f, 0f, 2.0f, DebugDrawColor.Gray, 0.12f);
        int filledCount = _runtime.DisplayedWater;
        for (int i = 0; i < _runtime.DrinkLimit; i++)
        {
            bool filled = i < filledCount;
            _debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(-6f, -1.1f + i * 0.5f),
                HalfWidth = 1.0f,
                HalfHeight = 0.2f,
                Thickness = 0.1f,
                Color = filled
                    ? (_runtime.AllPhasesComplete || filledCount >= _runtime.DrinkLimit
                        ? DebugDrawColor.Green
                        : DebugDrawColor.Cyan)
                    : DebugDrawColor.Gray
            });
        }
    }

    private void DrawPatrolMarker()
    {
        int step = Math.Clamp(_runtime.DisplayedPatrolStep, 0, PatrolStops.Length - 1);
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
