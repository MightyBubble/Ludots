using System;
using System.Collections.Generic;
using System.Numerics;
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
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using MassNavigationMod;
using MassNavigationMod.Runtime;

namespace MassNavigationTotalWarEntryMod.Runtime;

internal sealed class TotalWarShowcaseRuntime
{
    private static readonly float DiscSlotGoldenAngleRadians = MathF.PI * (3f - MathF.Sqrt(5f));

    private readonly TotalWarSpawnReceiptRuntime _spawnReceipts = new();
    private TotalWarShowcaseConfig? _config;
    private MassNavigationAgentSeed[] _agentSeeds = Array.Empty<MassNavigationAgentSeed>();
    private TotalWarSoldierAgentSpawnPlan[] _soldierAgentPlans = Array.Empty<TotalWarSoldierAgentSpawnPlan>();
    private TotalWarFormationPlan[] _formationPlans = Array.Empty<TotalWarFormationPlan>();
    private TotalWarObstacleOverlayPlan[] _obstacleOverlayPlans = Array.Empty<TotalWarObstacleOverlayPlan>();
    private Entity[] _formationEntities = Array.Empty<Entity>();
    private Entity[] _soldierEntitiesByAgentIndex = Array.Empty<Entity>();
    private Entity[] _obstacleOverlayEntities = Array.Empty<Entity>();
    private Entity[] _initialSelectionScratch = Array.Empty<Entity>();
    private float[] _soldierTargetWorldXCm = Array.Empty<float>();
    private float[] _soldierTargetWorldYCm = Array.Empty<float>();
    private byte[] _soldierTargetSnapshotInitialized = Array.Empty<byte>();
    private bool _systemsInstalled;
    private bool _scenarioSpawned;
    private bool _initialSelectionApplied;
    private int _receiptChannelId;
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
        _initialSelectionApplied = false;
        ResetSpawnReceipts(engine, config);
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
        engine.InsertSystemBeforeRequired<MassNavigationPreSimulationStepSystem>(
            new TotalWarSpawnReceiptBindingSystem(engine, this, simulation),
            SystemGroup.PostMovement);
        engine.InsertSystemBeforeRequired<MassNavigationPreSimulationStepSystem>(
            new TotalWarFormationRuntimeSystem(engine, this, simulation),
            SystemGroup.PostMovement);
        TotalWarShowcaseConfig config = EnsureConfig(engine);
        engine.RegisterPresentationSystem(new TotalWarFormationOutlinePresentationSystem(engine, this, config));
        engine.RegisterPresentationSystem(new TotalWarObstacleOverlayPresentationSystem(engine, this, simulation.WorldConfig.Obstacles.Length));
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
            _initialSelectionApplied = false;
            ResetSpawnReceipts(engine, config);
            DestroyShowcaseOwnedEntities(engine);
            ClearFormationCaches();
        }

        if (!_scenarioSpawned)
        {
            SpawnScenario(engine, config);
            return;
        }

        if (_spawnReceipts.PendingCount != 0)
        {
            return;
        }

            SyncSoldierTargetsFromFormationAgents(engine, simulation, config.SoldierTargetSync);
            SyncFormationStates(engine, simulation);
            TryApplyInitialSelection(engine, config);
    }

    private void SpawnScenario(GameEngine engine, TotalWarShowcaseConfig config)
    {
        MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("Total War showcase requires RuntimeEntitySpawnQueue.");
        RuntimeEntitySpawnReceiptQueue receiptQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptQueue)
            ?? throw new InvalidOperationException("Total War showcase requires RuntimeEntitySpawnReceiptQueue.");
        int receiptChannelId = ResolveReceiptChannelId(engine, config);
        int pendingReceipts = receiptQueue.CountForChannel(receiptChannelId);
        if (pendingReceipts != 0)
        {
            throw new InvalidOperationException(
                $"Total War showcase requires its runtime spawn receipt channel to be empty before scenario bootstrap; pending={pendingReceipts}.");
        }
        int pendingRequests = spawnQueue.CountForReceiptChannel(receiptChannelId);
        if (pendingRequests != 0)
        {
            throw new InvalidOperationException(
                $"Total War showcase requires its runtime spawn request channel to be empty before scenario bootstrap; pending={pendingRequests}.");
        }

        ClearSelection(engine);
        BuildAgentPlans(engine, simulation, config);
        simulation.ResetRuntimeState(engine.World, _agentSeeds);
        BuildObstacleOverlayPlans(simulation);
        DestroyShowcaseOwnedEntities(engine);

        int spawnRequestCount = _soldierAgentPlans.Length + _formationPlans.Length + _obstacleOverlayPlans.Length;
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

        ResetSpawnReceipts(engine, config);
        MapId mapId = RequireCurrentMapId(engine, config.MapId);
        EnqueueFormationAgentSpawns(engine, config, spawnQueue, mapId, receiptChannelId);
        EnqueueObstacleOverlaySpawns(engine, config, spawnQueue, mapId, receiptChannelId);
        EnsureSoldierEntityCache();
        for (int i = 0; i < _soldierAgentPlans.Length; i++)
        {
            TotalWarSoldierAgentSpawnPlan plan = _soldierAgentPlans[i];
            int receiptId = _spawnReceipts.Allocate(TotalWarSpawnReceiptBinding.ForSoldier(
                plan.MassNavAgentIndex,
                plan.FormationIndex,
                plan.SlotIndex,
                plan.TemplateId));

            var request = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = plan.TemplateId,
                MapId = mapId,
                WorldPositionCm = Fix64Vec2.FromInt((int)MathF.Round(plan.WorldXCm), (int)MathF.Round(plan.WorldYCm)),
                HasWorldPosition = 1,
                FacingAngleRad = plan.FacingRad,
                HasFacing = 1,
                EmitReceipt = 1,
                ReceiptChannelId = receiptChannelId,
                ReceiptId = receiptId,
            };

            if (!spawnQueue.TryEnqueue(in request))
            {
                throw new InvalidOperationException("Total War showcase failed to enqueue runtime entity spawn request.");
            }
        }

        _scenarioSpawned = true;
        _initialSelectionApplied = false;
        _observedSceneResetCount = simulation.SceneResetCount;
        simulation.MarkScenarioSpawned();
        simulation.MarkStructuralChange();
    }

    public bool TryConsumeReceipt(int receiptId, out TotalWarSpawnReceiptBinding binding)
    {
        return _spawnReceipts.TryConsume(receiptId, out binding);
    }

    public int ResolveReceiptChannelId(GameEngine engine, TotalWarShowcaseConfig config)
    {
        if (_receiptChannelId > 0)
        {
            return _receiptChannelId;
        }

        RuntimeEntitySpawnReceiptChannelRegistry channels = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptChannelRegistry)
            ?? throw new InvalidOperationException("Total War showcase requires RuntimeEntitySpawnReceiptChannelRegistry.");
        _receiptChannelId = channels.Register(config.RuntimeSpawnReceiptChannelKey);
        return _receiptChannelId;
    }

    private void ResetSpawnReceipts(GameEngine engine, TotalWarShowcaseConfig config)
    {
        int receiptChannelId = ResolveReceiptChannelId(engine, config);
        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("Total War showcase requires RuntimeEntitySpawnQueue.");
        spawnQueue.RemoveForReceiptChannel(receiptChannelId);
        RuntimeEntitySpawnReceiptQueue receiptQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptQueue)
            ?? throw new InvalidOperationException("Total War showcase requires RuntimeEntitySpawnReceiptQueue.");
        while (receiptQueue.TryDequeueForChannel(receiptChannelId, out _))
        {
        }

        _spawnReceipts.Reset();
    }

    internal void ResetSpawnReceiptsForTests(GameEngine engine, TotalWarShowcaseConfig config)
    {
        ResetSpawnReceipts(engine, config);
    }

    public void RegisterSpawnedSoldier(Entity entity, in TotalWarSpawnReceiptBinding binding)
    {
        if ((uint)binding.MassNavAgentIndex >= (uint)_soldierEntitiesByAgentIndex.Length)
        {
            throw new InvalidOperationException($"Total War showcase soldier agent MassNav index {binding.MassNavAgentIndex} exceeds its scenario cache.");
        }

        if (_soldierEntitiesByAgentIndex[binding.MassNavAgentIndex] != Entity.Null)
        {
            throw new InvalidOperationException($"Total War showcase soldier agent MassNav index {binding.MassNavAgentIndex} was already bound.");
        }

        _soldierEntitiesByAgentIndex[binding.MassNavAgentIndex] = entity;
    }

    public void RegisterSpawnedFormationAgent(GameEngine engine, Entity entity, in TotalWarSpawnReceiptBinding binding)
    {
        if ((uint)binding.FormationIndex >= (uint)_formationPlans.Length)
        {
            throw new InvalidOperationException($"Total War showcase formation index {binding.FormationIndex} exceeds its scenario cache.");
        }

        if (_formationEntities.Length != _formationPlans.Length)
        {
            throw new InvalidOperationException("Total War showcase formation entity cache was not initialized before formation agent receipt binding.");
        }

        if (_formationEntities[binding.FormationIndex] != Entity.Null)
        {
            throw new InvalidOperationException($"Total War showcase formation index {binding.FormationIndex} was already bound.");
        }

        TotalWarFormationPlan plan = _formationPlans[binding.FormationIndex];
        TotalWarFormationConfig formation = ActiveConfig.Formations[binding.FormationIndex];
        _formationEntities[binding.FormationIndex] = entity;
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

    public void RegisterSpawnedObstacleOverlay(Entity entity, in TotalWarSpawnReceiptBinding binding)
    {
        if (_obstacleOverlayEntities.Length != _obstacleOverlayPlans.Length)
        {
            throw new InvalidOperationException("Total War showcase obstacle overlay cache was not initialized before overlay receipt binding.");
        }

        for (int i = 0; i < _obstacleOverlayEntities.Length; i++)
        {
            if (_obstacleOverlayEntities[i] != Entity.Null)
            {
                continue;
            }

            _obstacleOverlayEntities[i] = entity;
            return;
        }

        throw new InvalidOperationException(
            $"Total War showcase received more obstacle overlay entities than planned ({_obstacleOverlayEntities.Length}) for template '{binding.TemplateId}'.");
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

    public bool TryGetSoldierEntityByAgentIndex(int soldierAgentIndex, out Entity entity)
    {
        if ((uint)soldierAgentIndex >= (uint)_soldierEntitiesByAgentIndex.Length)
        {
            entity = Entity.Null;
            return false;
        }

        entity = _soldierEntitiesByAgentIndex[soldierAgentIndex];
        return entity != Entity.Null;
    }

    private void BuildAgentPlans(GameEngine engine, MassNavigationSimulationRuntime simulation, TotalWarShowcaseConfig config)
    {
        MassNavigationAgentProfileSetConfig profileSet = simulation.Config.AgentProfiles;
        config.ValidateAgentProfileReferences(profileSet);

        int agentCount = config.Formations.Length;
        int soldierCount = 0;
        for (int i = 0; i < config.Formations.Length; i++)
        {
            int count = config.Formations[i].SoldierCount;
            agentCount += count;
            soldierCount += count;
        }

        if (_agentSeeds.Length != agentCount)
        {
            _agentSeeds = new MassNavigationAgentSeed[agentCount];
        }

        if (_soldierAgentPlans.Length != soldierCount)
        {
            _soldierAgentPlans = new TotalWarSoldierAgentSpawnPlan[soldierCount];
        }

        if (_formationPlans.Length != config.Formations.Length)
        {
            _formationPlans = new TotalWarFormationPlan[config.Formations.Length];
        }

        int agentIndex = 0;
        MassNavigationAgentLayer formationLayer = RequireTemplateLayer(engine, config.FormationAgent.TemplateId);
        MassNavigationAgentProfileConfig formationProfile = config.ResolveFormationAgentProfile(profileSet);
        for (int formationIndex = 0; formationIndex < config.Formations.Length; formationIndex++)
        {
            TotalWarFormationConfig formation = config.Formations[formationIndex];
            float facingRad = formation.FacingDeg * (MathF.PI / 180f);
            Vector2 local = simulation.ToLocalCm(new Vector2(formation.CenterXCm, formation.CenterYCm));
            _agentSeeds[agentIndex] = new MassNavigationAgentSeed(
                formation.TeamId,
                local.X,
                local.Y,
                formationProfile.Heavy,
                formationProfile.NavMass,
                formationProfile.VisualScale,
                formationProfile.BodyRadiusCm,
                formationProfile.SpeedCmPerSecond,
                formationLayer);
            _formationPlans[formationIndex] = new TotalWarFormationPlan(
                formationIndex,
                agentIndex,
                formation.Id,
                formation.Label,
                formation.TeamId,
                firstSoldierAgentIndex: 0,
                firstSoldierPlanIndex: 0,
                formation.SoldierCount,
                formation.CenterXCm,
                formation.CenterYCm,
                facingRad,
                lastTargetCenterWorldXCm: 0f,
                lastTargetCenterWorldYCm: 0f,
                lastTargetFacingRad: 0f,
                lastTargetSnapshotInitialized: false,
                lastCarrierCenterWorldXCm: formation.CenterXCm,
                lastCarrierCenterWorldYCm: formation.CenterYCm,
                carrierSnapshotInitialized: false,
                targetRevision: 0);
            agentIndex++;
        }

        int soldierPlanIndex = 0;
        for (int formationIndex = 0; formationIndex < config.Formations.Length; formationIndex++)
        {
            TotalWarFormationConfig formation = config.Formations[formationIndex];
            int firstSoldierAgentIndex = agentIndex;
            int firstSoldierPlanIndex = soldierPlanIndex;
            float facingRad = formation.FacingDeg * (MathF.PI / 180f);
            MassNavigationAgentLayer soldierLayer = RequireTemplateLayer(engine, formation.SoldierAgent.TemplateId);
            MassNavigationAgentProfileConfig soldierProfile = config.ResolveSoldierAgentProfile(profileSet, formationIndex);
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
                Vector2 local = simulation.ToLocalCm(new Vector2(worldX, worldY));
                _agentSeeds[agentIndex] = new MassNavigationAgentSeed(
                    formation.TeamId,
                    local.X,
                    local.Y,
                    soldierProfile.Heavy,
                    soldierProfile.NavMass,
                    soldierProfile.VisualScale,
                    soldierProfile.BodyRadiusCm,
                    soldierProfile.SpeedCmPerSecond,
                    soldierLayer);
                _soldierAgentPlans[soldierPlanIndex] = new TotalWarSoldierAgentSpawnPlan(
                    agentIndex,
                    formationIndex,
                    slotIndex,
                    formation.TeamId,
                    formation.SoldierAgent.TemplateId,
                    soldierProfile.Heavy,
                    soldierProfile.NavMass,
                    soldierProfile.VisualScale,
                    soldierProfile.BodyRadiusCm,
                    soldierProfile.SpeedCmPerSecond,
                    worldX,
                    worldY,
                    facingRad,
                    slotOffset.X,
                    slotOffset.Y);
                agentIndex++;
                soldierPlanIndex++;
            }

            _formationPlans[formationIndex] = new TotalWarFormationPlan(
                formationIndex,
                _formationPlans[formationIndex].MassNavAgentIndex,
                formation.Id,
                formation.Label,
                formation.TeamId,
                firstSoldierAgentIndex,
                firstSoldierPlanIndex,
                formation.SoldierCount,
                formation.CenterXCm,
                formation.CenterYCm,
                facingRad,
                lastTargetCenterWorldXCm: 0f,
                lastTargetCenterWorldYCm: 0f,
                lastTargetFacingRad: 0f,
                lastTargetSnapshotInitialized: false,
                lastCarrierCenterWorldXCm: _formationPlans[formationIndex].LastCarrierCenterWorldXCm,
                lastCarrierCenterWorldYCm: _formationPlans[formationIndex].LastCarrierCenterWorldYCm,
                carrierSnapshotInitialized: false,
                targetRevision: 0);
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
        if (_soldierEntitiesByAgentIndex.Length != _agentSeeds.Length)
        {
            _soldierEntitiesByAgentIndex = new Entity[_agentSeeds.Length];
        }

        Array.Fill(_soldierEntitiesByAgentIndex, Entity.Null);
        if (_soldierTargetWorldXCm.Length != _agentSeeds.Length)
        {
            _soldierTargetWorldXCm = new float[_agentSeeds.Length];
            _soldierTargetWorldYCm = new float[_agentSeeds.Length];
            _soldierTargetSnapshotInitialized = new byte[_agentSeeds.Length];
        }
        else
        {
            Array.Clear(_soldierTargetWorldXCm);
            Array.Clear(_soldierTargetWorldYCm);
            Array.Clear(_soldierTargetSnapshotInitialized);
        }

    }

    private void EnqueueFormationAgentSpawns(
        GameEngine engine,
        TotalWarShowcaseConfig config,
        RuntimeEntitySpawnQueue spawnQueue,
        MapId mapId,
        int receiptChannelId)
    {
        if (_formationEntities.Length != _formationPlans.Length)
        {
            _formationEntities = new Entity[_formationPlans.Length];
        }

        Array.Fill(_formationEntities, Entity.Null);
        for (int i = 0; i < _formationPlans.Length; i++)
        {
            TotalWarFormationPlan plan = _formationPlans[i];
            int receiptId = _spawnReceipts.Allocate(TotalWarSpawnReceiptBinding.ForFormationAgent(
                plan.MassNavAgentIndex,
                plan.FormationIndex,
                config.FormationAgent.TemplateId));
            var request = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = config.FormationAgent.TemplateId,
                MapId = mapId,
                WorldPositionCm = Fix64Vec2.FromInt((int)MathF.Round(plan.InitialCenterXCm), (int)MathF.Round(plan.InitialCenterYCm)),
                HasWorldPosition = 1,
                FacingAngleRad = plan.FacingRad,
                HasFacing = 1,
                EmitReceipt = 1,
                ReceiptChannelId = receiptChannelId,
                ReceiptId = receiptId,
            };

            if (!spawnQueue.TryEnqueue(in request))
            {
                throw new InvalidOperationException("Total War showcase failed to enqueue formation agent spawn request.");
            }
        }
    }

    private void EnqueueObstacleOverlaySpawns(
        GameEngine engine,
        TotalWarShowcaseConfig config,
        RuntimeEntitySpawnQueue spawnQueue,
        MapId mapId,
        int receiptChannelId)
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
            int receiptId = _spawnReceipts.Allocate(TotalWarSpawnReceiptBinding.ForObstacleOverlay(
                plan.RadiusCm,
                config.ObstacleOverlay.TemplateId));
            var request = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = config.ObstacleOverlay.TemplateId,
                MapId = mapId,
                WorldPositionCm = Fix64Vec2.FromInt((int)MathF.Round(plan.WorldXCm), (int)MathF.Round(plan.WorldYCm)),
                HasWorldPosition = 1,
                EmitReceipt = 1,
                ReceiptChannelId = receiptChannelId,
                ReceiptId = receiptId,
            };

            if (!spawnQueue.TryEnqueue(in request))
            {
                throw new InvalidOperationException("Total War showcase failed to enqueue obstacle overlay spawn request.");
            }
        }
    }

    private void SyncSoldierTargetsFromFormationAgents(
        GameEngine engine,
        MassNavigationSimulationRuntime simulation,
        TotalWarSoldierTargetSyncConfig targetSync)
    {
        float targetChangeEpsilonSq = targetSync.TargetChangeEpsilonCm * targetSync.TargetChangeEpsilonCm;
        for (int formationIndex = 0; formationIndex < _formationPlans.Length; formationIndex++)
        {
            TotalWarFormationPlan plan = _formationPlans[formationIndex];
            if ((uint)plan.MassNavAgentIndex >= (uint)simulation.NavigationAgentCount)
            {
                throw new InvalidOperationException(
                    $"Total War showcase formation agent MassNav index {plan.MassNavAgentIndex} exceeds MassNavigation agent count {simulation.NavigationAgentCount}.");
            }

            MassNavigationCarriedRangeSyncResult carrierSync = simulation.SyncCarriedAgentRangeToCarrier(
                plan.MassNavAgentIndex,
                plan.FirstSoldierAgentIndex,
                plan.SoldierCount,
                plan.CarrierSnapshotInitialized,
                plan.LastCarrierCenterWorldXCm,
                plan.LastCarrierCenterWorldYCm);
            float formationWorldX = carrierSync.CarrierWorldXCm;
            float formationWorldY = carrierSync.CarrierWorldYCm;
            if (carrierSync.AppliedDisplacement)
            {
                TranslateSoldierTargetCacheRange(
                    plan.FirstSoldierAgentIndex,
                    plan.SoldierCount,
                    carrierSync.DisplacementWorldXCm,
                    carrierSync.DisplacementWorldYCm);
            }

            plan = plan.WithCarrierSnapshot(formationWorldX, formationWorldY);
            float anchorLocalX = carrierSync.CarrierLocalXCm;
            float anchorLocalY = carrierSync.CarrierLocalYCm;

            float facingRad = ResolveFormationFacing(engine, plan.FormationIndex);

            float forwardX = MathF.Cos(facingRad);
            float forwardY = MathF.Sin(facingRad);
            float lateralX = -forwardY;
            float lateralY = forwardX;
            float deltaX = formationWorldX - plan.LastTargetCenterWorldXCm;
            float deltaY = formationWorldY - plan.LastTargetCenterWorldYCm;
            float facingDelta = MathF.Abs(NormalizeAngleRadians(facingRad - plan.LastTargetFacingRad));
            bool facingChanged =
                !plan.LastTargetSnapshotInitialized ||
                facingDelta >= targetSync.FacingChangeEpsilonRadians;
            bool targetChanged =
                !plan.LastTargetSnapshotInitialized ||
                (deltaX * deltaX) + (deltaY * deltaY) >= targetChangeEpsilonSq ||
                facingChanged;
            if (!targetChanged)
            {
                _formationPlans[formationIndex] = plan;
                continue;
            }

            for (int slotIndex = 0; slotIndex < plan.SoldierCount; slotIndex++)
            {
                int soldierAgentIndex = plan.FirstSoldierAgentIndex + slotIndex;
                if ((uint)soldierAgentIndex >= (uint)simulation.NavigationAgentCount)
                {
                    throw new InvalidOperationException(
                        $"Total War showcase soldier agent MassNav index {soldierAgentIndex} exceeds MassNavigation agent count {simulation.NavigationAgentCount}.");
                }

                int soldierPlanIndex = plan.FirstSoldierPlanIndex + slotIndex;
                if ((uint)soldierPlanIndex >= (uint)_soldierAgentPlans.Length)
                {
                    throw new InvalidOperationException(
                        $"Total War showcase soldier plan index {soldierPlanIndex} exceeds planned soldier agent count {_soldierAgentPlans.Length}.");
                }

                TotalWarSoldierAgentSpawnPlan soldierAgentPlan = _soldierAgentPlans[soldierPlanIndex];
                float slotOffsetX = (lateralX * soldierAgentPlan.SlotOffsetXCm) + (forwardX * soldierAgentPlan.SlotOffsetYCm);
                float slotOffsetY = (lateralY * soldierAgentPlan.SlotOffsetXCm) + (forwardY * soldierAgentPlan.SlotOffsetYCm);
                MassNavigationCarriedSlotTarget resolvedTarget = simulation.ResolveCarriedAgentSlotTarget(
                    soldierAgentIndex,
                    anchorLocalX,
                    anchorLocalY,
                    slotOffsetX,
                    slotOffsetY);
                if (ShouldWriteSoldierTarget(soldierAgentIndex, resolvedTarget.WorldXCm, resolvedTarget.WorldYCm, targetChangeEpsilonSq))
                {
                    simulation.ApplyCarriedAgentSlotTarget(soldierAgentIndex, in resolvedTarget, resetRecovery: targetChanged);
                }

                if (facingChanged)
                {
                    WriteSoldierFacing(engine, soldierAgentIndex, facingRad);
                }
            }

            _formationPlans[formationIndex] = plan.WithTargetSnapshot(
                formationWorldX,
                formationWorldY,
                facingRad,
                plan.TargetRevision + 1);
        }
    }

    private void TranslateSoldierTargetCacheRange(
        int firstSoldierAgentIndex,
        int soldierCount,
        float deltaXCm,
        float deltaYCm)
    {
        if (soldierCount <= 0 || (deltaXCm == 0f && deltaYCm == 0f))
        {
            return;
        }

        int end = firstSoldierAgentIndex + soldierCount;
        if (firstSoldierAgentIndex < 0 ||
            end < firstSoldierAgentIndex ||
            end > _soldierTargetSnapshotInitialized.Length ||
            end > _soldierTargetWorldXCm.Length ||
            end > _soldierTargetWorldYCm.Length)
        {
            throw new InvalidOperationException(
                $"Total War showcase soldier target cache does not cover carried MassNav agent range [{firstSoldierAgentIndex}, {end}).");
        }

        for (int agentIndex = firstSoldierAgentIndex; agentIndex < end; agentIndex++)
        {
            if (_soldierTargetSnapshotInitialized[agentIndex] == 0)
            {
                continue;
            }

            _soldierTargetWorldXCm[agentIndex] += deltaXCm;
            _soldierTargetWorldYCm[agentIndex] += deltaYCm;
        }
    }

    private bool ShouldWriteSoldierTarget(
        int soldierAgentIndex,
        float targetWorldXCm,
        float targetWorldYCm,
        float targetChangeEpsilonSq)
    {
        if ((uint)soldierAgentIndex >= (uint)_soldierTargetWorldXCm.Length ||
            (uint)soldierAgentIndex >= (uint)_soldierTargetWorldYCm.Length ||
            (uint)soldierAgentIndex >= (uint)_soldierTargetSnapshotInitialized.Length)
        {
            throw new InvalidOperationException(
                $"Total War showcase soldier target cache does not cover MassNav agent index {soldierAgentIndex}.");
        }

        bool initialized = _soldierTargetSnapshotInitialized[soldierAgentIndex] != 0;
        float deltaX = targetWorldXCm - _soldierTargetWorldXCm[soldierAgentIndex];
        float deltaY = targetWorldYCm - _soldierTargetWorldYCm[soldierAgentIndex];
        if (initialized && (deltaX * deltaX) + (deltaY * deltaY) < targetChangeEpsilonSq)
        {
            return false;
        }

        _soldierTargetWorldXCm[soldierAgentIndex] = targetWorldXCm;
        _soldierTargetWorldYCm[soldierAgentIndex] = targetWorldYCm;
        _soldierTargetSnapshotInitialized[soldierAgentIndex] = 1;
        return true;
    }

    private static float NormalizeAngleRadians(float angle)
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
            if ((uint)plan.MassNavAgentIndex >= (uint)simulation.NavigationAgentCount)
            {
                throw new InvalidOperationException(
                    $"Total War showcase formation agent MassNav index {plan.MassNavAgentIndex} exceeds MassNavigation agent count {simulation.NavigationAgentCount}.");
            }

            Vector2 centerWorld = simulation.GetAgentWorldPositionCm(plan.MassNavAgentIndex);
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

    private void WriteSoldierFacing(GameEngine engine, int soldierAgentIndex, float facingRad)
    {
        if ((uint)soldierAgentIndex >= (uint)_soldierEntitiesByAgentIndex.Length)
        {
            throw new InvalidOperationException(
                $"Total War showcase soldier agent MassNav index {soldierAgentIndex} exceeds bound soldier entity cache {_soldierEntitiesByAgentIndex.Length}.");
        }

        Entity soldier = _soldierEntitiesByAgentIndex[soldierAgentIndex];
        if (!engine.World.IsAlive(soldier))
        {
            throw new InvalidOperationException(
                $"Total War showcase soldier agent MassNav index {soldierAgentIndex} was not bound to a live entity before slot sync.");
        }

        if (!engine.World.Has<FacingDirection>(soldier))
        {
            throw new InvalidOperationException(
                $"Total War showcase soldier entity {soldier.Id} requires {nameof(FacingDirection)} for explicit formation facing.");
        }

        ref FacingDirection facing = ref engine.World.Get<FacingDirection>(soldier);
        if (facing.AngleRad != facingRad)
        {
            facing.AngleRad = facingRad;
        }
    }

    private void TryApplyInitialSelection(GameEngine engine, TotalWarShowcaseConfig config)
    {
        if (_initialSelectionApplied || _spawnReceipts.PendingCount != 0)
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
            throw new InvalidOperationException(
                $"Total War showcase initial selection formation '{config.InitialSelectionFormationId}' was not bound to a live entity.");
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

        for (int i = 0; i < _soldierEntitiesByAgentIndex.Length; i++)
        {
            Entity entity = _soldierEntitiesByAgentIndex[i];
            if (engine.World.IsAlive(entity))
            {
                MarkPresentationDestroyPending(engine.World, entity, "soldier agent");
            }

            _soldierEntitiesByAgentIndex[i] = Entity.Null;
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
        if (_soldierEntitiesByAgentIndex.Length > 0)
        {
            Array.Fill(_soldierEntitiesByAgentIndex, Entity.Null);
        }

        if (_formationEntities.Length > 0)
        {
            Array.Fill(_formationEntities, Entity.Null);
        }

        if (_obstacleOverlayEntities.Length > 0)
        {
            Array.Fill(_obstacleOverlayEntities, Entity.Null);
        }

        if (_soldierTargetSnapshotInitialized.Length > 0)
        {
            Array.Clear(_soldierTargetSnapshotInitialized);
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

    private static MassNavigationAgentLayer RequireTemplateLayer(GameEngine engine, string templateId)
    {
        EntityTemplate template = RequireTemplate(engine, templateId);
        return MassNavigationTemplateLayerResolver.RequireAgentLayer(template, templateId);
    }

    private static EntityTemplate RequireTemplate(GameEngine engine, string templateId)
    {
        EntityTemplate template = engine.MapLoader.TemplateRegistry.Get(templateId);
        return template ?? throw new InvalidOperationException($"Total War showcase requires configured entity template '{templateId}'.");
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
            int massNavAgentIndex,
            string id,
            string label,
            int teamId,
            int firstSoldierAgentIndex,
            int firstSoldierPlanIndex,
            int soldierCount,
            float initialCenterXCm,
            float initialCenterYCm,
            float facingRad,
            float lastTargetCenterWorldXCm,
            float lastTargetCenterWorldYCm,
            float lastTargetFacingRad,
            bool lastTargetSnapshotInitialized,
            float lastCarrierCenterWorldXCm,
            float lastCarrierCenterWorldYCm,
            bool carrierSnapshotInitialized,
            int targetRevision)
        {
            FormationIndex = formationIndex;
            MassNavAgentIndex = massNavAgentIndex;
            Id = id;
            Label = label;
            TeamId = teamId;
            FirstSoldierAgentIndex = firstSoldierAgentIndex;
            FirstSoldierPlanIndex = firstSoldierPlanIndex;
            SoldierCount = soldierCount;
            InitialCenterXCm = initialCenterXCm;
            InitialCenterYCm = initialCenterYCm;
            FacingRad = facingRad;
            LastTargetCenterWorldXCm = lastTargetCenterWorldXCm;
            LastTargetCenterWorldYCm = lastTargetCenterWorldYCm;
            LastTargetFacingRad = lastTargetFacingRad;
            LastTargetSnapshotInitialized = lastTargetSnapshotInitialized;
            LastCarrierCenterWorldXCm = lastCarrierCenterWorldXCm;
            LastCarrierCenterWorldYCm = lastCarrierCenterWorldYCm;
            CarrierSnapshotInitialized = carrierSnapshotInitialized;
            TargetRevision = targetRevision;
        }

        public int FormationIndex { get; }
        public int MassNavAgentIndex { get; }
        public string Id { get; }
        public string Label { get; }
        public int TeamId { get; }
        public int FirstSoldierAgentIndex { get; }
        public int FirstSoldierPlanIndex { get; }
        public int SoldierCount { get; }
        public float InitialCenterXCm { get; }
        public float InitialCenterYCm { get; }
        public float FacingRad { get; }
        public float LastTargetCenterWorldXCm { get; }
        public float LastTargetCenterWorldYCm { get; }
        public float LastTargetFacingRad { get; }
        public bool LastTargetSnapshotInitialized { get; }
        public float LastCarrierCenterWorldXCm { get; }
        public float LastCarrierCenterWorldYCm { get; }
        public bool CarrierSnapshotInitialized { get; }
        public int TargetRevision { get; }

        public TotalWarFormationPlan WithTargetSnapshot(
            float centerWorldXCm,
            float centerWorldYCm,
            float facingRad,
            int targetRevision)
        {
            return new TotalWarFormationPlan(
                FormationIndex,
                MassNavAgentIndex,
                Id,
                Label,
                TeamId,
                FirstSoldierAgentIndex,
                FirstSoldierPlanIndex,
                SoldierCount,
                InitialCenterXCm,
                InitialCenterYCm,
                FacingRad,
                centerWorldXCm,
                centerWorldYCm,
                facingRad,
                lastTargetSnapshotInitialized: true,
                LastCarrierCenterWorldXCm,
                LastCarrierCenterWorldYCm,
                CarrierSnapshotInitialized,
                targetRevision);
        }

        public TotalWarFormationPlan WithCarrierSnapshot(float centerWorldXCm, float centerWorldYCm)
        {
            return new TotalWarFormationPlan(
                FormationIndex,
                MassNavAgentIndex,
                Id,
                Label,
                TeamId,
                FirstSoldierAgentIndex,
                FirstSoldierPlanIndex,
                SoldierCount,
                InitialCenterXCm,
                InitialCenterYCm,
                FacingRad,
                LastTargetCenterWorldXCm,
                LastTargetCenterWorldYCm,
                LastTargetFacingRad,
                LastTargetSnapshotInitialized,
                centerWorldXCm,
                centerWorldYCm,
                carrierSnapshotInitialized: true,
                TargetRevision);
        }
    }

    private readonly struct TotalWarSoldierAgentSpawnPlan
    {
        public TotalWarSoldierAgentSpawnPlan(
            int massNavAgentIndex,
            int formationIndex,
            int slotIndex,
            int teamId,
            string templateId,
            bool heavy,
            float navMass,
            float visualScale,
            float bodyRadiusCm,
            float speedCmPerSecond,
            float worldXCm,
            float worldYCm,
            float facingRad,
            float slotOffsetXCm,
            float slotOffsetYCm)
        {
            MassNavAgentIndex = massNavAgentIndex;
            FormationIndex = formationIndex;
            SlotIndex = slotIndex;
            TeamId = teamId;
            TemplateId = templateId;
            Heavy = heavy;
            NavMass = navMass;
            VisualScale = visualScale;
            BodyRadiusCm = bodyRadiusCm;
            SpeedCmPerSecond = speedCmPerSecond;
            WorldXCm = worldXCm;
            WorldYCm = worldYCm;
            FacingRad = facingRad;
            SlotOffsetXCm = slotOffsetXCm;
            SlotOffsetYCm = slotOffsetYCm;
        }

        public int MassNavAgentIndex { get; }
        public int FormationIndex { get; }
        public int SlotIndex { get; }
        public int TeamId { get; }
        public string TemplateId { get; }
        public bool Heavy { get; }
        public float NavMass { get; }
        public float VisualScale { get; }
        public float BodyRadiusCm { get; }
        public float SpeedCmPerSecond { get; }
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
}
