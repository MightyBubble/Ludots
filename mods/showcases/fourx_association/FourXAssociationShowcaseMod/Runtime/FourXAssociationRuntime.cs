using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Arch.Core;
using FourXAssociationShowcaseMod.Input;
using FourXAssociationShowcaseMod.UI;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Exchange;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.Gameplay.Progression.Components;
using Ludots.Core.Gameplay.Progression.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Knowledge;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI;

namespace FourXAssociationShowcaseMod.Runtime;

public sealed class FourXAssociationRuntime
{
    private readonly IModContext _context;
    private readonly FourXAssociationPanelController _panelController;
    private readonly List<MemberRuntimeEntry> _members = new(8);
    private readonly Entity[] _memberBuffer = new Entity[16];
    private readonly List<string> _log = new(12);
    private FourXAssociationConfig? _config;
    private GameEngine? _engine;
    private Entity _playerA = Entity.Null;
    private Entity _playerB = Entity.Null;
    private Entity _cityA = Entity.Null;
    private Entity _cityB = Entity.Null;
    private Entity _cityAStash = Entity.Null;
    private Entity _hiddenUnit = Entity.Null;
    private Entity _teamHost = Entity.Null;
    private Entity _researcher = Entity.Null;
    private int _goldAttributeId;
    private int _diplomacyTypeId;
    private int _trustMetricId;
    private int _tradePactFlagId;
    private int _tradeOperationId;
    private int _supplyItemId;
    private int _cityStashLayoutId;
    private int _teamScopeId;
    private int _knowledgeTick;
    private int _expiredKnowledgeRecords;
    private int _activeResearchMembers;
    private int _researchProgress;
    private int _lastContribution;
    private bool _scenarioReady;
    private bool _hiddenBeforeScout = true;
    private bool _visibleAfterScout;
    private bool _hiddenAfterDecay = true;
    private bool _pactSigned;
    private bool _techUnlocked;
    private ExchangeExecutionStatus _tradeBeforePact = ExchangeExecutionStatus.MissingOperation;
    private ExchangeExecutionStatus _tradeAfterPact = ExchangeExecutionStatus.MissingOperation;
    private string _status = "Load the 4X association showcase map.";

    public FourXAssociationRuntime(IModContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _panelController = new FourXAssociationPanelController(this);
    }

