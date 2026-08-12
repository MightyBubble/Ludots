using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Arch.Core;
using Arch.System;
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
using Ludots.Core.Knowledge;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using FormationCapabilityShowcaseMod.Systems;

namespace FormationCapabilityShowcaseMod.Runtime;

internal sealed class FormationCapabilityShowcaseRuntime
{
    private static readonly QueryDescription FormationAnchorCandidateQuery = new QueryDescription()
        .WithAll<FormationAnchorState>();
    private static readonly QueryDescription FormationFollowerCandidateQuery = new QueryDescription()
        .WithAll<FormationMemberState, MassNavigationAgentIndex>();
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
    private ISystem<float>? _scenarioBindingSystem;
    private ISystem<float>? _showcaseStateSystem;
    private ISystem<float>? _localOrderSourceSystem;
    private ISystem<float>? _formationOutlinePresentationSystem;
    private ISystem<float>? _obstacleOverlayPresentationSystem;
    private bool _systemsInstalled;
    private bool _scenarioSpawned;
    private bool _obstacleOverlaySpawnsQueued;
    private bool _initialCommandSourceApplied;
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
        UninstallSystems(engine);
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

        MassNavigationSimulationRuntime simulation = RequireCurrentSimulation(engine);
        FormationCapabilityShowcaseConfig config = EnsureConfig(engine);
        OrderQueue orders = engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new InvalidOperationException("Formation Capability showcase requires OrderQueue.");
        Ludots.Core.Modding.IModContext context = _context
            ?? throw new InvalidOperationException("Formation Capability showcase requires IModContext before installing local order source.");

        var scenarioBindingSystem = new FormationCapabilityShowcaseScenarioBindingSystem(engine, this);
        var showcaseStateSystem = new FormationCapabilityShowcaseStateSystem(engine, this);
        var localOrderSourceSystem = new FormationCapabilityLocalOrderSourceSystem(
            engine.World,
            engine.GlobalContext,
            orders,
            context,
            ResolveMaxSlotsPerFormation(config),
            config.OrderBatchCapacity);
        var formationOutlinePresentationSystem = new FormationCapabilityShowcaseFormationOutlinePresentationSystem(engine, this, config);
        var obstacleOverlayPresentationSystem = new FormationCapabilityShowcaseObstacleOverlayPresentationSystem(engine, this, simulation.Config.Solver.MaxObstacleCount);

