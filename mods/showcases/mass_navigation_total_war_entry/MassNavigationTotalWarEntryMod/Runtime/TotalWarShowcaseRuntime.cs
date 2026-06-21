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
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Selection;
using Ludots.Core.Map;
using Ludots.Core.MassCrowd;
using Ludots.Core.MassCrowd.Runtime;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;

namespace MassNavigationTotalWarEntryMod.Runtime;

internal sealed class TotalWarShowcaseRuntime
{
    private static readonly float DiscSlotGoldenAngleRadians = MathF.PI * (3f - MathF.Sqrt(5f));
    private static readonly QueryDescription FormationAnchorCandidateQuery = new QueryDescription()
        .WithAll<MassCrowdFormationAnchor, MassCrowdAgentIndex>();
    private static readonly QueryDescription FormationFollowerCandidateQuery = new QueryDescription()
        .WithAll<MassCrowdFormationFollower, MassCrowdAgentIndex>();
    private static readonly QueryDescription ObstacleOverlayCandidateQuery = new QueryDescription()
        .WithAll<EntityTemplateKeyRef, WorldPositionCm>();

    private TotalWarShowcaseConfig? _config;
    private TotalWarSoldierAgentSpawnPlan[] _soldierAgentPlans = Array.Empty<TotalWarSoldierAgentSpawnPlan>();
    private TotalWarFormationPlan[] _formationPlans = Array.Empty<TotalWarFormationPlan>();
    private TotalWarObstacleOverlayPlan[] _obstacleOverlayPlans = Array.Empty<TotalWarObstacleOverlayPlan>();
    private Entity[] _formationEntities = Array.Empty<Entity>();
    private Entity[] _soldierEntitiesByPlanIndex = Array.Empty<Entity>();
    private Entity[] _obstacleOverlayEntities = Array.Empty<Entity>();
    private Entity[] _initialSelectionScratch = Array.Empty<Entity>();
    private readonly List<PendingFormationBinding> _pendingFormationBindings = new();
    private readonly List<PendingSoldierBinding> _pendingSoldierBindings = new();
    private readonly List<PendingObstacleOverlayBinding> _pendingObstacleOverlayBindings = new();
    private bool _systemsInstalled;
    private bool _scenarioSpawned;
    private bool _obstacleOverlaySpawnsQueued;
    private bool _initialSelectionApplied;
    private int _observedSceneResetCount;

    public TotalWarShowcaseConfig ActiveConfig => _config
        ?? throw new InvalidOperationException("Total War showcase config has not been loaded.");

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        TotalWarShowcaseConfig config = EnsureConfig(engine);
        EnsureInitialSelectionScratch(config);
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

