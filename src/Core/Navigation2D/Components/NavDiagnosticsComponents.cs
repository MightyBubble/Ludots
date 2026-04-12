namespace Ludots.Core.Navigation2D.Components
{
    public struct Navigation2DPerfStats
    {
        public int FixedHz;
        public int NavigationHz;
        public int NavigationStepsLastFixedTick;
        public double NavigationUpdateMs;
        public int ActiveAgents;
        public int ActiveGroups;
    }
}
