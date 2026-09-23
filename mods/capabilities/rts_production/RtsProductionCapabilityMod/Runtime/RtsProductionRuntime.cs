using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Exchange;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.Gameplay.Progression.Components;
using Ludots.Core.Gameplay.Progression.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace RtsProductionCapabilityMod.Runtime;

public sealed class RtsProductionRuntime
{
	private readonly List<string> _logs = new(16);
	private readonly List<RtsProductionQueueItem> _queue = new(32);
	private readonly Dictionary<string, FactionRuntime> _factions = new(StringComparer.Ordinal);
	private readonly Dictionary<string, ProductionLineConfig> _productionById = new(StringComparer.Ordinal);
	private readonly Dictionary<string, TechConfig> _techById = new(StringComparer.Ordinal);
	private readonly Dictionary<string, TradeOfferState> _tradeStates = new(StringComparer.Ordinal);
	private readonly Entity[] _publishBuffer = new Entity[256];
	private RtsProductionConfig? _config;
	private GameEngine? _engine;
	private string _currentFactionId = string.Empty;
	private string _saveStatus = "Save UI ready.";
	private string _aiStatus = "AI idle.";
	private bool _scenarioReady;
	private int _diplomacyTypeId;
	private int _trustMetricId;
	private int _tradePactFlagId;
	private int _atWarFlagId;
	private int _embargoFlagId;
	private int _tradeOperationId;

	public Task HandleMapFocusedAsync(ScriptContext context)
	{
		GameEngine? engine = context.GetEngine();
		if (engine == null)
		{
			return Task.CompletedTask;
		}

		if (!RtsProductionIds.IsProductionMap(engine.CurrentMapSession?.MapConfig))
		{
			Reset();
			return Task.CompletedTask;
		}

		_engine = engine;
		EnsureConfig(engine);
		EnsureScenario(engine);
		ApplySelectionView(engine);
		return Task.CompletedTask;
	}

	public Task HandleMapUnloadedAsync(ScriptContext context)
	{
		if (context.GetEngine() is GameEngine engine &&
			RtsProductionIds.IsProductionMap(engine.CurrentMapSession?.MapConfig))
		{
			Reset();
		}

		return Task.CompletedTask;
	}

	public void Update(GameEngine engine, float deltaSeconds)
	{
		if (!_scenarioReady || _config == null || !RtsProductionIds.IsProductionMap(engine.CurrentMapSession?.MapConfig))
		{
			return;
		}

		AdvanceQueue(engine);
		RunAi(engine);
		PublishCollections(engine);
	}

	public RtsProductionSnapshot BuildSnapshot(GameEngine engine)
	{
		if (!_scenarioReady || _config == null)
		{
			return RtsProductionSnapshot.Empty;
		}

		var factionSnapshots = new List<RtsFactionSnapshot>(_config.Factions.Length);
		for (int i = 0; i < _config.Factions.Length; i++)
		{
			FactionConfig cfg = _config.Factions[i];
			FactionRuntime faction = _factions[cfg.Id];
			factionSnapshots.Add(new RtsFactionSnapshot(
				cfg.Id,
				cfg.Label,
				cfg.PlayerId,
				cfg.TeamId,
				cfg.Accent,
				cfg.AiControlled,
				SnapshotResources(faction),
				faction.Units.ToArray(),
				faction.Buildings.ToArray(),
				SnapshotQueue(cfg.Id)));
		}

		return new RtsProductionSnapshot
		{
			ScenarioId = _config.ScenarioId,
			Title = _config.Title,
			Flavor = _config.Flavor,
			HudStyle = _config.HudStyle,
			Summary = _config.Summary,
			CurrentFactionId = _currentFactionId,
			CurrentTick = engine.GameSession?.CurrentTick ?? 0,
			ScenarioReady = _scenarioReady,
			Acceptance = _config.Acceptance,
			Factions = factionSnapshots,
			AvailableProduction = BuildAvailableProduction(),
			Techs = BuildTechSnapshots(engine),
			Treaties = BuildTreatySnapshots(engine),
			TradeOffers = BuildTradeOfferSnapshots(),
			LogLines = _logs.ToArray(),
			SaveSlotCount = CountSaveSlots(),
			SaveStatus = _saveStatus,
			AiStatus = _aiStatus,
		};
	}

	public void SelectFaction(GameEngine engine, string factionId)
	{
		EnsureScenario(engine);
		if (!_factions.ContainsKey(factionId))
		{
			throw new InvalidOperationException($"Unknown faction view '{factionId}'.");
		}

		_currentFactionId = factionId;
		ApplySelectionView(engine);
		Log($"View switched to {_config!.RequireFactionConfig(factionId).Label}.");
	}

	public bool StartProduction(GameEngine engine, string productionId)
	{
		EnsureScenario(engine);
		if (!_productionById.TryGetValue(productionId, out ProductionLineConfig? line) || line == null)
		{
			throw new InvalidOperationException($"Unknown production line '{productionId}'.");
		}

		FactionRuntime faction = _factions[line.FactionId];
		if (!IsTechUnlockedOrEmpty(faction, line.RequiredTech))
		{
			Log($"{line.Label} is locked by {line.RequiredTech}.");
			return false;
		}

		if (!TryPayCost(faction, line.Cost))
		{
			Log($"{line.Label} cannot start; resource cost is not met.");
			return false;
		}

		int tick = engine.GameSession?.CurrentTick ?? 0;
		_queue.Add(new RtsProductionQueueItem(
			line.Id,
			QueueItemKind.Production,
			line.FactionId,
			line.Label,
			line.Output,
			line.Paradigm,
			tick,
			line.DurationTicks,
			0,
			line.Summary));
		Log($"{FactionLabel(line.FactionId)} started {line.Label} ({line.Paradigm}).");
		return true;
	}

