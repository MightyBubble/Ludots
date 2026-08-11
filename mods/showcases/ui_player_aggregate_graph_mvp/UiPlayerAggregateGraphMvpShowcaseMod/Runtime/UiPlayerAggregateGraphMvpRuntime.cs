using System;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using Ludots.UI;
using UiPlayerAggregateGraphMvpShowcaseMod.Input;
using UiPlayerAggregateGraphMvpShowcaseMod.UI;

namespace UiPlayerAggregateGraphMvpShowcaseMod.Runtime;

public sealed class UiPlayerAggregateGraphMvpRuntime
{
    private readonly UiPlayerAggregateGraphMvpPanelController _panelController;
    private UiPlayerAggregateGraphMvpConfig? _config;
    private Entity _owner = Entity.Null;
    private Entity _shutDownBuilding = Entity.Null;
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

        ref AttributeBuffer attributes = ref engine.World.Get<AttributeBuffer>(_shutDownBuilding);
        attributes.SetBase(_oreAttributeId, 0f);
        attributes.SetBase(_crystalAttributeId, 0f);
        _buildingShutDown = true;
        _status = $"{config.ShutDownBuildingName} shut down; resource attributes set to 0.";
        ExecuteAggregateGraph(engine);
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
        _oreTotal = RequireSummaryFloat(values, _owner, _config.SummaryKeys.OreTotal);
        _crystalTotal = RequireSummaryFloat(values, _owner, _config.SummaryKeys.CrystalTotal);
        if (!_buildingShutDown)
        {
            _status = "Tally graph projections are live on the resource strip.";
        }
    }

    private static float RequireSummaryFloat(GraphOutputValueStore values, Entity owner, string key)
    {
        if (!values.TryGet(owner, key, out GraphOutputValueHandle handle) ||
            !values.TryGetView(handle, out GraphOutputValueView view))
        {
            throw new InvalidOperationException(
                $"GraphOutputValueStore is missing required summary key '{key}' on owner #{owner.Id}. Silent zero is forbidden.");
        }

        if (view.Kind != GraphOutputValueKind.Float)
        {
            throw new InvalidOperationException(
                $"GraphOutputValueStore key '{key}' must be Float, found '{view.Kind}'.");
        }

        return view.FloatValue;
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
        for (int i = 0; i < config.Buildings.Length; i++)
        {
            _ = FindEntityByName(engine.World, config.Buildings[i].Name);
        }

        _scenarioReady = true;
        _status = "Scenario ready: faction owner and producer buildings are live.";
    }

    private void ResetScenario()
    {
        _owner = Entity.Null;
        _shutDownBuilding = Entity.Null;
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
