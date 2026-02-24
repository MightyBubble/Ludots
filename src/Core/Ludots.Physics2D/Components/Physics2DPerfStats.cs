namespace Ludots.Core.Physics2D.Components
{
    public struct Physics2DPerfStats
    {
        public int FixedHz;
        public int PhysicsHz;
        public int PhysicsStepsLastFixedTick;
        public double PhysicsUpdateMs;
        public int PotentialPairs;
        public int ContactPairs;
    }
}
