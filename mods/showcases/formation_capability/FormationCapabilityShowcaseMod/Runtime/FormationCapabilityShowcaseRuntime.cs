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
using Ludots.Core.Gameplay.GAS.Orders;
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
    private const string RotateLeftActionId = "MassNavigation_RotateLeft";
    private const string RotateRightActionId = "MassNavigation_RotateRight";
    private const float RotateStepRadians = MathF.PI / 8f;

    private static readonly float DiscSlotGoldenAngleRadians = MathF.PI * (3f - MathF.Sqrt(5f));
    private static readonly QueryDescription FormationAnchorCandidateQuery = new QueryDescription()
        .WithAll<MassNavigationFormationAnchor, MassNavigationAgentIndex>()
        .WithNone<SuspendedTag>();
    private static readonly QueryDescription FormationFollowerCandidateQuery = new QueryDescription()
        .WithAll<MassNavigationFormationFollower, MassNavigationAgentIndex>()
        .WithNone<SuspendedTag>();
    private static readonly QueryDescription ObstacleOverlayCandidateQuery = new QueryDescription()
        .WithAll<EntityTemplateKeyRef, WorldPositionCm>()
        .WithNone<SuspendedTag>();

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
    private bool _systemsInstalled;
    private bool _scenarioSpawned;
    private bool _obstacleOverlaySpawnsQueued;
    private bool _initialCommandSourceApplied;
    private Entity _lastCommandSourceOwner = Entity.Null;
    private uint _lastCommandSourceRevision;
    private int _lastStructuralRevision = -1;
    private bool _lastHadCommandActors;
    private int _observedSceneResetCount;

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
        _obstacleOverlaySpawnsQueued = false;
        _initialCommandSourceApplied = false;
        RemovePendingScenarioSpawns(engine, config);
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
        MassNavigationRuntimeBinding runtimeBinding = engine.GetService(MassNavigationKeys.RuntimeBinding)
            ?? throw new InvalidOperationException("Formation Capability showcase requires MassNavigationRuntimeBinding before installing systems.");
        engine.RegisterSystem(
            new FormationCapabilityShowcaseScenarioBindingSystem(engine, this, runtimeBinding),
            SystemGroup.RuntimeEntityBinding);
        engine.InsertSystemBeforeRequired<MassNavigationPreSimulationStepSystem>(
            new FormationCapabilityShowcaseFormationRuntimeSystem(engine, this, runtimeBinding),
            SystemGroup.PostMovement);
        OrderQueue orders = engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new InvalidOperationException("Formation Capability showcase requires OrderQueue.");
        Ludots.Core.Modding.IModContext context = _context
            ?? throw new InvalidOperationException("Formation Capability showcase requires IModContext before installing local order source.");
        engine.RegisterSystem(
            new FormationCapabilityLocalOrderSourceSystem(engine, this, engine.World, engine.GlobalContext, orders, context, runtimeBinding),
            SystemGroup.InputCollection);
        engine.RegisterSystem(
            new FormationCapabilityCommandSourceRotateSystem(engine, this, runtimeBinding),
            SystemGroup.InputCollection);
        FormationCapabilityShowcaseConfig config = EnsureConfig(engine);
        engine.InsertPresentationSystemBefore<PerformerRuleSystem>(new FormationCapabilityShowcaseFormationOutlinePresentationSystem(engine, this, config));
        engine.InsertPresentationSystemBefore<PerformerRuleSystem>(new FormationCapabilityShowcaseObstacleOverlayPresentationSystem(engine, this, simulation.CaptureSolverRuntimeConfig().MaxObstacleCount));
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

        if (simulation.SceneResetCount != _observedSceneResetCount)
        {
            _observedSceneResetCount = simulation.SceneResetCount;
            _scenarioSpawned = false;
            _obstacleOverlaySpawnsQueued = false;
            _initialCommandSourceApplied = false;
            RemovePendingScenarioSpawns(engine, config);
            DestroyShowcaseOwnedEntities(engine);
            ClearFormationCaches();
        }

        if (!_scenarioSpawned)
        {
            SpawnScenario(engine, config);
            return;
        }

        SyncFormationStates(engine, simulation);
        PublishLocalFormationKnowledge(engine);
        TryApplyInitialCommandSource(engine, config);
        SyncCommandActors(engine, simulation);
    }

    private void SpawnScenario(GameEngine engine, FormationCapabilityShowcaseConfig config)
    {
        MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("Formation Capability showcase requires RuntimeEntitySpawnQueue.");

        ClearCommandActorSnapshot(engine);
        BuildAgentPlans(engine, simulation, config);
        DestroyShowcaseOwnedEntities(engine);

        int spawnRequestCount = _soldierAgentPlans.Length + _formationPlans.Length;
        if (spawnQueue.FreeCapacity < spawnRequestCount)
        {
            throw new InvalidOperationException(
                $"Formation Capability showcase requires RuntimeEntitySpawnQueue free capacity {spawnRequestCount}, actual {spawnQueue.FreeCapacity}.");
        }

        ConfigureRuntimeTeams(simulation, config);
        ConfigureRelationships(config);
        ValidateAuthoring(engine, config);

        TeamEntityLookup teamLookup = engine.GetService(CoreServiceKeys.TeamEntityLookup)
            ?? throw new InvalidOperationException("Formation Capability showcase requires TeamEntityLookup.");
        var registeredTeamIds = new HashSet<int>(_formationPlans.Length);
        for (int i = 0; i < _formationPlans.Length; i++)
        {
            FormationCapabilityShowcaseFormationConfig team = ResolveFormationTeam(config, _formationPlans[i].TeamId);
            if (!registeredTeamIds.Add(team.TeamId))
            {
                continue;
            }

            teamLookup.Register(
                team.TeamId,
                RelationshipTeamBootstrapper.EnsureTeamEntity(engine.World, teamLookup, team.TeamId, team.Label));
        }

        RemovePendingScenarioSpawns(engine, config);
        MapId mapId = RequireCurrentMapId(engine, config.MapId);
        EnqueueFormationAgentSpawns(engine, config, spawnQueue, mapId);
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
                TeamIdOverride = plan.TeamId,
                ComponentPatches = CreateFormationFollowerPatch(
                    _formationPlans[plan.FormationIndex].Id,
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
        _obstacleOverlaySpawnsQueued = false;
        _initialCommandSourceApplied = false;
        _observedSceneResetCount = simulation.SceneResetCount;
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

    private void RegisterSpawnedSoldier(Entity entity, in MassNavigationFormationFollower follower)
    {
        int formationIndex = ResolveFormationIndex(MassNavigationFormationRegistry.GetName(follower.FormationId));
        int planIndex = ResolveSoldierPlanIndex(formationIndex, follower.SlotIndex);
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
        UpsertComponent(engine.World, entity, new FormationCapabilityShowcaseFormationAgent { FormationIndex = plan.FormationIndex });
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
        UpsertComponent(engine.World, entity, new Team { Id = formation.TeamId });
        if (engine.World.Has<MassNavigationAgent>(entity) && engine.World.Has<MassNavigationAgentIndex>(entity))
        {
            MassNavigationAgentBinding.MarkDirty(engine.World, entity);
        }
        UpsertComponent(engine.World, entity, new PlayerOwner { PlayerId = formation.OwnerPlayerId });
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
            Span<MassNavigationFormationAnchor> anchors = chunk.GetSpan<MassNavigationFormationAnchor>();
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                if (engine.World.Has<FormationCapabilityShowcaseFormationAgent>(entity))
                {
                    continue;
                }

                if (engine.World.Has<PresentationDestroyPending>(entity))
                {
                    continue;
                }

                string formationId = MassNavigationFormationRegistry.GetName(anchors[index].FormationId);
                int formationIndex = ResolveFormationIndex(formationId);
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
            Span<MassNavigationFormationFollower> followers = chunk.GetSpan<MassNavigationFormationFollower>();
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                if (engine.World.Has<FormationCapabilityShowcaseFormationSoldier>(entity) ||
                    engine.World.Has<PresentationDestroyPending>(entity))
                {
                    continue;
                }

                _pendingSoldierBindings.Add(new PendingSoldierBinding(entity, followers[index]));
            }
        }
        for (int i = 0; i < _pendingSoldierBindings.Count; i++)
        {
            PendingSoldierBinding binding = _pendingSoldierBindings[i];
            MassNavigationFormationFollower follower = binding.Follower;
            UpsertComponent(engine.World, binding.Entity, new FormationCapabilityShowcaseFormationSoldier
            {
                FormationIndex = ResolveFormationIndex(MassNavigationFormationRegistry.GetName(follower.FormationId)),
                SlotIndex = follower.SlotIndex,
            });
            RegisterSpawnedSoldier(binding.Entity, in follower);
        }

        BindObstacleOverlays(engine, config);
        simulation.MarkStructuralChange();
    }

    private void PublishLocalFormationKnowledge(GameEngine engine)
    {
        if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? viewerObj) ||
            viewerObj is not Entity viewer ||
            !engine.World.IsAlive(viewer) ||
            !engine.World.TryGet(viewer, out Team viewerTeam))
        {
            return;
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
                !engine.World.TryGet(formation, out Team formationTeam) ||
                formationTeam.Id != viewerTeam.Id)
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
        MassNavigationAgentProfilePlanSet profileSet = simulation.Plan.AgentProfiles;
        config.ValidateAgentProfileReferences(profileSet);

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
        MapId mapId)
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
            var request = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = config.FormationAgent.TemplateId,
                MapId = mapId,
                WorldPositionCm = Fix64Vec2.FromInt((int)MathF.Round(plan.InitialCenterXCm), (int)MathF.Round(plan.InitialCenterYCm)),
                HasWorldPosition = 1,
                FacingAngleRad = plan.FacingRad,
                HasFacing = 1,
                TeamIdOverride = plan.TeamId,
                PlayerOwnerIdOverride = formation.OwnerPlayerId,
                ComponentPatches = CreateFormationAnchorPatch(plan.Id, plan.SoldierCount),
            };

            if (!spawnQueue.TryEnqueue(in request))
            {
                throw new InvalidOperationException("Formation Capability showcase failed to enqueue formation agent spawn request.");
            }
        }
    }

    private static RuntimeEntitySpawnComponentPatch[] CreateFormationAnchorPatch(string formationId, int slotCount)
    {
        return
        [
            new RuntimeEntitySpawnComponentPatch(
                "MassNavigationFormationAnchor",
                new JsonObject
                {
                    ["formationId"] = formationId,
                    ["slotCount"] = slotCount,
                }),
        ];
    }

    private static RuntimeEntitySpawnComponentPatch[] CreateFormationFollowerPatch(
        string formationId,
        int slotIndex,
        float localOffsetXCm,
        float localOffsetYCm)
    {
        return
        [
            new RuntimeEntitySpawnComponentPatch(
                "MassNavigationFormationFollower",
                new JsonObject
                {
                    ["formationId"] = formationId,
                    ["slotIndex"] = slotIndex,
                    ["localOffsetXCm"] = localOffsetXCm,
                    ["localOffsetYCm"] = localOffsetYCm,
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

    private int RequireFormationAgentIndex(GameEngine engine, int formationIndex)
    {
        if ((uint)formationIndex >= (uint)_formationEntities.Length)
        {
            throw new InvalidOperationException(
                $"Formation Capability showcase formation index {formationIndex} exceeds bound formation entity cache {_formationEntities.Length}.");
        }

        Entity formation = _formationEntities[formationIndex];
        if (!engine.World.IsAlive(formation))
        {
            throw new InvalidOperationException(
                $"Formation Capability showcase formation index {formationIndex} was not bound to a live entity before MassNavigation sync.");
        }

        return RequireMassNavigationAgentIndex(engine, formation, $"formation index {formationIndex}");
    }

    private static int RequireMassNavigationAgentIndex(GameEngine engine, Entity entity, string label)
    {
        if (!engine.World.TryGet(entity, out MassNavigationAgentIndex agentIndex))
        {
            throw new InvalidOperationException(
                $"Formation Capability showcase {label} requires {nameof(MassNavigationAgentIndex)} from the Core MassNavigation runtime before formation sync.");
        }

        return agentIndex.Value;
    }

    private void SyncFormationStates(
        GameEngine engine,
        MassNavigationSimulationRuntime simulation)
    {
        for (int i = 0; i < _formationPlans.Length; i++)
        {
            if ((uint)i >= (uint)_formationEntities.Length)
            {
                throw new InvalidOperationException(
                    $"Formation Capability showcase formation entity cache length {_formationEntities.Length} is smaller than planned formations {_formationPlans.Length}.");
            }

            Entity formation = _formationEntities[i];
            if (!engine.World.IsAlive(formation))
            {
                continue;
            }

            FormationCapabilityShowcaseFormationPlan plan = _formationPlans[i];
            int formationAgentIndex = RequireFormationAgentIndex(engine, plan.FormationIndex);
            Vector2 centerWorld = simulation.GetAgentWorldPositionCm(formationAgentIndex);
            float centerX = centerWorld.X;
            float centerY = centerWorld.Y;
            float facingRad = ResolveFormationFacing(engine, plan.FormationIndex);

            var center = Fix64Vec2.FromInt((int)MathF.Round(centerX), (int)MathF.Round(centerY));
            ref FormationCapabilityShowcaseFormationState state = ref engine.World.Get<FormationCapabilityShowcaseFormationState>(formation);
            if (state.SoldierCount != plan.SoldierCount ||
                state.AliveSoldierCount != plan.SoldierCount ||
                state.CenterXCm != centerX ||
                state.CenterYCm != centerY ||
                state.FacingRad != facingRad)
            {
                state.SoldierCount = plan.SoldierCount;
                state.AliveSoldierCount = plan.SoldierCount;
                state.CenterXCm = centerX;
                state.CenterYCm = centerY;
                state.FacingRad = facingRad;
            }

            ref WorldPositionCm worldPosition = ref engine.World.Get<WorldPositionCm>(formation);
            if (worldPosition.Value != center)
            {
                worldPosition.Value = center;
            }
        }
    }

    private float ResolveFormationFacing(GameEngine engine, int formationIndex)
    {
        if ((uint)formationIndex >= (uint)_formationEntities.Length)
        {
            throw new InvalidOperationException(
                $"Formation Capability showcase formation index {formationIndex} exceeds bound formation entity cache {_formationEntities.Length}.");
        }

        Entity formation = _formationEntities[formationIndex];
        if (!engine.World.IsAlive(formation))
        {
            throw new InvalidOperationException(
                $"Formation Capability showcase formation index {formationIndex} was not bound to a live entity before soldier slot sync.");
        }

        if (!engine.World.Has<FacingDirection>(formation))
        {
            throw new InvalidOperationException(
                $"Formation Capability showcase formation entity {formation.Id} requires {nameof(FacingDirection)} for explicit soldier slot facing.");
        }

        return engine.World.Get<FacingDirection>(formation).AngleRad;
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

    private void SyncCommandActors(GameEngine engine, MassNavigationSimulationRuntime simulation)
    {
        EntityCollectionStore collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("Formation Capability showcase requires EntityCollectionStore before syncing command actors.");
        if (!TryResolveLocalCommandSourceOwner(engine, out Entity owner))
        {
            ClearCommandActorsIfNeeded(simulation);
            return;
        }

        if (!EntityCollectionContextRuntime.TryDescribeView(
                collections,
                owner,
                EntityCollectionKeys.CommandSource,
                out EntityCollectionView view))
        {
            ClearCommandActorsIfNeeded(simulation);
            _lastCommandSourceOwner = owner;
            return;
        }

        bool structuralChanged = _lastStructuralRevision != simulation.StructuralChangeRevision;
        if (_lastHadCommandActors &&
            _lastCommandSourceOwner == owner &&
            _lastCommandSourceRevision == view.Revision &&
            !structuralChanged)
        {
            return;
        }

        Span<Entity> commandActors = simulation.EnsureCommandActorScratch(view.Count);
        int written = EntityCollectionContextRuntime.Copy(
            collections,
            owner,
            EntityCollectionKeys.CommandSource,
            commandActors);
        if (written != view.Count)
        {
            throw new InvalidOperationException(
                $"Formation Capability showcase expected {view.Count} command actor row(s), copied {written}.");
        }

        simulation.SetCommandActorSnapshot(commandActors[..written], view.Revision);
        simulation.ObserveCommandActorSyncTick();
        _lastCommandSourceOwner = owner;
        _lastCommandSourceRevision = view.Revision;
        _lastStructuralRevision = simulation.StructuralChangeRevision;
        _lastHadCommandActors = true;
    }

    private void ClearCommandActorsIfNeeded(MassNavigationSimulationRuntime simulation)
    {
        if (_lastHadCommandActors || simulation.CommandActorCount > 0)
        {
            simulation.ClearCommandActorSnapshot();
        }

        _lastCommandSourceOwner = Entity.Null;
        _lastCommandSourceRevision = 0;
        _lastStructuralRevision = simulation.StructuralChangeRevision;
        _lastHadCommandActors = false;
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

    }

    private static void ConfigureRuntimeTeams(MassNavigationSimulationRuntime simulation, FormationCapabilityShowcaseConfig config)
    {
        var teamIds = new List<int>(config.Formations.Length);
        for (int i = 0; i < config.Formations.Length; i++)
        {
            int teamId = config.Formations[i].TeamId;
            if (!teamIds.Contains(teamId))
            {
                teamIds.Add(teamId);
            }
        }

        simulation.ConfigureTeams(teamIds.ToArray());
        simulation.SetActiveTeam(ResolveInitialCommandSourceTeamId(config));
    }

    private static int ResolveInitialCommandSourceTeamId(FormationCapabilityShowcaseConfig config)
    {
        for (int i = 0; i < config.Formations.Length; i++)
        {
            FormationCapabilityShowcaseFormationConfig formation = config.Formations[i];
            if (string.Equals(formation.Id, config.InitialCommandSourceFormationId, StringComparison.Ordinal))
            {
                return formation.TeamId;
            }
        }

        throw new InvalidOperationException(
            $"Formation Capability showcase initial command-source formation '{config.InitialCommandSourceFormationId}' is not configured.");
    }

    private static void ConfigureRelationships(FormationCapabilityShowcaseConfig config)
    {
        TeamManager.Clear();
        TeamManager.DefaultRelationship = config.ParsedDefaultTeamRelationship;
    }

    private static FormationCapabilityShowcaseFormationConfig ResolveFormationTeam(
        FormationCapabilityShowcaseConfig config,
        int teamId)
    {
        for (int i = 0; i < config.Formations.Length; i++)
        {
            if (config.Formations[i].TeamId == teamId)
            {
                return config.Formations[i];
            }
        }

        throw new InvalidOperationException($"Formation Capability showcase has no configured formation team {teamId}.");
    }

    private static MassNavigationSimulationRuntime RequireSimulation(GameEngine engine)
    {
        return engine.GetService(MassNavigationKeys.SimulationRuntime)
            ?? throw new InvalidOperationException("Formation Capability showcase requires MassNavigationSimulationRuntime.");
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

    private static void ClearCommandActorSnapshot(GameEngine engine)
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

    private static int ResolveLocalPlayerOwnerId(GameEngine engine)
    {
        if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) ||
            localObj is not Entity local ||
            !engine.World.IsAlive(local))
        {
            throw new InvalidOperationException("Formation Capability showcase requires LocalPlayerEntity before binding local formation ownership.");
        }

        if (!engine.World.TryGet(local, out PlayerOwner owner))
        {
            throw new InvalidOperationException("Formation Capability showcase LocalPlayerEntity must author PlayerOwner before binding local formation ownership.");
        }

        return owner.PlayerId;
    }

    private sealed class FormationCapabilityCommandSourceRotateSystem : Arch.System.ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly FormationCapabilityShowcaseRuntime _showcaseRuntime;
        private readonly MassNavigationRuntimeBinding _runtimeBinding;
        private Entity[] _actorsScratch = Array.Empty<Entity>();

        public FormationCapabilityCommandSourceRotateSystem(
            GameEngine engine,
            FormationCapabilityShowcaseRuntime showcaseRuntime,
            MassNavigationRuntimeBinding runtimeBinding)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _showcaseRuntime = showcaseRuntime ?? throw new ArgumentNullException(nameof(showcaseRuntime));
            _runtimeBinding = runtimeBinding ?? throw new ArgumentNullException(nameof(runtimeBinding));
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (!_showcaseRuntime.IsCurrentShowcaseMap(_engine) ||
                !MassNavigationIds.IsCurrentNavigationRuntimeReady(_engine) ||
                _runtimeBinding.Current is not MassNavigationSimulationRuntime simulation ||
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
            EnsureActorScratchCapacity(commandActorCount);
            int actorCount = commandActorCount > 0
                ? EntityCollectionContextRuntime.Copy(
                    _engine.GlobalContext,
                    commandSourceOwner,
                    EntityCollectionKeys.CommandSource,
                    _actorsScratch)
                : 0;
            if (actorCount <= 0 ||
                !CanLocalPlayerCommand(_engine, _actorsScratch.AsSpan(0, actorCount)))
            {
                simulation.RejectCommandUnauthorizedCommandActors(0f, 0f);
                return;
            }

            simulation.NavGroupRuntime.RotateCommandActors(
                _engine.World,
                simulation.AgentState,
                _actorsScratch.AsSpan(0, actorCount),
                deltaRadians);
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private static bool CanLocalPlayerCommand(GameEngine engine, ReadOnlySpan<Entity> actors)
        {
            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) ||
                localObj is not Entity local ||
                !engine.World.IsAlive(local) ||
                !engine.World.TryGet(local, out PlayerOwner localOwner))
            {
                return false;
            }

            int liveActors = 0;
            for (int i = 0; i < actors.Length; i++)
            {
                Entity actor = actors[i];
                if (!engine.World.IsAlive(actor))
                {
                    continue;
                }

                if (!engine.World.TryGet(actor, out PlayerOwner owner) ||
                    owner.PlayerId != localOwner.PlayerId)
                {
                    return false;
                }

                liveActors++;
            }

            return liveActors > 0;
        }

        private void EnsureActorScratchCapacity(int required)
        {
            if (_actorsScratch.Length >= required)
            {
                return;
            }

            int next = _actorsScratch.Length == 0 ? 4 : _actorsScratch.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _actorsScratch, next);
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

    private readonly record struct PendingSoldierBinding(Entity Entity, MassNavigationFormationFollower Follower);

    private readonly record struct PendingObstacleOverlayBinding(Entity Entity, int OverlayIndex);
}
