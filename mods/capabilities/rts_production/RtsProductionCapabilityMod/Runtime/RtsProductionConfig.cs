using System.Text.Json;
using System.Text.Json.Serialization;

namespace RtsProductionCapabilityMod.Runtime;

public sealed class RtsProductionConfig
{
	public string ScenarioId { get; set; } = string.Empty;
	public string Title { get; set; } = string.Empty;
	public string Flavor { get; set; } = string.Empty;
	public string HudStyle { get; set; } = string.Empty;
	public string MapId { get; set; } = string.Empty;
	public string Summary { get; set; } = string.Empty;
	public string[] Acceptance { get; set; } = Array.Empty<string>();
	public string[] Resources { get; set; } = Array.Empty<string>();
	public FactionConfig[] Factions { get; set; } = Array.Empty<FactionConfig>();
	public ProductionLineConfig[] ProductionLines { get; set; } = Array.Empty<ProductionLineConfig>();
	public TechConfig[] Techs { get; set; } = Array.Empty<TechConfig>();
	public TreatyConfig[] Treaties { get; set; } = Array.Empty<TreatyConfig>();
	public TradeOfferConfig[] TradeOffers { get; set; } = Array.Empty<TradeOfferConfig>();
	public AiConfig[] Ai { get; set; } = Array.Empty<AiConfig>();

	public static RtsProductionConfig Load(Stream stream)
	{
		var options = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true,
			ReadCommentHandling = JsonCommentHandling.Skip,
			AllowTrailingCommas = true,
		};
		options.Converters.Add(new JsonStringEnumConverter());
		return JsonSerializer.Deserialize<RtsProductionConfig>(stream, options)
			?? throw new InvalidOperationException("RtsProduction scenario config could not be deserialized.");
	}

	public void Validate()
	{
		Require(ScenarioId, "scenarioId");
		Require(Title, "title");
		Require(MapId, "mapId");
		if (Resources.Length == 0)
		{
			throw new InvalidOperationException($"Scenario '{ScenarioId}' must define at least one resource.");
		}

		if (Factions.Length < 2)
		{
			throw new InvalidOperationException($"Scenario '{ScenarioId}' must define at least two factions.");
		}

		if (ProductionLines.Length == 0)
		{
			throw new InvalidOperationException($"Scenario '{ScenarioId}' must define production lines.");
		}

		if (Techs.Length == 0)
		{
			throw new InvalidOperationException($"Scenario '{ScenarioId}' must define progression techs.");
		}

		if (Treaties.Length == 0)
		{
			throw new InvalidOperationException($"Scenario '{ScenarioId}' must define diplomacy treaties.");
		}

		if (TradeOffers.Length == 0)
		{
			throw new InvalidOperationException($"Scenario '{ScenarioId}' must define trade offers.");
		}

		for (int i = 0; i < Factions.Length; i++)
		{
			Factions[i].Validate(ScenarioId, i);
		}

		for (int i = 0; i < ProductionLines.Length; i++)
		{
			ProductionLines[i].Validate(ScenarioId, i);
			RequireFaction(ProductionLines[i].FactionId);
		}

		for (int i = 0; i < Techs.Length; i++)
		{
			Techs[i].Validate(ScenarioId, i);
			RequireFaction(Techs[i].FactionId);
		}

		for (int i = 0; i < Treaties.Length; i++)
		{
			Treaties[i].Validate(ScenarioId, i);
			RequireFaction(Treaties[i].SourceFactionId);
			RequireFaction(Treaties[i].TargetFactionId);
		}

		for (int i = 0; i < TradeOffers.Length; i++)
		{
			TradeOffers[i].Validate(ScenarioId, i);
			RequireFaction(TradeOffers[i].SourceFactionId);
			RequireFaction(TradeOffers[i].TargetFactionId);
			RequireResource(TradeOffers[i].GiveResource);
			RequireResource(TradeOffers[i].ReceiveResource);
		}
	}

	public FactionConfig RequireFactionConfig(string factionId)
	{
		for (int i = 0; i < Factions.Length; i++)
		{
			if (string.Equals(Factions[i].Id, factionId, StringComparison.Ordinal))
			{
				return Factions[i];
			}
		}

		throw new InvalidOperationException($"Scenario '{ScenarioId}' references unknown faction '{factionId}'.");
	}

	public bool HasTech(string techId)
	{
		for (int i = 0; i < Techs.Length; i++)
		{
			if (string.Equals(Techs[i].Id, techId, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private void RequireFaction(string factionId) => _ = RequireFactionConfig(factionId);

	private void RequireResource(string resource)
	{
		for (int i = 0; i < Resources.Length; i++)
		{
			if (string.Equals(Resources[i], resource, StringComparison.Ordinal))
			{
				return;
			}
		}

		throw new InvalidOperationException($"Scenario '{ScenarioId}' references unknown resource '{resource}'.");
	}

	private static void Require(string value, string field)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidOperationException($"RtsProduction config requires '{field}'.");
		}
	}
}

public sealed class FactionConfig
{
	public string Id { get; set; } = string.Empty;
	public string Label { get; set; } = string.Empty;
	public int PlayerId { get; set; }
	public int TeamId { get; set; }
	public string Accent { get; set; } = "#F6D77C";
	public ResourceAmountConfig[] StartingResources { get; set; } = Array.Empty<ResourceAmountConfig>();
	public string[] StartingUnits { get; set; } = Array.Empty<string>();
	public string[] StartingBuildings { get; set; } = Array.Empty<string>();
	public bool AiControlled { get; set; }

	public void Validate(string scenarioId, int index)
	{
		Require(Id, scenarioId, $"factions[{index}].id");
		Require(Label, scenarioId, $"factions[{index}].label");
		if (PlayerId <= 0 || TeamId <= 0)
		{
			throw new InvalidOperationException($"Scenario '{scenarioId}' faction '{Id}' must define positive playerId and teamId.");
		}
	}

	private static void Require(string value, string scenarioId, string field)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidOperationException($"Scenario '{scenarioId}' requires '{field}'.");
		}
	}
}

