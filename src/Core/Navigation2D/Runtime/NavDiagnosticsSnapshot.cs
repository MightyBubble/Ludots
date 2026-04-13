namespace Ludots.Core.Navigation2D.Runtime
{
    public sealed class NavDiagnosticsSnapshot
    {
        public int ActiveAgents { get; set; }
        public int ActiveGroups { get; set; }
        public int ArrivedGroups { get; set; }
        public int RetryCount { get; set; }
        public int TimeoutCount { get; set; }
        public int AbandonCount { get; set; }
        public int PreciseOrcaAgents { get; set; }
        public int CrowdFlowAgents { get; set; }
        public int HybridAgents { get; set; }
        public int FixedHz { get; set; }
        public int NavigationHz { get; set; }
        public int NavigationStepsLastFixedTick { get; set; }
        public int PhysicsHz { get; set; }
        public int PhysicsStepsLastFixedTick { get; set; }
        public double NavigationMs { get; set; }
        public double PhysicsMs { get; set; }
        public double PresentationMs { get; set; }
        public double FrameMs { get; set; }
        public long FrameAllocBytes { get; set; }
        public long HeapBytes { get; set; }
        public string ActiveRuleSummary { get; set; } = "n/a";
    }
}
