namespace TeamResearchShowcaseMod.Runtime;

public readonly record struct TeamResearchSnapshot(
    int ActiveMemberCount,
    int ConfiguredMemberCount,
    int RequiredMemberCount,
    int Progress,
    int ResearchCost,
    int LastContribution,
    bool RequirementSatisfied,
    bool Unlocked,
    string Status,
    string[] MemberLines,
    string[] UnlockLines);
