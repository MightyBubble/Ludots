namespace Ludots.Core.Gameplay.GAS.Components
{
    public struct EntityLocalClock
    {
        public int AccumulatorPermille;
        public int LocalStep;
        public int ScalePermille;
        // 0 表示 Time.EntityScalePermille 还没写过。默认 0 同时也是合法的冻住，时钟不能把没落地的字段当成冻住。
        public byte ScaleLanded;
    }
}
