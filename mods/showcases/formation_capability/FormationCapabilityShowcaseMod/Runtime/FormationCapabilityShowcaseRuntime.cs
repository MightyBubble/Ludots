using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Knowledge;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.MovePlanning;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using FormationCapabilityShowcaseMod.Systems;

namespace FormationCapabilityShowcaseMod.Runtime;

internal sealed class FormationCapabilityShowcaseRuntime
{
    private const string RotateLeftActionId = "FormationCapability_RotateLeft";
    private const string RotateRightActionId = "FormationCapability_RotateRight";
    private const float RotateStepRadians = MathF.PI / 8f;

    private static readonly float DiscSlotGoldenAngleRadians = MathF.PI * (3f - MathF.Sqrt(5f));
    private static readonly QueryDescription FormationAnchorCandidateQuery = new QueryDescription()
        .WithAll<FormationCapabilityShowcaseFormationAgent, MassNavigationAgentIndex>();
    private static readonly QueryDescription FormationFollowerCandidateQuery = new QueryDescription()
        .WithAll<FormationCapabilityShowcaseFormationSoldier, MassNavigationAgentIndex>();
    private static readonly QueryDescription FormationExecutionAnchorQuery = new QueryDescription()
        .WithAll<FormationCapabilityShowcaseFormationAgent, FormationCapabilityShowcaseCommandState, FormationCapabilityShowcaseFormationState, MassNavigationAgentIndex, FacingDirection, WorldPositionCm>()
        .WithNone<PresentationDestroyPending, SuspendedTag>();
    private static readonly QueryDescription FormationExecutionSoldierQuery = new QueryDescription()
        .WithAll<FormationCapabilityShowcaseFormationSoldier, MassNavigationAgentIndex, FacingDirection>()
        .WithNone<PresentationDestroyPending, SuspendedTag>();
    private static readonly QueryDescription ObstacleOverlayCandidateQuery = new QueryDescription()
        .WithAll<EntityTemplateKeyRef, WorldPositionCm>();

    private readonly Ludots.Core.Modding.IModContext? _context;
    private FormationCapabilityShowcaseConfig? _config;
    private FormationCapabilityShowcaseSoldierAgentSpawnPlan[] _soldierAgentPlans = Array.Empty<FormationCapabilityShowcaseSoldierAgentSpawnPlan>();
    private FormationCapabilityShowcaseFormationPlan[] _formationPlans = Array.Empty<FormationCapabilityShowcaseFormationPlan>();
    private FormationCapabilityShowcaseObstacleOverlayPlan[] _obstacleOverlayPlans = Array.Empty<FormationCapabilityShowcaseObstacleOverlayPlan>();
    private Entity[] _formationEntities = Array.Empty<Entity>();
    private Entity[] _soldierEntitiesByPlanIndex = Array.Empty<Entity>();
    private Entity[] _obstacleOverlayEntities = Array.Empty<Entity>();
    private Entity[] _initialCommandSourceScratch = Array.Empty<Entity>();
    private readonly List<PendingFormationBinding> _pendingFormationBindings = new();
    private readonly List<PendingSoldierBinding> _pendingSoldierBindings = new();
    private readonly List<PendingObstacleOverlayBinding> _pendingObstacleOverlayBindings = new();
    private MassNavigationMovePlanExecutionSink? _movePlanExecutionSink;
    private float[] _lastTargetCenterXByFormation = Array.Empty<float>();
    private float[] _lastTargetCenterYByFormation = Array.Empty<float>();
    private float[] _lastTargetFacingByFormation = Array.Empty<float>();
    private byte[] _targetSnapshotInitializedByFormation = Array.Empty<byte>();
    private Vector2[] _anchorCenterByFormation = Array.Empty<Vector2>();
    private FormationCapabilityShowcaseCommandState[] _commandByFormation = Array.Empty<FormationCapabilityShowcaseCommandState>();
    private FormationCapabilityShowcaseFormationAgent[] _agentConfigByFormation = Array.Empty<FormationCapabilityShowcaseFormationAgent>();
    private byte[] _anchorSeenByFormation = Array.Empty<byte>();
    private byte[] _soldierSeenByPlan = Array.Empty<byte>();
    private int[] _aliveSoldierCountByFormation = Array.Empty<int>();
    private byte[] _targetChangedByFormation = Array.Empty<byte>();
    private bool _systemsInstalled;
    private bool _scenarioSpawned;
    private bool _executionBindingReady;
    private bool _obstacleOverlaySpawnsQueued;
    private bool _initialCommandSourceApplied;
    private int _rotateOrderRejectCount;

    internal int RotateOrderRejectCount => _rotateOrderRejectCount;

    public FormationCapabilityShowcaseConfig ActiveConfig => _config
        ?? throw new InvalidOperationException("Formation Capability showcase config has not been loaded.");

    public FormationCapabilityShowcaseRuntime()
    {
    }

