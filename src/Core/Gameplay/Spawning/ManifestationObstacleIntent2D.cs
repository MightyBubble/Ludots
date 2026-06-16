using System;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Gameplay.Spawning
{
    public enum ManifestationObstacleShape2D : byte
    {
        Circle = 0,
        Box = 1,
        Polygon = 2,
        GeometryProfile = 3
    }

    public enum ObstacleGeometryPieceShape2D : byte
    {
        Circle = 0,
        Box = 1,
        Polygon = 2
    }

    public struct ObstacleGeometryPiece2D
    {
        public ObstacleGeometryPieceShape2D Shape;
        public int RadiusCm;
        public int HalfWidthCm;
        public int HalfHeightCm;
        public int LocalOffsetXCm;
        public int LocalOffsetYCm;
        public byte VertexCount;
    }

    /// <summary>
    /// Core-owned authored obstacle geometry profile for one logical entity.
    /// Pieces are local-space primitives; lower layers materialize them without child entities.
    /// </summary>
    public unsafe struct ObstacleGeometryProfile2D
    {
        public const int MaxPieces = 32;
        public const int MaxPolygonVertices = 8;
        public const int MaxPolygonVertexSlots = MaxPieces * MaxPolygonVertices;

        public byte PieceCount;
        public fixed byte PieceShapeValues[MaxPieces];
        public fixed int RadiusCms[MaxPieces];
        public fixed int HalfWidthCms[MaxPieces];
        public fixed int HalfHeightCms[MaxPieces];
        public fixed int LocalOffsetXCms[MaxPieces];
        public fixed int LocalOffsetYCms[MaxPieces];
        public fixed byte PolygonVertexCounts[MaxPieces];
        public fixed int PolygonVertexXCms[MaxPolygonVertexSlots];
        public fixed int PolygonVertexYCms[MaxPolygonVertexSlots];

        public void SetCircle(int pieceIndex, int radiusCm, int localOffsetXCm, int localOffsetYCm)
        {
            ValidatePieceIndex(pieceIndex);
            if (radiusCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radiusCm));
            }

            EnsurePieceCount(pieceIndex);
            fixed (byte* shapes = PieceShapeValues)
            fixed (int* radii = RadiusCms)
            fixed (int* halfWidths = HalfWidthCms)
            fixed (int* halfHeights = HalfHeightCms)
            fixed (int* offsetsX = LocalOffsetXCms)
            fixed (int* offsetsY = LocalOffsetYCms)
            fixed (byte* vertexCounts = PolygonVertexCounts)
            {
                shapes[pieceIndex] = (byte)ObstacleGeometryPieceShape2D.Circle;
                radii[pieceIndex] = radiusCm;
                halfWidths[pieceIndex] = 0;
                halfHeights[pieceIndex] = 0;
                offsetsX[pieceIndex] = localOffsetXCm;
                offsetsY[pieceIndex] = localOffsetYCm;
                vertexCounts[pieceIndex] = 0;
            }
        }

        public void SetBox(int pieceIndex, int halfWidthCm, int halfHeightCm, int localOffsetXCm, int localOffsetYCm)
        {
            ValidatePieceIndex(pieceIndex);
            if (halfWidthCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(halfWidthCm));
            }

            if (halfHeightCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(halfHeightCm));
            }

            EnsurePieceCount(pieceIndex);
            fixed (byte* shapes = PieceShapeValues)
            fixed (int* radii = RadiusCms)
            fixed (int* halfWidths = HalfWidthCms)
            fixed (int* halfHeights = HalfHeightCms)
            fixed (int* offsetsX = LocalOffsetXCms)
            fixed (int* offsetsY = LocalOffsetYCms)
            fixed (byte* vertexCounts = PolygonVertexCounts)
            {
                shapes[pieceIndex] = (byte)ObstacleGeometryPieceShape2D.Box;
                radii[pieceIndex] = 0;
                halfWidths[pieceIndex] = halfWidthCm;
                halfHeights[pieceIndex] = halfHeightCm;
                offsetsX[pieceIndex] = localOffsetXCm;
                offsetsY[pieceIndex] = localOffsetYCm;
                vertexCounts[pieceIndex] = 0;
            }
        }

        public void SetPolygonVertexCount(int pieceIndex, int vertexCount)
        {
            ValidatePieceIndex(pieceIndex);
            if (vertexCount < 3 || vertexCount > MaxPolygonVertices)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexCount));
            }

            EnsurePieceCount(pieceIndex);
            fixed (byte* shapes = PieceShapeValues)
            fixed (int* radii = RadiusCms)
            fixed (int* halfWidths = HalfWidthCms)
            fixed (int* halfHeights = HalfHeightCms)
            fixed (byte* vertexCounts = PolygonVertexCounts)
            {
                shapes[pieceIndex] = (byte)ObstacleGeometryPieceShape2D.Polygon;
                radii[pieceIndex] = 0;
                halfWidths[pieceIndex] = 0;
                halfHeights[pieceIndex] = 0;
                vertexCounts[pieceIndex] = (byte)vertexCount;
            }
        }

        public void SetPieceLocalOffset(int pieceIndex, int localOffsetXCm, int localOffsetYCm)
        {
            ValidatePieceIndex(pieceIndex);
            EnsurePieceCount(pieceIndex);
            fixed (int* offsetsX = LocalOffsetXCms)
            fixed (int* offsetsY = LocalOffsetYCms)
            {
                offsetsX[pieceIndex] = localOffsetXCm;
                offsetsY[pieceIndex] = localOffsetYCm;
            }
        }

        public void SetPolygonVertex(int pieceIndex, int vertexIndex, in WorldCmInt2 value)
        {
            ValidatePieceIndex(pieceIndex);
            ValidateVertexIndex(vertexIndex);
            int slot = (pieceIndex * MaxPolygonVertices) + vertexIndex;
            fixed (int* xs = PolygonVertexXCms)
            fixed (int* ys = PolygonVertexYCms)
            {
                xs[slot] = value.X;
                ys[slot] = value.Y;
            }
        }

        public readonly ObstacleGeometryPiece2D GetPiece(int pieceIndex)
        {
            if ((uint)pieceIndex >= PieceCount)
            {
                throw new ArgumentOutOfRangeException(nameof(pieceIndex));
            }

            fixed (byte* shapes = PieceShapeValues)
            fixed (int* radii = RadiusCms)
            fixed (int* halfWidths = HalfWidthCms)
            fixed (int* halfHeights = HalfHeightCms)
            fixed (int* offsetsX = LocalOffsetXCms)
            fixed (int* offsetsY = LocalOffsetYCms)
            fixed (byte* vertexCounts = PolygonVertexCounts)
            {
                return new ObstacleGeometryPiece2D
                {
                    Shape = (ObstacleGeometryPieceShape2D)shapes[pieceIndex],
                    RadiusCm = radii[pieceIndex],
                    HalfWidthCm = halfWidths[pieceIndex],
                    HalfHeightCm = halfHeights[pieceIndex],
                    LocalOffsetXCm = offsetsX[pieceIndex],
                    LocalOffsetYCm = offsetsY[pieceIndex],
                    VertexCount = vertexCounts[pieceIndex]
                };
            }
        }

        public readonly WorldCmInt2 GetPolygonVertex(int pieceIndex, int vertexIndex)
        {
            if ((uint)pieceIndex >= PieceCount)
            {
                throw new ArgumentOutOfRangeException(nameof(pieceIndex));
            }

            ValidateVertexIndex(vertexIndex);
            int slot = (pieceIndex * MaxPolygonVertices) + vertexIndex;
            fixed (int* xs = PolygonVertexXCms)
            fixed (int* ys = PolygonVertexYCms)
            {
                return new WorldCmInt2(xs[slot], ys[slot]);
            }
        }

        private void EnsurePieceCount(int pieceIndex)
        {
            if (PieceCount < pieceIndex + 1)
            {
                PieceCount = (byte)(pieceIndex + 1);
            }
        }

        private static void ValidatePieceIndex(int pieceIndex)
        {
            if ((uint)pieceIndex >= MaxPieces)
            {
                throw new ArgumentOutOfRangeException(nameof(pieceIndex));
            }
        }

        private static void ValidateVertexIndex(int vertexIndex)
        {
            if ((uint)vertexIndex >= MaxPolygonVertices)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexIndex));
            }
        }
    }

    /// <summary>
    /// Runtime manifestation declares blocker intent here; lower layers materialize
    /// the actual physics collider and/or navigation obstacle components.
    /// </summary>
    public struct ManifestationObstacleIntent2D
    {
        public ManifestationObstacleShape2D Shape;
        public byte SinkPhysicsCollider;
        public byte SinkNavigationObstacle;
        public int RadiusCm;
        public int HalfWidthCm;
        public int HalfHeightCm;
        public int LocalOffsetXCm;
        public int LocalOffsetYCm;
        public int NavRadiusCm;
    }

    public struct ManifestationObstaclePolygon2D
    {
        public const int MaxVertices = 8;

        public byte VertexCount;
        public WorldCmInt2 Vertex0;
        public WorldCmInt2 Vertex1;
        public WorldCmInt2 Vertex2;
        public WorldCmInt2 Vertex3;
        public WorldCmInt2 Vertex4;
        public WorldCmInt2 Vertex5;
        public WorldCmInt2 Vertex6;
        public WorldCmInt2 Vertex7;

        public readonly WorldCmInt2 GetVertex(int index)
        {
            return index switch
            {
                0 => Vertex0,
                1 => Vertex1,
                2 => Vertex2,
                3 => Vertex3,
                4 => Vertex4,
                5 => Vertex5,
                6 => Vertex6,
                7 => Vertex7,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
        }

        public void SetVertex(int index, in WorldCmInt2 value)
        {
            switch (index)
            {
                case 0:
                    Vertex0 = value;
                    break;
                case 1:
                    Vertex1 = value;
                    break;
                case 2:
                    Vertex2 = value;
                    break;
                case 3:
                    Vertex3 = value;
                    break;
                case 4:
                    Vertex4 = value;
                    break;
                case 5:
                    Vertex5 = value;
                    break;
                case 6:
                    Vertex6 = value;
                    break;
                case 7:
                    Vertex7 = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }

    public struct ManifestationObstacleBridge2DState
    {
        public int ShapeDataIndex;
        public int ShapeSignature;
        public int PoseSignature;
        public int SinkSignature;
    }

    public struct ManifestationObstacleBridge2DDirty
    {
    }
}
