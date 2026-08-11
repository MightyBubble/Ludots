using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.DebugDraw;
using UiPlayerAggregateGraphMvpShowcaseMod.Runtime;

namespace UiPlayerAggregateGraphMvpShowcaseMod.Systems;

internal sealed class UiPlayerAggregateGraphMvpPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly UiPlayerAggregateGraphMvpRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;

    public UiPlayerAggregateGraphMvpPresentationSystem(
        GameEngine engine,
        UiPlayerAggregateGraphMvpRuntime runtime,
        DebugDrawCommandBuffer debugDraw)
    {
        _engine = engine;
        _runtime = runtime;
        _debugDraw = debugDraw;
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float t)
    {
    }

    public void Update(in float t)
    {
        _runtime.RefreshPanel(_engine);
        DrawProducerMarkers();
    }

    public void AfterUpdate(in float t)
    {
    }

    public void Dispose()
    {
    }

    private void DrawProducerMarkers()
    {
        if (!UiPlayerAggregateGraphMvpIds.IsShowcaseMap(_engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        _debugDraw.Clear();
        ReadOnlySpan<UiPlayerAggregateProducerMarker> markers = _runtime.ProducerMarkers;
        for (int i = 0; i < markers.Length; i++)
        {
            UiPlayerAggregateProducerMarker marker = markers[i];
            DebugDrawColor color = marker.Offline
                ? new DebugDrawColor(180, 70, 70)
                : new DebugDrawColor(80, 200, 140);

            float half = 0.85f;
            _debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(marker.XMeters, marker.ZMeters),
                HalfWidth = half,
                HalfHeight = half,
                Thickness = 0.1f,
                Color = color
            });
            _debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(marker.XMeters, marker.ZMeters),
                HalfWidth = half * 0.55f,
                HalfHeight = half * 0.55f,
                Thickness = 0.08f,
                Color = color
            });
            _debugDraw.Circles.Add(new DebugDrawCircle2D
            {
                Center = new Vector2(marker.XMeters, marker.ZMeters),
                Radius = marker.Offline ? 0.28f : 0.42f,
                Thickness = 0.08f,
                Color = marker.Offline ? DebugDrawColor.Red : DebugDrawColor.Yellow
            });
        }
    }
}