    public FourXAssociationSnapshot Snapshot => BuildSnapshot();

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            return Task.CompletedTask;
        }

        if (!FourXAssociationIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            ClearPanel(engine);
            return Task.CompletedTask;
        }

        _engine = engine;
        EnsureConfig();
        ActivateInputContext(engine.GetService(CoreServiceKeys.InputHandler));
        EnsureScenario(engine);
        RefreshPanelInternal(engine);
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            return Task.CompletedTask;
        }

        if (FourXAssociationIds.IsShowcaseMap(context.Get(CoreServiceKeys.MapId).Value))
        {
            DeactivateInputContext(engine.GetService(CoreServiceKeys.InputHandler));
            ClearPanel(engine);
            ResetScenario();
        }

        return Task.CompletedTask;
    }

    public void Scout(GameEngine engine)
    {
        EnsureScenario(engine);
        FourXAssociationConfig config = _config!;
        KnowledgeProjectionStore knowledge = RequireKnowledgeStore(engine);
        _knowledgeTick++;
        knowledge.Upsert(
            _playerA,
            _hiddenUnit,
            new KnowledgeDisclosureRecord(
                KnowledgePresence.LiveVisible,
                KnowledgePositionAccess.Live,
                KnowledgeIdMask256.Empty.WithId(_goldAttributeId),
                KnowledgeIdMask256.Empty.WithId(_diplomacyTypeId),
                KnowledgeIdMask256.Empty,
                _playerA,
                _knowledgeTick,
                _knowledgeTick + config.KnowledgeTtlTicks,
                config.ConfidencePermille,
                revision: 0));
        _visibleAfterScout = CanReadHiddenUnit(engine);
        _hiddenAfterDecay = false;
        _status = _visibleAfterScout
            ? "Scout report reveals the Amber envoy through the knowledge projection store."
            : "Scout report failed to reveal the Amber envoy.";
        PushLog(_status);
        RefreshPanelInternal(engine);
    }

    public void AdvanceFog(GameEngine engine)
    {
        EnsureScenario(engine);
        FourXAssociationConfig config = _config!;
        KnowledgeProjectionStore knowledge = RequireKnowledgeStore(engine);
        _knowledgeTick += config.KnowledgeTtlTicks + 1;
        _expiredKnowledgeRecords += knowledge.Expire(_knowledgeTick);
        knowledge.Compact();
        _hiddenAfterDecay = !CanReadHiddenUnit(engine);
        _status = _hiddenAfterDecay
            ? "Fog decay expired the stale scout report."
            : "The Amber envoy is still visible after fog decay.";
        PushLog(_status);
        RefreshPanelInternal(engine);
    }

    public void SignPact(GameEngine engine)
    {
        EnsureScenario(engine);
        FourXAssociationConfig config = _config!;
        RelationshipRuntime relationships = RequireRelationships(engine);
        relationships.EnsureLink(_playerA, _playerB, _diplomacyTypeId);
        relationships.SetMetric(_playerA, _playerB, _diplomacyTypeId, _trustMetricId, config.PactTrust);
        relationships.SetFlag(_playerA, _playerB, _diplomacyTypeId, _tradePactFlagId, enabled: true);
        _pactSigned = true;
        _status = "Diplomatic trade pact signed; Exchange relationship gate can now pass.";
        PushLog(_status);
        RefreshPanelInternal(engine);
    }

    public ExchangeExecutionResult TryTrade(GameEngine engine)
    {
        EnsureScenario(engine);
        ExchangeRuntime exchange = engine.GetService(CoreServiceKeys.ExchangeRuntime)
            ?? throw new InvalidOperationException("ExchangeRuntime missing.");
        ExchangeExecutionResult result = exchange.TryExecute(
            _tradeOperationId,
            new ExchangeExecutionContext(_playerA, _playerB, _cityA));
        if (_pactSigned)
        {
            _tradeAfterPact = result.Status;
        }
        else
        {
            _tradeBeforePact = result.Status;
        }

        _status = result.Succeeded
            ? "Trade convoy settled with Gold attribute input and Supply output."
            : $"Trade convoy blocked: {result.Status}.";
        PushLog(_status);
        RefreshPanelInternal(engine);
        return result;
    }

    public void AddResearcher(GameEngine engine)
    {
        EnsureScenario(engine);
        if (_activeResearchMembers >= _members.Count)
        {
            _status = "All configured researchers are already active.";
            PushLog(_status);
            RefreshPanelInternal(engine);
            return;
        }

        _activeResearchMembers++;
        PublishMemberCollection(engine);
        _status = $"Research member added: {_members[_activeResearchMembers - 1].Config.Label}.";
        PushLog(_status);
        RefreshPanelInternal(engine);
    }

    public void ResearchPulse(GameEngine engine)
    {
        EnsureScenario(engine);
        FourXAssociationConfig config = _config!;
        if (!EvaluateRequirement(engine))
        {
            _lastContribution = 0;
            _status = $"Shared technology needs {config.RequiredMembers} active members.";
            PushLog(_status);
            RefreshPanelInternal(engine);
            return;
        }

        _lastContribution = ComputeContribution(config);
        _researchProgress = Math.Min(config.ResearchCost, _researchProgress + _lastContribution);
        if (_researchProgress >= config.ResearchCost && !_techUnlocked)
        {
            CompleteProgression(engine);
            _techUnlocked = true;
            _status = $"{config.UnlockLabel} unlocked for the team scope.";
        }
        else
        {
            _status = $"Research pulse +{_lastContribution}; {_researchProgress}/{config.ResearchCost}.";
        }

        PushLog(_status);
        RefreshPanelInternal(engine);
    }

    public void RefreshPanel(GameEngine engine)
    {
        if (FourXAssociationIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            RefreshPanelInternal(engine);
        }
        else
        {
            ClearPanel(engine);
        }
    }

    internal FourXAssociationPanelState BuildPanelState()
    {
        FourXAssociationConfig? config = _config;
        FourXAssociationSnapshot snapshot = BuildSnapshot();
        return new FourXAssociationPanelState(
            Header: config?.Header ?? "4X Association Showcase",
            Summary: config?.Summary ?? string.Empty,
            Controls: config?.Controls ?? string.Empty,
            Status: snapshot.Status,
            ContractLines:
            [
                $"fog gate: before scout hidden={snapshot.HiddenBeforeScout}, after scout visible={snapshot.VisibleAfterScout}, after decay hidden={snapshot.HiddenAfterDecay}",
                $"diplomacy gate: before pact={snapshot.TradeBeforePact}, after pact={snapshot.TradeAfterPact}",
                $"attribute trade: gold={snapshot.Gold}, supplies={snapshot.SupplyCount}",
                $"shared tech: members={snapshot.ActiveResearchMembers}/{snapshot.RequiredResearchMembers}, progress={snapshot.ResearchProgress}/{snapshot.ResearchCost}, unlocked={snapshot.TechUnlocked}",
                $"ownership: root player={snapshot.OwnershipRootMatchesPlayer}, city stash edge={snapshot.OwnershipDirectCityToStash}"
            ],
            LogLines: _log.Count == 0 ? new[] { snapshot.Status } : _log.ToArray());
    }

    private void EnsureConfig()
    {
        if (_config != null)
        {
            return;
        }

        using Stream stream = _context.GetResource($"{_context.ModId}:assets/FourXAssociation/fourx_association_config.json");
        _config = FourXAssociationConfig.Load(stream);
    }

    private void EnsureScenario(GameEngine engine)
    {
        if (_scenarioReady)
        {
            return;
        }

        if (_config == null)
        {
            throw new InvalidOperationException("FourX association config was not loaded.");
        }

        ResolveIds(engine);
        BuildScenario(engine);
        PublishMemberCollection(engine);
        _hiddenBeforeScout = !CanReadHiddenUnit(engine);
        _scenarioReady = true;
        _status = "Scenario ready: scout, sign a pact, trade supplies, and unlock shared research.";
        PushLog(_status);
    }

    private void ResolveIds(GameEngine engine)
    {
        FourXAssociationConfig config = _config!;
        _goldAttributeId = AttributeRegistry.Register(config.GoldAttribute);
        RelationshipTypeRegistry relationshipTypes = engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
            ?? throw new InvalidOperationException("RelationshipTypeRegistry missing.");
        RelationshipMetricRegistry relationshipMetrics = engine.GetService(CoreServiceKeys.RelationshipMetricRegistry)
            ?? throw new InvalidOperationException("RelationshipMetricRegistry missing.");
        RelationshipFlagRegistry relationshipFlags = engine.GetService(CoreServiceKeys.RelationshipFlagRegistry)
            ?? throw new InvalidOperationException("RelationshipFlagRegistry missing.");
        ExchangeOperationRegistry operations = engine.GetService(CoreServiceKeys.ExchangeOperationRegistry)
            ?? throw new InvalidOperationException("ExchangeOperationRegistry missing.");
        ItemDefinitionRegistry items = engine.GetService(CoreServiceKeys.ItemDefinitionRegistry)
            ?? throw new InvalidOperationException("ItemDefinitionRegistry missing.");
        ItemLayoutRegistry layouts = engine.GetService(CoreServiceKeys.ItemLayoutRegistry)
            ?? throw new InvalidOperationException("ItemLayoutRegistry missing.");
        ScopeKeyRegistry scopeKeys = engine.GetService(CoreServiceKeys.ScopeKeyRegistry)
            ?? throw new InvalidOperationException("ScopeKeyRegistry missing.");

        _diplomacyTypeId = relationshipTypes.GetId(config.DiplomacyType);
        _trustMetricId = relationshipMetrics.GetId(config.TrustMetric);
        _tradePactFlagId = relationshipFlags.GetId(config.TradePactFlag);
        _tradeOperationId = operations.GetId(config.TradeOperation);
        _supplyItemId = items.GetId(config.SupplyItem);
        _cityStashLayoutId = layouts.GetId(config.CityStashLayout);
        _teamScopeId = scopeKeys.GetId(config.TeamScope);
        if (_goldAttributeId < 0 ||
            _diplomacyTypeId < 0 ||
            _trustMetricId < 0 ||
            _tradePactFlagId < 0 ||
            _tradeOperationId <= 0 ||
            _supplyItemId <= 0 ||
            _cityStashLayoutId <= 0 ||
            _teamScopeId <= 0)
        {
            throw new InvalidOperationException("FourX association showcase could not resolve configured registry ids.");
        }
    }

    private void BuildScenario(GameEngine engine)
    {
        FourXAssociationConfig config = _config!;
        World world = engine.World;
        var mapId = new MapId(FourXAssociationIds.MapId);
        _playerA = world.Create(new Name { Value = config.PlayerAName }, new MapEntity { MapId = mapId }, new AttributeBuffer());
        _playerB = world.Create(new Name { Value = config.PlayerBName }, new MapEntity { MapId = mapId });
        _cityA = world.Create(new Name { Value = config.CityAName }, new MapEntity { MapId = mapId });
        _cityB = world.Create(new Name { Value = config.CityBName }, new MapEntity { MapId = mapId });
        _hiddenUnit = world.Create(new Name { Value = config.HiddenUnitName }, new MapEntity { MapId = mapId });
        _teamHost = world.Create(
            new Name { Value = config.TeamHostName },
            new MapEntity { MapId = mapId },
            new ProgressionStateBuffer(),
            new ScopeMembershipRevision());
        _researcher = world.Create(
            new Name { Value = config.ResearcherName },
            new MapEntity { MapId = mapId },
            new ScopeRefBuffer(),
            new ScopeMemberTag());

        ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(_playerA);
        attributes.SetCurrent(_goldAttributeId, config.StartingGold);

        OwnershipResolver ownership = engine.GetService(CoreServiceKeys.OwnershipResolver)
            ?? throw new InvalidOperationException("OwnershipResolver missing.");
        ownership.EnsureOwnership(_playerA, _cityA);
        ownership.EnsureOwnership(_playerB, _cityB);
        ownership.EnsureOwnership(_playerB, _hiddenUnit);

        InventoryRuntimeService inventory = engine.GetService(CoreServiceKeys.InventoryRuntimeService)
            ?? throw new InvalidOperationException("InventoryRuntimeService missing.");
        _cityAStash = inventory.CreateContainer(_cityA, _cityStashLayoutId, ItemContainerPurpose.Stash);

        BuildResearchMembers(engine, config);
        ProgressionRequirementEvaluator evaluator = engine.GetService(CoreServiceKeys.ProgressionRequirementEvaluator)
            ?? throw new InvalidOperationException("ProgressionRequirementEvaluator missing.");
        if (!evaluator.TryBindScope(_researcher, _teamScopeId, _teamHost))
        {
            throw new InvalidOperationException("FourX association researcher could not bind to the configured team scope.");
        }
    }

    private void BuildResearchMembers(GameEngine engine, FourXAssociationConfig config)
    {
        int memberTagId = TagRegistry.Register(config.MemberTag);
        _members.Clear();
        _activeResearchMembers = 0;
        for (int i = 0; i < config.Members.Length; i++)
        {
            MemberConfig memberConfig = config.Members[i];
            var tags = default(GameplayTagContainer);
            tags.AddTag(memberTagId);
            Entity member = engine.World.Create(
                new Name { Value = memberConfig.Label },
                new MapEntity { MapId = new MapId(FourXAssociationIds.MapId) },
                tags,
                new TagCountContainer());
            _members.Add(new MemberRuntimeEntry(memberConfig, member));
            if (memberConfig.StartsActive)
            {
                _activeResearchMembers++;
            }
        }

        if (_activeResearchMembers <= 0)
        {
            _activeResearchMembers = 1;
        }
    }

    private void PublishMemberCollection(GameEngine engine)
    {
        FourXAssociationConfig config = _config!;
        EntityCollectionStore collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("EntityCollectionStore missing.");
        for (int i = 0; i < _activeResearchMembers; i++)
        {
            _memberBuffer[i] = _members[i].Entity;
        }

        collections.Replace(
            _teamHost,
            EntityCollectionDescriptor.Create(
                config.CollectionKey,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.Display,
                contextEntity: _teamHost,
                primaryEntity: _teamHost,
                title: config.UnlockLabel,
                summary: "4X association team research membership"),
            _memberBuffer.AsSpan(0, _activeResearchMembers));

        ScopeResolver resolver = engine.GetService(CoreServiceKeys.ScopeResolver)
            ?? throw new InvalidOperationException("ScopeResolver missing.");
        resolver.BumpMembershipRevision(_teamHost);
    }

    private bool EvaluateRequirement(GameEngine engine)
    {
        int requirementId = ProgressionRequirementIdRegistry.GetId(_config!.RequirementId);
        ProgressionRequirementEvaluator evaluator = engine.GetService(CoreServiceKeys.ProgressionRequirementEvaluator)
            ?? throw new InvalidOperationException("ProgressionRequirementEvaluator missing.");
        var context = new RoleResolverContext(actor: _researcher, subject: _researcher);
        return evaluator.Evaluate(requirementId, in context);
    }

    private void CompleteProgression(GameEngine engine)
    {
        int progressionId = ProgressionIdRegistry.GetId(_config!.ProgressionId);
        ProgressionRequirementEvaluator evaluator = engine.GetService(CoreServiceKeys.ProgressionRequirementEvaluator)
            ?? throw new InvalidOperationException("ProgressionRequirementEvaluator missing.");
        if (!evaluator.TryComplete(_teamHost, progressionId))
        {
            throw new InvalidOperationException($"Failed to complete progression '{_config.ProgressionId}'.");
        }
    }

    private int ComputeContribution(FourXAssociationConfig config)
    {
        int totalMultiplier = 0;
        for (int i = 0; i < _activeResearchMembers; i++)
        {
            totalMultiplier += Math.Max(1, _members[i].Config.ContributionMultiplier);
        }

        return totalMultiplier * config.ContributionPerMember;
    }

    private bool CanReadHiddenUnit(GameEngine engine)
    {
        KnowledgeProjectionResolver resolver = engine.GetService(CoreServiceKeys.KnowledgeProjectionResolver)
            ?? throw new InvalidOperationException("KnowledgeProjectionResolver missing.");
        return resolver.CanReadPosition(_playerA, _hiddenUnit, _knowledgeTick, KnowledgePositionAccess.Live);
    }

    private KnowledgeProjectionStore RequireKnowledgeStore(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)
            ?? throw new InvalidOperationException("KnowledgeProjectionStore missing.");
    }

    private RelationshipRuntime RequireRelationships(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.RelationshipRuntime)
            ?? throw new InvalidOperationException("RelationshipRuntime missing.");
    }

    private FourXAssociationSnapshot BuildSnapshot()
    {
        GameEngine? engine = _engine;
        FourXAssociationConfig? config = _config;
        int gold = 0;
        int supplyCount = 0;
        bool requirementSatisfied = false;
        bool rootMatches = false;
        bool directCityToStash = false;
        if (engine != null && _scenarioReady && config != null)
        {
            if (engine.World.IsAlive(_playerA) && engine.World.Has<AttributeBuffer>(_playerA))
            {
                AttributeBuffer attributes = engine.World.Get<AttributeBuffer>(_playerA);
                gold = (int)attributes.GetCurrent(_goldAttributeId);
            }

            InventoryRuntimeService inventory = engine.GetService(CoreServiceKeys.InventoryRuntimeService)
                ?? throw new InvalidOperationException("InventoryRuntimeService missing.");
            supplyCount = inventory.CountStackUnits(_playerA, _supplyItemId);
            requirementSatisfied = EvaluateRequirement(engine);

            OwnershipResolver ownership = engine.GetService(CoreServiceKeys.OwnershipResolver)
                ?? throw new InvalidOperationException("OwnershipResolver missing.");
            rootMatches = ownership.TryResolveRootOwner(_cityAStash, out Entity rootOwner) && rootOwner == _playerA;
            directCityToStash = ownership.TryGetDirectOwner(_cityAStash, out Entity directOwner) && directOwner == _cityA;
        }

        return new FourXAssociationSnapshot(
            HiddenBeforeScout: _hiddenBeforeScout,
            VisibleAfterScout: _visibleAfterScout,
            HiddenAfterDecay: _hiddenAfterDecay,
            KnowledgeTick: _knowledgeTick,
            ExpiredKnowledgeRecords: _expiredKnowledgeRecords,
            TradeBeforePact: _tradeBeforePact,
            TradeAfterPact: _tradeAfterPact,
            Gold: gold,
            SupplyCount: supplyCount,
            PactSigned: _pactSigned,
            ActiveResearchMembers: _activeResearchMembers,
            RequiredResearchMembers: config?.RequiredMembers ?? 0,
            ResearchProgress: _researchProgress,
            ResearchCost: config?.ResearchCost ?? 0,
            ResearchRequirementSatisfied: requirementSatisfied,
            TechUnlocked: _techUnlocked,
            OwnershipRootMatchesPlayer: rootMatches,
            OwnershipDirectCityToStash: directCityToStash,
            Status: _status);
    }

    private void RefreshPanelInternal(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panelController.MountOrRefresh(root, engine);
        }
    }

    private void ClearPanel(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panelController.ClearIfOwned(root);
        }
    }

    private void ResetScenario()
    {
        _engine = null;
        _playerA = Entity.Null;
        _playerB = Entity.Null;
        _cityA = Entity.Null;
        _cityB = Entity.Null;
        _cityAStash = Entity.Null;
        _hiddenUnit = Entity.Null;
        _teamHost = Entity.Null;
        _researcher = Entity.Null;
        _knowledgeTick = 0;
        _expiredKnowledgeRecords = 0;
        _activeResearchMembers = 0;
        _researchProgress = 0;
        _lastContribution = 0;
        _scenarioReady = false;
        _hiddenBeforeScout = true;
        _visibleAfterScout = false;
        _hiddenAfterDecay = true;
        _pactSigned = false;
        _techUnlocked = false;
        _tradeBeforePact = ExchangeExecutionStatus.MissingOperation;
        _tradeAfterPact = ExchangeExecutionStatus.MissingOperation;
        _members.Clear();
        _log.Clear();
        _status = "Load the 4X association showcase map.";
    }

    private void PushLog(string line)
    {
        _log.Insert(0, line);
        if (_log.Count > 6)
        {
            _log.RemoveAt(_log.Count - 1);
        }
    }

    private static void ActivateInputContext(PlayerInputHandler? input)
    {
        if (input == null || !input.HasContext(FourXAssociationInputContexts.Showcase))
        {
            return;
        }

        input.PushContext(FourXAssociationInputContexts.Showcase);
    }

    private static void DeactivateInputContext(PlayerInputHandler? input)
    {
        input?.PopContext(FourXAssociationInputContexts.Showcase);
    }

    private readonly record struct MemberRuntimeEntry(MemberConfig Config, Entity Entity);
}
