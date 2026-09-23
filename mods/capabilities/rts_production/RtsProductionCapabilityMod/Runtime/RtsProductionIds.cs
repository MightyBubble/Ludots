using Ludots.Core.Config;

namespace RtsProductionCapabilityMod.Runtime;

public static class RtsProductionIds
{
	public const string InstalledKey = "RtsProductionCapabilityMod.Installed";
	public const string RuntimeKey = "RtsProductionCapabilityMod.Runtime";
	public const string AiDisabledKey = "RtsProductionCapabilityMod.AiDisabled";
	public const string ActivationMapTag = "capability.rts_production";
	public const string DiplomacyType = "Showcase.Diplomacy";
	public const string TrustMetric = "Showcase.Trust";
	public const string TradePactFlag = "Showcase.TradePact";
	public const string AtWarFlag = "Showcase.AtWar";
	public const string EmbargoFlag = "Showcase.Embargo";
	public const string TradeOperationId = "showcase.production.trade.attribute";
	public const string FactionScopeKey = "showcase.faction";

	public static bool IsProductionMap(MapConfig? mapConfig)
	{
		if (mapConfig?.Tags == null)
		{
			return false;
		}

		for (int i = 0; i < mapConfig.Tags.Count; i++)
		{
			if (string.Equals(mapConfig.Tags[i], ActivationMapTag, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}
}