        TotalWarShowcaseConfig config = EnsureConfig(engine);
        string mapId = context.Get(CoreServiceKeys.MapId).Value;
        if (!string.Equals(mapId, config.MapId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        _scenarioSpawned = false;
        _obstacleOverlaySpawnsQueued = false;
        _initialSelectionApplied = false;
        RemovePendingScenarioSpawns(engine, config);
        MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
        simulation.ResetRuntimeState(engine.World);
        ClearFormationCaches();
        return Task.CompletedTask;
    }

    private TotalWarShowcaseConfig EnsureConfig(GameEngine engine)
    {
        if (_config != null)
        {
            return _config;
        }

        if (engine.ConfigPipeline == null)
        {
            throw new InvalidOperationException("Total War showcase requires ConfigPipeline before loading config.");
        }

        _config = new TotalWarShowcaseConfigLoader(engine.ConfigPipeline).Load(
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
        engine.RegisterSystem(
            new TotalWarScenarioBindingSystem(engine, this, simulation),
            SystemGroup.RuntimeEntityBinding);
        engine.InsertSystemBeforeRequired<MassNavigationPreSimulationStepSystem>(
            new TotalWarFormationRuntimeSystem(engine, this, simulation),
            SystemGroup.PostMovement);
        TotalWarShowcaseConfig config = EnsureConfig(engine);
        engine.RegisterPresentationSystem(new TotalWarFormationOutlinePresentationSystem(engine, this, config));
        engine.RegisterPresentationSystem(new TotalWarObstacleOverlayPresentationSystem(engine, this, simulation.Config.Solver.MaxObstacleCount));
        _systemsInstalled = true;
    }

    public bool IsCurrentShowcaseMap(GameEngine engine)
    {
        TotalWarShowcaseConfig config = EnsureConfig(engine);
        return string.Equals(engine.CurrentMapSession?.MapId.Value, config.MapId, StringComparison.Ordinal);
    }

    public void Tick(GameEngine engine, MassNavigationSimulationRuntime simulation)
    {
        TotalWarShowcaseConfig config = EnsureConfig(engine);
        if (!string.Equals(engine.CurrentMapSession?.MapId.Value, config.MapId, StringComparison.Ordinal))
        {
            return;
        }

        if (simulation.SceneResetCount != _observedSceneResetCount)
        {
            _observedSceneResetCount = simulation.SceneResetCount;
            _scenarioSpawned = false;
            _obstacleOverlaySpawnsQueued = false;
            _initialSelectionApplied = false;
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
        TryApplyInitialSelection(engine, config);
    }

    private void SpawnScenario(GameEngine engine, TotalWarShowcaseConfig config)
    {
        MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("Total War showcase requires RuntimeEntitySpawnQueue.");

        ClearSelection(engine);
        BuildAgentPlans(engine, simulation, config);
        DestroyShowcaseOwnedEntities(engine);

        int spawnRequestCount = _soldierAgentPlans.Length + _formationPlans.Length;
        if (spawnQueue.FreeCapacity < spawnRequestCount)
        {
            throw new InvalidOperationException(
                $"Total War showcase requires RuntimeEntitySpawnQueue free capacity {spawnRequestCount}, actual {spawnQueue.FreeCapacity}.");
        }

        ConfigureScenarioTeams(simulation, config);
        ConfigureRelationships(simulation.Config);
        ValidateAuthoring(engine, config);

        TeamEntityLookup teamLookup = engine.GetService(CoreServiceKeys.TeamEntityLookup)
            ?? throw new InvalidOperationException("Total War showcase requires TeamEntityLookup.");
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
        EnqueueFormationAgentSpawns(engine, config, spawnQueue, mapId);
        EnsureSoldierEntityCache();
        for (int i = 0; i < _soldierAgentPlans.Length; i++)
        {
            TotalWarSoldierAgentSpawnPlan plan = _soldierAgentPlans[i];
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
                throw new InvalidOperationException("Total War showcase failed to enqueue runtime entity spawn request.");
            }
        }

        _scenarioSpawned = true;
        _obstacleOverlaySpawnsQueued = false;
        _initialSelectionApplied = false;
        _observedSceneResetCount = simulation.SceneResetCount;
        simulation.MarkScenarioSpawned();
        simulation.MarkStructuralChange();
    }

    private static void RemovePendingScenarioSpawns(GameEngine engine, TotalWarShowcaseConfig config)
    {
        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("Total War showcase requires RuntimeEntitySpawnQueue.");
        spawnQueue.RemoveForMapAndTemplates(
            RequireCurrentMapId(engine, config.MapId),
            BuildPendingSpawnTemplateScratch(config));
    }

    private static string[] BuildPendingSpawnTemplateScratch(TotalWarShowcaseConfig config)
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

    private void RegisterSpawnedSoldier(Entity entity, in MassCrowdFormationFollower follower)
    {
        int formationIndex = ResolveFormationIndex(MassCrowdFormationRegistry.GetName(follower.FormationId));
        int planIndex = ResolveSoldierPlanIndex(formationIndex, follower.SlotIndex);
        if ((uint)planIndex >= (uint)_soldierEntitiesByPlanIndex.Length)
        {
            throw new InvalidOperationException($"Total War showcase soldier plan index {planIndex} exceeds its scenario cache.");
        }

        if (_soldierEntitiesByPlanIndex[planIndex] != Entity.Null)
        {
            throw new InvalidOperationException($"Total War showcase soldier plan index {planIndex} was already bound.");
        }

        _soldierEntitiesByPlanIndex[planIndex] = entity;
    }

    private void RegisterSpawnedFormationAgent(GameEngine engine, Entity entity, int formationIndex)
    {
        if ((uint)formationIndex >= (uint)_formationPlans.Length)
        {
            throw new InvalidOperationException($"Total War showcase formation index {formationIndex} exceeds its scenario cache.");
        }

        if (_formationEntities.Length != _formationPlans.Length)
        {
            throw new InvalidOperationException("Total War showcase formation entity cache was not initialized before formation sidecar binding.");
        }

        if (_formationEntities[formationIndex] != Entity.Null)
        {
            throw new InvalidOperationException($"Total War showcase formation index {formationIndex} was already bound.");
        }

        TotalWarFormationPlan plan = _formationPlans[formationIndex];
        TotalWarFormationConfig formation = ActiveConfig.Formations[formationIndex];
        _formationEntities[formationIndex] = entity;
        UpsertComponent(engine.World, entity, new TotalWarFormationAgent { FormationIndex = plan.FormationIndex });
        UpsertComponent(engine.World, entity, new TotalWarFormationState
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
        UpsertComponent(engine.World, entity, SelectionSelectableState.EnabledByDefault);
        UpsertComponent(engine.World, entity, new Team { Id = formation.TeamId });
        UpsertComponent(engine.World, entity, new PlayerOwner { PlayerId = formation.OwnerPlayerId });
    }

    private void RegisterSpawnedObstacleOverlay(GameEngine engine, Entity entity, int overlayIndex)
    {
        if (_obstacleOverlayEntities.Length != _obstacleOverlayPlans.Length)
        {
            throw new InvalidOperationException("Total War showcase obstacle overlay cache was not initialized before overlay binding.");
        }

        if ((uint)overlayIndex >= (uint)_obstacleOverlayEntities.Length)
        {
            throw new InvalidOperationException($"Total War showcase obstacle overlay index {overlayIndex} exceeds planned overlays {_obstacleOverlayEntities.Length}.");
        }

        if (_obstacleOverlayEntities[overlayIndex] != Entity.Null)
        {
            throw new InvalidOperationException($"Total War showcase obstacle overlay index {overlayIndex} was already bound.");
        }

        _obstacleOverlayEntities[overlayIndex] = entity;
        UpsertComponent(engine.World, entity, ActiveConfig.ObstacleOverlay.ToComponent(_obstacleOverlayPlans[overlayIndex].RadiusCm));
    }

    public void BindComponentAuthoredScenarioEntities(
        GameEngine engine,
        MassNavigationSimulationRuntime simulation)
    {
        TotalWarShowcaseConfig config = EnsureConfig(engine);
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
            Span<MassCrowdFormationAnchor> anchors = chunk.GetSpan<MassCrowdFormationAnchor>();
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                if (engine.World.Has<TotalWarFormationAgent>(entity))
                {
                    continue;
                }

                if (engine.World.Has<PresentationDestroyPending>(entity))
                {
                    continue;
                }

                string formationId = MassCrowdFormationRegistry.GetName(anchors[index].FormationId);
                int formationIndex = ResolveFormationIndex(formationId);
                _pendingFormationBindings.Add(new PendingFormationBinding(entity, formationIndex));
            }
        }
        for (int i = 0; i < _pendingFormationBindings.Count; i++)
        {
            PendingFormationBinding binding = _pendingFormationBindings[i];
            RegisterSpawnedFormationAgent(engine, binding.Entity, binding.FormationIndex);
        }

        _pendingSoldierBindings.Clear();
        foreach (ref var chunk in engine.World.Query(in FormationFollowerCandidateQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            Span<MassCrowdFormationFollower> followers = chunk.GetSpan<MassCrowdFormationFollower>();
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                if (engine.World.Has<TotalWarFormationSoldier>(entity) ||
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
            MassCrowdFormationFollower follower = binding.Follower;
            UpsertComponent(engine.World, binding.Entity, new TotalWarFormationSoldier
            {
                FormationIndex = ResolveFormationIndex(MassCrowdFormationRegistry.GetName(follower.FormationId)),
                SlotIndex = follower.SlotIndex,
            });
            RegisterSpawnedSoldier(binding.Entity, in follower);
        }

        BindObstacleOverlays(engine, config);
        simulation.MarkStructuralChange();
    }

    private void EnsureObstacleOverlaySpawnsQueued(
        GameEngine engine,
        MassNavigationSimulationRuntime simulation,
        TotalWarShowcaseConfig config)
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
            ?? throw new InvalidOperationException("Total War showcase requires RuntimeEntitySpawnQueue.");
        if (spawnQueue.FreeCapacity < _obstacleOverlayPlans.Length)
        {
            throw new InvalidOperationException(
                $"Total War showcase requires RuntimeEntitySpawnQueue free capacity {_obstacleOverlayPlans.Length}, actual {spawnQueue.FreeCapacity}.");
        }

        EnqueueObstacleOverlaySpawns(
            engine,
            config,
            spawnQueue,
            RequireCurrentMapId(engine, config.MapId));
        _obstacleOverlaySpawnsQueued = true;
    }

    private void BindObstacleOverlays(GameEngine engine, TotalWarShowcaseConfig config)
    {
        if (_obstacleOverlayEntities.Length != _obstacleOverlayPlans.Length)
        {
            return;
        }

        EntityTemplateKeyRegistry templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("Total War showcase requires EntityTemplateKeyRegistry.");
        int overlayTemplateKey = templateKeys.GetId(config.ObstacleOverlay.TemplateId);
        if (overlayTemplateKey <= 0)
        {
            throw new InvalidOperationException($"Total War showcase obstacle overlay template '{config.ObstacleOverlay.TemplateId}' was not registered in EntityTemplateKeyRegistry.");
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
                if (engine.World.Has<TotalWarObstacleOverlay>(entity) ||
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

            TotalWarObstacleOverlayPlan plan = _obstacleOverlayPlans[i];
            if ((int)MathF.Round(plan.WorldXCm) == xCm &&
                (int)MathF.Round(plan.WorldYCm) == yCm)
            {
                return i;
            }
        }

        throw new InvalidOperationException(
            $"Total War showcase obstacle overlay at ({xCm},{yCm}) does not match an unbound obstacle overlay plan.");
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

    private void BuildAgentPlans(GameEngine engine, MassNavigationSimulationRuntime simulation, TotalWarShowcaseConfig config)
    {
        MassNavigationAgentProfileSetConfig profileSet = simulation.Config.AgentProfiles;
        var geometryProfiles = engine.GetService(CoreServiceKeys.AgentProfiles)
            ?? throw new InvalidOperationException("Total War showcase requires AgentProfiles.");
        config.ValidateAgentProfileReferences(profileSet, geometryProfiles);

        int soldierCount = 0;
        for (int i = 0; i < config.Formations.Length; i++)
        {
            int count = config.Formations[i].SoldierCount;
            soldierCount += count;
        }

        if (_soldierAgentPlans.Length != soldierCount)
        {
            _soldierAgentPlans = new TotalWarSoldierAgentSpawnPlan[soldierCount];
        }

        if (_formationPlans.Length != config.Formations.Length)
        {
            _formationPlans = new TotalWarFormationPlan[config.Formations.Length];
        }

        for (int formationIndex = 0; formationIndex < config.Formations.Length; formationIndex++)
        {
            TotalWarFormationConfig formation = config.Formations[formationIndex];
            float facingRad = formation.FacingDeg * (MathF.PI / 180f);
            _formationPlans[formationIndex] = new TotalWarFormationPlan(
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
            TotalWarFormationConfig formation = config.Formations[formationIndex];
            int firstSoldierPlanIndex = soldierPlanIndex;
            float facingRad = formation.FacingDeg * (MathF.PI / 180f);
            float forwardX = MathF.Cos(facingRad);
            float forwardY = MathF.Sin(facingRad);
            float lateralX = -forwardY;
            float lateralY = forwardX;
            TotalWarFormationSlotConfig slots = formation.Slots;

            for (int slotIndex = 0; slotIndex < slots.SoldierCount; slotIndex++)
            {
                Vector2 slotOffset = ResolveSlotOffset(slots, slotIndex);
                float lateralOffset = slotOffset.X;
                float depthOffset = slotOffset.Y;
                float worldX = formation.CenterXCm + (lateralX * lateralOffset) + (forwardX * depthOffset);
                float worldY = formation.CenterYCm + (lateralY * lateralOffset) + (forwardY * depthOffset);
                _soldierAgentPlans[soldierPlanIndex] = new TotalWarSoldierAgentSpawnPlan(
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

            _formationPlans[formationIndex] = new TotalWarFormationPlan(
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
            _obstacleOverlayPlans = new TotalWarObstacleOverlayPlan[obstacleCount];
        }

        for (int obstacleIndex = 0; obstacleIndex < obstacleCount; obstacleIndex++)
        {
            MassNavigationObstacleSnapshot obstacle = simulation.GetObstacleWorldSnapshot(obstacleIndex);
            _obstacleOverlayPlans[obstacleIndex] = new TotalWarObstacleOverlayPlan(
                obstacle.WorldXCm,
                obstacle.WorldYCm,
                obstacle.RadiusCm);
        }
    }

    private static Vector2 ResolveSlotOffset(
        TotalWarFormationSlotConfig slots,
        int slotIndex)
    {
        if (slots.LayoutKind == TotalWarFormationSlotLayout.Grid)
        {
            TotalWarFormationGridSlotConfig grid = slots.RequiredGrid;
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
        TotalWarShowcaseConfig config,
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
            TotalWarFormationPlan plan = _formationPlans[i];
            TotalWarFormationConfig formation = config.Formations[plan.FormationIndex];
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
                throw new InvalidOperationException("Total War showcase failed to enqueue formation agent spawn request.");
            }
        }
    }

    private static RuntimeEntitySpawnComponentPatch[] CreateFormationAnchorPatch(string formationId, int slotCount)
    {
        return
        [
            new RuntimeEntitySpawnComponentPatch(
                "MassCrowdFormationAnchor",
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
                "MassCrowdFormationFollower",
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
        TotalWarShowcaseConfig config,
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
            TotalWarObstacleOverlayPlan plan = _obstacleOverlayPlans[i];
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
                throw new InvalidOperationException("Total War showcase failed to enqueue obstacle overlay spawn request.");
            }
        }
    }

    private int ResolveSoldierPlanIndex(int formationIndex, int slotIndex)
    {
        if ((uint)formationIndex >= (uint)_formationPlans.Length)
        {
            throw new InvalidOperationException(
                $"Total War showcase formation index {formationIndex} exceeds planned formation count {_formationPlans.Length}.");
        }

        TotalWarFormationPlan plan = _formationPlans[formationIndex];
        if ((uint)slotIndex >= (uint)plan.SoldierCount)
        {
            throw new InvalidOperationException(
                $"Total War showcase soldier slot index {slotIndex} exceeds formation '{plan.Id}' soldier count {plan.SoldierCount}.");
        }

        return plan.FirstSoldierPlanIndex + slotIndex;
    }

    private int RequireFormationAgentIndex(GameEngine engine, int formationIndex)
    {
        if ((uint)formationIndex >= (uint)_formationEntities.Length)
        {
            throw new InvalidOperationException(
                $"Total War showcase formation index {formationIndex} exceeds bound formation entity cache {_formationEntities.Length}.");
        }

        Entity formation = _formationEntities[formationIndex];
        if (!engine.World.IsAlive(formation))
        {
            throw new InvalidOperationException(
                $"Total War showcase formation index {formationIndex} was not bound to a live entity before MassCrowd sync.");
        }

        return RequireMassCrowdAgentIndex(engine, formation, $"formation index {formationIndex}");
    }

    private static int RequireMassCrowdAgentIndex(GameEngine engine, Entity entity, string label)
    {
        if (!engine.World.TryGet(entity, out MassCrowdAgentIndex agentIndex))
        {
            throw new InvalidOperationException(
                $"Total War showcase {label} requires {nameof(MassCrowdAgentIndex)} from the Core MassCrowd runtime before formation sync.");
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
                    $"Total War showcase formation entity cache length {_formationEntities.Length} is smaller than planned formations {_formationPlans.Length}.");
            }

            Entity formation = _formationEntities[i];
            if (!engine.World.IsAlive(formation))
            {
                continue;
            }

            TotalWarFormationPlan plan = _formationPlans[i];
            int formationAgentIndex = RequireFormationAgentIndex(engine, plan.FormationIndex);
            Vector2 centerWorld = simulation.GetAgentWorldPositionCm(formationAgentIndex);
            float centerX = centerWorld.X;
            float centerY = centerWorld.Y;
            float facingRad = ResolveFormationFacing(engine, plan.FormationIndex);

            var center = Fix64Vec2.FromInt((int)MathF.Round(centerX), (int)MathF.Round(centerY));
            ref TotalWarFormationState state = ref engine.World.Get<TotalWarFormationState>(formation);
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
                $"Total War showcase formation index {formationIndex} exceeds bound formation entity cache {_formationEntities.Length}.");
        }

        Entity formation = _formationEntities[formationIndex];
        if (!engine.World.IsAlive(formation))
        {
            throw new InvalidOperationException(
                $"Total War showcase formation index {formationIndex} was not bound to a live entity before soldier slot sync.");
        }

        if (!engine.World.Has<FacingDirection>(formation))
        {
            throw new InvalidOperationException(
                $"Total War showcase formation entity {formation.Id} requires {nameof(FacingDirection)} for explicit soldier slot facing.");
        }

        return engine.World.Get<FacingDirection>(formation).AngleRad;
    }

    private void TryApplyInitialSelection(GameEngine engine, TotalWarShowcaseConfig config)
    {
        if (_initialSelectionApplied)
        {
            return;
        }

        SelectionRuntime selection = engine.GetService(CoreServiceKeys.SelectionRuntime)
            ?? throw new InvalidOperationException("Total War showcase requires SelectionRuntime before applying configured initial selection.");
        Entity owner = ResolveLocalSelectionOwner(engine);
        int formationIndex = ResolveFormationIndex(config.InitialSelectionFormationId);
        EnsureInitialSelectionScratch(config);

        if ((uint)formationIndex >= (uint)_formationEntities.Length)
        {
            throw new InvalidOperationException(
                $"Total War showcase initial selection formation index {formationIndex} exceeds bound formation entity cache {_formationEntities.Length}.");
        }

        Entity formation = _formationEntities[formationIndex];
        if (!engine.World.IsAlive(formation))
        {
            return;
        }

        if (!SelectionEligibility.CanAcquire(engine.World, owner, formation, selection.TargetRelationFilter))
        {
            throw new InvalidOperationException(
                $"Total War showcase initial selection formation '{config.InitialSelectionFormationId}' must pass selection.targetFilter.relationFilter '{selection.TargetRelationFilter}'.");
        }

        _initialSelectionScratch[0] = formation;
        if (!selection.ReplaceSelection(owner, SelectionSetKeys.LivePrimary, _initialSelectionScratch.AsSpan(0, 1)))
        {
            throw new InvalidOperationException("Total War showcase failed to author its configured initial selection.");
        }

        if (!SelectionContextRuntime.TrySetCurrentView(
                engine.World,
                engine.GlobalContext,
                selection,
                owner,
                SelectionViewKeys.Primary,
                owner,
                SelectionSetKeys.LivePrimary,
                out SelectionViewDescriptor viewDescriptor))
        {
            throw new InvalidOperationException("Total War showcase failed to bind LivePrimary as the primary selection view.");
        }

        if (!selection.TryDescribeSelection(owner, SelectionSetKeys.LivePrimary, out SelectionContainerDescriptor descriptor))
        {
            throw new InvalidOperationException("Total War showcase failed to describe the initial selection it just authored.");
        }

        if (viewDescriptor.Container.Container != descriptor.Container)
        {
            throw new InvalidOperationException("Total War showcase initial selection view does not resolve to LivePrimary.");
        }

        _initialSelectionApplied = true;
    }

    private void EnsureInitialSelectionScratch(TotalWarShowcaseConfig config)
    {
        if (_initialSelectionScratch.Length == config.InitialSelectionEntityCapacity)
        {
            return;
        }

        _initialSelectionScratch = new Entity[config.InitialSelectionEntityCapacity];
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

        throw new InvalidOperationException($"Total War showcase formation '{formationId}' was not planned.");
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

    private static void ConfigureScenarioTeams(MassNavigationSimulationRuntime simulation, TotalWarShowcaseConfig config)
    {
        MassNavigationScenarioTeamConfig[] configuredTeams = simulation.Config.Scenario.Teams;
        int[] teamIds = new int[configuredTeams.Length];
        for (int i = 0; i < configuredTeams.Length; i++)
        {
            teamIds[i] = configuredTeams[i].Id;
        }

        simulation.ConfigureScenarioTeams(teamIds);
        simulation.SetSelectedTeam(ResolveInitialSelectionTeamId(config));
    }

    private static int ResolveInitialSelectionTeamId(TotalWarShowcaseConfig config)
    {
        for (int i = 0; i < config.Formations.Length; i++)
        {
            TotalWarFormationConfig formation = config.Formations[i];
            if (string.Equals(formation.Id, config.InitialSelectionFormationId, StringComparison.Ordinal))
            {
                return formation.TeamId;
            }
        }

        throw new InvalidOperationException(
            $"Total War showcase initial selection formation '{config.InitialSelectionFormationId}' is not configured.");
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

        throw new InvalidOperationException($"Total War showcase formation references MassNavigation scenario team {teamId}, but that team is not configured.");
    }

    private static MassNavigationSimulationRuntime RequireSimulation(GameEngine engine)
    {
        return engine.GetService(MassNavigationKeys.SimulationRuntime)
            ?? throw new InvalidOperationException("Total War showcase requires MassNavigationSimulationRuntime.");
    }

    private static MapId RequireCurrentMapId(GameEngine engine, string configuredMapId)
    {
        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("Total War showcase requires an active map session before scenario bootstrap.");
        if (!string.Equals(session.MapId.Value, configuredMapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Total War showcase scenario bootstrap requires active map '{configuredMapId}', got '{session.MapId.Value}'.");
        }

        return session.MapId;
    }

    private static void ValidateAuthoring(GameEngine engine, TotalWarShowcaseConfig config)
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
            throw new InvalidOperationException("Total War showcase template id must be non-empty.");
        }

        if (!engine.MapLoader.TemplateRegistry.Contains(templateId))
        {
            throw new InvalidOperationException($"Total War showcase requires configured entity template '{templateId}'.");
        }

        EntityTemplateKeyRegistry templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("Total War showcase requires EntityTemplateKeyRegistry.");
        if (!templateKeys.TryGetId(templateId, out int templateKeyId) || templateKeyId <= 0)
        {
            throw new InvalidOperationException($"Total War showcase template '{templateId}' was not registered in EntityTemplateKeyRegistry.");
        }
    }

    private static void MarkPresentationDestroyPending(World world, Entity entity, string label)
    {
        PresentationEntityLifecycle.RequestDestroy(world, entity, $"Total War showcase {label}");
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

    private static void ClearSelection(GameEngine engine)
    {
        SelectionRuntime selection = engine.GetService(CoreServiceKeys.SelectionRuntime)
            ?? throw new InvalidOperationException("Total War showcase requires SelectionRuntime before clearing selection.");
        Entity owner = ResolveLocalSelectionOwner(engine);
        selection.ClearSelection(owner, SelectionSetKeys.LivePrimary);
        selection.ClearSelection(owner, SelectionSetKeys.FormationPrimary);
        selection.ClearSelection(owner, SelectionSetKeys.CommandPreview);
        selection.ClearSelection(owner, SelectionSetKeys.CommandSnapshot);
    }

    private static Entity ResolveLocalSelectionOwner(GameEngine engine)
    {
        if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localPlayerObj) ||
            localPlayerObj is not Entity local ||
            !engine.World.IsAlive(local))
        {
            throw new InvalidOperationException("Total War showcase requires LocalPlayerEntity before mutating selection.");
        }

        return local;
    }

    private static int ResolveLocalPlayerOwnerId(GameEngine engine)
    {
        if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) ||
            localObj is not Entity local ||
            !engine.World.IsAlive(local))
        {
            throw new InvalidOperationException("Total War showcase requires LocalPlayerEntity before binding local formation ownership.");
        }

        if (!engine.World.TryGet(local, out PlayerOwner owner))
        {
            throw new InvalidOperationException("Total War showcase LocalPlayerEntity must author PlayerOwner before binding local formation ownership.");
        }

        return owner.PlayerId;
    }

    private readonly struct TotalWarFormationPlan
    {
        public TotalWarFormationPlan(
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

    private readonly struct TotalWarSoldierAgentSpawnPlan
    {
        public TotalWarSoldierAgentSpawnPlan(
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

    private readonly struct TotalWarObstacleOverlayPlan
    {
        public TotalWarObstacleOverlayPlan(float worldXCm, float worldYCm, float radiusCm)
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

    private readonly record struct PendingSoldierBinding(Entity Entity, MassCrowdFormationFollower Follower);

    private readonly record struct PendingObstacleOverlayBinding(Entity Entity, int OverlayIndex);
}
