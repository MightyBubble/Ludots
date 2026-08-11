namespace CapabilityStandardGraphBehaviorCommon;

public sealed class GraphShowcaseMetrics
{
    public string ShowcaseId { get; set; } = "";
    public int AgentCount;
    public double LastThinkMs;
    public double MaxThinkMs;
    public int ThinkWaves;
    public string Detail { get; set; } = "";
}