    public FormationCapabilityShowcaseRuntime(Ludots.Core.Modding.IModContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        FormationCapabilityShowcaseConfig config = EnsureConfig(engine);
        EnsureInitialCommandSourceScratch(config);
        string mapId = context.Get(CoreServiceKeys.MapId).Value;
        if (!string.Equals(mapId, config.MapId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        EnsureSystemsInstalled(engine);
        if (!_scenarioSpawned)
        {
            SpawnScenario(engine, config);
        }

        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            return Task.CompletedTask;
        }

        FormationCapabilityShowcaseConfig config = EnsureConfig(engine);
        string mapId = context.Get(CoreServiceKeys.MapId).Value;
        if (!string.Equals(mapId, config.MapId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        _scenarioSpawned = false;
        _executionBindingReady = false;
        _obstacleOverlaySpawnsQueued = false;
        _initialCommandSourceApplied = false;
        RemovePendingScenarioSpawns(engine, config);
        MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
        ClearFormationExecutionTargets(engine);
        simulation.ResetRuntimeState(engine.World);
        ClearFormationCaches();
        return Task.CompletedTask;
    }

    private FormationCapabilityShowcaseConfig EnsureConfig(GameEngine engine)
    {
        if (_config != null)
        {
            return _config;
        }

        if (engine.ConfigPipeline == null)
        {
            throw new InvalidOperationException("Formation Capability showcase requires ConfigPipeline before loading config.");
        }

        _config = new FormationCapabilityShowcaseConfigLoader(engine.ConfigPipeline).Load(
            engine.ConfigCatalog,
            engine.ConfigConflictReport);
        return _config;
    }

    private void EnsureSystemsInstalled(GameEngine engine)
    {
        if (_systemsInstalled)
        {
            return;
        }

        MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
        _movePlanExecutionSink = new MassNavigationMovePlanExecutionSink(simulation);
        engine.RegisterSystem(
            new FormationCapabilityShowcaseScenarioBindingSystem(engine, this, simulation),
            SystemGroup.RuntimeEntityBinding);
        engine.InsertSystemBeforeRequired<MassNavigationPreSimulationStepSystem>(
            new FormationCapabilityShowcaseFormationRuntimeSystem(engine, this, simulation),
            SystemGroup.PostMovement);
        OrderQueue orders = engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new InvalidOperationException("Formation Capability showcase requires OrderQueue.");
        Ludots.Core.Modding.IModContext context = _context
            ?? throw new InvalidOperationException("Formation Capability showcase requires IModContext before installing local order source.");
        engine.RegisterSystem(
            new FormationCapabilityLocalOrderSourceSystem(engine.World, engine.GlobalContext, orders, context),
            SystemGroup.InputCollection);
        FormationCapabilityShowcaseConfig config = EnsureConfig(engine);
        engine.RegisterSystem(
            new FormationCapabilityCommandSourceRotateSystem(
                engine,
                this,
                orders,
                config.OrderBatchCapacity),
            SystemGroup.InputCollection);
        engine.RegisterSystem(
            new FormationCapabilityOrderSystem(
                engine,
                this,
                config.OrderBatchCapacity),
            SystemGroup.AbilityActivation);
        engine.InsertPresentationSystemBefore<PerformerRuleSystem>(new FormationCapabilityShowcaseFormationOutlinePresentationSystem(engine, this, config));
        engine.InsertPresentationSystemBefore<PerformerRuleSystem>(new FormationCapabilityShowcaseObstacleOverlayPresentationSystem(engine, this, simulation.Config.Solver.MaxObstacleCount));
        _systemsInstalled = true;
    }

    public bool IsCurrentShowcaseMap(GameEngine engine)
    {
        FormationCapabilityShowcaseConfig config = EnsureConfig(engine);
        return string.Equals(engine.CurrentMapSession?.MapId.Value, config.MapId, StringComparison.Ordinal);
    }

    public void Tick(GameEngine engine, MassNavigationSimulationRuntime simulation)
    {
        FormationCapabilityShowcaseConfig config = EnsureConfig(engine);
        if (!string.Equals(engine.CurrentMapSession?.MapId.Value, config.MapId, StringComparison.Ordinal))
        {
            return;
        }

        if (!_scenarioSpawned)
        {
            SpawnScenario(engine, config);
            return;
        }

        ApplyFormationExecutionTargets(engine, simulation);
        PublishLocalFormationKnowledge(engine);
        TryApplyInitialCommandSource(engine, config);
    }

    private void SpawnScenario(GameEngine engine, FormationCapabilityShowcaseConfig config)
    {
        MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("Formation Capability showcase requires RuntimeEntitySpawnQueue.");

        ClearCommandSource(engine);
        BuildAgentPlans(engine, simulation, config);
        DestroyShowcaseOwnedEntities(engine);

        int spawnRequestCount = _soldierAgentPlans.Length + _formationPlans.Length;
        if (spawnQueue.FreeCapacity < spawnRequestCount)
        {
            throw new InvalidOperationException(
                $"Formation Capability showcase requires RuntimeEntitySpawnQueue free capacity {spawnRequestCount}, actual {spawnQueue.FreeCapacity}.");
        }

        ConfigureScenarioTeams(simulation, config);
        ConfigureRelationships(simulation.Config);
        ValidateAuthoring(engine, config);

        TeamEntityLookup teamLookup = engine.GetService(CoreServiceKeys.TeamEntityLookup)
            ?? throw new InvalidOperationException("Formation Capability showcase requires TeamEntityLookup.");
        PlayerEntityLookup playerLookup = engine.GetService(CoreServiceKeys.PlayerEntityLookup)
            ?? throw new InvalidOperationException("Formation Capability showcase requires PlayerEntityLookup.");
        var registeredTeamIds = new HashSet<int>(_formationPlans.Length);
        for (int i = 0; i < _formationPlans.Length; i++)
        {
            MassNavigationScenarioTeamConfig team = ResolveScenarioTeam(simulation.Config.Scenario, _formationPlans[i].TeamId);
            if (!registeredTeamIds.Add(team.Id))
            {
                continue;
            }

            teamLookup.Register(
                team.Id,
                RelationshipTeamBootstrapper.EnsureTeamEntity(engine.World, teamLookup, team.Id, team.Name));
        }

        RemovePendingScenarioSpawns(engine, config);
        MapId mapId = RequireCurrentMapId(engine, config.MapId);
        EnqueueFormationAgentSpawns(engine, config, spawnQueue, mapId, teamLookup, playerLookup);
        EnsureSoldierEntityCache();
        for (int i = 0; i < _soldierAgentPlans.Length; i++)
        {
            FormationCapabilityShowcaseSoldierAgentSpawnPlan plan = _soldierAgentPlans[i];
            var request = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = plan.TemplateId,
                MapId = mapId,
                WorldPositionCm = Fix64Vec2.FromInt((int)MathF.Round(plan.WorldXCm), (int)MathF.Round(plan.WorldYCm)),
                HasWorldPosition = 1,
                FacingAngleRad = plan.FacingRad,
                HasFacing = 1,
                MembershipTarget = RequireLiveTeamDomain(engine, teamLookup, plan.TeamId),
                HasMembershipTarget = 1,
                ComponentPatches = CreateFormationFollowerPatch(
                    plan.FormationIndex,
                    plan.SlotIndex,
                    plan.SlotOffsetXCm,
                    plan.SlotOffsetYCm),
            };

            if (!spawnQueue.TryEnqueue(in request))
            {
                throw new InvalidOperationException("Formation Capability showcase failed to enqueue runtime entity spawn request.");
            }
        }

        _scenarioSpawned = true;
        _executionBindingReady = false;
        _obstacleOverlaySpawnsQueued = false;
        _initialCommandSourceApplied = false;
        simulation.MarkScenarioSpawned();
        simulation.MarkStructuralChange();
    }

    private static void RemovePendingScenarioSpawns(GameEngine engine, FormationCapabilityShowcaseConfig config)
    {
        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("Formation Capability showcase requires RuntimeEntitySpawnQueue.");
        spawnQueue.RemoveForMapAndTemplates(
            RequireCurrentMapId(engine, config.MapId),
            BuildPendingSpawnTemplateScratch(config));
    }

    private static string[] BuildPendingSpawnTemplateScratch(FormationCapabilityShowcaseConfig config)
    {
        int count = 2 + config.Formations.Length;
        string[] templateIds = new string[count];
        templateIds[0] = config.FormationAgent.TemplateId;
        templateIds[1] = config.ObstacleOverlay.TemplateId;
        for (int i = 0; i < config.Formations.Length; i++)
        {
            templateIds[i + 2] = config.Formations[i].SoldierAgent.TemplateId;
        }

        return templateIds;
    }

    private void RegisterSpawnedSoldier(Entity entity, in FormationCapabilityShowcaseFormationSoldier soldier)
    {
        int planIndex = ResolveSoldierPlanIndex(soldier.FormationIndex, soldier.SlotIndex);
        if ((uint)planIndex >= (uint)_soldierEntitiesByPlanIndex.Length)
        {
            throw new InvalidOperationException($"Formation Capability showcase soldier plan index {planIndex} exceeds its scenario cache.");
        }

        if (_soldierEntitiesByPlanIndex[planIndex] != Entity.Null)
        {
            throw new InvalidOperationException($"Formation Capability showcase soldier plan index {planIndex} was already bound.");
        }

        _soldierEntitiesByPlanIndex[planIndex] = entity;
    }

    private void RegisterSpawnedFormationAgent(GameEngine engine, Entity entity, int formationIndex)
    {
        if ((uint)formationIndex >= (uint)_formationPlans.Length)
        {
            throw new InvalidOperationException($"Formation Capability showcase formation index {formationIndex} exceeds its scenario cache.");
        }

        if (_formationEntities.Length != _formationPlans.Length)
        {
            throw new InvalidOperationException("Formation Capability showcase formation entity cache was not initialized before formation agent binding.");
        }

        if (_formationEntities[formationIndex] != Entity.Null)
        {
            throw new InvalidOperationException($"Formation Capability showcase formation index {formationIndex} was already bound.");
        }

        FormationCapabilityShowcaseFormationPlan plan = _formationPlans[formationIndex];
        FormationCapabilityShowcaseFormationConfig formation = ActiveConfig.Formations[formationIndex];
        _formationEntities[formationIndex] = entity;
        UpsertComponent(engine.World, entity, new FormationCapabilityShowcaseFormationAgent
        {
            FormationIndex = plan.FormationIndex,
            SlotCount = plan.SoldierCount,
            TargetChangeEpsilonCm = ActiveConfig.TargetChangeEpsilonCm,
            FacingChangeEpsilonRadians = ActiveConfig.FacingChangeEpsilonRadians,
        });
        UpsertComponent(engine.World, entity, new FormationCapabilityShowcaseCommandState
        {
            TargetCenterXCm = plan.InitialCenterXCm,
            TargetCenterYCm = plan.InitialCenterYCm,
            TargetFacingRad = plan.FacingRad,
            HasMoveTarget = 1,
        });
        UpsertComponent(engine.World, entity, new FormationCapabilityShowcaseFormationState
        {
            SoldierCount = plan.SoldierCount,
            AliveSoldierCount = plan.SoldierCount,
            CenterXCm = plan.InitialCenterXCm,
            CenterYCm = plan.InitialCenterYCm,
            FacingRad = plan.FacingRad,
        });
        UpsertComponent(engine.World, entity, formation.Outline.ToComponent(plan.Id));
        UpsertComponent(engine.World, entity, formation.Outline.ToSpatialBounds());
        UpsertComponent(engine.World, entity, formation.Outline.ToSpatialFootprint(plan.Id));
        UpsertComponent(engine.World, entity, default(CommandSourceSelectableTag));
        UpsertComponent(engine.World, entity, CommandSourceSelectableState.EnabledByDefault);
    }

    private void RegisterSpawnedObstacleOverlay(GameEngine engine, Entity entity, int overlayIndex)
    {
        if (_obstacleOverlayEntities.Length != _obstacleOverlayPlans.Length)
        {
            throw new InvalidOperationException("Formation Capability showcase obstacle overlay cache was not initialized before overlay binding.");
        }

        if ((uint)overlayIndex >= (uint)_obstacleOverlayEntities.Length)
        {
            throw new InvalidOperationException($"Formation Capability showcase obstacle overlay index {overlayIndex} exceeds planned overlays {_obstacleOverlayEntities.Length}.");
        }

        if (_obstacleOverlayEntities[overlayIndex] != Entity.Null)
        {
            throw new InvalidOperationException($"Formation Capability showcase obstacle overlay index {overlayIndex} was already bound.");
        }

        _obstacleOverlayEntities[overlayIndex] = entity;
        UpsertComponent(engine.World, entity, ActiveConfig.ObstacleOverlay.ToComponent(_obstacleOverlayPlans[overlayIndex].RadiusCm));
    }

    public void BindComponentAuthoredScenarioEntities(
        GameEngine engine,
        MassNavigationSimulationRuntime simulation)
    {
        FormationCapabilityShowcaseConfig config = EnsureConfig(engine);
        if (_formationEntities.Length != _formationPlans.Length ||
            _soldierEntitiesByPlanIndex.Length != _soldierAgentPlans.Length)
        {
            return;
        }

        EnsureObstacleOverlaySpawnsQueued(engine, simulation, config);

        _pendingFormationBindings.Clear();
        foreach (ref var chunk in engine.World.Query(in FormationAnchorCandidateQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            Span<FormationCapabilityShowcaseFormationAgent> anchors = chunk.GetSpan<FormationCapabilityShowcaseFormationAgent>();
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                if (engine.World.Has<PresentationDestroyPending>(entity))
                {
                    continue;
                }

                int formationIndex = anchors[index].FormationIndex;
                if ((uint)formationIndex >= (uint)_formationEntities.Length ||
                    _formationEntities[formationIndex] == entity)
                {
                    continue;
                }
                _pendingFormationBindings.Add(new PendingFormationBinding(entity, formationIndex));
            }
        }
        for (int i = 0; i < _pendingFormationBindings.Count; i++)
        {
            PendingFormationBinding binding = _pendingFormationBindings[i];
            RegisterSpawnedFormationAgent(engine, binding.Entity, binding.FormationIndex);
        }
        PublishLocalFormationKnowledge(engine);

        _pendingSoldierBindings.Clear();
        foreach (ref var chunk in engine.World.Query(in FormationFollowerCandidateQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            Span<FormationCapabilityShowcaseFormationSoldier> followers = chunk.GetSpan<FormationCapabilityShowcaseFormationSoldier>();
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                if (engine.World.Has<PresentationDestroyPending>(entity))
                {
                    continue;
                }

                int planIndex = ResolveSoldierPlanIndex(followers[index].FormationIndex, followers[index].SlotIndex);
                if ((uint)planIndex >= (uint)_soldierEntitiesByPlanIndex.Length ||
                    _soldierEntitiesByPlanIndex[planIndex] == entity)
                {
                    continue;
                }
                _pendingSoldierBindings.Add(new PendingSoldierBinding(entity, followers[index]));
            }
        }
        for (int i = 0; i < _pendingSoldierBindings.Count; i++)
        {
            PendingSoldierBinding binding = _pendingSoldierBindings[i];
            FormationCapabilityShowcaseFormationSoldier soldier = binding.Soldier;
            RegisterSpawnedSoldier(binding.Entity, in soldier);
        }

        BindObstacleOverlays(engine, config);
        simulation.MarkStructuralChange();
    }

    private void PublishLocalFormationKnowledge(GameEngine engine)
    {
        if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? viewerObj) ||
            viewerObj is not Entity viewer ||
            !engine.World.IsAlive(viewer))
        {
            return;
        }

        ControlDomainQuery controlDomains = engine.GetService(CoreServiceKeys.ControlDomainQuery)
            ?? throw new InvalidOperationException("Formation Capability showcase requires ControlDomainQuery before publishing local formation knowledge.");
        if (!controlDomains.TryResolveControlDomain(viewer, out Entity viewerDomain))
        {
            throw new InvalidOperationException("Formation Capability showcase local player has no relationship control domain.");
        }

        KnowledgeProjectionStore store = engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)
            ?? throw new InvalidOperationException("Formation Capability showcase requires KnowledgeProjectionStore before publishing local formation knowledge.");
        int observedTick = KnowledgeProjectionConsumer.ResolveCurrentTick(engine.GlobalContext);
        var empty = KnowledgeIdMask256.Empty;

