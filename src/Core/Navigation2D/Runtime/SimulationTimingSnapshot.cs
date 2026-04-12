namespace Ludots.Core.Navigation2D.Runtime
{
    public sealed class SimulationTimingSnapshot
    {
        public int FixedHz { get; set; }
        public int NavigationHz { get; set; }
        public int NavigationStepsLastFixedTick { get; set; }
        public int PhysicsHz { get; set; }
        public int PhysicsStepsLastFixedTick { get; set; }
        public double NavigationMs { get; set; }
        public double PhysicsMs { get; set; }
    }
}
