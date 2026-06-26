using System;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Gameplay.Spawning
{
    public enum ManifestationObstacleShape2D : byte
    {
        Circle = 0,
        Box = 1,
        Polygon = 2
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

    /// <summary>
    /// Marker for persistent topology obstacles that are allowed to dirty runtime navmesh tiles.
    /// The obstacle geometry remains owned by ManifestationObstacleIntent2D or CompoundObstacle2D.
    /// </summary>
    public struct RuntimeNavMeshStructuralObstacle
    {
    }

    /// <summary>
    /// One logical entity can own several local-space obstacle pieces.
    /// Physics and navigation components are derived state; this component is the authored SSOT.
    /// </summary>
    public unsafe struct CompoundObstacle2D
    {
        public const int MaxPieces = 4;
        public const int MaxVerticesPerPolygon = ManifestationObstaclePolygon2D.MaxVertices;
        public const int MaxVertices = MaxPieces * MaxVerticesPerPolygon;

        public byte SinkPhysicsCollider;
        public byte SinkNavigationObstacle;
        public byte PieceCount;
        public fixed byte ShapeValues[MaxPieces];
        public fixed int RadiusCms[MaxPieces];
        public fixed int HalfWidthCms[MaxPieces];
        public fixed int HalfHeightCms[MaxPieces];
        public fixed int LocalOffsetXCms[MaxPieces];
        public fixed int LocalOffsetYCms[MaxPieces];
        public fixed int NavRadiusCms[MaxPieces];
        public fixed byte PolygonVertexCounts[MaxPieces];
        public fixed int VertexXs[MaxVertices];
        public fixed int VertexYs[MaxVertices];

        public void SetPiece(
            int pieceIndex,
            ManifestationObstacleShape2D shape,
            int radiusCm,
            int halfWidthCm,
            int halfHeightCm,
            int localOffsetXCm,
            int localOffsetYCm,
            int navRadiusCm)
        {
            ValidatePieceIndex(pieceIndex);
            fixed (byte* shapeValues = ShapeValues)
            fixed (int* radiusCms = RadiusCms)
            fixed (int* halfWidthCms = HalfWidthCms)
            fixed (int* halfHeightCms = HalfHeightCms)
            fixed (int* localOffsetXCms = LocalOffsetXCms)
            fixed (int* localOffsetYCms = LocalOffsetYCms)
            fixed (int* navRadiusCms = NavRadiusCms)
            {
                shapeValues[pieceIndex] = (byte)shape;
                radiusCms[pieceIndex] = radiusCm;
                halfWidthCms[pieceIndex] = halfWidthCm;
                halfHeightCms[pieceIndex] = halfHeightCm;
                localOffsetXCms[pieceIndex] = localOffsetXCm;
                localOffsetYCms[pieceIndex] = localOffsetYCm;
                navRadiusCms[pieceIndex] = navRadiusCm;
            }

            if (PieceCount < pieceIndex + 1)
            {
                PieceCount = (byte)(pieceIndex + 1);
            }
        }

        public readonly ManifestationObstacleShape2D GetShape(int pieceIndex)
        {
            ValidateDeclaredPieceIndex(pieceIndex);
            fixed (byte* shapeValues = ShapeValues)
            {
                return (ManifestationObstacleShape2D)shapeValues[pieceIndex];
            }
        }

        public readonly int GetRadiusCm(int pieceIndex)
        {
            ValidateDeclaredPieceIndex(pieceIndex);
            fixed (int* radiusCms = RadiusCms)
            {
                return radiusCms[pieceIndex];
            }
        }

        public readonly int GetHalfWidthCm(int pieceIndex)
        {
            ValidateDeclaredPieceIndex(pieceIndex);
            fixed (int* halfWidthCms = HalfWidthCms)
            {
                return halfWidthCms[pieceIndex];
            }
        }

        public readonly int GetHalfHeightCm(int pieceIndex)
        {
            ValidateDeclaredPieceIndex(pieceIndex);
            fixed (int* halfHeightCms = HalfHeightCms)
            {
                return halfHeightCms[pieceIndex];
            }
        }

        public readonly int GetLocalOffsetXCm(int pieceIndex)
        {
            ValidateDeclaredPieceIndex(pieceIndex);
            fixed (int* localOffsetXCms = LocalOffsetXCms)
            {
                return localOffsetXCms[pieceIndex];
            }
        }

        public readonly int GetLocalOffsetYCm(int pieceIndex)
        {
            ValidateDeclaredPieceIndex(pieceIndex);
            fixed (int* localOffsetYCms = LocalOffsetYCms)
            {
                return localOffsetYCms[pieceIndex];
            }
        }

        public readonly int GetNavRadiusCm(int pieceIndex)
        {
            ValidateDeclaredPieceIndex(pieceIndex);
            fixed (int* navRadiusCms = NavRadiusCms)
            {
                return navRadiusCms[pieceIndex];
            }
        }

        public void SetPolygonVertexCount(int pieceIndex, int vertexCount)
        {
            ValidatePieceIndex(pieceIndex);
            if (vertexCount < 0 || vertexCount > MaxVerticesPerPolygon)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexCount));
            }

            fixed (byte* counts = PolygonVertexCounts)
            {
                counts[pieceIndex] = (byte)vertexCount;
            }
        }

        public readonly int GetPolygonVertexCount(int pieceIndex)
        {
            ValidateDeclaredPieceIndex(pieceIndex);
            fixed (byte* counts = PolygonVertexCounts)
            {
                return counts[pieceIndex];
            }
        }

        public void SetVertex(int pieceIndex, int vertexIndex, in WorldCmInt2 value)
        {
            int offset = GetVertexOffset(pieceIndex, vertexIndex);
            fixed (int* xs = VertexXs)
            fixed (int* ys = VertexYs)
            {
                xs[offset] = value.X;
                ys[offset] = value.Y;
            }
        }

        public readonly WorldCmInt2 GetVertex(int pieceIndex, int vertexIndex)
        {
            int offset = GetDeclaredVertexOffset(pieceIndex, vertexIndex);
            fixed (int* xs = VertexXs)
            fixed (int* ys = VertexYs)
            {
                return new WorldCmInt2(xs[offset], ys[offset]);
            }
        }

        private static void ValidatePieceIndex(int pieceIndex)
        {
            if ((uint)pieceIndex >= MaxPieces)
            {
                throw new ArgumentOutOfRangeException(nameof(pieceIndex));
            }
        }

        private readonly void ValidateDeclaredPieceIndex(int pieceIndex)
        {
            if ((uint)pieceIndex >= PieceCount)
            {
                throw new ArgumentOutOfRangeException(nameof(pieceIndex));
            }
        }

        private static int GetVertexOffset(int pieceIndex, int vertexIndex)
        {
            ValidatePieceIndex(pieceIndex);
            if ((uint)vertexIndex >= MaxVerticesPerPolygon)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexIndex));
            }

            return pieceIndex * MaxVerticesPerPolygon + vertexIndex;
        }

        private readonly int GetDeclaredVertexOffset(int pieceIndex, int vertexIndex)
        {
            ValidateDeclaredPieceIndex(pieceIndex);
            if ((uint)vertexIndex >= (uint)GetPolygonVertexCount(pieceIndex))
            {
                throw new ArgumentOutOfRangeException(nameof(vertexIndex));
            }

            return GetVertexOffset(pieceIndex, vertexIndex);
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