	public bool StartResearch(GameEngine engine, string techId)
	{
		EnsureScenario(engine);
		if (!_techById.TryGetValue(techId, out TechConfig? tech) || tech == null)
		{
			throw new InvalidOperationException($"Unknown tech '{techId}'.");
		}

		FactionRuntime faction = _factions[tech.FactionId];
		if (faction.CompletedTechs.Contains(tech.Id))
		{
			Log($"{tech.Label} is already complete.");
			return false;
		}

		if (!PrerequisitesMet(faction, tech))
		{
			Log($"{tech.Label} is locked by prerequisites.");
			return false;
		}

		if (QueueContains(QueueItemKind.Research, tech.Id))
		{
			Log($"{tech.Label} is already in progress.");
			return false;
		}

		if (!TryPayCost(faction, tech.Cost))
		{
			Log($"{tech.Label} cannot start; resource cost is not met.");
			return false;
		}

		int tick = engine.GameSession?.CurrentTick ?? 0;
		_queue.Add(new RtsProductionQueueItem(
			tech.Id,
			QueueItemKind.Research,
			tech.FactionId,
			tech.Label,
			tech.Id,
			ProductionParadigm.Train,
			tick,
			tech.DurationTicks,
			0,
			tech.Summary));
		Log($"{FactionLabel(tech.FactionId)} started research {tech.Label}.");
		return true;
	}

	public void SignTreaty(GameEngine engine, string treatyId)
	{
		EnsureScenario(engine);
		TreatyConfig treaty = RequireTreaty(treatyId);
		RelationshipRuntime relationships = RequireRelationships(engine);
		FactionRuntime source = _factions[treaty.SourceFactionId];
		FactionRuntime target = _factions[treaty.TargetFactionId];
		relationships.EnsureLink(source.PlayerEntity, target.PlayerEntity, _diplomacyTypeId);
		relationships.SetMetric(source.PlayerEntity, target.PlayerEntity, _diplomacyTypeId, _trustMetricId, (short)treaty.TrustDelta);
		relationships.SetFlag(source.PlayerEntity, target.PlayerEntity, _diplomacyTypeId, _tradePactFlagId, treaty.TradePact);
		relationships.SetFlag(source.PlayerEntity, target.PlayerEntity, _diplomacyTypeId, _atWarFlagId, !treaty.Peace);
		relationships.SetFlag(source.PlayerEntity, target.PlayerEntity, _diplomacyTypeId, _embargoFlagId, enabled: false);
		Log($"{treaty.Label} signed: {FactionLabel(treaty.SourceFactionId)} -> {FactionLabel(treaty.TargetFactionId)}.");
	}

	public void TearTreaty(GameEngine engine, string treatyId)
	{
		EnsureScenario(engine);
		TreatyConfig treaty = RequireTreaty(treatyId);
		RelationshipRuntime relationships = RequireRelationships(engine);
		FactionRuntime source = _factions[treaty.SourceFactionId];
		FactionRuntime target = _factions[treaty.TargetFactionId];
		relationships.EnsureLink(source.PlayerEntity, target.PlayerEntity, _diplomacyTypeId);
		relationships.SetFlag(source.PlayerEntity, target.PlayerEntity, _diplomacyTypeId, _tradePactFlagId, enabled: false);
		relationships.SetFlag(source.PlayerEntity, target.PlayerEntity, _diplomacyTypeId, _embargoFlagId, enabled: true);
		relationships.SetMetric(source.PlayerEntity, target.PlayerEntity, _diplomacyTypeId, _trustMetricId, (short)-25);
		Log($"{treaty.Label} torn down; embargo blocks trade.");
	}

	public void ProposeTrade(string offerId)
	{
		EnsureReady();
		TradeOfferConfig offer = RequireTradeOffer(offerId);
		_tradeStates[offer.Id] = TradeOfferState.Pending;
		Log($"{offer.Label} proposed: {offer.GiveAmount} {offer.GiveResource} for {offer.ReceiveAmount} {offer.ReceiveResource}.");
	}

