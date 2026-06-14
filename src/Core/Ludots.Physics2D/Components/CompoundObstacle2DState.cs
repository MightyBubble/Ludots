using System;
using Ludots.Core.Gameplay.Spawning;

namespace Ludots.Core.Physics2D.Components
{
    public unsafe struct CompoundObstacle2DState
    {
        public const int MaxPieces = CompoundObstacle2D.MaxPieces;

        public byte PieceCount;
        public byte SinkPhysicsCollider;
        public byte SinkNavigationObstacle;
        public int ShapeSignature;
        public fixed byte ShapeValues[MaxPieces];
        public fixed int ShapeDataIndices[MaxPieces];
        public fixed int NavRadiusCms[MaxPieces];

        public void SetPiece(
            int pieceIndex,
            ManifestationObstacleShape2D shape,
            int shapeDataIndex,
            int navRadiusCm)
        {
            ValidatePieceIndex(pieceIndex);
            fixed (byte* shapeValues = ShapeValues)
            fixed (int* shapeDataIndices = ShapeDataIndices)
            fixed (int* navRadiusCms = NavRadiusCms)
            {
                shapeValues[pieceIndex] = (byte)shape;
                shapeDataIndices[pieceIndex] = shapeDataIndex;
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

        public readonly int GetShapeDataIndex(int pieceIndex)
        {
            ValidateDeclaredPieceIndex(pieceIndex);
            fixed (int* shapeDataIndices = ShapeDataIndices)
            {
                return shapeDataIndices[pieceIndex];
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
    }
}
