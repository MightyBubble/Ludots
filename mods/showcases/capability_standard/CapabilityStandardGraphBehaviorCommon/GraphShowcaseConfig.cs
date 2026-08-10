namespace CapabilityStandardGraphBehaviorCommon;

public sealed class GraphShowcaseConfig
{
    public int AgentCount { get; set; } = 10_000;
    public float ThinkPeriodSeconds { get; set; } = 0.2f;
    public int BtLeafCount { get; set; } = 7;
    public float ThinkBudgetMs { get; set; } = 5f;
}
