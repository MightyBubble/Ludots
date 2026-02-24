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

    public struct Mass2D
    {
        public Fix64 InverseMass;
        public Fix64 InverseInertia;
        public readonly bool IsStatic => InverseMass == Fix64.Zero;
        public readonly bool IsDynamic => InverseMass > Fix64.Zero;
        public static readonly Mass2D Static = new Mass2D { InverseMass = Fix64.Zero, InverseInertia = Fix64.Zero };

        public static Mass2D FromFloat(float inverseMass, float inverseInertia) => new Mass2D
        {
            InverseMass = Fix64.FromFloat(inverseMass),
            InverseInertia = Fix64.FromFloat(inverseInertia)
        };
    }
}
