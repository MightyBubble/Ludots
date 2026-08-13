using System;
using System.Diagnostics;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.MultiLayerGraph;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace CapabilityStandardGraphOpsEventMod.Runtime;

public sealed class GraphOpsEventRuntime : IDisposable
{
    private const int DispatchTemplateId = 99;

    private readonly GraphShowcaseConfig _config = new();
    private GraphProgramRegistry? _programs;
    private TargetDispatchPresetRegistry? _targetDispatchPresets;
    private World? _world;
    private GasGraphRuntimeApi? _api;
    private GameplayEventBus? _eventBus;
    private EffectRequestQueue? _effectRequests;
    private RelationshipRuntime? _relationships;
    private ControlDomainQuery? _controlDomains;
    private EntityCollectionStore? _entityCollections;
    private LoadedGraphRuntime? _loadedGraph;
    private Entity _playerRep;
    private Entity _controller;
    private Entity _unit;
    private Entity _snapMarker;
    private Entity _farSnapMarker;
    private int _dispatchGraphId;
    private int _placementGraphId;
    private byte _controlsReg;
    private byte _projectionReg;
    private byte _payloadIntReg;
    private byte _payloadFloatReg;
    private byte _aimXReg;
    private byte _aimYReg;
    private byte _clampReg;
    private byte _inCircleReg;
    private byte _snapCollectionFlagsReg;
    private byte _snapEdgeReg;
    private float _accum;
    private int _wave;
    private int _eventCount;
    private int _dispatchCount;
    private int _payloadInt;
    private float _payloadFloat;
    private int _aimX;
    private int _aimY;
    private bool _controlsOk;
    private bool _projectionOk;
    private bool _clampOk;
    private bool _inCircleOk;
    private bool _snapCollectionOk;
    private bool _snapEdgeOk;
    private int _snappedX;
    private int _snappedY;
    private bool _ownsPrograms;

    public float PlayerRepX => -2f;
    public float PlayerRepY => 0f;
    public float ControllerX => 2f;
    public float ControllerY => 1.5f;
    public float UnitX => 0f;
    public float UnitY => 0f;
    public float SnapMarkerX => 0.12f;
    public float SnapMarkerY => 0.08f;
    public int EventCount => _eventCount;
    public int DispatchCount => _dispatchCount;
    public bool ControlsOk => _controlsOk;
    public bool ProjectionOk => _projectionOk;
    public bool SnapCollectionOk => _snapCollectionOk;
    public bool SnapEdgeOk => _snapEdgeOk;
    public int PayloadInt => _payloadInt;
    public float PayloadFloat => _payloadFloat;
    public bool ClampOk => _clampOk;
    public bool InCircleOk => _inCircleOk;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_graph_ops_event" };

    public void Bind(
        GraphProgramRegistry programs,
        TargetDispatchPresetRegistry targetDispatchPresets,
        EntityCollectionStore entityCollections)
    {
        _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        _targetDispatchPresets = targetDispatchPresets ?? throw new ArgumentNullException(nameof(targetDispatchPresets));
        _entityCollections = entityCollections ?? throw new ArgumentNullException(nameof(entityCollections));
        _ownsPrograms = false;
    }

    public void BindStandaloneFromModAssets()
    {
        string assetsRoot = GraphOpsEventGraphBootstrap.FindModAssetsRoot();
        _programs = GraphOpsEventGraphBootstrap.LoadModGraphs(
            assetsRoot,
            out TargetDispatchPresetRegistry presets,
            out EntityCollectionStore collections);
        _targetDispatchPresets = presets;
        _entityCollections = collections;
        _ownsPrograms = true;
    }

