using Arch.Core;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Physics2D.Components
{
    public struct ActiveCollisionPairTag
    {
    }

    /// <summary>
    /// 碰撞对（全定点数域）。
    /// 存储碰撞检测结果和迭代求解器的累积冲量。
    /// </summary>
    public struct CollisionPair
    {
        public bool IsActive;
        public Entity EntityA;
        public Entity EntityB;
        public byte ShapeSlotA;
        public byte ShapeSlotB;

        public Position2D PositionA;
        public Position2D PositionB;
        public Rotation2D RotationA;
        public Rotation2D RotationB;
        public Collider2D ColliderA;
        public Collider2D ColliderB;
        public Velocity2D VelocityA;
        public Velocity2D VelocityB;
        public Mass2D MassA;
        public Mass2D MassB;
        public PhysicsMaterial2D MaterialA;
        public PhysicsMaterial2D MaterialB;
        public byte HasMaterialA;
        public byte HasMaterialB;
        public byte IsSleepingA;
        public byte IsSleepingB;

        public Fix64 CombinedFriction;
        public Fix64 CombinedRestitution;

        public Fix64Vec2 Normal;
        public Fix64 Penetration;
        public int ContactCount;

        public Fix64 AccumulatedNormalImpulse0;
        public Fix64 AccumulatedTangentImpulse0;
    }
}
