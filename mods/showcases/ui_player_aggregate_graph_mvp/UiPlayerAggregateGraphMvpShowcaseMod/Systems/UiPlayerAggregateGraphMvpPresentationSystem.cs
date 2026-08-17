using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using UiPlayerAggregateGraphMvpShowcaseMod.Runtime;
using Ludots.Platform.Abstractions;

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
        UiPlayerAggregateMarkerStyle style = _runtime.RequireMarkerStyle();
        ReadOnlySpan<UiPlayerAggregateProducerMarker> markers = _runtime.ProducerMarkers;
        for (int i = 0; i < markers.Length; i++)
        {
            UiPlayerAggregateProducerMarker marker = markers[i];
            DebugDrawColor color = marker.Offline
                ? ToDebugColor(style.OfflineColor)
                : ToDebugColor(style.OnlineColor);

            float half = style.HalfSizeMeters;
            _debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(marker.XMeters, marker.ZMeters),
                HalfWidth = half,
                HalfHeight = half,
                Thickness = style.OuterThickness,
                Color = color
            });
            _debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(marker.XMeters, marker.ZMeters),
                HalfWidth = half * style.InnerScale,
                HalfHeight = half * style.InnerScale,
                Thickness = style.InnerThickness,
                Color = color
            });
            _debugDraw.Circles.Add(new DebugDrawCircle2D
            {
                Center = new Vector2(marker.XMeters, marker.ZMeters),
                Radius = marker.Offline ? style.OfflineDotRadius : style.OnlineDotRadius,
                Thickness = style.DotThickness,
                Color = marker.Offline
                    ? ToDebugColor(style.OfflineDotColor)
                    : ToDebugColor(style.OnlineDotColor)
            });
        }
    }

    private static DebugDrawColor ToDebugColor(UiPlayerAggregateRgbaColor color)
    {
        return new DebugDrawColor(color.R, color.G, color.B, color.A);
    }
}
