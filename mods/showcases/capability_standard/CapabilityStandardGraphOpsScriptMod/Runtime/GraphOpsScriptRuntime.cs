using System;
using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace CapabilityStandardGraphOpsScriptMod.Runtime;

public sealed class GraphOpsScriptRuntime
{
    private enum Phase
    {
        Drink,
        Patrol,
        ConstPipeline
    }

    private readonly GraphShowcaseConfig _config = new();
    private GraphProgramRegistry? _programs;
    private GraphActionCatalog? _actions;
    private GraphFunctionCatalog? _catalog;
    private Phase _phase = Phase.Drink;
    private int _drinkGraphId;
    private int _patrolGraphId;
    private int _invokeConstGraphId;
    private GraphExecutionCursor _cursor;
    private readonly int[] _ints = new int[GraphVmLimits.MaxIntRegisters];
    private readonly byte[] _bools = new byte[GraphVmLimits.MaxBoolRegisters];
    private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];
    private bool _started;
    private bool _sawYield;
    private float _accum;

    public const string DrinkActionName = "script.drinkUntilFull";
    public const string InvokeConstGraphKey = "Graph.FuncLib.Demo.InvokeConstSeven";
    public const string ConstFunctionName = "demo.const.seven";
    public const string CaptionTitle = "喝茶续杯、巡逻与常量管线";
    public static readonly Vector2[] PatrolStops =
    {
        new(-4f, -2f), new(4f, -2f), new(4f, 2f), new(-4f, 2f)
    };

    private GraphOpsStageVisuals? _stage;
    private Entity _patrolProxy;
    private bool _visualsSpawned;
    public int DrinkLimit { get; } = 5;
    public int PatrolLimit { get; } = 2;
    public int Water => _ints[0];
    public int PatrolStep => _ints[0];
    public int DisplayedWater => CompletedWater > 0 ? Math.Max(CompletedWater, Water) : Water;
    public int DisplayedPatrolStep => CompletedPatrolSteps > 0 ? Math.Max(CompletedPatrolSteps, PatrolStep) : PatrolStep;
    public int CompletedWater { get; private set; }
    public int CompletedPatrolSteps { get; private set; }
    public int ConstValue { get; private set; }
    public bool AllPhasesComplete { get; private set; }
    public bool SawYield => _sawYield;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_graph_ops_script" };

    public void Bind(GraphProgramRegistry programs, GraphActionCatalog actions, GraphFunctionCatalog catalog)
    {
        _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public void BindStageVisuals(GraphOpsStageVisuals stage)
    {
        _stage = stage ?? throw new ArgumentNullException(nameof(stage));
    }

    public void EnsureWorld()
    {
        if (_drinkGraphId > 0) return;
        if (_programs == null || _actions == null || _catalog == null)
        {
            throw new InvalidOperationException("GraphOpsScriptRuntime.Bind(Registry, ActionCatalog, FunctionCatalog) required.");
        }

        _drinkGraphId = GraphRegistryScriptResolver.RequireActionId(_actions, DrinkActionName);
        _patrolGraphId = GraphRegistryScriptResolver.RequireActionId(_actions, BehaviorTreeScriptKeys.Patrol);
        _invokeConstGraphId = GraphRegistryScriptResolver.RequireId(InvokeConstGraphKey);
        _ = GraphRegistryScriptResolver.RequireProgram(_programs, _drinkGraphId);
        _ = GraphRegistryScriptResolver.RequireProgram(_programs, _patrolGraphId);
        _ = GraphRegistryScriptResolver.RequireProgram(_programs, _invokeConstGraphId);
        _ = _catalog.Require(ConstFunctionName);
        EnsureInvokeGraphPatched();
        Metrics.AgentCount = 1;
        Metrics.Detail = "先喝茶续杯，再巡逻推进，最后走常量管线。";
        SpawnStageVisuals();
    }

    private void SpawnStageVisuals()
    {
        if (_stage == null || _visualsSpawned)
        {
            return;
        }

        _stage.Spawn(GraphOpsVisualTemplates.Caster, "喝茶人", -6f, 0f, 100f, 100f);
        Vector2 firstStop = PatrolStops[0];
        _patrolProxy = _stage.Spawn(GraphOpsVisualTemplates.Scout, "巡逻", firstStop.X, firstStop.Y, 100f, 100f);
        _visualsSpawned = true;
    }

    private void SyncPatrolVisual()
    {
        if (_stage == null || !_visualsSpawned)
        {
            return;
        }

        int step = Math.Clamp(DisplayedPatrolStep, 0, PatrolStops.Length - 1);
        Vector2 pos = PatrolStops[step];
        _stage.SetPosition(_patrolProxy, pos.X, pos.Y);
    }

    private void EnsureInvokeGraphPatched()
    {
        if (!_programs!.TryGetRegistration(_invokeConstGraphId, out GraphProgramRegistration registration))
        {
            throw new InvalidOperationException($"Invoke graph id {_invokeConstGraphId} is not registered.");
        }

        GraphProgramSymbolPatcher.PatchFuncLib(registration.Symbols, registration.Program, _catalog!);
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        SpawnStageVisuals();
        if (AllPhasesComplete) return;

        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds) return;
        _accum = 0f;

        if (!_started)
        {
            ResetSlice();
            _started = true;
        }

        int graphId = ActiveGraphId();
        ReadOnlySpan<GraphInstruction> program = GraphRegistryScriptResolver.RequireProgram(_programs!, graphId);
        var state = new GraphExecutionState
        {
            Programs = _programs,
            I = _ints,
            B = _bools,
            CallStack = _callStack,
            CallStackCount = _cursor.CallStackCount,
            ReturnInt = _cursor.ReturnInt,
            Status = GraphExecutionStatus.Running
        };

        var sw = Stopwatch.StartNew();
        GraphSliceResult result = GasGraphOpHandlerTable.ExecuteSlice(
            ref state,
            program,
            GasGraphOpHandlerTable.Instance,
            ref _cursor,
            budgetSteps: 64);
        sw.Stop();

        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;
        SyncPatrolVisual();

        if (result.Yielded)
        {
            _sawYield = true;
            Metrics.Detail = BuildYieldDetail();
            return;
        }

        if (!result.Halted)
        {
            throw new InvalidOperationException(
                $"Script slice returned unexpected status {result.Status}; increase budget.");
        }

        AdvancePhase(result.ReturnInt);
    }

    private int ActiveGraphId()
        => _phase switch
        {
            Phase.Drink => _drinkGraphId,
            Phase.Patrol => _patrolGraphId,
            Phase.ConstPipeline => _invokeConstGraphId,
            _ => throw new InvalidOperationException($"Unknown phase {_phase}.")
        };

    private void AdvancePhase(int returnInt)
    {
        switch (_phase)
        {
            case Phase.Drink:
                CompletedWater = returnInt;
                Metrics.Detail = $"茶已喝满：水位 {returnInt}/{DrinkLimit}。";
                _phase = Phase.Patrol;
                ResetSlice();
                break;
            case Phase.Patrol:
                CompletedPatrolSteps = PatrolStep;
                Metrics.Detail = $"巡逻完成：走完 {CompletedPatrolSteps}/{PatrolLimit} 站。";
                _phase = Phase.ConstPipeline;
                ResetSlice();
                break;
            case Phase.ConstPipeline:
                ConstValue = returnInt;
                AllPhasesComplete = true;
                Metrics.Detail = $"常量管线就绪：配方算出 {ConstValue}。";
                break;
        }
    }

    private string BuildYieldDetail()
        => _phase switch
        {
            Phase.Drink => $"喝茶续杯中：水位 {Water}/{DrinkLimit}，等待下一回合继续。",
            Phase.Patrol => $"巡逻推进中：第 {PatrolStep}/{PatrolLimit} 站，等待下一回合继续。",
            Phase.ConstPipeline => "常量管线执行中，等待下一回合继续。",
            _ => Metrics.Detail
        };

    private void ResetSlice()
    {
        _cursor.Reset();
        Array.Clear(_ints, 0, _ints.Length);
        Array.Clear(_bools, 0, _bools.Length);
        Array.Clear(_callStack, 0, _callStack.Length);
        _started = true;
    }
}
