using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;

using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelProjection;
using Ludots.UI;
using UiPlayerAggregateGraphMvpShowcaseMod.Input;
using UiPlayerAggregateGraphMvpShowcaseMod.UI;
using Ludots.Platform.Abstractions;

namespace UiPlayerAggregateGraphMvpShowcaseMod.Runtime;

public readonly struct UiPlayerAggregateProducerMarker
{
    public UiPlayerAggregateProducerMarker(float xMeters, float zMeters, bool offline)
    {
        XMeters = xMeters;
        ZMeters = zMeters;
        Offline = offline;
    }

    public float XMeters { get; }
    public float ZMeters { get; }
    public bool Offline { get; }
}

public sealed class UiPlayerAggregateGraphMvpRuntime
{
    private readonly UiPlayerAggregateGraphMvpPanelController _panelController;
    private readonly string _bootstrapOreAttribute;
    private readonly string _bootstrapCrystalAttribute;
    private UiPlayerAggregateGraphMvpConfig? _config;
    private Entity _owner = Entity.Null;
    private Entity _shutDownBuilding = Entity.Null;
    private Entity[] _producerEntities = Array.Empty<Entity>();
    private UiPlayerAggregateProducerMarker[] _producerMarkers = Array.Empty<UiPlayerAggregateProducerMarker>();
    private int _graphId;
    private int _oreAttributeId = AttributeRegistry.InvalidId;
    private int _crystalAttributeId = AttributeRegistry.InvalidId;
    private bool _scenarioReady;
    private bool _buildingShutDown;
    private float _oreTotal;
    private float _crystalTotal;
    private string _status = "Load the player aggregate graph MVP map.";
    private uint _randomSeed = 1u;
    private IGraphRuntimeApi? _graphApi;

    public UiPlayerAggregateGraphMvpRuntime(string bootstrapOreAttribute, string bootstrapCrystalAttribute)
    {
        if (string.IsNullOrWhiteSpace(bootstrapOreAttribute) || string.IsNullOrWhiteSpace(bootstrapCrystalAttribute))
        {
            throw new ArgumentException("Bootstrap resource attribute names are required for graph symbol patch ordering.");
        }

        _bootstrapOreAttribute = bootstrapOreAttribute.Trim();
        _bootstrapCrystalAttribute = bootstrapCrystalAttribute.Trim();
        _panelController = new UiPlayerAggregateGraphMvpPanelController(this);
    }

    public UiPlayerAggregateGraphMvpSnapshot Snapshot => BuildSnapshot();

    public ReadOnlySpan<UiPlayerAggregateProducerMarker> ProducerMarkers => _producerMarkers;

    public UiPlayerAggregateMarkerStyle RequireMarkerStyle()
    {
        if (_config == null)
        {
            throw new InvalidOperationException("Player aggregate graph MVP marker style requires loaded showcase config.");
        }

        return _config.Presentation.Markers;
    }

