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

        public Velocity2D VelocityA;
        public Velocity2D VelocityB;
        public Mass2D MassA;
        public Mass2D MassB;

        public Fix64 CombinedFriction;
        public Fix64 CombinedRestitution;

        public Fix64Vec2 Normal;
        public Fix64 Penetration;
        public Fix64Vec2 LocalContactPoint0;
        public Fix64Vec2 LocalContactPoint1;
        public int ContactCount;

        public Fix64 AccumulatedNormalImpulse0;
        public Fix64 AccumulatedTangentImpulse0;
        public Fix64 AccumulatedNormalImpulse1;
        public Fix64 AccumulatedTangentImpulse1;
    }
}
