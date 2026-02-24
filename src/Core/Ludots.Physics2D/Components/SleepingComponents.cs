using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Physics2D.Components
{
    public struct SleepingTag
    {
    }

    public struct Island
    {
        public int IslandId;
    }

    /// <summary>
    /// 运动状态追踪（定点数），用于睡眠判定。
    /// </summary>
    public struct Motion
    {
        public Fix64 LinearSpeed;
        public Fix64 AngularSpeed;
        public int SleepTimer;
    }
}
