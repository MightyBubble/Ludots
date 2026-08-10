namespace CapabilityStandardGraphBehaviorCommon;

public sealed class GraphShowcaseConfig
{
    public int FeaturedAgentCount { get; set; } = 12;
    public int CrowdBandCount { get; set; } = 10_000;
    public bool ShowCrowdBand { get; set; } = true;
    public float ThinkPeriodSeconds { get; set; } = 0.2f;
    public float ThinkBudgetMs { get; set; } = 5f;
    public float SightRadius { get; set; } = 5.5f;
    public float AttackRadius { get; set; } = 1.25f;
    public float AlertRadius { get; set; } = 5f;
    public float CombatRadius { get; set; } = 2f;
    public float PatrolSpeed { get; set; } = 3.2f;
    public float ChaseSpeed { get; set; } = 5.5f;

    /// <summary>Legacy alias used by older metrics paths.</summary>
    public int AgentCount
    {
        get => FeaturedAgentCount;
        set => FeaturedAgentCount = value;
    }
}
