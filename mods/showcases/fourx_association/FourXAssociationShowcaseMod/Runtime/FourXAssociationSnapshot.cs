using Ludots.Core.Gameplay.Exchange;

namespace FourXAssociationShowcaseMod.Runtime;

public readonly record struct FourXAssociationSnapshot(
    bool HiddenBeforeScout,
    bool VisibleAfterScout,
    bool HiddenAfterDecay,
    int KnowledgeTick,
    int ExpiredKnowledgeRecords,
    ExchangeExecutionStatus TradeBeforePact,
    ExchangeExecutionStatus TradeAfterPact,
    int Gold,
    int SupplyCount,
    bool PactSigned,
    int ActiveResearchMembers,
    int RequiredResearchMembers,
    int ResearchProgress,
    int ResearchCost,
    bool ResearchRequirementSatisfied,
    bool TechUnlocked,
    bool OwnershipRootMatchesPlayer,
    bool OwnershipDirectCityToStash,
    string Status);
