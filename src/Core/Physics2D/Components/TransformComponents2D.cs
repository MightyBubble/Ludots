using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Physics2D.Components
{
    public struct Position2D
    {
        public Fix64Vec2 Value;

        public static readonly Position2D Zero = new Position2D { Value = Fix64Vec2.Zero };

        public static Position2D FromCm(int x, int y) => new Position2D
        {
            Value = Fix64Vec2.FromInt(x, y)
        };

        public static Position2D FromCmFloat(float x, float y) => new Position2D
        {
            Value = Fix64Vec2.FromFloat(x, y)
        };
    }

    public struct Rotation2D
    {
        public Fix64 Value;

        public static readonly Rotation2D Identity = new Rotation2D { Value = Fix64.Zero };

        public static Rotation2D FromRadians(float radians) => new Rotation2D
        {
            Value = Fix64.FromFloat(radians)
        };

        public static Rotation2D FromDegrees(float degrees) => new Rotation2D
        {
            Value = Fix64.FromFloat(degrees) * Fix64.Deg2Rad
        };
    }

    public struct PreviousPosition2D
    {
        public Fix64Vec2 Value;
    }
}
