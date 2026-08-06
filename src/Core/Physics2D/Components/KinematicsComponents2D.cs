using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Physics2D.Components
{
    public struct Velocity2D
    {
        public Fix64Vec2 Linear;
        public Fix64 Angular;

        public static readonly Velocity2D Zero = new Velocity2D
        {
            Linear = Fix64Vec2.Zero,
            Angular = Fix64.Zero
        };

        public static Velocity2D FromCmPerSec(float vx, float vy, float angular = 0f) => new Velocity2D
        {
            Linear = Fix64Vec2.FromFloat(vx, vy),
            Angular = Fix64.FromFloat(angular)
        };
    }

    /// <summary>
    /// Three-state body model (issue #732):
    /// Static    — InverseMass == 0, no kinematic flag; never moves, cached by the static broadphase layer.
    /// Dynamic   — InverseMass > 0; integrated, receives forces and impulses.
    /// Kinematic — InverseMass == 0 with the kinematic flag; pose driven externally each fixed step,
    ///             infinite mass for the solver, tracked in the dynamic broadphase layer.
    /// The body type is an authoring-time declaration and must not change during the entity lifetime.
    /// </summary>
    public struct Mass2D
    {
        public Fix64 InverseMass;
        public Fix64 InverseInertia;
        public byte KinematicFlag;
        public readonly bool IsStatic => KinematicFlag == 0 && InverseMass == Fix64.Zero;
        public readonly bool IsDynamic => KinematicFlag == 0 && InverseMass > Fix64.Zero;
        public readonly bool IsKinematic => KinematicFlag != 0;
        public static readonly Mass2D Static = new Mass2D { InverseMass = Fix64.Zero, InverseInertia = Fix64.Zero };
        public static readonly Mass2D Kinematic = new Mass2D { InverseMass = Fix64.Zero, InverseInertia = Fix64.Zero, KinematicFlag = 1 };

        public static Mass2D FromFloat(float inverseMass, float inverseInertia) => new Mass2D
        {
            InverseMass = Fix64.FromFloat(inverseMass),
            InverseInertia = Fix64.FromFloat(inverseInertia)
        };
    }
}
