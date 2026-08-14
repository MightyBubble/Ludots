using System;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial
{
    public enum SpatialBoundsKind : byte
    {
        Point = 0,
        Footprint2D = 1,
        Box3D = 2,
    }

    /// <summary>
    /// Core-owned local-space spatial bounds contract.
    /// The local center is resolved through WorldPositionCm and optional FacingDirection.
    /// </summary>
    public struct SpatialBounds
    {
        public byte KindValue;
        public int LocalCenterXCm;
        public int LocalCenterYCm;
        public int LocalCenterZCm;

        public SpatialBoundsKind Kind
        {
            readonly get => (SpatialBoundsKind)KindValue;
            set => KindValue = (byte)value;
        }

        public static SpatialBounds Point => new()
        {
            Kind = SpatialBoundsKind.Point,
        };
    }

    public struct SpatialBox3D
    {
        public int HalfSizeXCm;
        public int HalfSizeYCm;
        public int HalfSizeZCm;
    }

    /// <summary>
    /// Footprint polygons live on the local XZ plane and are transformed by WorldPositionCm plus FacingDirection.
    /// Multiple disjoint polygons are supported for selection and other screen-space queries.
    /// </summary>
    public unsafe struct SpatialFootprint2D
    {
        public const int MaxPolygons = 4;
        public const int MaxVerticesPerPolygon = 8;
        public const int MaxVertices = MaxPolygons * MaxVerticesPerPolygon;

        public byte PolygonCount;
        public fixed byte PolygonVertexCounts[MaxPolygons];
        public fixed int VertexXs[MaxVertices];
        public fixed int VertexZs[MaxVertices];

        public void SetPolygonVertexCount(int polygonIndex, int vertexCount)
        {
            if ((uint)polygonIndex >= MaxPolygons)
            {
                throw new ArgumentOutOfRangeException(nameof(polygonIndex));
            }

            if (vertexCount < 0 || vertexCount > MaxVerticesPerPolygon)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexCount));
            }

            fixed (byte* counts = PolygonVertexCounts)
            {
                counts[polygonIndex] = (byte)vertexCount;
            }

            if (PolygonCount < polygonIndex + 1)
            {
                PolygonCount = (byte)(polygonIndex + 1);
            }
        }

        public readonly int GetPolygonVertexCount(int polygonIndex)
        {
            if ((uint)polygonIndex >= PolygonCount)
            {
                throw new ArgumentOutOfRangeException(nameof(polygonIndex));
            }

            fixed (byte* counts = PolygonVertexCounts)
            {
                return counts[polygonIndex];
            }
        }

        public void SetVertex(int polygonIndex, int vertexIndex, in WorldCmInt2 value)
        {
            int offset = GetVertexOffset(polygonIndex, vertexIndex, requireDeclaredVertex: true);
            fixed (int* xs = VertexXs)
            fixed (int* zs = VertexZs)
            {
                xs[offset] = value.X;
                zs[offset] = value.Y;
            }
        }

        public readonly WorldCmInt2 GetVertex(int polygonIndex, int vertexIndex)
        {
            int offset = GetVertexOffset(polygonIndex, vertexIndex, requireDeclaredVertex: true);
            fixed (int* xs = VertexXs)
            fixed (int* zs = VertexZs)
            {
                return new WorldCmInt2(xs[offset], zs[offset]);
            }
        }

        private readonly int GetVertexOffset(int polygonIndex, int vertexIndex, bool requireDeclaredVertex)
        {
            if ((uint)polygonIndex >= MaxPolygons)
            {
                throw new ArgumentOutOfRangeException(nameof(polygonIndex));
            }

            fixed (byte* counts = PolygonVertexCounts)
            {
                int polygonCount = PolygonCount;
                if ((uint)polygonIndex >= polygonCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(polygonIndex));
                }

                int offset = 0;
                for (int i = 0; i < polygonIndex; i++)
                {
                    offset += counts[i];
                }

                int count = counts[polygonIndex];
                if (requireDeclaredVertex && ((uint)vertexIndex >= (uint)count))
                {
                    throw new ArgumentOutOfRangeException(nameof(vertexIndex));
                }

                if ((uint)vertexIndex >= MaxVerticesPerPolygon)
                {
                    throw new ArgumentOutOfRangeException(nameof(vertexIndex));
                }

                return offset + vertexIndex;
            }
        }
    }
}
