using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Physics
{
    /// <summary>
    /// 2D 力输入（定点数 cm/s²）。
    /// 由 GAS ForceInput2DSink 写入，由 IntegrationSystem2D 消费。
    /// </summary>
    public struct ForceInput2D
    {
        public Fix64Vec2 Force;
    }
}