    public UiPlayerAggregateGraphMvpConfig RequireConfig(GameEngine engine) => EnsureConfig(engine);

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        UiPlayerAggregateGraphMvpConfig config = EnsureConfig(engine);
        if (!UiPlayerAggregateGraphMvpIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value) &&
            !UiPlayerAggregateGraphMvpIds.IsShowcaseMap(context.Get(CoreServiceKeys.MapId).Value))
        {
            ClearPanel(engine);
            return Task.CompletedTask;
        }

        ActivateInputContext(engine.GetService(CoreServiceKeys.InputHandler));
        EnsureScenario(engine, config);
        ExecuteAggregateGraph(engine);
        RefreshPanel(engine);
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        if (_config != null && UiPlayerAggregateGraphMvpIds.IsShowcaseMap(context.Get(CoreServiceKeys.MapId).Value))
        {
            DeactivateInputContext(engine.GetService(CoreServiceKeys.InputHandler));
            ClearPanel(engine);
            ResetScenario();
        }

        return Task.CompletedTask;
    }

    public void Tick(GameEngine engine)
    {
        if (!UiPlayerAggregateGraphMvpIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        UiPlayerAggregateGraphMvpConfig config = EnsureConfig(engine);
        EnsureScenario(engine, config);

        if (engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader input &&
            input.PressedThisFrame(UiPlayerAggregateGraphMvpIds.ShutDownBuildingActionId))
        {
            ShutDownBuilding(engine);
        }

        ExecuteAggregateGraph(engine);
        RefreshProducerMarkers(engine);
    }

    public void ShutDownBuilding(GameEngine engine)
    {
        UiPlayerAggregateGraphMvpConfig config = EnsureConfig(engine);
        EnsureScenario(engine, config);
        if (_buildingShutDown)
        {
            _status = $"{config.ShutDownBuildingName} is already shut down.";
            return;
        }

        if (_shutDownBuilding == Entity.Null || !engine.World.IsAlive(_shutDownBuilding))
        {
            throw new InvalidOperationException($"Shut-down building '{config.ShutDownBuildingName}' is missing.");
        }

        if (!engine.World.Has<AttributeBuffer>(_shutDownBuilding))
        {
            throw new InvalidOperationException($"Shut-down building '{config.ShutDownBuildingName}' has no AttributeBuffer.");
        }

        TagOps tagOps = engine.GetService(CoreServiceKeys.TagOps)
            ?? throw new InvalidOperationException("UiPlayerAggregateGraphMvp requires TagOps.");
        AttributeMutationOps.SetBase(engine.World, _shutDownBuilding, _oreAttributeId, 0f, tagOps);
        AttributeMutationOps.SetBase(engine.World, _shutDownBuilding, _crystalAttributeId, 0f, tagOps);
        _buildingShutDown = true;
        _status = $"{config.ShutDownBuildingName} shut down; resource attributes set to 0.";
        ExecuteAggregateGraph(engine);
        RefreshProducerMarkers(engine);
    }

    public void RefreshPanel(GameEngine engine)
    {
        if (UiPlayerAggregateGraphMvpIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
            {
                _panelController.MountOrRefresh(root, engine);
            }
        }
        else
        {
            ClearPanel(engine);
        }
    }

    internal UiPlayerAggregateGraphMvpPanelState BuildPanelState()
    {
        UiPlayerAggregateGraphMvpSnapshot snapshot = BuildSnapshot();
        UiPlayerAggregateGraphMvpConfig config = _config
            ?? throw new InvalidOperationException("Player aggregate graph MVP panel requires loaded showcase config.");
        return new UiPlayerAggregateGraphMvpPanelState(
            Title: snapshot.Title,
            Copy: snapshot.Copy,
            Controls: snapshot.Controls,
            Status: snapshot.Status,
            GraphId: snapshot.GraphId,
            OreBinding: config.OreBinding,
            CrystalBinding: config.CrystalBinding,
            OreTotal: snapshot.OreTotal,
            CrystalTotal: snapshot.CrystalTotal,
            BuildingShutDown: snapshot.BuildingShutDown,
            ShutDownBuildingName: snapshot.ShutDownBuildingName,
            PanelStyle: config.Presentation.Panel);
    }

    private UiPlayerAggregateGraphMvpSnapshot BuildSnapshot()
    {
        UiPlayerAggregateGraphMvpConfig? config = _config;
        return new UiPlayerAggregateGraphMvpSnapshot(
            Title: config?.Presentation.Title ?? string.Empty,
            Copy: config?.Presentation.Copy ?? string.Empty,
            Controls: config?.Presentation.Controls ?? string.Empty,
            Status: _status,
            GraphId: config?.GraphId ?? string.Empty,
            OreSummaryKey: config?.SummaryKeys.OreTotal ?? string.Empty,
            CrystalSummaryKey: config?.SummaryKeys.CrystalTotal ?? string.Empty,
            OreTotal: _oreTotal,
            CrystalTotal: _crystalTotal,
            BuildingShutDown: _buildingShutDown,
            ShutDownBuildingName: config?.ShutDownBuildingName ?? string.Empty);
    }

    private void ExecuteAggregateGraph(GameEngine engine)
    {
        if (!_scenarioReady || _config == null)
        {
            return;
        }

        if (_owner == Entity.Null || !engine.World.IsAlive(_owner))
        {
            throw new InvalidOperationException($"Faction owner '{_config.FactionOwnerName}' is missing for graph execution.");
        }

        GraphReturnWriter writer = engine.GetService(CoreServiceKeys.GraphReturnWriter)
            ?? throw new InvalidOperationException("GraphReturnWriter is missing.");
        IGraphRuntimeApi api = _graphApi ??= RequireGraphApi(engine);
        IntVector2 targetPos = default;
        writer.ExecuteAndWrite(_graphId, _owner, _owner, Entity.Null, Entity.Null, targetPos, NextSeed(), api);

        GraphOutputValueStore values = engine.GetService(CoreServiceKeys.GraphOutputValueStore)
            ?? throw new InvalidOperationException("GraphOutputValueStore is missing.");
        var reader = new PanelProjectionReader(engine.World, values);
        UiPlayerAggregatePanelBinding oreBinding = _config.OreBinding;
        UiPlayerAggregatePanelBinding crystalBinding = _config.CrystalBinding;
        _oreTotal = reader.Resolve(_owner, new PanelPin(oreBinding.VariableId, oreBinding.GraphOutputKey, realtime: true, defaultValue: 0f)).FloatValue;
        _crystalTotal = reader.Resolve(_owner, new PanelPin(crystalBinding.VariableId, crystalBinding.GraphOutputKey, realtime: true, defaultValue: 0f)).FloatValue;
        if (!_buildingShutDown)
        {
            _status = "Tally graph projections are live on the resource strip.";
        }
    }

    private UiPlayerAggregateGraphMvpConfig EnsureConfig(GameEngine engine)
    {
        if (_config != null)
        {
            return _config;
        }

        if (engine.ConfigPipeline == null)
        {
            throw new InvalidOperationException("Player aggregate graph MVP requires ConfigPipeline before loading config.");
        }

        _config = new UiPlayerAggregateGraphMvpConfigLoader(engine.ConfigPipeline).Load(
            engine.ConfigCatalog
                ?? throw new InvalidOperationException("Player aggregate graph MVP requires ConfigCatalog."),
            engine.ConfigConflictReport
                ?? throw new InvalidOperationException("Player aggregate graph MVP requires ConfigConflictReport."));
        RequireBootstrapAttributesMatchPipeline(_config);
        return _config;
    }

    private void RequireBootstrapAttributesMatchPipeline(UiPlayerAggregateGraphMvpConfig config)
    {
        if (!string.Equals(config.Attributes.Ore, _bootstrapOreAttribute, StringComparison.Ordinal) ||
            !string.Equals(config.Attributes.Crystal, _bootstrapCrystalAttribute, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Player aggregate graph MVP attributes from ConfigPipeline must match OnLoad bootstrap registration " +
                $"(bootstrap ore='{_bootstrapOreAttribute}' crystal='{_bootstrapCrystalAttribute}', " +
                $"pipeline ore='{config.Attributes.Ore}' crystal='{config.Attributes.Crystal}'). " +
                "AttributeRegistry is frozen before graph patch; diverging Replace merges are fail-closed.");
        }
    }

    private void EnsureScenario(GameEngine engine, UiPlayerAggregateGraphMvpConfig config)
    {
        if (_scenarioReady)
        {
            return;
        }

        _graphId = GraphIdRegistry.GetId(config.GraphId);
        if (_graphId <= 0)
        {
            throw new InvalidOperationException($"Query graph '{config.GraphId}' is not registered.");
        }

        _oreAttributeId = AttributeRegistry.GetId(config.Attributes.Ore);
        _crystalAttributeId = AttributeRegistry.GetId(config.Attributes.Crystal);
        if (_oreAttributeId == AttributeRegistry.InvalidId || _crystalAttributeId == AttributeRegistry.InvalidId)
        {
            throw new InvalidOperationException(
                $"Resource attributes '{config.Attributes.Ore}' / '{config.Attributes.Crystal}' are not registered.");
        }

        InjectPlayerTeamIdIntoAggregateGraph(engine, _graphId, config.PlayerTeamId);
        RequireGraphOutputKeysMatchPanelBindings(engine, _graphId, config);

        _owner = FindEntityByName(engine.World, config.FactionOwnerName);
        _shutDownBuilding = FindEntityByName(engine.World, config.ShutDownBuildingName);
        _producerEntities = new Entity[config.Buildings.Length];
        _producerMarkers = new UiPlayerAggregateProducerMarker[config.Buildings.Length];
        for (int i = 0; i < config.Buildings.Length; i++)
        {
            _producerEntities[i] = FindEntityByName(engine.World, config.Buildings[i].Name);
        }

        ApplyPlayerTeam(engine.World, _owner, config.PlayerTeamId, config.FactionOwnerName);
        TagOps seedTagOps = engine.GetService(CoreServiceKeys.TagOps)
            ?? throw new InvalidOperationException("UiPlayerAggregateGraphMvp requires TagOps.");
        for (int i = 0; i < _producerEntities.Length; i++)
        {
            UiPlayerAggregateBuildingSeed seed = config.Buildings[i];
            ApplyPlayerTeam(engine.World, _producerEntities[i], config.PlayerTeamId, seed.Name);
            ApplyBuildingSeed(engine.World, _producerEntities[i], seed, _oreAttributeId, _crystalAttributeId, seedTagOps);
        }

        _scenarioReady = true;
        _status = "Scenario ready: faction owner and producer buildings are live.";
        RefreshProducerMarkers(engine);
    }

    private void RefreshProducerMarkers(GameEngine engine)
    {
        if (!_scenarioReady || _config == null)
        {
            _producerMarkers = Array.Empty<UiPlayerAggregateProducerMarker>();
            return;
        }

        World world = engine.World;
        for (int i = 0; i < _producerEntities.Length; i++)
        {
            Entity entity = _producerEntities[i];
            if (entity == Entity.Null || !world.IsAlive(entity) || !world.Has<WorldPositionCm>(entity))
            {
                throw new InvalidOperationException(
                    $"Producer building '{_config.Buildings[i].Name}' is missing WorldPositionCm.");
            }

            ref WorldPositionCm pos = ref world.Get<WorldPositionCm>(entity);
            System.Numerics.Vector3 meters = WorldUnitsFix64.WorldCmToVisualMeters(in pos.Value);
            if (!world.Has<AttributeBuffer>(entity))
            {
                throw new InvalidOperationException(
                    $"Producer building '{_config.Buildings[i].Name}' is missing AttributeBuffer for marker stock.");
            }

            ref AttributeBuffer attrs = ref world.Get<AttributeBuffer>(entity);
            float stock = attrs.GetCurrent(_oreAttributeId) + attrs.GetCurrent(_crystalAttributeId);
            float offlineEpsilon = _config.Presentation.Markers.OfflineStockEpsilon;
            bool offline = stock <= offlineEpsilon;
            _producerMarkers[i] = new UiPlayerAggregateProducerMarker(meters.X, meters.Z, offline);
        }
    }

    private void ResetScenario()
    {
        _owner = Entity.Null;
        _shutDownBuilding = Entity.Null;
        _producerEntities = Array.Empty<Entity>();
        _producerMarkers = Array.Empty<UiPlayerAggregateProducerMarker>();
        _graphId = 0;
        _oreAttributeId = AttributeRegistry.InvalidId;
        _crystalAttributeId = AttributeRegistry.InvalidId;
        _scenarioReady = false;
        _buildingShutDown = false;
        _oreTotal = 0f;
        _crystalTotal = 0f;
        _status = "Load the player aggregate graph MVP map.";
        _graphApi = null;
    }

    private void ClearPanel(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panelController.ClearIfOwned(root);
        }
    }

    private static void ActivateInputContext(PlayerInputHandler? inputHandler)
    {
        if (inputHandler == null || !inputHandler.HasContext(UiPlayerAggregateGraphMvpInputContexts.Showcase))
        {
            return;
        }

        inputHandler.PushContext(UiPlayerAggregateGraphMvpInputContexts.Showcase);
    }

    private static void DeactivateInputContext(PlayerInputHandler? inputHandler)
    {
        inputHandler?.PopContext(UiPlayerAggregateGraphMvpInputContexts.Showcase);
    }

    private static IGraphRuntimeApi RequireGraphApi(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.GasGraphRuntimeApi)
            ?? throw new InvalidOperationException("Engine-owned production GasGraphRuntimeApi is missing.");
    }

    private static void InjectPlayerTeamIdIntoAggregateGraph(GameEngine engine, int graphId, int playerTeamId)
    {
        GraphProgramRegistry registry = engine.GetService(CoreServiceKeys.GraphProgramRegistry)
            ?? throw new InvalidOperationException("GraphProgramRegistry is missing.");
        if (!registry.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program) || program.IsEmpty)
        {
            throw new InvalidOperationException($"Aggregate graph id {graphId} has no compiled program.");
        }

        Span<GraphInstruction> instructions = MemoryMarshal.CreateSpan(
            ref MemoryMarshal.GetReference(program),
            program.Length);
        int patched = 0;
        for (int i = 0; i < instructions.Length; i++)
        {
            if ((GraphNodeOp)instructions[i].Op != GraphNodeOp.QueryFilterTeam)
            {
                continue;
            }

            if (instructions[i].Flags != 0)
            {
                throw new InvalidOperationException(
                    "Player aggregate graph MVP requires QueryFilterTeam to use the Imm teamId field so showcase config can inject playerTeamId.");
            }

            instructions[i].Imm = playerTeamId;
            patched++;
        }

        if (patched != 1)
        {
            throw new InvalidOperationException(
                $"Player aggregate graph MVP expected exactly one QueryFilterTeam Imm injection site, found {patched}.");
        }

        if (!registry.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> verify) ||
            !TryReadInjectedTeamId(verify, out int injected) ||
            injected != playerTeamId)
        {
            throw new InvalidOperationException(
                $"Failed to inject playerTeamId {playerTeamId} into aggregate QueryFilterTeam Imm.");
        }
    }

    private static bool TryReadInjectedTeamId(ReadOnlySpan<GraphInstruction> program, out int teamId)
    {
        teamId = 0;
        int found = 0;
        for (int i = 0; i < program.Length; i++)
        {
            if ((GraphNodeOp)program[i].Op != GraphNodeOp.QueryFilterTeam || program[i].Flags != 0)
            {
                continue;
            }

            teamId = program[i].Imm;
            found++;
        }

        return found == 1;
    }

    private static void RequireGraphOutputKeysMatchPanelBindings(
        GameEngine engine,
        int graphId,
        UiPlayerAggregateGraphMvpConfig config)
    {
        GraphOutputSchemaRegistry schemas = engine.GetService(CoreServiceKeys.GraphOutputSchemaRegistry)
            ?? throw new InvalidOperationException("GraphOutputSchemaRegistry is missing.");
        GraphOutputSchema schema = schemas.Get(graphId);
        RequireSchemaContainsKey(schema, config.OreBinding.GraphOutputKey);
        RequireSchemaContainsKey(schema, config.CrystalBinding.GraphOutputKey);
    }

    private static void RequireSchemaContainsKey(GraphOutputSchema schema, string key)
    {
        ReadOnlySpan<GraphOutputBinding> bindings = schema.Bindings;
        for (int i = 0; i < bindings.Length; i++)
        {
            if (string.Equals(bindings[i].Key, key, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"Aggregate graph output schema is missing panel binding key '{key}'.");
    }

    private static void ApplyPlayerTeam(World world, Entity entity, int playerTeamId, string entityName)
    {
        if (!world.Has<Team>(entity))
        {
            throw new InvalidOperationException($"Showcase entity '{entityName}' requires a Team component.");
        }

        ref Team team = ref world.Get<Team>(entity);
        team.Id = playerTeamId;
    }

    private static void ApplyBuildingSeed(
        World world,
        Entity entity,
        UiPlayerAggregateBuildingSeed seed,
        int oreAttributeId,
        int crystalAttributeId,
        TagOps tagOps)
    {
        AttributeMutationOps.SetBase(world, entity, oreAttributeId, seed.Ore, tagOps);
        AttributeMutationOps.SetBase(world, entity, crystalAttributeId, seed.Crystal, tagOps);
    }

    private uint NextSeed()
    {
        _randomSeed ^= _randomSeed << 13;
        _randomSeed ^= _randomSeed >> 17;
        _randomSeed ^= _randomSeed << 5;
        return _randomSeed == 0u ? 1u : _randomSeed;
    }

    private static Entity FindEntityByName(World world, string name)
    {
        Entity found = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        world.Query(in query, (Entity entity, ref Name entityName) =>
        {
            if (found == Entity.Null &&
                string.Equals(entityName.Value, name, StringComparison.Ordinal))
            {
                found = entity;
            }
        });

        if (found == Entity.Null)
        {
            throw new InvalidOperationException($"Required showcase entity '{name}' was not found.");
        }

        return found;
    }
}
