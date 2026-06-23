using Ludots.Core.Gameplay.Exchange;

namespace GoldMarketShowcaseMod.Runtime;

public readonly record struct GoldMarketSnapshot(
    string BuyerName,
    string MarketName,
    int Gold,
    int Relics,
    int Bonuses,
    ExchangeExecutionStatus LastStatus,
    string Status,
    int SuccessfulPurchases,
    int FailedPurchases);
