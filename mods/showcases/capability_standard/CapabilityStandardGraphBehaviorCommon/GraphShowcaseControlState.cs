namespace CapabilityStandardGraphBehaviorCommon;

public sealed record GraphShowcaseControlState(
    string Title,
    string Summary,
    string Status,
    string Detail,
    string L2Description,
    bool Paused,
    bool L2Enabled,
    bool StimulusEnabled,
    float SightRadius,
    float ThinkPeriod,
    int AgentCount);
