using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Physics2D.Utils
{
    public static class MaterialCombiner
    {
        public static Fix64 CombineFriction(Fix64 frictionA, Fix64 frictionB) => Fix64Math.Sqrt(frictionA * frictionB);
        public static Fix64 CombineRestitution(Fix64 restitutionA, Fix64 restitutionB) => Fix64.Max(restitutionA, restitutionB);
    }
}
