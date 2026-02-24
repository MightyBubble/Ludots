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

    public struct SelectedTag
    {
    }
}
