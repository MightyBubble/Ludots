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
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using MassNavigationMod;
using MassNavigationMod.Runtime;

namespace MassNavigationTotalWarEntryMod.Runtime;

internal sealed class TotalWarShowcaseRuntime
{
    private static readonly float DiscSlotGoldenAngleRadians = MathF.PI * (3f - MathF.Sqrt(5f));

    private readonly TotalWarSpawnReceiptRuntime _spawnReceipts = new();
    private TotalWarShowcaseConfig? _config;
    private MassNavigationAgentSeed[] _agentSeeds = Array.Empty<MassNavigationAgentSeed>();
    private TotalWarUnitSpawnPlan[] _unitPlans = Array.Empty<TotalWarUnitSpawnPlan>();
    private TotalWarFormationPlan[] _formationPlans = Array.Empty<TotalWarFormationPlan>();
    private Entity[] _formationEntities = Array.Empty<Entity>();
    private Entity[] _soldierEntitiesByUnitIndex = Array.Empty<Entity>();
    private Entity[] _initialSelectionScratch = Array.Empty<Entity>();
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
        DestroyFormationAnchors(engine);
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
        engine.RegisterSystem(new TotalWarSpawnReceiptBindingSystem(engine, this, simulation), SystemGroup.PostMovement);
        engine.RegisterSystem(new TotalWarFormationRuntimeSystem(engine, this, simulation), SystemGroup.PostMovement);
        engine.RegisterPresentationSystem(new TotalWarFormationOutlinePresentationSystem(engine, this));
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
            DestroyFormationAnchors(engine);
            ClearFormationCaches();
        }

        if (!_scenarioSpawned)
        {
            SpawnScenario(engine, config);
            return;
        }

        SyncFormationStates(engine, simulation, config.FormationSync);
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

        BuildScenarioPlans(simulation, config);
        int spawnRequestCount = _unitPlans.Length + _formationPlans.Length;
        if (spawnQueue.FreeCapacity < spawnRequestCount)
        {
            throw new InvalidOperationException(
                $"Total War showcase requires RuntimeEntitySpawnQueue free capacity {spawnRequestCount}, actual {spawnQueue.FreeCapacity}.");
        }

        ClearSelection(engine);
        simulation.ResetRuntimeState(engine.World, _agentSeeds);
        DestroyFormationAnchors(engine);

        ConfigureScenarioTeams(simulation, config);
        ConfigureRelationships(simulation.Config);
        ValidateAuthoring(engine, config);

        TeamEntityLookup teamLookup = engine.GetService(CoreServiceKeys.TeamEntityLookup)
            ?? throw new InvalidOperationException("Total War showcase requires TeamEntityLookup.");
        var registeredTeamIds = new HashSet<int>();
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
        EnqueueFormationAnchorSpawns(engine, config, spawnQueue, mapId, receiptChannelId);
        EnsureSoldierEntityCache();
        for (int i = 0; i < _unitPlans.Length; i++)
        {
            TotalWarUnitSpawnPlan plan = _unitPlans[i];
            int receiptId = _spawnReceipts.Allocate(TotalWarSpawnReceiptBinding.ForSoldier(
                plan.UnitIndex,
                plan.FormationIndex,
                plan.SlotIndex,
                plan.TeamId,
                plan.Heavy,
                plan.NavMass,
                plan.VisualScale,
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
        if ((uint)binding.UnitIndex >= (uint)_soldierEntitiesByUnitIndex.Length)
        {
            throw new InvalidOperationException($"Total War showcase soldier unit index {binding.UnitIndex} exceeds its scenario cache.");
        }

        if (_soldierEntitiesByUnitIndex[binding.UnitIndex] != Entity.Null)
        {
            throw new InvalidOperationException($"Total War showcase soldier unit index {binding.UnitIndex} was already bound.");
        }

        _soldierEntitiesByUnitIndex[binding.UnitIndex] = entity;
    }

    public void RegisterSpawnedFormationAnchor(GameEngine engine, Entity entity, in TotalWarSpawnReceiptBinding binding)
    {
        if ((uint)binding.FormationIndex >= (uint)_formationPlans.Length)
        {
            throw new InvalidOperationException($"Total War showcase formation index {binding.FormationIndex} exceeds its scenario cache.");
        }

        if (_formationEntities.Length != _formationPlans.Length)
        {
            throw new InvalidOperationException("Total War showcase formation entity cache was not initialized before anchor receipt binding.");
        }

        if (_formationEntities[binding.FormationIndex] != Entity.Null)
        {
            throw new InvalidOperationException($"Total War showcase formation index {binding.FormationIndex} was already bound.");
        }

        TotalWarFormationPlan plan = _formationPlans[binding.FormationIndex];
        TotalWarFormationConfig formation = ActiveConfig.Formations[binding.FormationIndex];
        _formationEntities[binding.FormationIndex] = entity;
        UpsertComponent(engine.World, entity, new Team { Id = plan.TeamId });
        UpsertComponent(engine.World, entity, new TotalWarFormationAnchor { FormationIndex = plan.FormationIndex });
        UpsertComponent(engine.World, entity, new TotalWarFormationState
        {
            SoldierCount = plan.SoldierCount,
            AliveSoldierCount = 0,
            CenterXCm = plan.InitialCenterXCm,
            CenterYCm = plan.InitialCenterYCm,
            FacingRad = plan.FacingRad,
        });
        UpsertComponent(engine.World, entity, formation.Outline.ToComponent(plan.Id));
    }

    private void BuildScenarioPlans(MassNavigationSimulationRuntime simulation, TotalWarShowcaseConfig config)
    {
        int unitCount = 0;
        for (int i = 0; i < config.Formations.Length; i++)
        {
            unitCount += config.Formations[i].SoldierCount;
        }

        if (_agentSeeds.Length != unitCount)
        {
            _agentSeeds = new MassNavigationAgentSeed[unitCount];
            _unitPlans = new TotalWarUnitSpawnPlan[unitCount];
        }

        if (_formationPlans.Length != config.Formations.Length)
        {
            _formationPlans = new TotalWarFormationPlan[config.Formations.Length];
        }

        int unitIndex = 0;
        for (int formationIndex = 0; formationIndex < config.Formations.Length; formationIndex++)
        {
            TotalWarFormationConfig formation = config.Formations[formationIndex];
            int firstUnitIndex = unitIndex;
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
                Vector2 local = simulation.ToLocalCm(new Vector2(worldX, worldY));
                _agentSeeds[unitIndex] = new MassNavigationAgentSeed(
                    formation.TeamId,
                    local.X,
                    local.Y,
                    formation.Heavy,
                    formation.NavMass,
                    formation.VisualScale);
                _unitPlans[unitIndex] = new TotalWarUnitSpawnPlan(
                    unitIndex,
                    formationIndex,
                    slotIndex,
                    formation.TeamId,
                    formation.TemplateId,
                    formation.Heavy,
                    formation.NavMass,
                    formation.VisualScale,
                    worldX,
                    worldY,
                    facingRad);
                unitIndex++;
            }

            _formationPlans[formationIndex] = new TotalWarFormationPlan(
                formationIndex,
                formation.Id,
                formation.Label,
                formation.TeamId,
                firstUnitIndex,
                formation.SoldierCount,
                formation.CenterXCm,
                formation.CenterYCm,
                facingRad);
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
        if (_soldierEntitiesByUnitIndex.Length != _unitPlans.Length)
        {
            _soldierEntitiesByUnitIndex = new Entity[_unitPlans.Length];
        }

        Array.Fill(_soldierEntitiesByUnitIndex, Entity.Null);
    }

    private void EnqueueFormationAnchorSpawns(
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
            int receiptId = _spawnReceipts.Allocate(TotalWarSpawnReceiptBinding.ForFormationAnchor(
                plan.FormationIndex,
                plan.TeamId,
                config.FormationAnchorTemplateId));
            var request = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = config.FormationAnchorTemplateId,
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
                throw new InvalidOperationException("Total War showcase failed to enqueue formation anchor spawn request.");
            }
        }
    }

    private void SyncFormationStates(
        GameEngine engine,
        MassNavigationSimulationRuntime simulation,
        TotalWarFormationSyncConfig formationSync)
    {
        float facingVelocityEpsilonSq =
            formationSync.FacingVelocityEpsilonCmPerSecond *
            formationSync.FacingVelocityEpsilonCmPerSecond;
        for (int i = 0; i < _formationPlans.Length; i++)
        {
            Entity formation = i < _formationEntities.Length ? _formationEntities[i] : Entity.Null;
            if (!engine.World.IsAlive(formation))
            {
                continue;
            }

            TotalWarFormationPlan plan = _formationPlans[i];
            int aliveCount = 0;
            float centerX = 0f;
            float centerY = 0f;
            float velocityX = 0f;
            float velocityY = 0f;
            for (int localIndex = 0; localIndex < plan.SoldierCount; localIndex++)
            {
                int unitIndex = plan.FirstUnitIndex + localIndex;
                if ((uint)unitIndex >= (uint)_soldierEntitiesByUnitIndex.Length ||
                    !engine.World.IsAlive(_soldierEntitiesByUnitIndex[unitIndex]) ||
                    (uint)unitIndex >= (uint)simulation.MassFlow.UnitCount)
                {
                    continue;
                }

                centerX += simulation.ToWorldXCm(simulation.MassFlow.GetPositionX(unitIndex));
                centerY += simulation.ToWorldYCm(simulation.MassFlow.GetPositionY(unitIndex));
                Vector2 velocity = simulation.MassFlow.GetVelocityCmPerSecond(unitIndex);
                velocityX += velocity.X;
                velocityY += velocity.Y;
                aliveCount++;
            }

            float facingRad = plan.FacingRad;
            if (aliveCount > 0)
            {
                float invAlive = 1f / aliveCount;
                centerX *= invAlive;
                centerY *= invAlive;
                if ((velocityX * velocityX) + (velocityY * velocityY) > facingVelocityEpsilonSq)
                {
                    facingRad = MathF.Atan2(velocityY, velocityX);
                }
            }
            else
            {
                centerX = plan.InitialCenterXCm;
                centerY = plan.InitialCenterYCm;
            }

            var center = Fix64Vec2.FromInt((int)MathF.Round(centerX), (int)MathF.Round(centerY));
            ref TotalWarFormationState state = ref engine.World.Get<TotalWarFormationState>(formation);
            state.SoldierCount = plan.SoldierCount;
            state.AliveSoldierCount = aliveCount;
            state.CenterXCm = centerX;
            state.CenterYCm = centerY;
            state.FacingRad = facingRad;

            ref WorldPositionCm worldPosition = ref engine.World.Get<WorldPositionCm>(formation);
            worldPosition.Value = center;
            ref FacingDirection facing = ref engine.World.Get<FacingDirection>(formation);
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
        TotalWarFormationPlan plan = _formationPlans[formationIndex];
        if (_initialSelectionScratch.Length < plan.SoldierCount)
        {
            _initialSelectionScratch = new Entity[plan.SoldierCount];
        }

        int written = 0;
        for (int i = 0; i < plan.SoldierCount; i++)
        {
            int unitIndex = plan.FirstUnitIndex + i;
            if ((uint)unitIndex >= (uint)_soldierEntitiesByUnitIndex.Length)
            {
                continue;
            }

            Entity soldier = _soldierEntitiesByUnitIndex[unitIndex];
            if (!engine.World.IsAlive(soldier))
            {
                return;
            }

            _initialSelectionScratch[written++] = soldier;
        }

        if (!selection.ReplaceSelection(owner, SelectionSetKeys.LivePrimary, _initialSelectionScratch.AsSpan(0, written)))
        {
            throw new InvalidOperationException("Total War showcase failed to author its configured initial selection.");
        }

        selection.TryBindView(owner, SelectionViewKeys.Primary, owner, SelectionSetKeys.LivePrimary);
        engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = owner;
        engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
        if (!selection.TryDescribeSelection(owner, SelectionSetKeys.LivePrimary, out SelectionContainerDescriptor descriptor))
        {
            throw new InvalidOperationException("Total War showcase failed to describe the initial selection it just authored.");
        }

        if (!SelectionContextRuntime.TryDescribeCurrentView(engine.World, engine.GlobalContext, out SelectionViewDescriptor viewDescriptor) ||
            viewDescriptor.Container.Container != descriptor.Container)
        {
            throw new InvalidOperationException("Total War showcase initial selection view does not resolve to LivePrimary.");
        }

        _initialSelectionApplied = true;
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

    private void DestroyFormationAnchors(GameEngine engine)
    {
        for (int i = 0; i < _formationEntities.Length; i++)
        {
            Entity entity = _formationEntities[i];
            if (engine.World.IsAlive(entity))
            {
                MarkPresentationDestroyPending(engine.World, entity, "formation anchor");
            }

            _formationEntities[i] = Entity.Null;
        }
    }

    private void ClearFormationCaches()
    {
        if (_soldierEntitiesByUnitIndex.Length > 0)
        {
            Array.Fill(_soldierEntitiesByUnitIndex, Entity.Null);
        }

        if (_formationEntities.Length > 0)
        {
            Array.Fill(_formationEntities, Entity.Null);
        }
    }

    private static void ConfigureScenarioTeams(MassNavigationSimulationRuntime simulation, TotalWarShowcaseConfig config)
    {
        var uniqueTeamIds = new SortedSet<int>();
        for (int i = 0; i < config.Formations.Length; i++)
        {
            uniqueTeamIds.Add(config.Formations[i].TeamId);
        }

        int[] teamIds = new int[uniqueTeamIds.Count];
        uniqueTeamIds.CopyTo(teamIds);
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
        ValidateTemplate(engine, config.FormationAnchorTemplateId);
        for (int i = 0; i < config.Formations.Length; i++)
        {
            ValidateTemplate(engine, config.Formations[i].TemplateId);
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
        if (!world.Has<PresentationStableId>(entity))
        {
            throw new InvalidOperationException($"Total War showcase cannot destroy {label} without PresentationStableId.");
        }

        if (!world.Has<PresentationDestroyPending>(entity))
        {
            world.Add(entity, new PresentationDestroyPending());
        }

        if (world.Has<PresentationDestroyEventPublished>(entity))
        {
            world.Remove<PresentationDestroyEventPublished>(entity);
        }

        if (world.Has<PresentationOwnerHasPerformerPayload>(entity))
        {
            world.Remove<PresentationOwnerHasPerformerPayload>(entity);
        }
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

    private readonly struct TotalWarFormationPlan
    {
        public TotalWarFormationPlan(
            int formationIndex,
            string id,
            string label,
            int teamId,
            int firstUnitIndex,
            int soldierCount,
            float initialCenterXCm,
            float initialCenterYCm,
            float facingRad)
        {
            FormationIndex = formationIndex;
            Id = id;
            Label = label;
            TeamId = teamId;
            FirstUnitIndex = firstUnitIndex;
            SoldierCount = soldierCount;
            InitialCenterXCm = initialCenterXCm;
            InitialCenterYCm = initialCenterYCm;
            FacingRad = facingRad;
        }

        public int FormationIndex { get; }
        public string Id { get; }
        public string Label { get; }
        public int TeamId { get; }
        public int FirstUnitIndex { get; }
        public int SoldierCount { get; }
        public float InitialCenterXCm { get; }
        public float InitialCenterYCm { get; }
        public float FacingRad { get; }
    }

    private readonly struct TotalWarUnitSpawnPlan
    {
        public TotalWarUnitSpawnPlan(
            int unitIndex,
            int formationIndex,
            int slotIndex,
            int teamId,
            string templateId,
            bool heavy,
            float navMass,
            float visualScale,
            float worldXCm,
            float worldYCm,
            float facingRad)
        {
            UnitIndex = unitIndex;
            FormationIndex = formationIndex;
            SlotIndex = slotIndex;
            TeamId = teamId;
            TemplateId = templateId;
            Heavy = heavy;
            NavMass = navMass;
            VisualScale = visualScale;
            WorldXCm = worldXCm;
            WorldYCm = worldYCm;
            FacingRad = facingRad;
        }

        public int UnitIndex { get; }
        public int FormationIndex { get; }
        public int SlotIndex { get; }
        public int TeamId { get; }
        public string TemplateId { get; }
        public bool Heavy { get; }
        public float NavMass { get; }
        public float VisualScale { get; }
        public float WorldXCm { get; }
        public float WorldYCm { get; }
        public float FacingRad { get; }
    }
}