    public void EnsureWorld()
    {
        if (_world != null) return;
        if (_programs == null || _targetDispatchPresets == null || _entityCollections == null)
        {
            throw new InvalidOperationException(
                "GraphOpsEventRuntime.Bind(Registry, Presets, Collections) or BindStandaloneFromModAssets() required.");
        }

        _dispatchGraphId = RequireGraphId(GraphOpsEventGraphKeys.Dispatch);
        _placementGraphId = RequireGraphId(GraphOpsEventGraphKeys.Placement);
        ResolveControlRegisters();

        _world = World.Create();
        _eventBus = new GameplayEventBus();
        _effectRequests = new EffectRequestQueue();

        var types = new RelationshipTypeRegistry();
        int ownsType = types.Register("Owns");
        int controlsType = types.Register("Controls");
        _relationships = new RelationshipRuntime(
            _world,
            types,
            new RelationshipMetricRegistry(),
            new RelationshipFlagRegistry(),
            new RelationshipBandRegistry(),
            new RelationshipChangeBuffer(capacity: 16),
            new RelationshipReverseIndex(_world));
        var ownership = new OwnershipResolver(_relationships, ownsType);
        _controlDomains = new ControlDomainQuery(_world, _relationships, ownership, ownsType, controlsType);

        var knowledgeStore = new KnowledgeProjectionStore();
        var knowledgeResolver = new KnowledgeProjectionResolver(knowledgeStore);

        _api = new GasGraphRuntimeApi(
            _world,
            spatialQueries: null,
            coords: null,
            eventBus: _eventBus,
            effectRequests: _effectRequests,
            relationshipRuntime: _relationships,
            targetDispatchPresets: _targetDispatchPresets,
            entityCollections: _entityCollections);
        _api.BindTopologyServices(_controlDomains, knowledgeResolver, new DiscreteClock());
        _api.BindLoadedGraphRuntime(_loadedGraph = BuildNavGraph());

        _playerRep = _world.Create(
            new PlayerIdentity { PlayerId = 1 },
            new WorldPositionCm { Value = Fix64Vec2.Zero });
        _controller = _world.Create(new PlayerIdentity { PlayerId = 2 });
        _unit = _world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
        _snapMarker = _world.Create(new WorldPositionCm { Value = Fix64Vec2.FromInt(12, 8) });
        _farSnapMarker = _world.Create(new WorldPositionCm { Value = Fix64Vec2.FromInt(5000, 0) });

        ownership.EnsureOwnership(_playerRep, _unit);
        _relationships.EnsureLink(_controller, _playerRep, controlsType);
        knowledgeStore.Upsert(_controller, _unit, CreateDisclosure(_controller));

        Entity[] snapTargets = { _farSnapMarker, _snapMarker };
        _entityCollections.Replace(
            _playerRep,
            EntityCollectionDescriptor.Create(
                GraphOpsEventGraphKeys.SnapCollection,
                EntityCollectionSourceKind.Debug,
                EntityCollectionRoleKind.Debug),
            snapTargets);

        Metrics.AgentCount = 4;
        Metrics.Detail = "发事件/控制域/吸附沙盘就位：等待第一波图节点执行。";
    }

    public void Tick(float dt)
    {
        EnsureWorld();

        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds) return;
        _accum = 0f;
        _wave++;

        var sw = Stopwatch.StartNew();
        _effectRequests!.Clear();
        RunPlacementGraph();
        RunDispatchGraph();
        _eventBus!.Update();
        sw.Stop();

        _eventCount = _eventBus!.Events.Count;
        _dispatchCount = _effectRequests!.Count;

        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;

