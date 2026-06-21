using Ludots.Core.Gameplay.Exchange;

namespace DiplomacyTradeGateShowcaseMod.Runtime;

public readonly record struct DiplomacyTradeGateSnapshot(
    string SourceName,
    string TargetName,
    int Trust,
    bool Embargo,
    int SourceCredits,
    int TargetGoods,
    int SuccessfulTrades,
    ExchangeExecutionStatus LastStatus,
    string Status,
    int RelationshipTypeId,
    int TrustMetricId,
    int EmbargoFlagId);
