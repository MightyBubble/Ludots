using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;

namespace CncTriNationFullGameMod.Systems;

internal sealed class CncTriNationGraphProjectionSystem : ISystem<float>
{
    private const float GraphExecutionIntervalSeconds = 1f;
    private const string MapId = "cnc_tri_nation_war";

    private readonly GameEngine _engine;
    private readonly World _world;
    private float _secondsSinceExecution;
    private IGraphRuntimeApi? _graphApi;
    private uint _randomSeed = 0xC0C711u;
    private int _armyCompositionGraphId;
    private int _sovietArmyGraphId;
    private int _yuriArmyGraphId;
    private bool _graphIdsResolved;

    public CncTriNationGraphProjectionSystem(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _world = engine.World;
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        if (!IsTargetMap())
        {
            return;
        }

        EnsureGraphIds();
        if (_armyCompositionGraphId <= 0 && _sovietArmyGraphId <= 0 && _yuriArmyGraphId <= 0)
        {
            return;
        }

        _secondsSinceExecution += MathF.Max(0f, dt);
        if (_secondsSinceExecution < GraphExecutionIntervalSeconds)
        {
            return;
        }

        _secondsSinceExecution = 0f;
        ExecuteGraphs();
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
    }

    private bool IsTargetMap()
    {
        return string.Equals(_engine.CurrentMapSession?.MapConfig?.Id, MapId, StringComparison.Ordinal);
    }

    private void EnsureGraphIds()
    {
        if (_graphIdsResolved)
        {
            return;
        }

        _armyCompositionGraphId = GraphIdRegistry.GetId("cnc.graph.armyComposition");
        _sovietArmyGraphId = GraphIdRegistry.GetId("cnc.graph.sovietArmy");
        _yuriArmyGraphId = GraphIdRegistry.GetId("cnc.graph.yuriArmy");
        _graphIdsResolved = true;
    }

    private void ExecuteGraphs()
    {
        Entity owner = ResolveGraphOwner();
        if (owner == Entity.Null || !_world.IsAlive(owner))
        {
            return;
        }

        GraphReturnWriter? writer = _engine.GetService(CoreServiceKeys.GraphReturnWriter);
        if (writer == null)
        {
            return;
        }

        IGraphRuntimeApi api = _graphApi ??= CreateGraphApi();
        IntVector2 targetPos = default;

        if (_armyCompositionGraphId > 0)
        {
            writer.ExecuteAndWrite(_armyCompositionGraphId, owner, owner, Entity.Null, Entity.Null, targetPos, NextSeed(), api);
        }

        if (_sovietArmyGraphId > 0)
        {
            writer.ExecuteAndWrite(_sovietArmyGraphId, owner, owner, Entity.Null, Entity.Null, targetPos, NextSeed(), api);
        }

        if (_yuriArmyGraphId > 0)
        {
            writer.ExecuteAndWrite(_yuriArmyGraphId, owner, owner, Entity.Null, Entity.Null, targetPos, NextSeed(), api);
        }

        PublishSummariesToGlobalContext(owner);
    }

    private void PublishSummariesToGlobalContext(Entity owner)
    {
        GraphOutputValueStore? values = _engine.GetService(CoreServiceKeys.GraphOutputValueStore);
        if (values == null)
        {
            return;
        }

        PublishIntSummaryToContext(values, owner, "cnc.summary.unitCount");
        PublishIntSummaryToContext(values, owner, "cnc.summary.sovietCount");
        PublishIntSummaryToContext(values, owner, "cnc.summary.yuriCount");

        if (values.TryGet(owner, "cnc.summary.totalHealth", out GraphOutputValueHandle totalHealthHandle) &&
            values.TryGetView(totalHealthHandle, out GraphOutputValueView totalHealthView))
        {
            _engine.GlobalContext["cnc.summary.totalHealth"] = totalHealthView.FloatValue;
        }
    }

    private void PublishIntSummaryToContext(GraphOutputValueStore values, Entity owner, string key)
    {
        if (!values.TryGet(owner, key, out GraphOutputValueHandle handle) ||
            !values.TryGetView(handle, out GraphOutputValueView view))
        {
            return;
        }

        _engine.GlobalContext[key] = view.IntValue;
    }

    private Entity ResolveGraphOwner()
    {
        Entity owner = _engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        if (owner != Entity.Null && _world.IsAlive(owner))
        {
            return owner;
        }

        Entity fallback = Entity.Null;
        var query = new QueryDescription().WithAll<PlayerOwner>();
        _world.Query(in query, (Entity entity, ref PlayerOwner playerOwner) =>
        {
            if (fallback == Entity.Null && playerOwner.PlayerId == 1)
            {
                fallback = entity;
            }
        });

        return fallback;
    }

    private IGraphRuntimeApi CreateGraphApi()
    {
        return GasGraphRuntimeApi.CreateProduction(
            _world,
            _engine.SpatialQueries,
            _engine.SpatialCoords,
            _engine.EventBus,
            _engine.GetService(CoreServiceKeys.EffectRequestQueue),
            _engine.GlobalContext);
    }

    private uint NextSeed()
    {
        _randomSeed ^= _randomSeed << 13;
        _randomSeed ^= _randomSeed >> 17;
        _randomSeed ^= _randomSeed << 5;
        return _randomSeed == 0u ? 1u : _randomSeed;
    }
}
