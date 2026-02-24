namespace Ludots.Core.Engine
{
    public struct SimulationBudgetFuseEvent
    {
        public int LogicTick;
        public int BudgetMs;
        public int SliceLimit;
        public byte Reason;
    }
}