	public ExchangeExecutionStatus AcceptTrade(GameEngine engine, string offerId)
	{
		EnsureScenario(engine);
		TradeOfferConfig offer = RequireTradeOffer(offerId);
		FactionRuntime source = _factions[offer.SourceFactionId];
		FactionRuntime target = _factions[offer.TargetFactionId];
		int giveAttribute = AttributeRegistry.Register(offer.GiveResource);
		int receiveAttribute = AttributeRegistry.Register(offer.ReceiveResource);
		var operation = new ExchangeOperationDefinition
		{
			Id = $"{RtsProductionIds.TradeOperationId}.{offer.Id}",
			RelationshipRequirements =
			[
				new ExchangeRelationshipRequirement(
					RoleSlot.Source,
					RoleSlot.Target,
					_diplomacyTypeId,
					_trustMetricId,
					minimumMetric: 0,
					maximumMetric: short.MaxValue,
					flagId: _tradePactFlagId,
					requiredFlagValue: true),
			],
			Inputs =
			[
				ExchangeInputDefinition.AttributeCost(RoleSlot.Source, giveAttribute, offer.GiveAmount),
				ExchangeInputDefinition.AttributeCost(RoleSlot.Target, receiveAttribute, offer.ReceiveAmount),
			],
			Outputs = Array.Empty<ExchangeOutputDefinition>(),
		};
		ExchangeScopedOperationStore scoped = engine.GetService(CoreServiceKeys.ExchangeScopedOperationStore)
			?? throw new InvalidOperationException("ExchangeScopedOperationStore missing.");
		scoped.Set(_tradeOperationId, ScopeKey.Named(source.ScopeKeyId), operation);
		ExchangeRuntime exchange = engine.GetService(CoreServiceKeys.ExchangeRuntime)
			?? throw new InvalidOperationException("ExchangeRuntime missing.");
		var result = exchange.TryExecute(
			new ExchangeOperationKey(_tradeOperationId, ScopeKey.Named(source.ScopeKeyId)),
			new ExchangeExecutionContext(source.PlayerEntity, target.PlayerEntity, source.HqEntity, ScopeKey.Named(source.ScopeKeyId)));

		if (!result.Succeeded)
		{
			_tradeStates[offer.Id] = result.Status == ExchangeExecutionStatus.RelationshipDenied
				? TradeOfferState.RelationshipDenied
				: TradeOfferState.InsufficientInput;
			Log($"{offer.Label} failed: {result.Status}.");
			return result.Status;
		}

		AddResource(target, giveAttribute, offer.GiveAmount);
		AddResource(source, receiveAttribute, offer.ReceiveAmount);

		_tradeStates[offer.Id] = TradeOfferState.Accepted;
		Log($"{offer.Label} accepted through ExchangeRuntime gate.");
		return result.Status;
	}

	public void RejectTrade(string offerId)
	{
		EnsureReady();
		TradeOfferConfig offer = RequireTradeOffer(offerId);
		_tradeStates[offer.Id] = TradeOfferState.Rejected;
		Log($"{offer.Label} rejected.");
	}

