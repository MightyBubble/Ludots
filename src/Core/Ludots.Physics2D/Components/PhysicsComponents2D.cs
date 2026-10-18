using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Physics2D.Components
{
    public struct Collider2D
    {
        public ColliderType2D Type;
        public int ShapeDataIndex;
    }

    public enum ColliderType2D : byte
    {
        Circle = 0,
        Box = 1,
        Polygon = 2
    }

    public struct PhysicsMaterial2D
    {
        public Fix64 Friction;
        public Fix64 Restitution;
        public Fix64 BaseDamping;
    }

    public struct DampingField
    {
        public Fix64 Radius;
        public Fix64 DampingValue;
    }

    public struct AppliedDamping
    {
        public Fix64 TotalFieldDamping;
    }
}
