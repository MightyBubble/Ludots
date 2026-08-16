using System;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
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

    public UiPlayerAggregateGraphMvpRuntime()
    {
        _panelController = new UiPlayerAggregateGraphMvpPanelController(this);
    }

    public UiPlayerAggregateGraphMvpSnapshot Snapshot => BuildSnapshot();

    public ReadOnlySpan<UiPlayerAggregateProducerMarker> ProducerMarkers => _producerMarkers;

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
        return new UiPlayerAggregateGraphMvpPanelState(
            Title: snapshot.Title,
            Copy: snapshot.Copy,
            Controls: snapshot.Controls,
            Status: snapshot.Status,
            GraphId: snapshot.GraphId,
            OreSummaryKey: snapshot.OreSummaryKey,
            CrystalSummaryKey: snapshot.CrystalSummaryKey,
            OreTotal: snapshot.OreTotal,
            CrystalTotal: snapshot.CrystalTotal,
            BuildingShutDown: snapshot.BuildingShutDown,
            ShutDownBuildingName: snapshot.ShutDownBuildingName);
    }

    private UiPlayerAggregateGraphMvpSnapshot BuildSnapshot()
    {
        UiPlayerAggregateGraphMvpConfig? config = _config;
        return new UiPlayerAggregateGraphMvpSnapshot(
            Title: config?.Presentation.Title ?? "Player Resource Overview",
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
        _oreTotal = reader.ResolveFloat(
            _owner,
            new PanelVariableBinding(
                "oreTotal",
                PanelBindingSourceKind.AggregateProjection,
                attributeId: null,
                graphOutputKey: _config.SummaryKeys.OreTotal));
        _crystalTotal = reader.ResolveFloat(
            _owner,
            new PanelVariableBinding(
                "crystalTotal",
                PanelBindingSourceKind.AggregateProjection,
                attributeId: null,
                graphOutputKey: _config.SummaryKeys.CrystalTotal));
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
        return _config;
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

        _owner = FindEntityByName(engine.World, config.FactionOwnerName);
        _shutDownBuilding = FindEntityByName(engine.World, config.ShutDownBuildingName);
        _producerEntities = new Entity[config.Buildings.Length];
        _producerMarkers = new UiPlayerAggregateProducerMarker[config.Buildings.Length];
        for (int i = 0; i < config.Buildings.Length; i++)
        {
            _producerEntities[i] = FindEntityByName(engine.World, config.Buildings[i].Name);
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
            System.Numerics.Vector3 meters = WorldUnits.WorldCmToVisualMeters(in pos.Value);
            bool offline = false;
            if (world.Has<AttributeBuffer>(entity))
            {
                ref AttributeBuffer attrs = ref world.Get<AttributeBuffer>(entity);
                float stock = attrs.GetCurrent(_oreAttributeId) + attrs.GetCurrent(_crystalAttributeId);
                offline = stock <= 0.01f;
            }

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
