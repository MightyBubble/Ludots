using System;
using Ludots.Core.Gameplay.Spawning;

namespace Ludots.Core.Navigation2D.Components
{
    public enum NavObstacleShape2D : byte
    {
        Circle = 0,
        Box = 1,
        Polygon = 2
    }

    public struct NavObstacle2D
    {
        public NavObstacleShape2D Shape;
        public int ShapeDataIndex;
    }

    public unsafe struct NavCompoundObstacle2D
    {
        public byte PieceCount;
        public fixed byte PieceShapeValues[ObstacleGeometryProfile2D.MaxPieces];
        public fixed int ShapeDataIndices[ObstacleGeometryProfile2D.MaxPieces];

        public void SetPiece(int pieceIndex, NavObstacleShape2D shape, int shapeDataIndex)
        {
            if ((uint)pieceIndex >= ObstacleGeometryProfile2D.MaxPieces)
            {
                throw new ArgumentOutOfRangeException(nameof(pieceIndex));
            }

            if (shapeDataIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(shapeDataIndex));
            }

            if (PieceCount < pieceIndex + 1)
            {
                PieceCount = (byte)(pieceIndex + 1);
            }

            fixed (byte* shapes = PieceShapeValues)
            fixed (int* indices = ShapeDataIndices)
            {
                shapes[pieceIndex] = (byte)shape;
                indices[pieceIndex] = shapeDataIndex;
            }
        }

        public readonly (NavObstacleShape2D Shape, int ShapeDataIndex) GetPiece(int pieceIndex)
        {
            if ((uint)pieceIndex >= PieceCount)
            {
                throw new ArgumentOutOfRangeException(nameof(pieceIndex));
            }

            fixed (byte* shapes = PieceShapeValues)
            fixed (int* indices = ShapeDataIndices)
            {
                return ((NavObstacleShape2D)shapes[pieceIndex], indices[pieceIndex]);
            }
        }
    }
}
