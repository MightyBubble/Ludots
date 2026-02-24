using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D
{
    public static class ShapeDataStorage2D
    {
        private static readonly List<CircleShapeData> CircleShapes = new(1024);
        private static readonly List<BoxShapeData> BoxShapes = new(1024);
        private static readonly List<PolygonShapeData> PolygonShapes = new(256);
        private static readonly Dictionary<int, ColliderType2D> IndexToTypeMap = new();
        private static readonly Dictionary<int, int> IndexToCircleLocalIndex = new();
        private static readonly Dictionary<int, int> IndexToBoxLocalIndex = new();
        private static readonly Dictionary<int, int> IndexToPolygonLocalIndex = new();
        private static int _nextIndex;

        /// <summary>
        /// 注册圆形碰撞体（定点数厘米）。
        /// </summary>
        public static int RegisterCircle(Fix64 radius, Fix64Vec2 localCenter = default)
        {
            CircleShapes.Add(new CircleShapeData { Radius = radius, LocalCenter = localCenter });
            int index = _nextIndex++;
            IndexToTypeMap[index] = ColliderType2D.Circle;
            IndexToCircleLocalIndex[index] = CircleShapes.Count - 1;
            return index;
        }

        /// <summary>
        /// 注册圆形碰撞体（浮点厘米，仅用于初始化）。
        /// </summary>
        public static int RegisterCircle(float radius, float localCenterX = 0f, float localCenterY = 0f)
        {
            return RegisterCircle(Fix64.FromFloat(radius), Fix64Vec2.FromFloat(localCenterX, localCenterY));
        }

        /// <summary>
        /// 注册矩形碰撞体（定点数厘米）。
        /// </summary>
        public static int RegisterBox(Fix64 halfWidth, Fix64 halfHeight, Fix64Vec2 localCenter = default)
        {
            BoxShapes.Add(new BoxShapeData { HalfWidth = halfWidth, HalfHeight = halfHeight, LocalCenter = localCenter });
            int index = _nextIndex++;
            IndexToTypeMap[index] = ColliderType2D.Box;
            IndexToBoxLocalIndex[index] = BoxShapes.Count - 1;
            return index;
        }

        /// <summary>
        /// 注册矩形碰撞体（浮点厘米，仅用于初始化）。
        /// </summary>
        public static int RegisterBox(float halfWidth, float halfHeight, float localCenterX = 0f, float localCenterY = 0f)
        {
            return RegisterBox(Fix64.FromFloat(halfWidth), Fix64.FromFloat(halfHeight),
                Fix64Vec2.FromFloat(localCenterX, localCenterY));
        }

        /// <summary>
        /// 注册多边形碰撞体（定点数厘米）。
        /// </summary>
        public static int RegisterPolygon(Fix64Vec2[] vertices)
        {
            if (vertices == null || vertices.Length < 3)
            {
                throw new ArgumentException("Polygon requires at least 3 vertices.", nameof(vertices));
            }

            if (vertices.Length > 8)
            {
                throw new ArgumentException("Polygon vertex count must be <= 8.", nameof(vertices));
            }

            var center = Fix64Vec2.Zero;
            for (int i = 0; i < vertices.Length; i++)
            {
                center = center + vertices[i];
            }
            center = center / Fix64.FromInt(vertices.Length);

            PolygonShapes.Add(new PolygonShapeData { Vertices = vertices, VertexCount = vertices.Length, LocalCenter = center });
            int index = _nextIndex++;
            IndexToTypeMap[index] = ColliderType2D.Polygon;
            IndexToPolygonLocalIndex[index] = PolygonShapes.Count - 1;
            return index;
        }

        public static bool TryGetCircle(int index, out CircleShapeData data)
        {
            if (IndexToTypeMap.TryGetValue(index, out var type) &&
                type == ColliderType2D.Circle &&
                IndexToCircleLocalIndex.TryGetValue(index, out int localIndex))
            {
                if ((uint)localIndex < (uint)CircleShapes.Count)
                {
                    data = CircleShapes[localIndex];
                    return true;
                }
            }

            data = default;
            return false;
        }

        public static bool TryGetBox(int index, out BoxShapeData data)
        {
            if (IndexToTypeMap.TryGetValue(index, out var type) &&
                type == ColliderType2D.Box &&
                IndexToBoxLocalIndex.TryGetValue(index, out int localIndex))
            {
                if ((uint)localIndex < (uint)BoxShapes.Count)
                {
                    data = BoxShapes[localIndex];
                    return true;
                }
            }

            data = default;
            return false;
        }

        public static bool TryGetPolygon(int index, out PolygonShapeData data)
        {
            if (IndexToTypeMap.TryGetValue(index, out var type) &&
                type == ColliderType2D.Polygon &&
                IndexToPolygonLocalIndex.TryGetValue(index, out int localIndex))
            {
                if ((uint)localIndex < (uint)PolygonShapes.Count)
                {
                    data = PolygonShapes[localIndex];
                    return true;
                }
            }

            data = default;
            return false;
        }

        public static ColliderType2D GetShapeType(int index)
        {
            if (!IndexToTypeMap.TryGetValue(index, out var type))
            {
                throw new KeyNotFoundException($"ShapeDataIndex not registered: {index}");
            }

            return type;
        }

        public static void Clear()
        {
            CircleShapes.Clear();
            BoxShapes.Clear();
            PolygonShapes.Clear();
            IndexToTypeMap.Clear();
            IndexToCircleLocalIndex.Clear();
            IndexToBoxLocalIndex.Clear();
            IndexToPolygonLocalIndex.Clear();
            _nextIndex = 0;
        }
    }
}
