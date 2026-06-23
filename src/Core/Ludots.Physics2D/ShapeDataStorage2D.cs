using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D
{
    public sealed class ShapeDataStorage2D
    {
        private readonly struct ShapeEntry
        {
            public readonly ColliderType2D Type;
            public readonly int LocalIndex;

            public ShapeEntry(ColliderType2D type, int localIndex)
            {
                Type = type;
                LocalIndex = localIndex;
            }
        }

        private readonly List<CircleShapeData> _circleShapes;
        private readonly List<BoxShapeData> _boxShapes;
        private readonly List<PolygonShapeData> _polygonShapes;
        private readonly List<ShapeEntry> _entries;

        public ShapeDataStorage2D()
            : this(1024, 1024, 256)
        {
        }

        public ShapeDataStorage2D(int circleCapacity, int boxCapacity, int polygonCapacity)
        {
            _circleShapes = new List<CircleShapeData>(Math.Max(0, circleCapacity));
            _boxShapes = new List<BoxShapeData>(Math.Max(0, boxCapacity));
            _polygonShapes = new List<PolygonShapeData>(Math.Max(0, polygonCapacity));
            _entries = new List<ShapeEntry>(Math.Max(0, circleCapacity + boxCapacity + polygonCapacity));
        }

        public int ShapeCount => _entries.Count;

        public int RegisterCircle(Fix64 radius, Fix64Vec2 localCenter = default)
        {
            int localIndex = _circleShapes.Count;
            _circleShapes.Add(new CircleShapeData { Radius = radius, LocalCenter = localCenter });
            return AddEntry(ColliderType2D.Circle, localIndex);
        }

        public int RegisterCircle(float radius, float localCenterX = 0f, float localCenterY = 0f)
        {
            return RegisterCircle(Fix64.FromFloat(radius), Fix64Vec2.FromFloat(localCenterX, localCenterY));
        }

        public int RegisterBox(Fix64 halfWidth, Fix64 halfHeight, Fix64Vec2 localCenter = default)
        {
            int localIndex = _boxShapes.Count;
            _boxShapes.Add(new BoxShapeData { HalfWidth = halfWidth, HalfHeight = halfHeight, LocalCenter = localCenter });
            return AddEntry(ColliderType2D.Box, localIndex);
        }

        public int RegisterBox(float halfWidth, float halfHeight, float localCenterX = 0f, float localCenterY = 0f)
        {
            return RegisterBox(Fix64.FromFloat(halfWidth), Fix64.FromFloat(halfHeight),
                Fix64Vec2.FromFloat(localCenterX, localCenterY));
        }

        public int RegisterPolygon(Fix64Vec2[] vertices, Fix64Vec2 localOffset = default)
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

            int localIndex = _polygonShapes.Count;
            _polygonShapes.Add(new PolygonShapeData
            {
                Vertices = vertices,
                VertexCount = vertices.Length,
                LocalCenter = center,
                LocalOffset = localOffset
            });

            return AddEntry(ColliderType2D.Polygon, localIndex);
        }

        public bool TryGetCircle(int index, out CircleShapeData data)
        {
            if (TryResolve(index, ColliderType2D.Circle, out int localIndex) &&
                (uint)localIndex < (uint)_circleShapes.Count)
            {
                data = _circleShapes[localIndex];
                return true;
            }

            data = default;
            return false;
        }

        public bool TryGetBox(int index, out BoxShapeData data)
        {
            if (TryResolve(index, ColliderType2D.Box, out int localIndex) &&
                (uint)localIndex < (uint)_boxShapes.Count)
            {
                data = _boxShapes[localIndex];
                return true;
            }

            data = default;
            return false;
        }

        public bool TryGetPolygon(int index, out PolygonShapeData data)
        {
            if (TryResolve(index, ColliderType2D.Polygon, out int localIndex) &&
                (uint)localIndex < (uint)_polygonShapes.Count)
            {
                data = _polygonShapes[localIndex];
                return true;
            }

            data = default;
            return false;
        }

        public ColliderType2D GetShapeType(int index)
        {
            if ((uint)index >= (uint)_entries.Count)
            {
                throw new KeyNotFoundException($"ShapeDataIndex not registered: {index}");
            }

            return _entries[index].Type;
        }

        public void Clear()
        {
            _circleShapes.Clear();
            _boxShapes.Clear();
            _polygonShapes.Clear();
            _entries.Clear();
        }

        private int AddEntry(ColliderType2D type, int localIndex)
        {
            int index = _entries.Count;
            _entries.Add(new ShapeEntry(type, localIndex));
            return index;
        }

        private bool TryResolve(int index, ColliderType2D expectedType, out int localIndex)
        {
            if ((uint)index < (uint)_entries.Count)
            {
                ShapeEntry entry = _entries[index];
                if (entry.Type == expectedType)
                {
                    localIndex = entry.LocalIndex;
                    return true;
                }
            }

            localIndex = -1;
            return false;
        }
    }
}
