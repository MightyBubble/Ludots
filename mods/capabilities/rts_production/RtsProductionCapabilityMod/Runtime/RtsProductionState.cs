using Arch.Core;

namespace RtsProductionCapabilityMod.Runtime;

public enum QueueItemKind
{
	Production,
	Research,
}

public enum TechNodeState
{
	Locked,
	Available,
	InProgress,
	Completed,
}

public enum TradeOfferState
{
	Draft,
	Pending,
	Accepted,
	Rejected,
	RelationshipDenied,
	InsufficientInput,
}

public readonly record struct RtsProductionQueueItem(
	string Id,
	QueueItemKind Kind,
	string FactionId,
	string Label,
	string Output,
	ProductionParadigm Paradigm,
	int StartedTick,
	int DurationTicks,
	int ProgressTicks,
	string Summary)
{
	public int ProgressPercent => DurationTicks <= 0 ? 100 : Math.Clamp(ProgressTicks * 100 / DurationTicks, 0, 100);
	public bool Complete => ProgressTicks >= DurationTicks;
}

public readonly record struct RtsProductionEntityRecord(
	Entity Entity,
	string Label,
	string Kind,
	string FactionId,
	bool Produced);

public readonly record struct RtsTechNodeSnapshot(
	string Id,
	string Label,
	string FactionId,
	TechNodeState State,
	string[] Requires,
	string Summary);

public readonly record struct RtsTreatySnapshot(
	string Id,
	string Label,
	string SourceFactionId,
	string TargetFactionId,
	int Trust,
	bool TradePact,
	bool AtWar,
	bool Embargo);

public readonly record struct RtsTradeOfferSnapshot(
	string Id,
	string Label,
	string SourceFactionId,
	string TargetFactionId,
	string GiveResource,
	int GiveAmount,
	string ReceiveResource,
	int ReceiveAmount,
	TradeOfferState State);

public readonly record struct RtsFactionSnapshot(
	string Id,
	string Label,
	int PlayerId,
	int TeamId,
	string Accent,
	bool AiControlled,
	IReadOnlyList<ResourceAmountConfig> Resources,
	IReadOnlyList<RtsProductionEntityRecord> Units,
	IReadOnlyList<RtsProductionEntityRecord> Buildings,
	IReadOnlyList<RtsProductionQueueItem> Queue);

public sealed class RtsProductionSnapshot
{
	public static RtsProductionSnapshot Empty { get; } = new();

	public string ScenarioId { get; init; } = string.Empty;
	public string Title { get; init; } = "Production Showcase";
	public string Flavor { get; init; } = string.Empty;
	public string HudStyle { get; init; } = string.Empty;
	public string Summary { get; init; } = string.Empty;
	public string CurrentFactionId { get; init; } = string.Empty;
	public int CurrentTick { get; init; }
	public bool ScenarioReady { get; init; }
	public IReadOnlyList<string> Acceptance { get; init; } = Array.Empty<string>();
	public IReadOnlyList<RtsFactionSnapshot> Factions { get; init; } = Array.Empty<RtsFactionSnapshot>();
	public IReadOnlyList<ProductionLineConfig> AvailableProduction { get; init; } = Array.Empty<ProductionLineConfig>();
	public IReadOnlyList<RtsTechNodeSnapshot> Techs { get; init; } = Array.Empty<RtsTechNodeSnapshot>();
	public IReadOnlyList<RtsTreatySnapshot> Treaties { get; init; } = Array.Empty<RtsTreatySnapshot>();
	public IReadOnlyList<RtsTradeOfferSnapshot> TradeOffers { get; init; } = Array.Empty<RtsTradeOfferSnapshot>();
	public IReadOnlyList<string> LogLines { get; init; } = Array.Empty<string>();
	public int SaveSlotCount { get; init; }
	public string SaveStatus { get; init; } = "Save UI ready.";
	public string AiStatus { get; init; } = "AI idle.";
}