public sealed class ResourceAmountConfig
{
	public string Resource { get; set; } = string.Empty;
	public int Amount { get; set; }
}

public enum ProductionParadigm
{
	DirectBuild,
	DeployBuild,
	WorkerBuild,
	WarpBuild,
	MorphBuild,
	CityQueue,
	Train,
}

public sealed class ProductionLineConfig
{
	public string Id { get; set; } = string.Empty;
	public string FactionId { get; set; } = string.Empty;
	public string Label { get; set; } = string.Empty;
	public ProductionParadigm Paradigm { get; set; }
	public string Producer { get; set; } = string.Empty;
	public string Output { get; set; } = string.Empty;
	public string OutputKind { get; set; } = "Unit";
	public int DurationTicks { get; set; } = 90;
	public ResourceAmountConfig[] Cost { get; set; } = Array.Empty<ResourceAmountConfig>();
	public string? RequiredTech { get; set; }
	public string Summary { get; set; } = string.Empty;

	public void Validate(string scenarioId, int index)
	{
		Require(Id, scenarioId, $"productionLines[{index}].id");
		Require(FactionId, scenarioId, $"productionLines[{index}].factionId");
		Require(Label, scenarioId, $"productionLines[{index}].label");
		Require(Output, scenarioId, $"productionLines[{index}].output");
		if (DurationTicks <= 0)
		{
			throw new InvalidOperationException($"Scenario '{scenarioId}' production line '{Id}' durationTicks must be positive.");
		}
	}

	private static void Require(string value, string scenarioId, string field)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidOperationException($"Scenario '{scenarioId}' requires '{field}'.");
		}
	}
}

public sealed class TechConfig
{
	public string Id { get; set; } = string.Empty;
	public string FactionId { get; set; } = string.Empty;
	public string Label { get; set; } = string.Empty;
	public int DurationTicks { get; set; } = 120;
	public string[] Requires { get; set; } = Array.Empty<string>();
	public ResourceAmountConfig[] Cost { get; set; } = Array.Empty<ResourceAmountConfig>();
	public string Summary { get; set; } = string.Empty;

	public void Validate(string scenarioId, int index)
	{
		Require(Id, scenarioId, $"techs[{index}].id");
		Require(FactionId, scenarioId, $"techs[{index}].factionId");
		Require(Label, scenarioId, $"techs[{index}].label");
		if (DurationTicks <= 0)
		{
			throw new InvalidOperationException($"Scenario '{scenarioId}' tech '{Id}' durationTicks must be positive.");
		}
	}

	private static void Require(string value, string scenarioId, string field)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidOperationException($"Scenario '{scenarioId}' requires '{field}'.");
		}
	}
}

public sealed class TreatyConfig
{
	public string Id { get; set; } = string.Empty;
	public string Label { get; set; } = string.Empty;
	public string SourceFactionId { get; set; } = string.Empty;
	public string TargetFactionId { get; set; } = string.Empty;
	public int TrustDelta { get; set; } = 25;
	public bool TradePact { get; set; } = true;
	public bool Peace { get; set; } = true;

	public void Validate(string scenarioId, int index)
	{
		Require(Id, scenarioId, $"treaties[{index}].id");
		Require(Label, scenarioId, $"treaties[{index}].label");
		Require(SourceFactionId, scenarioId, $"treaties[{index}].sourceFactionId");
		Require(TargetFactionId, scenarioId, $"treaties[{index}].targetFactionId");
	}

	private static void Require(string value, string scenarioId, string field)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidOperationException($"Scenario '{scenarioId}' requires '{field}'.");
		}
	}
}

public sealed class TradeOfferConfig
{
	public string Id { get; set; } = string.Empty;
	public string Label { get; set; } = string.Empty;
	public string SourceFactionId { get; set; } = string.Empty;
	public string TargetFactionId { get; set; } = string.Empty;
	public string GiveResource { get; set; } = string.Empty;
	public int GiveAmount { get; set; }
	public string ReceiveResource { get; set; } = string.Empty;
	public int ReceiveAmount { get; set; }

	public void Validate(string scenarioId, int index)
	{
		Require(Id, scenarioId, $"tradeOffers[{index}].id");
		Require(Label, scenarioId, $"tradeOffers[{index}].label");
		Require(SourceFactionId, scenarioId, $"tradeOffers[{index}].sourceFactionId");
		Require(TargetFactionId, scenarioId, $"tradeOffers[{index}].targetFactionId");
		Require(GiveResource, scenarioId, $"tradeOffers[{index}].giveResource");
		Require(ReceiveResource, scenarioId, $"tradeOffers[{index}].receiveResource");
		if (GiveAmount <= 0 || ReceiveAmount <= 0)
		{
			throw new InvalidOperationException($"Scenario '{scenarioId}' trade offer '{Id}' amounts must be positive.");
		}
	}

	private static void Require(string value, string scenarioId, string field)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidOperationException($"Scenario '{scenarioId}' requires '{field}'.");
		}
	}
}

public sealed class AiConfig
{
	public string FactionId { get; set; } = string.Empty;
	public string[] BuildOrder { get; set; } = Array.Empty<string>();
	public string[] ResearchOrder { get; set; } = Array.Empty<string>();
	public int ThinkEveryTicks { get; set; } = 90;
}