	public void SaveSmoke(GameEngine engine)
	{
		var storage = new FileSystemSaveStorage(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ludots", "ShowcaseSaves"));
		var slots = new Ludots.Core.Persistence.SaveSlotStore(storage);
		_saveStatus = $"Save storage ready; {slots.ListSlots().Count} slot(s) visible.";
		Log(_saveStatus);
	}

	private void EnsureConfig(GameEngine engine)
	{
		if (_config != null)
		{
			return;
		}

		string modId = ResolveScenarioModId(engine);
		using Stream stream = engine.VFS.GetStream($"{modId}:assets/RtsProduction/scenario.json");
		_config = RtsProductionConfig.Load(stream);
		_config.Validate();
	}

	private string ResolveScenarioModId(GameEngine engine)
	{
		string? mapId = engine.CurrentMapSession?.MapId.Value;
		if (string.IsNullOrWhiteSpace(mapId))
		{
			throw new InvalidOperationException("RtsProductionCapabilityMod requires an active map session.");
		}

		IReadOnlyList<string> loaded = engine.ModLoader.LoadedModIds;
		for (int i = loaded.Count - 1; i >= 0; i--)
		{
			string modId = loaded[i];
			try
			{
				using Stream stream = engine.VFS.GetStream($"{modId}:assets/RtsProduction/scenario.json");
				var config = RtsProductionConfig.Load(stream);
				if (string.Equals(config.MapId, mapId, StringComparison.Ordinal))
				{
					return modId;
				}
			}
			catch (FileNotFoundException)
			{
				// Absence of a scenario resource only means this loaded mod is not a production root.
			}
			catch (DirectoryNotFoundException)
			{
				// Absence of a scenario resource only means this loaded mod is not a production root.
			}
		}

		throw new InvalidOperationException($"No loaded mod provides assets/RtsProduction/scenario.json for map '{mapId}'.");
	}

	private void EnsureScenario(GameEngine engine)
	{
		if (_scenarioReady)
		{
			return;
		}

		if (_config == null)
		{
			throw new InvalidOperationException("RtsProduction config was not loaded.");
		}

		ResolveIds(engine);
		BuildScenario(engine);
		PublishCollections(engine);
		_currentFactionId = _config.Factions[0].Id;
		_scenarioReady = true;
		Log($"{_config.Title} ready with {_config.Factions.Length} faction views.");
	}

	private void ResolveIds(GameEngine engine)
	{
		RelationshipTypeRegistry relationshipTypes = engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
			?? throw new InvalidOperationException("RelationshipTypeRegistry missing.");
		RelationshipMetricRegistry relationshipMetrics = engine.GetService(CoreServiceKeys.RelationshipMetricRegistry)
			?? throw new InvalidOperationException("RelationshipMetricRegistry missing.");
		RelationshipFlagRegistry relationshipFlags = engine.GetService(CoreServiceKeys.RelationshipFlagRegistry)
			?? throw new InvalidOperationException("RelationshipFlagRegistry missing.");
		ExchangeOperationRegistry operations = engine.GetService(CoreServiceKeys.ExchangeOperationRegistry)
			?? throw new InvalidOperationException("ExchangeOperationRegistry missing.");
		ScopeKeyRegistry scopeKeys = engine.GetService(CoreServiceKeys.ScopeKeyRegistry)
			?? throw new InvalidOperationException("ScopeKeyRegistry missing.");

		_diplomacyTypeId = relationshipTypes.Register(RtsProductionIds.DiplomacyType, isSymmetric: false);
		_trustMetricId = relationshipMetrics.Register(RtsProductionIds.TrustMetric, -100, 100, 0);
		_tradePactFlagId = relationshipFlags.Register(RtsProductionIds.TradePactFlag);
		_atWarFlagId = relationshipFlags.Register(RtsProductionIds.AtWarFlag);
		_embargoFlagId = relationshipFlags.Register(RtsProductionIds.EmbargoFlag);
		_tradeOperationId = operations.Register(RtsProductionIds.TradeOperationId, new ExchangeOperationDefinition { Id = RtsProductionIds.TradeOperationId });
		_ = scopeKeys.Register(RtsProductionIds.FactionScopeKey);
		for (int i = 0; i < _config!.Resources.Length; i++)
		{
			AttributeRegistry.Register(_config.Resources[i]);
		}

		for (int i = 0; i < _config.Techs.Length; i++)
		{
			ProgressionIdRegistry.GetId(_config.Techs[i].Id);
		}
	}

	private void BuildScenario(GameEngine engine)
	{
		World world = engine.World;
		var mapId = new MapId(_config!.MapId);
		var teamLookup = engine.GetService(CoreServiceKeys.TeamEntityLookup) ?? new TeamEntityLookup();
		var playerLookup = engine.GetService(CoreServiceKeys.PlayerEntityLookup) ?? new PlayerEntityLookup();
		OwnershipResolver ownership = engine.GetService(CoreServiceKeys.OwnershipResolver)
			?? throw new InvalidOperationException("OwnershipResolver missing.");
		ScopeKeyRegistry scopeKeys = engine.GetService(CoreServiceKeys.ScopeKeyRegistry)
			?? throw new InvalidOperationException("ScopeKeyRegistry missing.");
		int factionScopeId = scopeKeys.GetId(RtsProductionIds.FactionScopeKey);

		_factions.Clear();
		_productionById.Clear();
		_techById.Clear();
		_tradeStates.Clear();

		for (int i = 0; i < _config.Factions.Length; i++)
		{
			FactionConfig cfg = _config.Factions[i];
			Entity team = ResolveOrCreateTeamEntity(world, teamLookup, mapId, cfg);
			Entity player = ResolveOrCreatePlayerEntity(world, playerLookup, mapId, cfg);
			teamLookup.Register(cfg.TeamId, team);
			playerLookup.Register(cfg.PlayerId, player);
			EnsureFactionComponents(world, player, cfg, mapId);
			ref AttributeBuffer attrs = ref world.Get<AttributeBuffer>(player);
			for (int r = 0; r < cfg.StartingResources.Length; r++)
			{
				int attrId = AttributeRegistry.Register(cfg.StartingResources[r].Resource);
				attrs.SetBase(attrId, cfg.StartingResources[r].Amount);
				attrs.SetCurrent(attrId, cfg.StartingResources[r].Amount);
			}

			var runtime = new FactionRuntime(cfg, player, team, player, factionScopeId);
			_factions.Add(cfg.Id, runtime);
			for (int b = 0; b < cfg.StartingBuildings.Length; b++)
			{
				Entity building = CreateOwnedEntity(world, ownership, player, mapId, cfg, cfg.StartingBuildings[b], "Building", produced: false);
				runtime.Buildings.Add(new RtsProductionEntityRecord(building, cfg.StartingBuildings[b], "Building", cfg.Id, Produced: false));
			}

			for (int u = 0; u < cfg.StartingUnits.Length; u++)
			{
				Entity unit = CreateOwnedEntity(world, ownership, player, mapId, cfg, cfg.StartingUnits[u], "Unit", produced: false);
				runtime.Units.Add(new RtsProductionEntityRecord(unit, cfg.StartingUnits[u], "Unit", cfg.Id, Produced: false));
			}
		}

		engine.SetService(CoreServiceKeys.TeamEntityLookup, teamLookup);
		engine.SetService(CoreServiceKeys.PlayerEntityLookup, playerLookup);

		for (int i = 0; i < _config.ProductionLines.Length; i++)
		{
			ProductionLineConfig line = _config.ProductionLines[i];
			if (!string.IsNullOrWhiteSpace(line.RequiredTech) && !_config.HasTech(line.RequiredTech))
			{
				throw new InvalidOperationException($"Production line '{line.Id}' references unknown required tech '{line.RequiredTech}'.");
			}

			_productionById.Add(line.Id, line);
		}

		for (int i = 0; i < _config.Techs.Length; i++)
		{
			TechConfig tech = _config.Techs[i];
			for (int r = 0; r < tech.Requires.Length; r++)
			{
				if (!_config.HasTech(tech.Requires[r]))
				{
					throw new InvalidOperationException($"Tech '{tech.Id}' references unknown prerequisite '{tech.Requires[r]}'.");
				}
			}

			_techById.Add(tech.Id, tech);
		}

		for (int i = 0; i < _config.TradeOffers.Length; i++)
		{
			_tradeStates[_config.TradeOffers[i].Id] = TradeOfferState.Draft;
		}
	}

	private static Entity ResolveOrCreateTeamEntity(World world, TeamEntityLookup teamLookup, MapId mapId, FactionConfig cfg)
	{
		if (teamLookup.TryGet(cfg.TeamId, out Entity team) && world.IsAlive(team))
		{
			return team;
		}

		return world.Create(
			new Name { Value = $"{cfg.Label} Team" },
			new TeamIdentity { TeamId = cfg.TeamId },
			new MapEntity { MapId = mapId });
	}

	private static Entity ResolveOrCreatePlayerEntity(World world, PlayerEntityLookup playerLookup, MapId mapId, FactionConfig cfg)
	{
		if (playerLookup.TryGet(cfg.PlayerId, out Entity player) && world.IsAlive(player))
		{
			return player;
		}

		return world.Create(
			new Name { Value = cfg.Label },
			new PlayerIdentity { PlayerId = cfg.PlayerId },
			new Team { Id = cfg.TeamId },
			new PlayerOwner { PlayerId = cfg.PlayerId },
			new MapEntity { MapId = mapId },
			new AttributeBuffer(),
			new ProgressionStateBuffer(),
			new ScopeMembershipRevision());
	}

	private static void EnsureFactionComponents(World world, Entity player, FactionConfig cfg, MapId mapId)
	{
		Upsert(world, player, new PlayerIdentity { PlayerId = cfg.PlayerId });
		Upsert(world, player, new Team { Id = cfg.TeamId });
		Upsert(world, player, new PlayerOwner { PlayerId = cfg.PlayerId });
		Upsert(world, player, new MapEntity { MapId = mapId });
		if (!world.Has<AttributeBuffer>(player))
		{
			world.Add(player, new AttributeBuffer());
		}

		if (!world.Has<ProgressionStateBuffer>(player))
		{
			world.Add(player, new ProgressionStateBuffer());
		}

		if (!world.Has<ScopeMembershipRevision>(player))
		{
			world.Add(player, new ScopeMembershipRevision());
		}
	}

	private static void Upsert<T>(World world, Entity entity, T component) where T : struct
	{
		if (world.Has<T>(entity))
		{
			world.Set(entity, component);
		}
		else
		{
			world.Add(entity, component);
		}
	}

	private static Entity CreateOwnedEntity(
		World world,
		OwnershipResolver ownership,
		Entity player,
		MapId mapId,
		FactionConfig faction,
		string label,
		string kind,
		bool produced)
	{
		Entity entity = world.Create(
			new Name { Value = label },
			new PlayerOwner { PlayerId = faction.PlayerId },
			new Team { Id = faction.TeamId },
			new MapEntity { MapId = mapId });
		ownership.EnsureOwnership(player, entity);
		return entity;
	}

	private void AdvanceQueue(GameEngine engine)
	{
		if (_queue.Count == 0)
		{
			return;
		}

		for (int i = _queue.Count - 1; i >= 0; i--)
		{
			RtsProductionQueueItem item = _queue[i];
			item = item with { ProgressTicks = Math.Min(item.DurationTicks, item.ProgressTicks + 1) };
			if (!item.Complete)
			{
				_queue[i] = item;
				continue;
			}

			CompleteQueueItem(engine, item);
			_queue.RemoveAt(i);
		}
	}

	private void CompleteQueueItem(GameEngine engine, RtsProductionQueueItem item)
	{
		if (item.Kind == QueueItemKind.Research)
		{
			FactionRuntime faction = _factions[item.FactionId];
			faction.CompletedTechs.Add(item.Id);
			ProgressionRequirementEvaluator evaluator = engine.GetService(CoreServiceKeys.ProgressionRequirementEvaluator)
				?? throw new InvalidOperationException("ProgressionRequirementEvaluator missing.");
			int progressionId = ProgressionIdRegistry.GetId(item.Id);
			if (!evaluator.TryComplete(faction.PlayerEntity, progressionId))
			{
				throw new InvalidOperationException($"Failed to complete progression '{item.Id}' for faction '{item.FactionId}'.");
			}

			Log($"{FactionLabel(item.FactionId)} completed {item.Label}.");
			return;
		}

		FactionRuntime owner = _factions[item.FactionId];
		World world = engine.World;
		OwnershipResolver ownership = engine.GetService(CoreServiceKeys.OwnershipResolver)
			?? throw new InvalidOperationException("OwnershipResolver missing.");
		string outputKind = ResolveOutputKind(item);
		Entity entity = CreateOwnedEntity(
			world,
			ownership,
			owner.PlayerEntity,
			new MapId(_config!.MapId),
			owner.Config,
			item.Output,
			outputKind,
			produced: true);
		var record = new RtsProductionEntityRecord(entity, item.Output, outputKind, item.FactionId, Produced: true);
		if (string.Equals(record.Kind, "Building", StringComparison.Ordinal))
		{
			owner.Buildings.Add(record);
		}
		else
		{
			owner.Units.Add(record);
		}

		Log($"{FactionLabel(item.FactionId)} completed {item.Output} via {item.Paradigm}.");
	}

	private string ResolveOutputKind(RtsProductionQueueItem item)
	{
		if (!_productionById.TryGetValue(item.Id, out ProductionLineConfig? line) || line == null)
		{
			throw new InvalidOperationException($"Production queue item '{item.Id}' has no production line definition.");
		}

		return string.Equals(line.OutputKind, "Building", StringComparison.OrdinalIgnoreCase)
			? "Building"
			: "Unit";
	}

	private void RunAi(GameEngine engine)
	{
		if (_config == null || _config.Ai.Length == 0 || IsAiDisabled(engine))
		{
			return;
		}

		int tick = engine.GameSession?.CurrentTick ?? 0;
		for (int i = 0; i < _config.Ai.Length; i++)
		{
			AiConfig ai = _config.Ai[i];
			if (ai.ThinkEveryTicks <= 0 || tick % ai.ThinkEveryTicks != 0)
			{
				continue;
			}

			for (int p = 0; p < ai.BuildOrder.Length; p++)
			{
				if (TryStartAiProduction(engine, ai.FactionId, ai.BuildOrder[p]))
				{
					_aiStatus = $"{FactionLabel(ai.FactionId)} AI queued {ai.BuildOrder[p]}.";
					return;
				}
			}

			for (int r = 0; r < ai.ResearchOrder.Length; r++)
			{
				if (TryStartAiResearch(engine, ai.FactionId, ai.ResearchOrder[r]))
				{
					_aiStatus = $"{FactionLabel(ai.FactionId)} AI researched {ai.ResearchOrder[r]}.";
					return;
				}
			}
		}
	}

	private bool TryStartAiProduction(GameEngine engine, string factionId, string productionId)
	{
		return _productionById.TryGetValue(productionId, out ProductionLineConfig? line) &&
			   line != null &&
			   string.Equals(line.FactionId, factionId, StringComparison.Ordinal) &&
			   !QueueContains(QueueItemKind.Production, productionId) &&
			   StartProduction(engine, productionId);
	}

	private static bool IsAiDisabled(GameEngine engine)
	{
		return engine.GlobalContext.TryGetValue(RtsProductionIds.AiDisabledKey, out object? disabledObj) &&
			   disabledObj is bool disabled &&
			   disabled;
	}

	private bool TryStartAiResearch(GameEngine engine, string factionId, string techId)
	{
		return _techById.TryGetValue(techId, out TechConfig? tech) &&
			   tech != null &&
			   string.Equals(tech.FactionId, factionId, StringComparison.Ordinal) &&
			   !_factions[factionId].CompletedTechs.Contains(techId) &&
			   !QueueContains(QueueItemKind.Research, techId) &&
			   StartResearch(engine, techId);
	}

	private void PublishCollections(GameEngine engine)
	{
		EntityCollectionStore collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
			?? throw new InvalidOperationException("EntityCollectionStore missing.");
		foreach (FactionRuntime faction in _factions.Values)
		{
			PublishCollection(collections, faction, $"{faction.Config.Id}.units", "Units", faction.Units);
			PublishCollection(collections, faction, $"{faction.Config.Id}.buildings", "Buildings", faction.Buildings);
			PublishQueueCollection(collections, faction);
		}
	}

	private void PublishCollection(
		EntityCollectionStore collections,
		FactionRuntime faction,
		string key,
		string title,
		List<RtsProductionEntityRecord> records)
	{
		int count = Math.Min(records.Count, _publishBuffer.Length);
		for (int i = 0; i < count; i++)
		{
			_publishBuffer[i] = records[i].Entity;
		}

		collections.Replace(
			faction.PlayerEntity,
			EntityCollectionDescriptor.Create(
				$"faction.{key}",
				EntityCollectionSourceKind.Explicit,
				EntityCollectionRoleKind.Display,
				contextEntity: faction.PlayerEntity,
				primaryEntity: faction.PlayerEntity,
				title: $"{faction.Config.Label} {title}",
				summary: "RtsProductionCapabilityMod ownership-backed collection"),
			_publishBuffer.AsSpan(0, count));
	}

	private void PublishQueueCollection(EntityCollectionStore collections, FactionRuntime faction)
	{
		collections.Replace(
			faction.PlayerEntity,
			EntityCollectionDescriptor.Create(
				$"faction.{faction.Config.Id}.production_queue",
				EntityCollectionSourceKind.Explicit,
				EntityCollectionRoleKind.Display,
				contextEntity: faction.PlayerEntity,
				primaryEntity: faction.PlayerEntity,
				title: $"{faction.Config.Label} Production Queue",
				summary: "Queue state is authored by RtsProductionCapabilityMod and mirrored in UI."),
			ReadOnlySpan<Entity>.Empty);
	}

	private void ApplySelectionView(GameEngine engine)
	{
		if (!_factions.TryGetValue(_currentFactionId, out FactionRuntime? faction) || faction == null)
		{
			return;
		}

		engine.SetService(CoreServiceKeys.LocalPlayerEntity, faction.PlayerEntity);
		engine.SetService(CoreServiceKeys.LocalPlayerId, faction.Config.PlayerId);
		if (!TrySelectParticipantPlayer(engine, faction.Config.PlayerId))
		{
			throw new InvalidOperationException("RtsProductionCapabilityMod requires ParticipantViewCapabilityMod.CommandService.");
		}
	}

	private static bool TrySelectParticipantPlayer(GameEngine engine, int playerId)
	{
		if (!engine.GlobalContext.TryGetValue("ParticipantViewCapabilityMod.CommandService", out object? service) ||
			service == null)
		{
			return false;
		}

		Type serviceType = service.GetType();
		System.Reflection.MethodInfo? selectPlayer = serviceType.GetMethod("SelectPlayer", new[] { typeof(GameEngine), typeof(int) });
		if (selectPlayer == null)
		{
			return false;
		}

		selectPlayer.Invoke(service, new object[] { engine, playerId });
		return true;
	}

	private IReadOnlyList<ResourceAmountConfig> SnapshotResources(FactionRuntime faction)
	{
		var resources = new List<ResourceAmountConfig>(_config!.Resources.Length);
		for (int i = 0; i < _config.Resources.Length; i++)
		{
			int attrId = AttributeRegistry.Register(_config.Resources[i]);
			int amount = 0;
			if (_engine != null && _engine.World.IsAlive(faction.PlayerEntity) && _engine.World.Has<AttributeBuffer>(faction.PlayerEntity))
			{
				AttributeBuffer attrs = _engine.World.Get<AttributeBuffer>(faction.PlayerEntity);
				amount = (int)attrs.GetCurrent(attrId);
			}

			resources.Add(new ResourceAmountConfig { Resource = _config.Resources[i], Amount = amount });
		}

		return resources;
	}

	private IReadOnlyList<RtsProductionQueueItem> SnapshotQueue(string factionId)
	{
		var items = new List<RtsProductionQueueItem>();
		for (int i = 0; i < _queue.Count; i++)
		{
			if (string.Equals(_queue[i].FactionId, factionId, StringComparison.Ordinal))
			{
				items.Add(_queue[i]);
			}
		}

		return items;
	}

	private IReadOnlyList<ProductionLineConfig> BuildAvailableProduction()
	{
		if (string.IsNullOrEmpty(_currentFactionId))
		{
			return Array.Empty<ProductionLineConfig>();
		}

		var list = new List<ProductionLineConfig>();
		FactionRuntime faction = _factions[_currentFactionId];
		for (int i = 0; i < _config!.ProductionLines.Length; i++)
		{
			ProductionLineConfig line = _config.ProductionLines[i];
			if (string.Equals(line.FactionId, _currentFactionId, StringComparison.Ordinal) &&
				IsTechUnlockedOrEmpty(faction, line.RequiredTech))
			{
				list.Add(line);
			}
		}

		return list;
	}

	private IReadOnlyList<RtsTechNodeSnapshot> BuildTechSnapshots(GameEngine engine)
	{
		var list = new List<RtsTechNodeSnapshot>(_config!.Techs.Length);
		for (int i = 0; i < _config.Techs.Length; i++)
		{
			TechConfig tech = _config.Techs[i];
			FactionRuntime faction = _factions[tech.FactionId];
			TechNodeState state = faction.CompletedTechs.Contains(tech.Id)
				? TechNodeState.Completed
				: QueueContains(QueueItemKind.Research, tech.Id)
					? TechNodeState.InProgress
					: PrerequisitesMet(faction, tech)
						? TechNodeState.Available
						: TechNodeState.Locked;
			list.Add(new RtsTechNodeSnapshot(tech.Id, tech.Label, tech.FactionId, state, tech.Requires, tech.Summary));
		}

		return list;
	}

	private IReadOnlyList<RtsTreatySnapshot> BuildTreatySnapshots(GameEngine engine)
	{
		var list = new List<RtsTreatySnapshot>(_config!.Treaties.Length);
		RelationshipRuntime relationships = RequireRelationships(engine);
		for (int i = 0; i < _config.Treaties.Length; i++)
		{
			TreatyConfig treaty = _config.Treaties[i];
			FactionRuntime source = _factions[treaty.SourceFactionId];
			FactionRuntime target = _factions[treaty.TargetFactionId];
			relationships.TryGetMetric(source.PlayerEntity, target.PlayerEntity, _diplomacyTypeId, _trustMetricId, out short trust);
			relationships.TryHasFlag(source.PlayerEntity, target.PlayerEntity, _diplomacyTypeId, _tradePactFlagId, out bool tradePact);
			relationships.TryHasFlag(source.PlayerEntity, target.PlayerEntity, _diplomacyTypeId, _atWarFlagId, out bool atWar);
			relationships.TryHasFlag(source.PlayerEntity, target.PlayerEntity, _diplomacyTypeId, _embargoFlagId, out bool embargo);
			list.Add(new RtsTreatySnapshot(treaty.Id, treaty.Label, treaty.SourceFactionId, treaty.TargetFactionId, trust, tradePact, atWar, embargo));
		}

		return list;
	}

	private IReadOnlyList<RtsTradeOfferSnapshot> BuildTradeOfferSnapshots()
	{
		var list = new List<RtsTradeOfferSnapshot>(_config!.TradeOffers.Length);
		for (int i = 0; i < _config.TradeOffers.Length; i++)
		{
			TradeOfferConfig offer = _config.TradeOffers[i];
			_tradeStates.TryGetValue(offer.Id, out TradeOfferState state);
			list.Add(new RtsTradeOfferSnapshot(
				offer.Id,
				offer.Label,
				offer.SourceFactionId,
				offer.TargetFactionId,
				offer.GiveResource,
				offer.GiveAmount,
				offer.ReceiveResource,
				offer.ReceiveAmount,
				state));
		}

		return list;
	}

	private bool TryPayCost(FactionRuntime faction, IReadOnlyList<ResourceAmountConfig> cost)
	{
		for (int i = 0; i < cost.Count; i++)
		{
			int attrId = AttributeRegistry.Register(cost[i].Resource);
			if (!HasResource(faction, attrId, cost[i].Amount))
			{
				return false;
			}
		}

		for (int i = 0; i < cost.Count; i++)
		{
			int attrId = AttributeRegistry.Register(cost[i].Resource);
			if (!TrySubtractResource(faction, attrId, cost[i].Amount))
			{
				throw new InvalidOperationException("Resource cost changed during production payment.");
			}
		}

		return true;
	}

	private bool HasResource(FactionRuntime faction, int attrId, int amount)
	{
		if (_engine == null || !_engine.World.Has<AttributeBuffer>(faction.PlayerEntity))
		{
			return false;
		}

		AttributeBuffer attrs = _engine.World.Get<AttributeBuffer>(faction.PlayerEntity);
		return attrs.GetCurrent(attrId) >= amount;
	}

	private bool TrySubtractResource(FactionRuntime faction, int attrId, int amount)
	{
		if (_engine == null || !_engine.World.Has<AttributeBuffer>(faction.PlayerEntity))
		{
			return false;
		}

		ref AttributeBuffer attrs = ref _engine.World.Get<AttributeBuffer>(faction.PlayerEntity);
		float current = attrs.GetCurrent(attrId);
		if (current < amount)
		{
			return false;
		}

		attrs.SetCurrent(attrId, current - amount);
		return true;
	}

	private void AddResource(FactionRuntime faction, int attrId, int amount)
	{
		if (_engine == null || !_engine.World.Has<AttributeBuffer>(faction.PlayerEntity))
		{
			return;
		}

		ref AttributeBuffer attrs = ref _engine.World.Get<AttributeBuffer>(faction.PlayerEntity);
		float current = attrs.GetCurrent(attrId);
		float next = current + amount;
		if (next > attrs.GetBase(attrId))
		{
			attrs.SetBase(attrId, next);
		}

		attrs.SetCurrent(attrId, next);
	}

	private bool IsTechUnlockedOrEmpty(FactionRuntime faction, string? techId)
		=> string.IsNullOrWhiteSpace(techId) || faction.CompletedTechs.Contains(techId);

	private bool PrerequisitesMet(FactionRuntime faction, TechConfig tech)
	{
		for (int i = 0; i < tech.Requires.Length; i++)
		{
			if (!faction.CompletedTechs.Contains(tech.Requires[i]))
			{
				return false;
			}
		}

		return true;
	}

	private bool QueueContains(QueueItemKind kind, string id)
	{
		for (int i = 0; i < _queue.Count; i++)
		{
			if (_queue[i].Kind == kind && string.Equals(_queue[i].Id, id, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private TreatyConfig RequireTreaty(string treatyId)
	{
		for (int i = 0; i < _config!.Treaties.Length; i++)
		{
			if (string.Equals(_config.Treaties[i].Id, treatyId, StringComparison.Ordinal))
			{
				return _config.Treaties[i];
			}
		}

		throw new InvalidOperationException($"Unknown treaty '{treatyId}'.");
	}

	private TradeOfferConfig RequireTradeOffer(string offerId)
	{
		for (int i = 0; i < _config!.TradeOffers.Length; i++)
		{
			if (string.Equals(_config.TradeOffers[i].Id, offerId, StringComparison.Ordinal))
			{
				return _config.TradeOffers[i];
			}
		}

		throw new InvalidOperationException($"Unknown trade offer '{offerId}'.");
	}

	private RelationshipRuntime RequireRelationships(GameEngine engine)
	{
		return engine.GetService(CoreServiceKeys.RelationshipRuntime)
			?? throw new InvalidOperationException("RelationshipRuntime missing.");
	}

	private int CountSaveSlots()
	{
		try
		{
			var storage = new FileSystemSaveStorage(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ludots", "ShowcaseSaves"));
			var slots = new Ludots.Core.Persistence.SaveSlotStore(storage);
			return slots.ListSlots().Count;
		}
		catch
		{
			return 0;
		}
	}

	private string FactionLabel(string factionId) => _config?.RequireFactionConfig(factionId).Label ?? factionId;

	private void EnsureReady()
	{
		if (!_scenarioReady || _config == null)
		{
			throw new InvalidOperationException("RtsProduction scenario is not ready.");
		}
	}

	private void Reset()
	{
		_engine = null;
		_config = null;
		_currentFactionId = string.Empty;
		_scenarioReady = false;
		_factions.Clear();
		_productionById.Clear();
		_techById.Clear();
		_tradeStates.Clear();
		_queue.Clear();
		_logs.Clear();
		_saveStatus = "Save UI ready.";
		_aiStatus = "AI idle.";
	}

	private void Log(string message)
	{
		_logs.Insert(0, message);
		if (_logs.Count > 10)
		{
			_logs.RemoveAt(_logs.Count - 1);
		}
	}

	private sealed class FactionRuntime
	{
		public FactionRuntime(FactionConfig config, Entity playerEntity, Entity teamEntity, Entity hqEntity, int scopeKeyId)
		{
			Config = config;
			PlayerEntity = playerEntity;
			TeamEntity = teamEntity;
			HqEntity = hqEntity;
			ScopeKeyId = scopeKeyId;
		}

		public FactionConfig Config { get; }
		public Entity PlayerEntity { get; }
		public Entity TeamEntity { get; }
		public Entity HqEntity { get; }
		public int ScopeKeyId { get; }
		public List<RtsProductionEntityRecord> Units { get; } = new();
		public List<RtsProductionEntityRecord> Buildings { get; } = new();
		public HashSet<string> CompletedTechs { get; } = new(StringComparer.Ordinal);
	}
}