        for (int i = 0; i < _formationEntities.Length; i++)
        {
            Entity formation = _formationEntities[i];
            if (!engine.World.IsAlive(formation) ||
                engine.World.Has<PresentationDestroyPending>(formation) ||
                !CommandSourceEligibility.IsSelectableNow(engine.World, formation) ||
                !controlDomains.TryResolveControlDomain(formation, out Entity formationDomain) ||
                formationDomain != viewerDomain)
            {
                continue;
            }

            store.Upsert(
                viewer,
                formation,
                new KnowledgeDisclosureRecord(
                    KnowledgePresence.LiveVisible,
                    KnowledgePositionAccess.Live,
                    empty,
                    empty,
                    empty,
                    viewer,
                    observedTick,
                    expiryTick: 0,
                    confidencePermille: 1000,
                    revision: 0));
        }
    }

    private void EnsureObstacleOverlaySpawnsQueued(
        GameEngine engine,
        MassNavigationSimulationRuntime simulation,
        FormationCapabilityShowcaseConfig config)
    {
        if (_obstacleOverlaySpawnsQueued)
        {
            return;
        }

        int obstacleCount = simulation.NavigationObstacleCount;
        if (obstacleCount <= 0)
        {
            return;
        }

        BuildObstacleOverlayPlans(simulation);
        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("Formation Capability showcase requires RuntimeEntitySpawnQueue.");
        if (spawnQueue.FreeCapacity < _obstacleOverlayPlans.Length)
        {
            throw new InvalidOperationException(
                $"Formation Capability showcase requires RuntimeEntitySpawnQueue free capacity {_obstacleOverlayPlans.Length}, actual {spawnQueue.FreeCapacity}.");
        }

        EnqueueObstacleOverlaySpawns(
            engine,
            config,
            spawnQueue,
            RequireCurrentMapId(engine, config.MapId));
        _obstacleOverlaySpawnsQueued = true;
    }

    private void BindObstacleOverlays(GameEngine engine, FormationCapabilityShowcaseConfig config)
    {
        if (_obstacleOverlayEntities.Length != _obstacleOverlayPlans.Length)
        {
            return;
        }

        EntityTemplateKeyRegistry templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("Formation Capability showcase requires EntityTemplateKeyRegistry.");
        int overlayTemplateKey = templateKeys.GetId(config.ObstacleOverlay.TemplateId);
        if (overlayTemplateKey <= 0)
        {
            throw new InvalidOperationException($"Formation Capability showcase obstacle overlay template '{config.ObstacleOverlay.TemplateId}' was not registered in EntityTemplateKeyRegistry.");
        }

        _pendingObstacleOverlayBindings.Clear();
        foreach (ref var chunk in engine.World.Query(in ObstacleOverlayCandidateQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            Span<EntityTemplateKeyRef> templateKeysSpan = chunk.GetSpan<EntityTemplateKeyRef>();
            Span<WorldPositionCm> worldPositions = chunk.GetSpan<WorldPositionCm>();
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                if (engine.World.Has<FormationCapabilityShowcaseObstacleOverlay>(entity) ||
                    engine.World.Has<PresentationDestroyPending>(entity))
                {
                    continue;
                }

                if (templateKeysSpan[index].TemplateKeyId != overlayTemplateKey)
                {
                    continue;
                }

                int overlayIndex = ResolveObstacleOverlayPlanIndex(worldPositions[index]);
                _pendingObstacleOverlayBindings.Add(new PendingObstacleOverlayBinding(entity, overlayIndex));
            }
        }
        for (int i = 0; i < _pendingObstacleOverlayBindings.Count; i++)
        {
            PendingObstacleOverlayBinding binding = _pendingObstacleOverlayBindings[i];
            RegisterSpawnedObstacleOverlay(engine, binding.Entity, binding.OverlayIndex);
        }
    }

    private int ResolveObstacleOverlayPlanIndex(in WorldPositionCm worldPosition)
    {
        int xCm = worldPosition.Value.X.ToInt();
        int yCm = worldPosition.Value.Y.ToInt();
        for (int i = 0; i < _obstacleOverlayPlans.Length; i++)
        {
            if (_obstacleOverlayEntities[i] != Entity.Null)
            {
                continue;
            }

            FormationCapabilityShowcaseObstacleOverlayPlan plan = _obstacleOverlayPlans[i];
            if ((int)MathF.Round(plan.WorldXCm) == xCm &&
                (int)MathF.Round(plan.WorldYCm) == yCm)
            {
                return i;
            }
        }

        throw new InvalidOperationException(
            $"Formation Capability showcase obstacle overlay at ({xCm}, {yCm}) does not match a planned obstacle overlay.");
    }

    public bool TryGetFormationEntity(int formationIndex, out Entity entity)
    {
        if ((uint)formationIndex >= (uint)_formationEntities.Length)
        {
            entity = Entity.Null;
            return false;
        }

        entity = _formationEntities[formationIndex];
        return entity != Entity.Null;
    }

    public bool TryGetSoldierEntityByPlanIndex(int soldierPlanIndex, out Entity entity)
    {
        if ((uint)soldierPlanIndex >= (uint)_soldierEntitiesByPlanIndex.Length)
        {
            entity = Entity.Null;
            return false;
        }

        entity = _soldierEntitiesByPlanIndex[soldierPlanIndex];
        return entity != Entity.Null;
    }

    private void BuildAgentPlans(GameEngine engine, MassNavigationSimulationRuntime simulation, FormationCapabilityShowcaseConfig config)
    {
        MassNavigationAgentProfileSetConfig profileSet = simulation.Config.AgentProfiles;
        var geometryProfiles = engine.GetService(CoreServiceKeys.AgentProfiles)
            ?? throw new InvalidOperationException("Capability Standard Formation Capability showcase requires AgentProfiles.");
        config.ValidateAgentProfileReferences(profileSet, geometryProfiles);

        int soldierCount = 0;
        for (int i = 0; i < config.Formations.Length; i++)
        {
            int count = config.Formations[i].SoldierCount;
            soldierCount += count;
        }

        if (_soldierAgentPlans.Length != soldierCount)
        {
            _soldierAgentPlans = new FormationCapabilityShowcaseSoldierAgentSpawnPlan[soldierCount];
        }

        if (_formationPlans.Length != config.Formations.Length)
        {
            _formationPlans = new FormationCapabilityShowcaseFormationPlan[config.Formations.Length];
            _lastTargetCenterXByFormation = new float[config.Formations.Length];
            _lastTargetCenterYByFormation = new float[config.Formations.Length];
            _lastTargetFacingByFormation = new float[config.Formations.Length];
            _targetSnapshotInitializedByFormation = new byte[config.Formations.Length];
            _anchorCenterByFormation = new Vector2[config.Formations.Length];
            _commandByFormation = new FormationCapabilityShowcaseCommandState[config.Formations.Length];
            _agentConfigByFormation = new FormationCapabilityShowcaseFormationAgent[config.Formations.Length];
            _anchorSeenByFormation = new byte[config.Formations.Length];
            _aliveSoldierCountByFormation = new int[config.Formations.Length];
            _targetChangedByFormation = new byte[config.Formations.Length];
        }
        else
        {
            Array.Clear(_targetSnapshotInitializedByFormation);
        }

        if (_soldierSeenByPlan.Length != soldierCount)
        {
            _soldierSeenByPlan = new byte[soldierCount];
        }

        for (int formationIndex = 0; formationIndex < config.Formations.Length; formationIndex++)
        {
            FormationCapabilityShowcaseFormationConfig formation = config.Formations[formationIndex];
            float facingRad = formation.FacingDeg * (MathF.PI / 180f);
            _formationPlans[formationIndex] = new FormationCapabilityShowcaseFormationPlan(
                formationIndex,
                formation.Id,
                formation.Label,
                formation.TeamId,
                firstSoldierPlanIndex: 0,
                formation.SoldierCount,
                formation.CenterXCm,
                formation.CenterYCm,
                facingRad);
        }

        int soldierPlanIndex = 0;
        for (int formationIndex = 0; formationIndex < config.Formations.Length; formationIndex++)
        {
            FormationCapabilityShowcaseFormationConfig formation = config.Formations[formationIndex];
            int firstSoldierPlanIndex = soldierPlanIndex;
            float facingRad = formation.FacingDeg * (MathF.PI / 180f);
            float forwardX = MathF.Cos(facingRad);
            float forwardY = MathF.Sin(facingRad);
            float lateralX = -forwardY;
            float lateralY = forwardX;
            FormationCapabilityShowcaseFormationSlotConfig slots = formation.Slots;

            for (int slotIndex = 0; slotIndex < slots.SoldierCount; slotIndex++)
            {
                Vector2 slotOffset = ResolveSlotOffset(slots, slotIndex);
                float lateralOffset = slotOffset.X;
                float depthOffset = slotOffset.Y;
                float worldX = formation.CenterXCm + (lateralX * lateralOffset) + (forwardX * depthOffset);
                float worldY = formation.CenterYCm + (lateralY * lateralOffset) + (forwardY * depthOffset);
                _soldierAgentPlans[soldierPlanIndex] = new FormationCapabilityShowcaseSoldierAgentSpawnPlan(
                    formationIndex,
                    slotIndex,
                    formation.TeamId,
                    formation.SoldierAgent.TemplateId,
                    worldX,
                    worldY,
                    facingRad,
                    slotOffset.X,
                    slotOffset.Y);
                soldierPlanIndex++;
            }

            _formationPlans[formationIndex] = new FormationCapabilityShowcaseFormationPlan(
                formationIndex,
                formation.Id,
                formation.Label,
                formation.TeamId,
                firstSoldierPlanIndex,
                formation.SoldierCount,
                formation.CenterXCm,
                formation.CenterYCm,
                facingRad);
        }
    }

    private void BuildObstacleOverlayPlans(MassNavigationSimulationRuntime simulation)
    {
        int obstacleCount = simulation.NavigationObstacleCount;
        if (_obstacleOverlayPlans.Length != obstacleCount)
        {
            _obstacleOverlayPlans = new FormationCapabilityShowcaseObstacleOverlayPlan[obstacleCount];
        }

        for (int obstacleIndex = 0; obstacleIndex < obstacleCount; obstacleIndex++)
        {
            MassNavigationObstacleSnapshot obstacle = simulation.GetObstacleWorldSnapshot(obstacleIndex);
            _obstacleOverlayPlans[obstacleIndex] = new FormationCapabilityShowcaseObstacleOverlayPlan(
                obstacle.WorldXCm,
                obstacle.WorldYCm,
                obstacle.RadiusCm);
        }
    }

    private static Vector2 ResolveSlotOffset(
        FormationCapabilityShowcaseFormationSlotConfig slots,
        int slotIndex)
    {
        if (slots.LayoutKind == FormationCapabilityShowcaseFormationSlotLayout.Grid)
        {
            FormationCapabilityShowcaseFormationGridSlotConfig grid = slots.RequiredGrid;
            int row = slotIndex / grid.Columns;
            int col = slotIndex % grid.Columns;
            float colCenter = (grid.Columns - 1) * 0.5f;
            float rowCenter = (grid.Rows - 1) * 0.5f;
            return new Vector2(
                (col - colCenter) * grid.SpacingXCm,
                (row - rowCenter) * grid.SpacingYCm);
        }

        if (slotIndex == 0)
        {
            return Vector2.Zero;
        }

        float radius = MathF.Sqrt(slotIndex) * slots.RequiredDisc.RingSpacingCm;
        float angle = slotIndex * DiscSlotGoldenAngleRadians;
        return new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
    }

    private void EnsureSoldierEntityCache()
    {
        if (_soldierEntitiesByPlanIndex.Length != _soldierAgentPlans.Length)
        {
            _soldierEntitiesByPlanIndex = new Entity[_soldierAgentPlans.Length];
        }

        Array.Fill(_soldierEntitiesByPlanIndex, Entity.Null);
    }

    private void EnqueueFormationAgentSpawns(
        GameEngine engine,
        FormationCapabilityShowcaseConfig config,
        RuntimeEntitySpawnQueue spawnQueue,
        MapId mapId,
        TeamEntityLookup teamLookup,
        PlayerEntityLookup playerLookup)
    {
        if (_formationEntities.Length != _formationPlans.Length)
        {
            _formationEntities = new Entity[_formationPlans.Length];
        }

        Array.Fill(_formationEntities, Entity.Null);
        for (int i = 0; i < _formationPlans.Length; i++)
        {
            FormationCapabilityShowcaseFormationPlan plan = _formationPlans[i];
            FormationCapabilityShowcaseFormationConfig formation = config.Formations[plan.FormationIndex];
            Entity teamDomain = RequireLiveTeamDomain(engine, teamLookup, plan.TeamId);
            bool hasOwnershipSource = playerLookup.TryGet(formation.OwnerPlayerId, out Entity ownershipSource) &&
                engine.World.IsAlive(ownershipSource);
            var request = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = config.FormationAgent.TemplateId,
                MapId = mapId,
                WorldPositionCm = Fix64Vec2.FromInt((int)MathF.Round(plan.InitialCenterXCm), (int)MathF.Round(plan.InitialCenterYCm)),
                HasWorldPosition = 1,
                FacingAngleRad = plan.FacingRad,
                HasFacing = 1,
                OwnershipSource = ownershipSource,
                HasOwnershipSource = hasOwnershipSource ? (byte)1 : (byte)0,
                MembershipTarget = teamDomain,
                HasMembershipTarget = 1,
                ComponentPatches = CreateFormationAnchorPatch(
                    plan.FormationIndex,
                    plan.SoldierCount,
                    config.TargetChangeEpsilonCm,
                    config.FacingChangeEpsilonRadians),
            };

            if (!spawnQueue.TryEnqueue(in request))
            {
                throw new InvalidOperationException("Formation Capability showcase failed to enqueue formation agent spawn request.");
            }
        }
    }

    private static Entity RequireLiveTeamDomain(GameEngine engine, TeamEntityLookup teamLookup, int teamId)
    {
        if (!teamLookup.TryGet(teamId, out Entity teamDomain) || !engine.World.IsAlive(teamDomain))
        {
            throw new InvalidOperationException(
                $"Formation Capability showcase requires a live relationship representative for team {teamId}.");
        }

        return teamDomain;
    }

    private static RuntimeEntitySpawnComponentPatch[] CreateFormationAnchorPatch(
        int formationIndex,
        int slotCount,
        float targetChangeEpsilonCm,
        float facingChangeEpsilonRadians)
    {
        return
        [
            new RuntimeEntitySpawnComponentPatch(
                "FormationCapabilityShowcaseFormationAgent",
                new JsonObject
                {
                    ["FormationIndex"] = formationIndex,
                    ["SlotCount"] = slotCount,
                    ["TargetChangeEpsilonCm"] = targetChangeEpsilonCm,
                    ["FacingChangeEpsilonRadians"] = facingChangeEpsilonRadians,
                }),
        ];
    }

    private static RuntimeEntitySpawnComponentPatch[] CreateFormationFollowerPatch(
        int formationIndex,
        int slotIndex,
        float localOffsetXCm,
        float localOffsetYCm)
    {
        return
        [
            new RuntimeEntitySpawnComponentPatch(
                "FormationCapabilityShowcaseFormationSoldier",
                new JsonObject
                {
                    ["FormationIndex"] = formationIndex,
                    ["SlotIndex"] = slotIndex,
                    ["LocalOffsetXCm"] = localOffsetXCm,
                    ["LocalOffsetYCm"] = localOffsetYCm,
                }),
        ];
    }

    private void EnqueueObstacleOverlaySpawns(
        GameEngine engine,
        FormationCapabilityShowcaseConfig config,
        RuntimeEntitySpawnQueue spawnQueue,
        MapId mapId)
    {
        if (_obstacleOverlayEntities.Length != _obstacleOverlayPlans.Length)
        {
            _obstacleOverlayEntities = new Entity[_obstacleOverlayPlans.Length];
        }

        Array.Fill(_obstacleOverlayEntities, Entity.Null);
        ValidateTemplate(engine, config.ObstacleOverlay.TemplateId);
        for (int i = 0; i < _obstacleOverlayPlans.Length; i++)
        {
            FormationCapabilityShowcaseObstacleOverlayPlan plan = _obstacleOverlayPlans[i];
            var request = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = config.ObstacleOverlay.TemplateId,
                MapId = mapId,
                WorldPositionCm = Fix64Vec2.FromInt((int)MathF.Round(plan.WorldXCm), (int)MathF.Round(plan.WorldYCm)),
                HasWorldPosition = 1,
            };

            if (!spawnQueue.TryEnqueue(in request))
            {
                throw new InvalidOperationException("Formation Capability showcase failed to enqueue obstacle overlay spawn request.");
            }
        }
    }

    private int ResolveSoldierPlanIndex(int formationIndex, int slotIndex)
    {
        if ((uint)formationIndex >= (uint)_formationPlans.Length)
        {
            throw new InvalidOperationException(
                $"Formation Capability showcase formation index {formationIndex} exceeds planned formation count {_formationPlans.Length}.");
        }

        FormationCapabilityShowcaseFormationPlan plan = _formationPlans[formationIndex];
        if ((uint)slotIndex >= (uint)plan.SoldierCount)
        {
            throw new InvalidOperationException(
                $"Formation Capability showcase soldier slot index {slotIndex} exceeds formation '{plan.Id}' soldier count {plan.SoldierCount}.");
        }

        return plan.FirstSoldierPlanIndex + slotIndex;
    }

    private void ClearFormationExecutionTargets(GameEngine engine)
    {
        if (_movePlanExecutionSink == null)
        {
            return;
        }

        for (int i = 0; i < _formationEntities.Length; i++)
        {
            Entity entity = _formationEntities[i];
            if (engine.World.IsAlive(entity))
            {
                _movePlanExecutionSink.Clear(engine.World, entity);
            }
        }

        for (int i = 0; i < _soldierEntitiesByPlanIndex.Length; i++)
        {
            Entity entity = _soldierEntitiesByPlanIndex[i];
            if (engine.World.IsAlive(entity))
            {
                _movePlanExecutionSink.Clear(engine.World, entity);
            }
        }
    }

    private void ApplyFormationExecutionTargets(GameEngine engine, MassNavigationSimulationRuntime simulation)
    {
        MassNavigationMovePlanExecutionSink sink = _movePlanExecutionSink
            ?? throw new InvalidOperationException("Formation Capability showcase requires a MovePlanning execution adapter.");
        if (_formationPlans.Length == 0 || _soldierAgentPlans.Length == 0)
        {
            return;
        }

        if (!_executionBindingReady)
        {
            for (int i = 0; i < _formationEntities.Length; i++)
            {
                Entity entity = _formationEntities[i];
                if (!engine.World.IsAlive(entity) ||
                    engine.World.Has<PresentationDestroyPending>(entity))
                {
                    return;
                }
            }

            for (int i = 0; i < _soldierEntitiesByPlanIndex.Length; i++)
            {
                Entity entity = _soldierEntitiesByPlanIndex[i];
                if (!engine.World.IsAlive(entity) ||
                    engine.World.Has<PresentationDestroyPending>(entity))
                {
                    return;
                }
            }

            _executionBindingReady = true;
        }

        Array.Clear(_anchorSeenByFormation);
        Array.Clear(_soldierSeenByPlan);
        Array.Clear(_aliveSoldierCountByFormation);
        Array.Clear(_targetChangedByFormation);

        foreach (ref var chunk in engine.World.Query(in FormationExecutionAnchorQuery))
        {
            Span<FormationCapabilityShowcaseFormationAgent> agents = chunk.GetSpan<FormationCapabilityShowcaseFormationAgent>();
            Span<FormationCapabilityShowcaseCommandState> commands = chunk.GetSpan<FormationCapabilityShowcaseCommandState>();
            Span<MassNavigationAgentIndex> agentIndices = chunk.GetSpan<MassNavigationAgentIndex>();
            foreach (int index in chunk)
            {
                int formationIndex = agents[index].FormationIndex;
                if ((uint)formationIndex >= (uint)_formationPlans.Length)
                {
                    throw new InvalidOperationException(
                        $"Formation Capability execution references formation index {formationIndex}, exceeding configured formations {_formationPlans.Length}.");
                }

                if (_anchorSeenByFormation[formationIndex] != 0)
                {
                    throw new InvalidOperationException(
                        $"Formation Capability formation index {formationIndex} has more than one live anchor.");
                }

                if (agents[index].SlotCount != _formationPlans[formationIndex].SoldierCount ||
                    !(agents[index].TargetChangeEpsilonCm > 0f) ||
                    !(agents[index].FacingChangeEpsilonRadians > 0f) ||
                    commands[index].HasMoveTarget == 0)
                {
                    throw new InvalidOperationException(
                        $"Formation Capability formation index {formationIndex} has invalid execution authoring.");
                }

                _anchorSeenByFormation[formationIndex] = 1;
                _commandByFormation[formationIndex] = commands[index];
                _agentConfigByFormation[formationIndex] = agents[index];
                _anchorCenterByFormation[formationIndex] = simulation.GetAgentWorldPositionCm(agentIndices[index].Value);
            }
        }

        foreach (ref var chunk in engine.World.Query(in FormationExecutionSoldierQuery))
        {
            Span<FormationCapabilityShowcaseFormationSoldier> soldiers = chunk.GetSpan<FormationCapabilityShowcaseFormationSoldier>();
            foreach (int index in chunk)
            {
                ref readonly FormationCapabilityShowcaseFormationSoldier soldier = ref soldiers[index];
                int planIndex = ResolveSoldierPlanIndex(soldier.FormationIndex, soldier.SlotIndex);
                if ((uint)planIndex >= (uint)_soldierAgentPlans.Length)
                {
                    throw new InvalidOperationException(
                        $"Formation Capability soldier slot {soldier.SlotIndex} exceeds formation {soldier.FormationIndex} plan range.");
                }

                if (_soldierSeenByPlan[planIndex] != 0)
                {
                    throw new InvalidOperationException(
                        $"Formation Capability soldier plan index {planIndex} is bound more than once.");
                }

                FormationCapabilityShowcaseSoldierAgentSpawnPlan plan = _soldierAgentPlans[planIndex];
                if (plan.FormationIndex != soldier.FormationIndex ||
                    plan.SlotIndex != soldier.SlotIndex ||
                    plan.SlotOffsetXCm != soldier.LocalOffsetXCm ||
                    plan.SlotOffsetYCm != soldier.LocalOffsetYCm)
                {
                    throw new InvalidOperationException(
                        $"Formation Capability soldier plan index {planIndex} does not match authored slot data.");
                }

                _soldierSeenByPlan[planIndex] = 1;
                _aliveSoldierCountByFormation[soldier.FormationIndex]++;
            }
        }

        for (int formationIndex = 0; formationIndex < _formationPlans.Length; formationIndex++)
        {
            if (_anchorSeenByFormation[formationIndex] == 0)
            {
                Entity cachedAnchor = _formationEntities[formationIndex];
                if (!engine.World.IsAlive(cachedAnchor) ||
                    engine.World.Has<PresentationDestroyPending>(cachedAnchor) ||
                    engine.World.Has<SuspendedTag>(cachedAnchor))
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"Formation Capability formation index {formationIndex} has a live anchor that is missing required execution components.");
            }

            Vector2 center = _anchorCenterByFormation[formationIndex];
            FormationCapabilityShowcaseCommandState command = _commandByFormation[formationIndex];
            FormationCapabilityShowcaseFormationAgent agent = _agentConfigByFormation[formationIndex];
            float dx = center.X - _lastTargetCenterXByFormation[formationIndex];
            float dy = center.Y - _lastTargetCenterYByFormation[formationIndex];
            float facingDelta = MathF.Abs(NormalizeFacingRadians(
                command.TargetFacingRad - _lastTargetFacingByFormation[formationIndex]));
            if (_targetSnapshotInitializedByFormation[formationIndex] == 0 ||
                (dx * dx) + (dy * dy) >= agent.TargetChangeEpsilonCm * agent.TargetChangeEpsilonCm ||
                facingDelta >= agent.FacingChangeEpsilonRadians)
            {
                _targetChangedByFormation[formationIndex] = 1;
            }
        }

        float anchorStopRadius = simulation.Config.Semantics.Group.UnitTargetStopThresholdCm;
        foreach (ref var chunk in engine.World.Query(in FormationExecutionAnchorQuery))
        {
            Span<FormationCapabilityShowcaseFormationAgent> agents = chunk.GetSpan<FormationCapabilityShowcaseFormationAgent>();
            Span<FormationCapabilityShowcaseCommandState> commands = chunk.GetSpan<FormationCapabilityShowcaseCommandState>();
            Span<FormationCapabilityShowcaseFormationState> states = chunk.GetSpan<FormationCapabilityShowcaseFormationState>();
            Span<FacingDirection> facings = chunk.GetSpan<FacingDirection>();
            Span<WorldPositionCm> worldPositions = chunk.GetSpan<WorldPositionCm>();
            ref Entity entityFirst = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                int formationIndex = agents[index].FormationIndex;
                FormationCapabilityShowcaseCommandState command = commands[index];
                var intent = new MovePlanExecutionIntent
                {
                    TargetWorldCm = new Vector2(command.TargetCenterXCm, command.TargetCenterYCm),
                    StopRadiusCm = anchorStopRadius,
                    HasTarget = 1,
                };
                if (!sink.TryApply(engine.World, Unsafe.Add(ref entityFirst, index), in intent))
                {
                    throw new InvalidOperationException(
                        $"Formation Capability failed to apply anchor target for formation {formationIndex}.");
                }

                facings[index].AngleRad = command.TargetFacingRad;
                Vector2 center = _anchorCenterByFormation[formationIndex];
                ref FormationCapabilityShowcaseFormationState state = ref states[index];
                state.SoldierCount = _formationPlans[formationIndex].SoldierCount;
                state.AliveSoldierCount = _aliveSoldierCountByFormation[formationIndex];
                state.CenterXCm = center.X;
                state.CenterYCm = center.Y;
                state.FacingRad = command.TargetFacingRad;
                worldPositions[index].Value = Fix64Vec2.FromInt(
                    (int)MathF.Round(center.X),
                    (int)MathF.Round(center.Y));
            }
        }

        float memberStopRadius = simulation.Config.Semantics.Group.UnitTargetStopThresholdCm;
        float minimumClearance = simulation.Config.Semantics.TargetProjection.GroupSlotClearanceCm;
        foreach (ref var chunk in engine.World.Query(in FormationExecutionSoldierQuery))
        {
            Span<FormationCapabilityShowcaseFormationSoldier> soldiers = chunk.GetSpan<FormationCapabilityShowcaseFormationSoldier>();
            Span<FacingDirection> facings = chunk.GetSpan<FacingDirection>();
            ref Entity entityFirst = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                ref readonly FormationCapabilityShowcaseFormationSoldier soldier = ref soldiers[index];
                int formationIndex = soldier.FormationIndex;
                if (_targetChangedByFormation[formationIndex] == 0)
                {
                    continue;
                }

                float facing = _commandByFormation[formationIndex].TargetFacingRad;
                float forwardX = MathF.Cos(facing);
                float forwardY = MathF.Sin(facing);
                float lateralX = -forwardY;
                float lateralY = forwardX;
                var offsetWorld = new Vector2(
                    (lateralX * soldier.LocalOffsetXCm) + (forwardX * soldier.LocalOffsetYCm),
                    (lateralY * soldier.LocalOffsetXCm) + (forwardY * soldier.LocalOffsetYCm));
                var intent = new MovePlanExecutionIntent
                {
                    TargetWorldCm = _anchorCenterByFormation[formationIndex] + offsetWorld,
                    ProjectionHintWorldCm = offsetWorld,
                    StopRadiusCm = memberStopRadius,
                    MinimumClearanceCm = minimumClearance,
                    HasTarget = 1,
                    ResolveNavigableTarget = 1,
                };
                if (!sink.TryApply(engine.World, Unsafe.Add(ref entityFirst, index), in intent))
                {
                    throw new InvalidOperationException(
                        $"Formation Capability failed to apply member target for formation {formationIndex}, slot {soldier.SlotIndex}.");
                }

                facings[index].AngleRad = facing;
            }
        }

        for (int formationIndex = 0; formationIndex < _formationPlans.Length; formationIndex++)
        {
            if (_targetChangedByFormation[formationIndex] == 0)
            {
                continue;
            }

            _lastTargetCenterXByFormation[formationIndex] = _anchorCenterByFormation[formationIndex].X;
            _lastTargetCenterYByFormation[formationIndex] = _anchorCenterByFormation[formationIndex].Y;
            _lastTargetFacingByFormation[formationIndex] = _commandByFormation[formationIndex].TargetFacingRad;
            _targetSnapshotInitializedByFormation[formationIndex] = 1;
        }
    }

    private static float NormalizeFacingRadians(float angle)
    {
        while (angle > MathF.PI)
        {
            angle -= MathF.Tau;
        }

        while (angle < -MathF.PI)
        {
            angle += MathF.Tau;
        }

        return angle;
    }

    private void TryApplyInitialCommandSource(GameEngine engine, FormationCapabilityShowcaseConfig config)
    {
        if (_initialCommandSourceApplied)
        {
            return;
        }

        EntityCollectionStore collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("Formation Capability showcase requires EntityCollectionStore before applying configured initial command source.");
        if (!TryResolveLocalCommandSourceOwner(engine, out Entity owner))
        {
            return;
        }

        int formationIndex = ResolveFormationIndex(config.InitialCommandSourceFormationId);
        EnsureInitialCommandSourceScratch(config);

        if ((uint)formationIndex >= (uint)_formationEntities.Length)
        {
            throw new InvalidOperationException(
                $"Formation Capability showcase initial command-source formation index {formationIndex} exceeds bound formation entity cache {_formationEntities.Length}.");
        }

        Entity formation = _formationEntities[formationIndex];
        if (!engine.World.IsAlive(formation))
        {
            return;
        }

        if (!CommandSourceEligibility.CanAcquire(engine.World, engine.GlobalContext, owner, formation, default))
        {
            throw new InvalidOperationException(
                $"Formation Capability showcase initial command source formation '{config.InitialCommandSourceFormationId}' must be command-source acquireable.");
        }

        _initialCommandSourceScratch[0] = formation;
        var descriptor = EntityCollectionDescriptor.Create(
            EntityCollectionKeys.CommandSource,
            EntityCollectionSourceKind.Explicit,
            EntityCollectionRoleKind.CommandSource,
            owner,
            formation,
            "Formation capability command source",
            "Configured initial formation command source.");
        collections.Replace(owner, descriptor, _initialCommandSourceScratch.AsSpan(0, 1), owner);

        _initialCommandSourceApplied = true;
    }

    private void EnsureInitialCommandSourceScratch(FormationCapabilityShowcaseConfig config)
    {
        if (_initialCommandSourceScratch.Length == config.InitialCommandSourceEntityCapacity)
        {
            return;
        }

        _initialCommandSourceScratch = new Entity[config.InitialCommandSourceEntityCapacity];
    }

    private int ResolveFormationIndex(string formationId)
    {
        for (int i = 0; i < _formationPlans.Length; i++)
        {
            if (string.Equals(_formationPlans[i].Id, formationId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new InvalidOperationException($"Formation Capability showcase formation '{formationId}' was not planned.");
    }

    private void DestroyShowcaseOwnedEntities(GameEngine engine)
    {
        for (int i = 0; i < _formationEntities.Length; i++)
        {
            Entity entity = _formationEntities[i];
            if (engine.World.IsAlive(entity))
            {
                MarkPresentationDestroyPending(engine.World, entity, "formation agent");
            }

            _formationEntities[i] = Entity.Null;
        }

        for (int i = 0; i < _soldierEntitiesByPlanIndex.Length; i++)
        {
            Entity entity = _soldierEntitiesByPlanIndex[i];
            if (engine.World.IsAlive(entity))
            {
                MarkPresentationDestroyPending(engine.World, entity, "soldier agent");
            }

            _soldierEntitiesByPlanIndex[i] = Entity.Null;
        }

        for (int i = 0; i < _obstacleOverlayEntities.Length; i++)
        {
            Entity entity = _obstacleOverlayEntities[i];
            if (engine.World.IsAlive(entity))
            {
                MarkPresentationDestroyPending(engine.World, entity, "obstacle overlay");
            }

            _obstacleOverlayEntities[i] = Entity.Null;
        }
    }

    private void ClearFormationCaches()
    {
        _executionBindingReady = false;
        if (_soldierEntitiesByPlanIndex.Length > 0)
        {
            Array.Fill(_soldierEntitiesByPlanIndex, Entity.Null);
        }

        if (_formationEntities.Length > 0)
        {
            Array.Fill(_formationEntities, Entity.Null);
        }

        if (_obstacleOverlayEntities.Length > 0)
        {
            Array.Fill(_obstacleOverlayEntities, Entity.Null);
        }

        if (_targetSnapshotInitializedByFormation.Length > 0)
        {
            Array.Clear(_targetSnapshotInitializedByFormation);
        }

    }

    private static void ConfigureScenarioTeams(MassNavigationSimulationRuntime simulation, FormationCapabilityShowcaseConfig config)
    {
        MassNavigationScenarioTeamConfig[] configuredTeams = simulation.Config.Scenario.Teams;
        int[] teamIds = new int[configuredTeams.Length];
        for (int i = 0; i < configuredTeams.Length; i++)
        {
            teamIds[i] = configuredTeams[i].Id;
        }

        simulation.ConfigureScenarioTeams(teamIds);
    }

    private static void ConfigureRelationships(MassNavigationConfig config)
    {
        TeamManager.LoadConfig(config.TeamRelationships);
    }

    private static MassNavigationScenarioTeamConfig ResolveScenarioTeam(MassNavigationScenarioConfig scenario, int teamId)
    {
        for (int i = 0; i < scenario.Teams.Length; i++)
        {
            if (scenario.Teams[i].Id == teamId)
            {
                return scenario.Teams[i];
            }
        }

        throw new InvalidOperationException($"Formation Capability showcase formation references MassNavigation scenario team {teamId}, but that team is not configured.");
    }

    private static MassNavigationSimulationRuntime RequireSimulation(GameEngine engine)
    {
        return engine.GetService(MassNavigationKeys.RuntimeBinding)?.RequireCurrent()
            ?? throw new InvalidOperationException("Formation Capability showcase requires a prepared MassNavigation runtime binding.");
    }

    private static MapId RequireCurrentMapId(GameEngine engine, string configuredMapId)
    {
        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("Formation Capability showcase requires an active map session before scenario bootstrap.");
        if (!string.Equals(session.MapId.Value, configuredMapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Formation Capability showcase scenario bootstrap requires active map '{configuredMapId}', got '{session.MapId.Value}'.");
        }

        return session.MapId;
    }

    private static void ValidateAuthoring(GameEngine engine, FormationCapabilityShowcaseConfig config)
    {
        ValidateTemplate(engine, config.FormationAgent.TemplateId);
        for (int i = 0; i < config.Formations.Length; i++)
        {
            ValidateTemplate(engine, config.Formations[i].SoldierAgent.TemplateId);
        }
    }

    private static void ValidateTemplate(GameEngine engine, string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new InvalidOperationException("Formation Capability showcase template id must be non-empty.");
        }

        if (!engine.MapLoader.TemplateRegistry.Contains(templateId))
        {
            throw new InvalidOperationException($"Formation Capability showcase requires configured entity template '{templateId}'.");
        }

        EntityTemplateKeyRegistry templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("Formation Capability showcase requires EntityTemplateKeyRegistry.");
        if (!templateKeys.TryGetId(templateId, out int templateKeyId) || templateKeyId <= 0)
        {
            throw new InvalidOperationException($"Formation Capability showcase template '{templateId}' was not registered in EntityTemplateKeyRegistry.");
        }
    }

    private static void MarkPresentationDestroyPending(World world, Entity entity, string label)
    {
        PresentationEntityLifecycle.RequestDestroy(world, entity, $"Formation Capability showcase {label}");
    }

    private static void UpsertComponent<T>(World world, Entity entity, T component)
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

    private static void ClearCommandSource(GameEngine engine)
    {
        EntityCollectionStore collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("Formation Capability showcase requires EntityCollectionStore before clearing command source.");
        if (TryResolveLocalCommandSourceOwner(engine, out Entity owner))
        {
            collections.Remove(owner, EntityCollectionKeys.CommandSource);
        }
    }

    private static Entity ResolveLocalCommandSourceOwner(GameEngine engine)
    {
        return TryResolveLocalCommandSourceOwner(engine, out Entity owner)
            ? owner
            : throw new InvalidOperationException("Formation Capability showcase requires LocalPlayerEntity before mutating command source.");
    }

    private static bool TryResolveLocalCommandSourceOwner(GameEngine engine, out Entity owner)
    {
        if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localPlayerObj) ||
            localPlayerObj is not Entity local ||
            !engine.World.IsAlive(local))
        {
            int playerId = ResolveLocalPlayerId(engine);
            PlayerEntityLookup lookup = engine.GetService(CoreServiceKeys.PlayerEntityLookup)
                ?? throw new InvalidOperationException("Formation Capability showcase requires PlayerEntityLookup before resolving command source owner.");
            if (playerId <= 0 ||
                !lookup.TryGet(playerId, out local) ||
                local == Entity.Null ||
                !engine.World.IsAlive(local))
            {
                owner = Entity.Null;
                return false;
            }

            engine.SetService(CoreServiceKeys.LocalPlayerEntity, local);
        }

        owner = local;
        return true;
    }

    private static int ResolveLocalPlayerId(GameEngine engine)
    {
        if (engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerId.Name, out object? playerIdObj) &&
            playerIdObj is int playerId &&
            playerId > 0)
        {
            return playerId;
        }

        return engine.MergedConfig?.StartupLocalPlayerId ?? 0;
    }

    internal sealed class FormationCapabilityCommandSourceRotateSystem : Arch.System.ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly FormationCapabilityShowcaseRuntime _owner;
        private readonly OrderQueue _orders;
        private readonly Entity[] _actorsScratch;
        private readonly Order[] _ordersScratch;
        private ControlDomainQuery? _controlDomains;
        private int _rotateOrderTypeId;

        public FormationCapabilityCommandSourceRotateSystem(
            GameEngine engine,
            FormationCapabilityShowcaseRuntime owner,
            OrderQueue orders,
            int actorCapacity)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _orders = orders ?? throw new ArgumentNullException(nameof(orders));
            if (actorCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(actorCapacity));
            }

            _actorsScratch = new Entity[actorCapacity];
            _ordersScratch = new Order[actorCapacity];
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (!_owner.IsCurrentShowcaseMap(_engine) ||
                !MassNavigationIds.IsCurrentNavigationRuntimeReady(_engine) ||
                _engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader input)
            {
                return;
            }

            float deltaRadians = 0f;
            if (input.PressedThisFrame(RotateLeftActionId))
            {
                deltaRadians -= RotateStepRadians;
            }

            if (input.PressedThisFrame(RotateRightActionId))
            {
                deltaRadians += RotateStepRadians;
            }

            if (!(MathF.Abs(deltaRadians) > 0f))
            {
                return;
            }

            Entity commandSourceOwner = ResolveLocalCommandSourceOwner(_engine);
            int commandActorCount = commandSourceOwner != Entity.Null
                ? EntityCollectionContextRuntime.GetCount(
                    _engine.GlobalContext,
                    commandSourceOwner,
                    EntityCollectionKeys.CommandSource)
                : 0;
            if (commandActorCount <= 0)
            {
                _owner._rotateOrderRejectCount++;
                return;
            }

            if (commandActorCount > _actorsScratch.Length)
            {
                throw new InvalidOperationException(
                    $"Formation Capability rotate order requires {commandActorCount} actors, exceeding configured command actor capacity {_actorsScratch.Length}.");
            }

            int actorCount = commandActorCount > 0
                ? EntityCollectionContextRuntime.Copy(
                    _engine.GlobalContext,
                    commandSourceOwner,
                    EntityCollectionKeys.CommandSource,
                    _actorsScratch)
                : 0;
            if (actorCount != commandActorCount)
            {
                throw new InvalidOperationException(
                    $"Formation Capability rotate command-source snapshot changed while copying {commandActorCount} actors; copied {actorCount}.");
            }

            if (actorCount > _orders.AvailableCapacity)
            {
                throw new InvalidOperationException(
                    $"Formation Capability rotate order requires {actorCount} queue entries, but only {_orders.AvailableCapacity} are available.");
            }

            if (!TryBuildOrders(actorCount, deltaRadians))
            {
                _owner._rotateOrderRejectCount++;
                return;
            }

            if (!_orders.TryEnqueueBatch(_ordersScratch.AsSpan(0, actorCount)))
            {
                throw new InvalidOperationException(
                    $"Formation Capability rotate order batch of {actorCount} actors failed after capacity preflight.");
            }
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private bool TryBuildOrders(int actorCount, float deltaRadians)
        {
            if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) ||
                localObj is not Entity local ||
                !_engine.World.IsAlive(local) ||
                !_engine.World.TryGet(local, out PlayerIdentity localIdentity))
            {
                throw new InvalidOperationException(
                    "Formation Capability rotate orders require a live local player entity with PlayerIdentity.");
            }

            _controlDomains ??= _engine.GetService(CoreServiceKeys.ControlDomainQuery)
                ?? throw new InvalidOperationException("Formation Capability rotate orders require ControlDomainQuery.");
            if (_rotateOrderTypeId == 0)
            {
                OrderTypeRegistry orderTypes = _engine.GetService(CoreServiceKeys.OrderTypeRegistry)
                    ?? throw new InvalidOperationException("Formation Capability rotate orders require OrderTypeRegistry.");
                if (!orderTypes.TryGetId(FormationCapabilityShowcaseOrderKeys.Rotate, out _rotateOrderTypeId))
                {
                    throw new InvalidOperationException(
                        $"Formation Capability rotate orders require '{FormationCapabilityShowcaseOrderKeys.Rotate}'.");
                }
            }

            for (int i = 0; i < actorCount; i++)
            {
                Entity actor = _actorsScratch[i];
                if (!_engine.World.IsAlive(actor) ||
                    _engine.World.Has<PresentationDestroyPending>(actor) ||
                    !_engine.World.Has<OrderBuffer>(actor) ||
                    !_engine.World.TryGet(actor, out FormationCapabilityShowcaseCommandState command))
                {
                    throw new InvalidOperationException(
                        $"Formation Capability rotate command-source actor {actor.Id} is not a live executable formation.");
                }

                if (!_controlDomains.TryResolveControlDomain(actor, out Entity domain) || domain != local)
                {
                    return false;
                }

                OrderArgs args = default;
                args.F0 = NormalizeFacingRadians(command.TargetFacingRad + deltaRadians);
                _ordersScratch[i] = new Order
                {
                    OrderTypeId = _rotateOrderTypeId,
                    PlayerId = localIdentity.PlayerId,
                    Actor = actor,
                    SubmitMode = OrderSubmitMode.Immediate,
                    Args = args,
                };
            }

            return true;
        }
    }

    internal sealed class FormationCapabilityOrderSystem : Arch.System.ISystem<float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<FormationCapabilityShowcaseFormationAgent, FormationCapabilityShowcaseCommandState, OrderBuffer, WorldPositionCm>()
            .WithNone<PresentationDestroyPending, SuspendedTag>();

        private readonly GameEngine _engine;
        private readonly FormationCapabilityShowcaseRuntime _owner;
        private readonly Entity[] _completedEntities;
        private readonly int[] _moveBatchPlayerIds;
        private readonly int[] _moveBatchSubmitSteps;
        private readonly OrderSubmitMode[] _moveBatchSubmitModes;
        private readonly float[] _moveBatchTargetXCm;
        private readonly float[] _moveBatchTargetYCm;
        private readonly float[] _moveBatchPositionSumXCm;
        private readonly float[] _moveBatchPositionSumYCm;
        private readonly int[] _moveBatchActorCounts;
        private int _moveBatchCount;
        private int _moveOrderTypeId;
        private int _rotateOrderTypeId;

        public FormationCapabilityOrderSystem(
            GameEngine engine,
            FormationCapabilityShowcaseRuntime owner,
            int capacity)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _completedEntities = new Entity[capacity];
            _moveBatchPlayerIds = new int[capacity];
            _moveBatchSubmitSteps = new int[capacity];
            _moveBatchSubmitModes = new OrderSubmitMode[capacity];
            _moveBatchTargetXCm = new float[capacity];
            _moveBatchTargetYCm = new float[capacity];
            _moveBatchPositionSumXCm = new float[capacity];
            _moveBatchPositionSumYCm = new float[capacity];
            _moveBatchActorCounts = new int[capacity];
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (!_owner.IsCurrentShowcaseMap(_engine))
            {
                return;
            }

            ResolveOrderTypes();
            int pendingCount = CountPendingOrders();
            if (pendingCount <= 0)
            {
                return;
            }

            if (pendingCount > _completedEntities.Length)
            {
                throw new InvalidOperationException(
                    $"Formation Capability order processing requires {pendingCount} entries, exceeding configured capacity {_completedEntities.Length}.");
            }

            ValidatePendingOrders();
            BuildMoveBatches();
            int completedCount = 0;
            foreach (ref var chunk in _engine.World.Query(in Query))
            {
                Span<FormationCapabilityShowcaseCommandState> commands = chunk.GetSpan<FormationCapabilityShowcaseCommandState>();
                Span<OrderBuffer> buffers = chunk.GetSpan<OrderBuffer>();
                Span<WorldPositionCm> worldPositions = chunk.GetSpan<WorldPositionCm>();
                ref Entity entityFirst = ref chunk.Entity(0);
                foreach (int index in chunk)
                {
                    ref OrderBuffer buffer = ref buffers[index];
                    if (!buffer.HasActive)
                    {
                        continue;
                    }

                    ref readonly Order order = ref buffer.ActiveOrder.Order;
                    if (order.OrderTypeId == _moveOrderTypeId)
                    {
                        int batchIndex = FindMoveBatch(in order);
                        int actorCount = _moveBatchActorCounts[batchIndex];
                        float targetXCm = order.Args.Spatial.WorldCm.X;
                        float targetYCm = order.Args.Spatial.WorldCm.Z;
                        if (actorCount > 1)
                        {
                            float centerXCm = _moveBatchPositionSumXCm[batchIndex] / actorCount;
                            float centerYCm = _moveBatchPositionSumYCm[batchIndex] / actorCount;
                            targetXCm += worldPositions[index].Value.X.ToFloat() - centerXCm;
                            targetYCm += worldPositions[index].Value.Y.ToFloat() - centerYCm;
                        }

                        commands[index].TargetCenterXCm = targetXCm;
                        commands[index].TargetCenterYCm = targetYCm;
                        commands[index].HasMoveTarget = 1;
                    }
                    else if (order.OrderTypeId == _rotateOrderTypeId)
                    {
                        commands[index].TargetFacingRad = NormalizeFacingRadians(order.Args.F0);
                    }
                    else
                    {
                        continue;
                    }

                    _completedEntities[completedCount++] = Unsafe.Add(ref entityFirst, index);
                }
            }

            OrderBufferSystem buffersSystem = _engine.GetService(CoreServiceKeys.OrderBufferSystem)
                ?? throw new InvalidOperationException("Formation Capability orders require OrderBufferSystem.");
            for (int i = 0; i < completedCount; i++)
            {
                buffersSystem.NotifyOrderComplete(_completedEntities[i]);
            }
        }

        private void BuildMoveBatches()
        {
            _moveBatchCount = 0;
            foreach (ref var chunk in _engine.World.Query(in Query))
            {
                Span<OrderBuffer> buffers = chunk.GetSpan<OrderBuffer>();
                Span<WorldPositionCm> worldPositions = chunk.GetSpan<WorldPositionCm>();
                foreach (int index in chunk)
                {
                    if (!buffers[index].HasActive)
                    {
                        continue;
                    }

                    ref readonly Order order = ref buffers[index].ActiveOrder.Order;
                    if (order.OrderTypeId != _moveOrderTypeId)
                    {
                        continue;
                    }

                    int batchIndex = FindMoveBatch(in order, allowCreate: true);
                    _moveBatchPositionSumXCm[batchIndex] += worldPositions[index].Value.X.ToFloat();
                    _moveBatchPositionSumYCm[batchIndex] += worldPositions[index].Value.Y.ToFloat();
                    _moveBatchActorCounts[batchIndex]++;
                }
            }
        }

        private int FindMoveBatch(in Order order, bool allowCreate = false)
        {
            float targetXCm = order.Args.Spatial.WorldCm.X;
            float targetYCm = order.Args.Spatial.WorldCm.Z;
            for (int i = 0; i < _moveBatchCount; i++)
            {
                if (_moveBatchPlayerIds[i] == order.PlayerId &&
                    _moveBatchSubmitSteps[i] == order.SubmitStep &&
                    _moveBatchSubmitModes[i] == order.SubmitMode &&
                    _moveBatchTargetXCm[i] == targetXCm &&
                    _moveBatchTargetYCm[i] == targetYCm)
                {
                    return i;
                }
            }

            if (!allowCreate)
            {
                throw new InvalidOperationException(
                    $"Formation move order {order.OrderId} was not included in the validated move batch snapshot.");
            }

            if (_moveBatchCount >= _moveBatchActorCounts.Length)
            {
                throw new InvalidOperationException(
                    $"Formation move batches exceed configured capacity {_moveBatchActorCounts.Length}.");
            }

            int batchIndex = _moveBatchCount++;
            _moveBatchPlayerIds[batchIndex] = order.PlayerId;
            _moveBatchSubmitSteps[batchIndex] = order.SubmitStep;
            _moveBatchSubmitModes[batchIndex] = order.SubmitMode;
            _moveBatchTargetXCm[batchIndex] = targetXCm;
            _moveBatchTargetYCm[batchIndex] = targetYCm;
            _moveBatchPositionSumXCm[batchIndex] = 0f;
            _moveBatchPositionSumYCm[batchIndex] = 0f;
            _moveBatchActorCounts[batchIndex] = 0;
            return batchIndex;
        }

        private void ValidatePendingOrders()
        {
            foreach (ref var chunk in _engine.World.Query(in Query))
            {
                Span<OrderBuffer> buffers = chunk.GetSpan<OrderBuffer>();
                foreach (int index in chunk)
                {
                    if (!buffers[index].HasActive)
                    {
                        continue;
                    }

                    ref readonly Order order = ref buffers[index].ActiveOrder.Order;
                    if (order.OrderTypeId == _moveOrderTypeId)
                    {
                        if (order.Args.Spatial.Kind != OrderSpatialKind.WorldCm ||
                            order.Args.Spatial.Mode != OrderCollectionMode.Single ||
                            !float.IsFinite(order.Args.Spatial.WorldCm.X) ||
                            !float.IsFinite(order.Args.Spatial.WorldCm.Y) ||
                            !float.IsFinite(order.Args.Spatial.WorldCm.Z) ||
                            order.Args.I0 != 0 ||
                            order.Args.I1 != 0 ||
                            order.Args.I2 != 0 ||
                            order.Args.I3 != 0 ||
                            order.Args.F0 != 0f ||
                            order.Args.F1 != 0f ||
                            order.Args.F2 != 0f ||
                            order.Args.F3 != 0f ||
                            order.Args.Spatial.A0 != 0 ||
                            order.Args.Spatial.A1 != 0 ||
                            order.Args.Spatial.A2 != 0 ||
                            order.Args.Spatial.PointCount != 0)
                        {
                            throw new InvalidOperationException(
                                $"Formation move order {order.OrderId} requires one WorldCm target and no extra payload.");
                        }
                    }
                    else if (order.OrderTypeId == _rotateOrderTypeId &&
                             (!float.IsFinite(order.Args.F0) ||
                              order.Args.Spatial.Kind != OrderSpatialKind.None ||
                              order.Args.Spatial.Mode != OrderCollectionMode.None ||
                              order.Args.I0 != 0 ||
                              order.Args.I1 != 0 ||
                              order.Args.I2 != 0 ||
                              order.Args.I3 != 0 ||
                              order.Args.F1 != 0f ||
                              order.Args.F2 != 0f ||
                              order.Args.F3 != 0f ||
                              order.Args.Spatial.A0 != 0 ||
                              order.Args.Spatial.A1 != 0 ||
                              order.Args.Spatial.A2 != 0 ||
                              order.Args.Spatial.PointCount != 0))
                    {
                        throw new InvalidOperationException(
                            $"Formation rotate order {order.OrderId} requires one finite target facing and no spatial or integer payload.");
                    }
                }
            }
        }

        private int CountPendingOrders()
        {
            int count = 0;
            foreach (ref var chunk in _engine.World.Query(in Query))
            {
                Span<OrderBuffer> buffers = chunk.GetSpan<OrderBuffer>();
                foreach (int index in chunk)
                {
                    if (buffers[index].HasActive &&
                        (buffers[index].ActiveOrder.Order.OrderTypeId == _moveOrderTypeId ||
                         buffers[index].ActiveOrder.Order.OrderTypeId == _rotateOrderTypeId))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private void ResolveOrderTypes()
        {
            if (_moveOrderTypeId != 0 && _rotateOrderTypeId != 0)
            {
                return;
            }

            OrderTypeRegistry orderTypes = _engine.GetService(CoreServiceKeys.OrderTypeRegistry)
                ?? throw new InvalidOperationException("Formation Capability orders require OrderTypeRegistry.");
            if (!orderTypes.TryGetId(FormationCapabilityShowcaseOrderKeys.Move, out _moveOrderTypeId) ||
                !orderTypes.TryGetId(FormationCapabilityShowcaseOrderKeys.Rotate, out _rotateOrderTypeId))
            {
                throw new InvalidOperationException(
                    $"Formation Capability requires '{FormationCapabilityShowcaseOrderKeys.Move}' and '{FormationCapabilityShowcaseOrderKeys.Rotate}' order types.");
            }
        }
    }

    private readonly struct FormationCapabilityShowcaseFormationPlan
    {
        public FormationCapabilityShowcaseFormationPlan(
            int formationIndex,
            string id,
            string label,
            int teamId,
            int firstSoldierPlanIndex,
            int soldierCount,
            float initialCenterXCm,
            float initialCenterYCm,
            float facingRad)
        {
            FormationIndex = formationIndex;
            Id = id;
            Label = label;
            TeamId = teamId;
            FirstSoldierPlanIndex = firstSoldierPlanIndex;
            SoldierCount = soldierCount;
            InitialCenterXCm = initialCenterXCm;
            InitialCenterYCm = initialCenterYCm;
            FacingRad = facingRad;
        }

        public int FormationIndex { get; }
        public string Id { get; }
        public string Label { get; }
        public int TeamId { get; }
        public int FirstSoldierPlanIndex { get; }
        public int SoldierCount { get; }
        public float InitialCenterXCm { get; }
        public float InitialCenterYCm { get; }
        public float FacingRad { get; }
    }

    private readonly struct FormationCapabilityShowcaseSoldierAgentSpawnPlan
    {
        public FormationCapabilityShowcaseSoldierAgentSpawnPlan(
            int formationIndex,
            int slotIndex,
            int teamId,
            string templateId,
            float worldXCm,
            float worldYCm,
            float facingRad,
            float slotOffsetXCm,
            float slotOffsetYCm)
        {
            FormationIndex = formationIndex;
            SlotIndex = slotIndex;
            TeamId = teamId;
            TemplateId = templateId;
            WorldXCm = worldXCm;
            WorldYCm = worldYCm;
            FacingRad = facingRad;
            SlotOffsetXCm = slotOffsetXCm;
            SlotOffsetYCm = slotOffsetYCm;
        }

        public int FormationIndex { get; }
        public int SlotIndex { get; }
        public int TeamId { get; }
        public string TemplateId { get; }
        public float WorldXCm { get; }
        public float WorldYCm { get; }
        public float FacingRad { get; }
        public float SlotOffsetXCm { get; }
        public float SlotOffsetYCm { get; }
    }

    private readonly struct FormationCapabilityShowcaseObstacleOverlayPlan
    {
        public FormationCapabilityShowcaseObstacleOverlayPlan(float worldXCm, float worldYCm, float radiusCm)
        {
            WorldXCm = worldXCm;
            WorldYCm = worldYCm;
            RadiusCm = radiusCm;
        }

        public float WorldXCm { get; }
        public float WorldYCm { get; }
        public float RadiusCm { get; }
    }

    private readonly record struct PendingFormationBinding(Entity Entity, int FormationIndex);

    private readonly record struct PendingSoldierBinding(Entity Entity, FormationCapabilityShowcaseFormationSoldier Soldier);

    private readonly record struct PendingObstacleOverlayBinding(Entity Entity, int OverlayIndex);
}
