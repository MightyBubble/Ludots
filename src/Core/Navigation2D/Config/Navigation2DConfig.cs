namespace Ludots.Core.Navigation2D.Config
{
    public sealed class Navigation2DConfig
    {
        public bool Enabled { get; set; } = false;
        public int MaxAgents { get; set; } = 50000;
        public int FlowIterationsPerTick { get; set; } = 4096;
    }
}