        Metrics.Detail =
            $"发事件×{_eventCount}；读载荷 int {_payloadInt} / float {_payloadFloat:F1}；" +
            $"控制域{Established(_controlsOk)}、知识投影{Established(_projectionOk)}；" +
            $"扇出派发×{_dispatchCount}；" +
            $"落点({_aimX},{_aimY})钳制{Established(_clampOk)}；是否在圆内{Established(_inCircleOk)}；" +
            $"集合{SnapPhrase(_snapCollectionOk)}、路网边{SnapPhrase(_snapEdgeOk)}→({_snappedX},{_snappedY})";
    }

    public void Dispose()
    {
        _world?.Dispose();
        _world = null;
        if (_ownsPrograms)
        {
            _programs = null;
            _targetDispatchPresets = null;
        }
    }

    private int RequireGraphId(string graphKey)
    {
        int graphId = GraphIdRegistry.GetId(graphKey);
        if (!_programs!.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program) || program.Length == 0)
        {
            throw new InvalidOperationException($"Graph '{graphKey}' missing from registry.");
        }

        return graphId;
    }

    private void ResolveControlRegisters()
    {
        if (!_programs!.TryGetProgram(_dispatchGraphId, out ReadOnlySpan<GraphInstruction> dispatch))
        {
            throw new InvalidOperationException("Dispatch graph missing after EnsureWorld.");
        }

        if (!_programs.TryGetProgram(_placementGraphId, out ReadOnlySpan<GraphInstruction> placement))
        {
            throw new InvalidOperationException("Placement graph missing after EnsureWorld.");
        }

        _controlsReg = FindDest(dispatch, GraphNodeOp.ControlDomainControls);
        _projectionReg = FindDest(dispatch, GraphNodeOp.KnowledgeHasProjection);
        _payloadIntReg = FindDest(dispatch, GraphNodeOp.LoadEventPayloadInt, imm: 0);
        _payloadFloatReg = FindDest(dispatch, GraphNodeOp.LoadEventPayloadFloat, imm: 3);
        _aimXReg = FindDest(placement, GraphNodeOp.LoadTargetPosX);
        _aimYReg = FindDest(placement, GraphNodeOp.LoadTargetPosY);
        _clampReg = FindDest(placement, GraphNodeOp.ClampTargetToRange);
        _inCircleReg = FindDest(placement, GraphNodeOp.IsPointInCircle);
        _snapCollectionFlagsReg = FindFlags(placement, GraphNodeOp.SnapToNearestInCollection);
        _snapEdgeReg = FindDest(placement, GraphNodeOp.SnapToNearestGraphEdge);
    }

    private void RunDispatchGraph()
    {
        if (!_programs!.TryGetProgram(_dispatchGraphId, out ReadOnlySpan<GraphInstruction> program))
        {
            throw new InvalidOperationException("Dispatch graph missing.");
        }

        Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
        Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
        Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
        Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
        Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
        Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
        targets[0] = _unit;
        var targetList = new GraphTargetList(targets);
        targetList.SetCount(1);

        var state = new GraphExecutionState
        {
            World = _world!,
            Caster = _playerRep,
            ExplicitTarget = _unit,
            TargetContext = _unit,
            Viewer = _controller,
            EventPayload = new GraphEventPayload
            {
                PayloadA = DispatchTemplateId,
                PayloadB = DispatchTemplateId,
                FloatD = 2.5f,
            },
            Api = _api!,
            F = floats,
            I = ints,
            B = bools,
            E = entities,
            Targets = targets,
            TargetList = targetList,
            CallStack = callStack,
            Status = GraphExecutionStatus.Running,
        };

        GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
        _controlsOk = bools[_controlsReg] != 0;
        _projectionOk = bools[_projectionReg] != 0;
        _payloadInt = ints[_payloadIntReg];
        _payloadFloat = floats[_payloadFloatReg];
    }

    private void RunPlacementGraph()
    {
        if (!_programs!.TryGetProgram(_placementGraphId, out ReadOnlySpan<GraphInstruction> program))
        {
            throw new InvalidOperationException("Placement graph missing.");
        }

        Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
        Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
        Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
        Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
        Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
        var targetPos = new IntVector2(36, 20);

        var state = new GraphExecutionState
        {
            World = _world!,
            Caster = _playerRep,
            ExplicitTarget = Entity.Null,
            TargetPosCm = targetPos,
            Api = _api!,
            F = floats,
            I = ints,
            B = bools,
            E = entities,
            CallStack = callStack,
            Status = GraphExecutionStatus.Running,
        };

        GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
        _aimX = ints[_aimXReg];
        _aimY = ints[_aimYReg];
        _clampOk = bools[_clampReg] != 0;
        _inCircleOk = bools[_inCircleReg] != 0;
        _snapCollectionOk = bools[_snapCollectionFlagsReg] != 0;
        _snapEdgeOk = bools[_snapEdgeReg] != 0;
        _snappedX = state.TargetPosCm.X;
        _snappedY = state.TargetPosCm.Y;
    }

    private static LoadedGraphRuntime BuildNavGraph()
    {
        var loadedChunks = new WorldGridLoadedChunks(chunkSizeCm: 1000, loadedChunkCapacity: 1);
        var store = new ChunkedNodeGraphStore();
        store.SubscribeToLoadedChunks(loadedChunks);
        long chunkKey = GraphChunkKey.Pack(0, 0);
        var graphBuilder = new NodeGraphBuilder(3, 2);
        graphBuilder.AddNode(0, 0);
        graphBuilder.AddNode(100, 0);
        graphBuilder.AddNode(200, 0);
        graphBuilder.AddEdge(0, 1, 100f);
        graphBuilder.AddEdge(1, 2, 100f);
        store.AddOrReplace(chunkKey, new GraphChunkData(graphBuilder.Build(), Array.Empty<GraphCrossEdge>()));
        loadedChunks.SetLoaded(chunkKey, loaded: true);
        return new LoadedGraphRuntime(store, loadedChunks, preferredProjectionCellSizeCm: 100);
    }

    private static KnowledgeDisclosureRecord CreateDisclosure(Entity source)
    {
        return new KnowledgeDisclosureRecord(
            KnowledgePresence.LiveVisible,
            KnowledgePositionAccess.Live,
            KnowledgeIdMask256.Empty,
            KnowledgeIdMask256.Empty,
            KnowledgeIdMask256.Empty,
            source,
            observedTick: 0,
            expiryTick: int.MaxValue,
            confidencePermille: 1000,
            revision: 0);
    }

    private static byte FindDest(ReadOnlySpan<GraphInstruction> program, GraphNodeOp op, int? imm = null)
    {
        for (int i = 0; i < program.Length; i++)
        {
            if (program[i].Op == (ushort)op && (!imm.HasValue || program[i].Imm == imm.Value))
            {
                return program[i].Dst;
            }
        }

        throw new InvalidOperationException($"Program missing op {op}.");
    }

    private static byte FindFlags(ReadOnlySpan<GraphInstruction> program, GraphNodeOp op)
    {
        for (int i = program.Length - 1; i >= 0; i--)
        {
            if (program[i].Op == (ushort)op)
            {
                return program[i].Flags;
            }
        }

        throw new InvalidOperationException($"Program missing flags op {op}.");
    }

    private static string Established(bool ok) => ok ? "成立" : "未成立";

    private static string SnapPhrase(bool ok) => ok ? "吸附成功" : "未吸上";
}
