using System;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Physics2D.Components
{
    public struct Collider2D
    {
        public ColliderType2D Type;
        public int ShapeDataIndex;
    }

    public unsafe struct CompoundCollider2D
    {
        public byte PieceCount;
        public fixed byte PieceShapeValues[ObstacleGeometryProfile2D.MaxPieces];
        public fixed int ShapeDataIndices[ObstacleGeometryProfile2D.MaxPieces];

        public void SetPiece(int pieceIndex, ColliderType2D shape, int shapeDataIndex)
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

        public readonly (ColliderType2D Shape, int ShapeDataIndex) GetPiece(int pieceIndex)
        {
            if ((uint)pieceIndex >= PieceCount)
            {
                throw new ArgumentOutOfRangeException(nameof(pieceIndex));
            }

            fixed (byte* shapes = PieceShapeValues)
            fixed (int* indices = ShapeDataIndices)
            {
                return ((ColliderType2D)shapes[pieceIndex], indices[pieceIndex]);
            }
        }
    }

    public enum ColliderType2D : byte
    {
        Circle = 0,
        Box = 1,
        Polygon = 2
    }

    /// <summary>
    /// 物理材质（定点数）。
    /// </summary>
    public struct PhysicsMaterial2D
    {
        public Fix64 Friction;
        public Fix64 Restitution;
        public Fix64 BaseDamping;

        public static readonly PhysicsMaterial2D Default = new PhysicsMaterial2D
        {
            Friction = Fix64.HalfValue,
            Restitution = Fix64.Zero,
            BaseDamping = Fix64.FromFloat(0.98f)
        };
    }

    /// <summary>
    /// 阻尼场（定点数）。
    /// </summary>
    public struct DampingField
    {
        public Fix64 Radius;
        public Fix64 DampingValue;
    }

    /// <summary>
    /// 实体当前受到的场阻尼总量（定点数）。
    /// </summary>
    public struct AppliedDamping
    {
        public Fix64 TotalFieldDamping;
    }

    public struct Physics2DStaticBodyState
    {
        public int ShapeSignature;
        public int PoseSignature;
        public int SinkSignature;
        public int BodyCount;
    }

    public struct Physics2DStaticBodyDirty
    {
    }
}