        try
        {
            engine.RegisterSystem(scenarioBindingSystem, SystemGroup.RuntimeEntityBinding);
            _scenarioBindingSystem = scenarioBindingSystem;
            engine.InsertSystemBeforeRequired<MassNavigationPreSimulationStepSystem>(showcaseStateSystem, SystemGroup.PostMovement);
            _showcaseStateSystem = showcaseStateSystem;
            engine.RegisterSystem(localOrderSourceSystem, SystemGroup.InputCollection);
            _localOrderSourceSystem = localOrderSourceSystem;
            engine.InsertPresentationSystemBefore<PerformerRuleSystem>(formationOutlinePresentationSystem);
            _formationOutlinePresentationSystem = formationOutlinePresentationSystem;
            engine.InsertPresentationSystemBefore<PerformerRuleSystem>(obstacleOverlayPresentationSystem);
            _obstacleOverlayPresentationSystem = obstacleOverlayPresentationSystem;
            _systemsInstalled = true;
        }
        catch
        {
            UninstallSystems(engine);
            throw;
        }
    }

    private void UninstallSystems(GameEngine engine)
    {
        bool hadSystems =
            _scenarioBindingSystem != null ||
            _showcaseStateSystem != null ||
            _localOrderSourceSystem != null ||
            _formationOutlinePresentationSystem != null ||
            _obstacleOverlayPresentationSystem != null;
        if (!hadSystems)
        {
            _systemsInstalled = false;
            return;
        }

        UnregisterPresentationSystem(engine, ref _obstacleOverlayPresentationSystem, nameof(_obstacleOverlayPresentationSystem));
        UnregisterPresentationSystem(engine, ref _formationOutlinePresentationSystem, nameof(_formationOutlinePresentationSystem));
        UnregisterSystem(engine, ref _localOrderSourceSystem, SystemGroup.InputCollection, nameof(_localOrderSourceSystem));
        UnregisterSystem(engine, ref _showcaseStateSystem, SystemGroup.PostMovement, nameof(_showcaseStateSystem));
        UnregisterSystem(engine, ref _scenarioBindingSystem, SystemGroup.RuntimeEntityBinding, nameof(_scenarioBindingSystem));
        _systemsInstalled = false;
    }

    private static void UnregisterSystem(
        GameEngine engine,
        ref ISystem<float>? system,
        SystemGroup group,
        string name)
    {
        if (system == null)
        {
            return;
        }

        ISystem<float> registered = system;
        if (!engine.UnregisterSystem(registered, group))
        {
            throw new InvalidOperationException($"Formation Capability showcase could not unregister required system '{name}' from group '{group}'.");
        }

        system = null;
    }

    private static void UnregisterPresentationSystem(
        GameEngine engine,
        ref ISystem<float>? system,
        string name)
    {
        if (system == null)
        {
            return;
        }

        ISystem<float> registered = system;
        if (!engine.UnregisterPresentationSystem(registered))
        {
            throw new InvalidOperationException($"Formation Capability showcase could not unregister required presentation system '{name}'.");
        }

        system = null;
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

        RefreshFormationRuntimeState(engine, simulation);
        PublishLocalFormationKnowledge(engine);
        TryApplyInitialCommandSource(engine, config);
    }

    private void RefreshFormationRuntimeState(GameEngine engine, MassNavigationSimulationRuntime simulation)
    {
        for (int formationIndex = 0; formationIndex < _formationPlans.Length; formationIndex++)
        {
            FormationCapabilityShowcaseFormationPlan plan = _formationPlans[formationIndex];
            float sumX = 0f;
            float sumY = 0f;
            int alive = 0;
            for (int slotIndex = 0; slotIndex < plan.SoldierCount; slotIndex++)
            {
                int memberIndex = plan.FirstSoldierPlanIndex + slotIndex;
                if ((uint)memberIndex >= (uint)_soldierEntitiesByPlanIndex.Length)
                {
                    throw new InvalidOperationException(
                        $"Formation {formationIndex} member cache index {memberIndex} exceeds {_soldierEntitiesByPlanIndex.Length}.");
                }

                Entity member = _soldierEntitiesByPlanIndex[memberIndex];
                if (!engine.World.IsAlive(member) ||
                    !simulation.TryGetAgentWorldPositionCm(engine.World, member, out Vector2 position))
                {
                    continue;
                }

                sumX += position.X;
                sumY += position.Y;
                alive++;
            }

            if (alive <= 0 ||
                (uint)formationIndex >= (uint)_formationEntities.Length ||
                !engine.World.IsAlive(_formationEntities[formationIndex]))
            {
                continue;
            }

            Entity anchor = _formationEntities[formationIndex];
            int centerX = FormationNumericEncoding.RoundCm(sumX / alive);
            int centerY = FormationNumericEncoding.RoundCm(sumY / alive);
            ref FormationRuntimeState state = ref engine.World.Get<FormationRuntimeState>(anchor);
            state.MemberCount = plan.SoldierCount;
            state.AliveMemberCount = alive;
            state.CenterXCm = centerX;
            state.CenterYCm = centerY;
            ref FacingDirection anchorFacing = ref engine.World.Get<FacingDirection>(anchor);
            state.FacingMicroRad = FormationNumericEncoding.EncodeRadians(anchorFacing.AngleRad);
            ref WorldPositionCm anchorPosition = ref engine.World.Get<WorldPositionCm>(anchor);
            anchorPosition.Value = Fix64Vec2.FromInt(centerX, centerY);
        }
    }

    private void SpawnScenario(GameEngine engine, FormationCapabilityShowcaseConfig config)
    {
        MassNavigationSimulationRuntime simulation = RequireCurrentSimulation(engine);
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
        EnqueueFormationAnchorSpawns(engine, config, spawnQueue, mapId, teamLookup, playerLookup);
        EnsureSoldierEntityCache();
        for (int i = 0; i < _soldierAgentPlans.Length; i++)
        {
            FormationCapabilityShowcaseSoldierAgentSpawnPlan plan = _soldierAgentPlans[i];
            FormationCapabilityShowcaseFormationConfig formation = config.Formations[plan.FormationIndex];
            bool hasOwnershipSource = TryResolvePlayerDomain(
                engine,
                playerLookup,
                formation.OwnerPlayerId,
                out Entity ownershipSource);
            var request = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = plan.TemplateId,
                MapId = mapId,
                WorldPositionCm = Fix64Vec2.FromInt((int)MathF.Round(plan.WorldXCm), (int)MathF.Round(plan.WorldYCm)),
                HasWorldPosition = 1,
                FacingAngleRad = plan.FacingRad,
                HasFacing = 1,
                OwnershipSource = ownershipSource,
                HasOwnershipSource = hasOwnershipSource ? (byte)1 : (byte)0,
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
        templateIds[0] = config.FormationAnchor.TemplateId;
        templateIds[1] = config.ObstacleOverlay.TemplateId;
        for (int i = 0; i < config.Formations.Length; i++)
        {
            templateIds[i + 2] = config.Formations[i].SoldierAgent.TemplateId;
        }

        return templateIds;
    }

    private void RegisterSpawnedSoldier(Entity entity, in FormationMemberState soldier)
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

    private void RegisterSpawnedFormationAnchor(GameEngine engine, Entity entity, int formationIndex)
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
        UpsertComponent(engine.World, entity, new FormationAnchorState
        {
            FormationIndex = plan.FormationIndex,
            SlotCount = plan.SoldierCount,
        });
        UpsertComponent(engine.World, entity, new FormationRuntimeState
        {
            MemberCount = plan.SoldierCount,
            AliveMemberCount = plan.SoldierCount,
            CenterXCm = FormationNumericEncoding.RoundCm(
                plan.InitialCenterXCm,
                $"Formation Capability formation '{plan.Id}' runtime center X"),
            CenterYCm = FormationNumericEncoding.RoundCm(
                plan.InitialCenterYCm,
                $"Formation Capability formation '{plan.Id}' runtime center Y"),
            FacingMicroRad = FormationNumericEncoding.EncodeRadians(
                plan.FacingRad,
                $"Formation Capability formation '{plan.Id}' runtime facing"),
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
            Span<FormationAnchorState> anchors = chunk.GetSpan<FormationAnchorState>();
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
            RegisterSpawnedFormationAnchor(engine, binding.Entity, binding.FormationIndex);
        }
        PublishLocalFormationKnowledge(engine);

        _pendingSoldierBindings.Clear();
        foreach (ref var chunk in engine.World.Query(in FormationFollowerCandidateQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            Span<FormationMemberState> followers = chunk.GetSpan<FormationMemberState>();
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
            FormationMemberState soldier = binding.Soldier;
            RegisterSpawnedSoldier(binding.Entity, in soldier);
        }

        BindObstacleOverlays(engine, config);
        simulation.MarkStructuralChange();
    }

    private void PublishLocalFormationKnowledge(GameEngine engine)
    {
        if (!ClientLocalSeatAccess.TryGetSolePossessedRep(engine.GlobalContext, out var viewer) ||
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
            FormationCapabilityShowcaseFormationSlotConfig slots = formation.Slots;
            var pose = new FormationPose(new Vector2(formation.CenterXCm, formation.CenterYCm), facingRad);

            for (int slotIndex = 0; slotIndex < slots.SoldierCount; slotIndex++)
            {
                Vector2 slotOffset = ResolveSlotOffset(slots, slotIndex);
                FormationTargetPlan target = FormationTargetPlanner.PlanMemberTarget(
                    in pose,
                    new FormationMember(formationIndex, slotIndex, slotOffset));
                _soldierAgentPlans[soldierPlanIndex] = new FormationCapabilityShowcaseSoldierAgentSpawnPlan(
                    formationIndex,
                    slotIndex,
                    formation.TeamId,
                    formation.SoldierAgent.TemplateId,
                    target.TargetWorldCm.X,
                    target.TargetWorldCm.Y,
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
            var plan = new FormationSlotPlan(
                FormationSlotLayout.Grid,
                grid.Columns * grid.Rows,
                grid.Columns,
                grid.Rows,
                grid.SpacingXCm,
                grid.SpacingYCm,
                RingSpacingCm: 0f);
            return FormationTargetPlanner.ResolveSlotOffset(in plan, slotIndex);
        }

        var discPlan = new FormationSlotPlan(
            FormationSlotLayout.Disc,
            slots.RequiredDisc.Count,
            Columns: 0,
            Rows: 0,
            SpacingXCm: 0f,
            SpacingYCm: 0f,
            RingSpacingCm: slots.RequiredDisc.RingSpacingCm);
        return FormationTargetPlanner.ResolveSlotOffset(in discPlan, slotIndex);
    }

    private void EnsureSoldierEntityCache()
    {
        if (_soldierEntitiesByPlanIndex.Length != _soldierAgentPlans.Length)
        {
            _soldierEntitiesByPlanIndex = new Entity[_soldierAgentPlans.Length];
        }

        Array.Fill(_soldierEntitiesByPlanIndex, Entity.Null);
    }

    private void EnqueueFormationAnchorSpawns(
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
            bool hasOwnershipSource = TryResolvePlayerDomain(
                engine,
                playerLookup,
                formation.OwnerPlayerId,
                out Entity ownershipSource);
            var request = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = config.FormationAnchor.TemplateId,
                MapId = mapId,
                WorldPositionCm = Fix64Vec2.FromInt((int)MathF.Round(plan.InitialCenterXCm), (int)MathF.Round(plan.InitialCenterYCm)),
                HasWorldPosition = 1,
                FacingAngleRad = plan.FacingRad,
                HasFacing = 1,
                OwnershipSource = ownershipSource,
                HasOwnershipSource = hasOwnershipSource ? (byte)1 : (byte)0,
                MembershipTarget = teamDomain,
                HasMembershipTarget = 1,
                ComponentPatches = CreateFormationAnchorPatch(plan.FormationIndex, plan.SoldierCount),
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

    private static bool TryResolvePlayerDomain(
        GameEngine engine,
        PlayerEntityLookup playerLookup,
        int playerId,
        out Entity playerDomain)
    {
        if (playerLookup.TryGet(playerId, out playerDomain) && engine.World.IsAlive(playerDomain))
        {
            return true;
        }

        playerDomain = Entity.Null;
        if (playerId == RequireSolePossessedPlayerId(engine))
        {
            throw new InvalidOperationException(
                $"Formation Capability showcase requires a live relationship representative for sole ClientLocalSeat possession player {playerId}.");
        }

        return false;
    }

    private static RuntimeEntitySpawnComponentPatch[] CreateFormationAnchorPatch(
        int formationIndex,
        int slotCount)
    {
        return
        [
            new RuntimeEntitySpawnComponentPatch(
                FormationCapabilityShowcaseFormationComponentAuthoring.AnchorStateComponentName,
                new JsonObject
                {
                    ["FormationIndex"] = formationIndex,
                    ["SlotCount"] = slotCount,
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
                FormationCapabilityShowcaseFormationComponentAuthoring.MemberStateComponentName,
                new JsonObject
                {
                    ["FormationIndex"] = formationIndex,
                    ["SlotIndex"] = slotIndex,
                    ["LocalOffsetXCm"] = FormationNumericEncoding.RoundCm(
                        localOffsetXCm,
                        $"Formation Capability slot {formationIndex}:{slotIndex} local offset X"),
                    ["LocalOffsetYCm"] = FormationNumericEncoding.RoundCm(
                        localOffsetYCm,
                        $"Formation Capability slot {formationIndex}:{slotIndex} local offset Y"),
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

    private static int ResolveMaxSlotsPerFormation(FormationCapabilityShowcaseConfig config)
    {
        int maxSlots = 0;
        for (int i = 0; i < config.Formations.Length; i++)
        {
            maxSlots = Math.Max(maxSlots, config.Formations[i].SoldierCount);
        }

        if (maxSlots <= 0)
        {
            throw new InvalidOperationException("Formation Capability showcase requires at least one authored formation slot.");
        }

        return maxSlots;
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

    internal MassNavigationSimulationRuntime RequireCurrentSimulation(GameEngine engine)
    {
        MassNavigationRuntimeBinding binding = engine.GetService(MassNavigationKeys.RuntimeBinding)
            ?? throw new InvalidOperationException("Formation Capability showcase requires an active MassNavigation runtime binding.");
        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("Formation Capability showcase requires an active map session before resolving MassNavigation runtime.");
        if (binding.Current is not MassNavigationSimulationRuntime simulation ||
            !string.Equals(binding.CurrentMapId.Value, session.MapId.Value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Formation Capability showcase requires the active MassNavigation runtime for the current map before scenario preparation.");
        }

        return simulation;
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
        ValidateTemplate(engine, config.FormationAnchor.TemplateId);
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
            : throw new InvalidOperationException(
                "Formation Capability showcase requires sole ClientLocalSeat possession from launchContext.localSeats / startupLocalSeats.");
    }

    private static bool TryResolveLocalCommandSourceOwner(GameEngine engine, out Entity owner)
    {
        if (!ClientLocalSeatAccess.TryGetSolePossessedRep(engine.GlobalContext, out var local) ||
            !engine.World.IsAlive(local))
        {
            owner = Entity.Null;
            return false;
        }

        owner = local;
        return true;
    }

    private static int RequireSolePossessedPlayerId(GameEngine engine)
    {
        ClientLocalSeatRegistry seats = ClientLocalSeatAccess.RequireRegistry(engine.GlobalContext);
        if (!seats.TryGetSoleSeat(out ClientLocalSeat seat) || !seat.HasPossession)
        {
            throw new InvalidOperationException(
                "Formation Capability showcase requires sole ClientLocalSeat possession from launchContext.localSeats / startupLocalSeats.");
        }

        return seat.PossessedPlayerId;
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

    private readonly record struct PendingSoldierBinding(Entity Entity, FormationMemberState Soldier);

    private readonly record struct PendingObstacleOverlayBinding(Entity Entity, int OverlayIndex);
}
