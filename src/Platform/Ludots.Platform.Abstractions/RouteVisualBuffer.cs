using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ludots.Platform.Abstractions
{
    /// <summary>路线标记形状(绘制语义在渲染端,数据端只给枚举)。</summary>
    public enum RouteVisualMarkerShape : byte
    {
        Ring = 0,
        Diamond = 1,
    }

    public readonly record struct RouteVisualId(int Value);

    public struct RouteVisualPolyline
    {
        public RouteVisualId Id;
        public Vector2[] PointsMeters;
        public float ThicknessMeters;
        public Vector4 Color;
    }

    public struct RouteVisualMarker
    {
        public RouteVisualId Id;
        public Vector2 PositionMeters;
        public RouteVisualMarkerShape Shape;
        public float RadiusMeters;
        public float ThicknessMeters;
        public Vector4 Color;
    }

    /// <summary>
    /// 路线 / 标记视觉缓冲(gameplay 呈现通道,与调试绘制 DebugDrawCommandBuffer 区分):
    /// 折线(世界米折点)与点标记,渲染端按地形高度跟随。路径展示、移动命令路线、
    /// 阵型槽位等 gameplay 叠加统一走这里;每帧 BeginFrame 后重写,未重写的记录自然消失。
    /// </summary>
    public sealed class RouteVisualBuffer
    {
        private readonly List<RouteVisualPolyline> _polylines = new(64);
        private readonly List<RouteVisualMarker> _markers = new(64);

        public IReadOnlyList<RouteVisualPolyline> Polylines => _polylines;
        public IReadOnlyList<RouteVisualMarker> Markers => _markers;
        public int Revision { get; private set; }

        public void BeginFrame()
        {
            Revision = Revision == int.MaxValue ? 1 : Revision + 1;
            _polylines.Clear();
            _markers.Clear();
        }

        public void AddPolyline(RouteVisualId id, ReadOnlySpan<Vector2> pointsMeters, float thicknessMeters, Vector4 color)
        {
            if (pointsMeters.Length < 2) throw new ArgumentException("折线至少需要两个点。", nameof(pointsMeters));
            _polylines.Add(new RouteVisualPolyline
            {
                Id = id,
                PointsMeters = pointsMeters.ToArray(),
                ThicknessMeters = thicknessMeters,
                Color = color,
            });
        }

        /// <summary>共享数组版(调用方保证数组不可变且寿命长于一帧;静态线框缓存用,避免逐帧拷贝)。</summary>
        public void AddPolylineShared(RouteVisualId id, Vector2[] pointsMeters, float thicknessMeters, Vector4 color)
        {
            if (pointsMeters.Length < 2) throw new ArgumentException("折线至少需要两个点。", nameof(pointsMeters));
            _polylines.Add(new RouteVisualPolyline
            {
                Id = id,
                PointsMeters = pointsMeters,
                ThicknessMeters = thicknessMeters,
                Color = color,
            });
        }

        public void AddMarker(RouteVisualId id, Vector2 positionMeters, RouteVisualMarkerShape shape, float radiusMeters, float thicknessMeters, Vector4 color)
        {
            if (radiusMeters <= 0) throw new ArgumentOutOfRangeException(nameof(radiusMeters));
            _markers.Add(new RouteVisualMarker
            {
                Id = id,
                PositionMeters = positionMeters,
                Shape = shape,
                RadiusMeters = radiusMeters,
                ThicknessMeters = thicknessMeters,
                Color = color,
            });
        }
    }
}
