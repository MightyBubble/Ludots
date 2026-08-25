using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace Ludots.Core.Gameplay.AI.Fsm
{
    /// <summary>
    /// FSM 图宿主（FSM-1a）：一张由 FsmState 糖分派的 Script 图 + 每 agent 一个私有
    /// MapVariableStore（相位 SSOT，map 变量语义）+ 每 agent 常驻执行帧。
    /// 每波每 agent 恰好一个 dispatch slice：喂胶水（传感器写 I[0]）→ 读 stateVar 分派到
    /// 相位臂 → 臂内 WriteMapVarInt 迁移相位，下一波生效（与 HFSM 每 tick 至多一次迁移
    /// 的节奏一致；波内不重派）。slice 必须 halt——FsmState 图不允许 Yield。
    /// </summary>
    public sealed class GraphFsmHost : IDisposable
    {
        private readonly GraphProgramRegistry _programs;
        private readonly int _graphId;
        private readonly GraphInstruction[] _program;
        private readonly string _stateVarName;
        private readonly World _world;
        private readonly GasGraphRuntimeApi _api;
        private readonly MapVariableStore[] _stores;
        private readonly MapId[] _mapIds;
        private readonly Dictionary<MapId, int> _agentByMap;
        private readonly GraphExecutionCursor[] _cursors;
        private readonly int[] _intRegisters;
        private readonly byte[] _boolRegisters;
        private readonly float[] _floatRegisters;
        private readonly Entity[] _entityRegisters;
        private readonly Entity[] _targetRegisters;
        private readonly int[] _callStacks;
        private readonly int[] _lastReturns;
        private int _count;

        public GraphFsmHost(GraphProgramRegistry programs, int graphId, int capacity, string stateVarName)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (string.IsNullOrWhiteSpace(stateVarName)) throw new ArgumentException("stateVarName is required.", nameof(stateVarName));
            _programs = programs ?? throw new ArgumentNullException(nameof(programs));
            _graphId = graphId;
            _program = programs.RequireProgramArray(graphId, GraphKind.Script, "GraphFsmHost");
            _stateVarName = stateVarName;
            Capacity = capacity;
            _world = World.Create();
            _api = new GasGraphRuntimeApi(_world);
            _stores = new MapVariableStore[capacity];
            _mapIds = new MapId[capacity];
            _agentByMap = new Dictionary<MapId, int>(capacity);
            _cursors = new GraphExecutionCursor[capacity];
            _intRegisters = new int[capacity * GraphVmLimits.MaxIntRegisters];
            _boolRegisters = new byte[capacity * GraphVmLimits.MaxBoolRegisters];
            _floatRegisters = new float[capacity * GraphVmLimits.MaxFloatRegisters];
            _entityRegisters = new Entity[capacity * GraphVmLimits.MaxEntityRegisters];
            _targetRegisters = new Entity[capacity * GraphVmLimits.MaxTargets];
            _callStacks = new int[capacity * GraphVmLimits.MaxCallStackDepth];
            _lastReturns = new int[capacity];
            _api.BindMapVariableStoreResolver(mapId =>
                _agentByMap.TryGetValue(mapId, out int agent) ? _stores[agent] : null);
        }

        public int Capacity { get; }
        public int Count => _count;
        public int GraphId => _graphId;
        /// <summary>Last HaltReturnInt per agent from the latest wave.</summary>
        public int[] LastReturns => _lastReturns;

        public int AddAgent()
        {
            if (_count >= Capacity)
            {
                throw new InvalidOperationException("GraphFsmHost is at capacity.");
            }

            int index = _count++;
            var mapId = new MapId($"fsm.agent.{index}");
            _mapIds[index] = mapId;
            _agentByMap[mapId] = index;
            _stores[index] = MapVariableStore.Create(
                mapId,
                new List<MapVariableDeclaration>
                {
                    new() { Name = _stateVarName, Type = MapVariableType.Int, Initial = 0 }
                });
            ResetAgent(index);
            return index;
        }

        /// <summary>Full wipe: cursor, registers, call stack, and the phase variable back to 0.</summary>
        public void ResetAgent(int agent)
        {
            ValidateAgent(agent);
            _cursors[agent].Reset();
            ClearRegisters(agent);
            _stores[agent].WriteInt(_stateVarName, 0);
            _lastReturns[agent] = 0;
        }

        /// <summary>Current FSM phase = the agent's map variable (enum declaration order).</summary>
        public int PhaseOf(int agent)
        {
            ValidateAgent(agent);
            return _stores[agent].ReadInt(_stateVarName);
        }

        /// <summary>
        /// One dispatch wave: every agent runs exactly one slice of the FSM graph.
        /// Sensors glue-feed I[0..] before the slice; arm-side WriteMapVarInt transitions
        /// become visible to the dispatch read on the NEXT wave.
        /// </summary>
        public GraphFsmThinkStats ThinkWave(int budgetSteps, IBehaviorTreeSensorFeed? sensors = null)
        {
            if (budgetSteps <= 0) throw new ArgumentOutOfRangeException(nameof(budgetSteps));

            int halted = 0;
            int steps = 0;
            for (int agent = 0; agent < _count; agent++)
            {
                ref GraphExecutionCursor cursor = ref _cursors[agent];
                cursor.Reset();
                ClearRegisters(agent);
                sensors?.WriteSensors(agent, _graphId,
                    _intRegisters.AsSpan(agent * GraphVmLimits.MaxIntRegisters, GraphVmLimits.MaxIntRegisters),
                    _boolRegisters.AsSpan(agent * GraphVmLimits.MaxBoolRegisters, GraphVmLimits.MaxBoolRegisters));

                GraphSliceResult result = GraphExecutor.ExecuteScriptSlice(
                    _world,
                    Entity.Null,
                    Entity.Null,
                    default,
                    _program,
                    _api,
                    _programs,
                    _floatRegisters.AsSpan(agent * GraphVmLimits.MaxFloatRegisters, GraphVmLimits.MaxFloatRegisters),
                    _intRegisters.AsSpan(agent * GraphVmLimits.MaxIntRegisters, GraphVmLimits.MaxIntRegisters),
                    _boolRegisters.AsSpan(agent * GraphVmLimits.MaxBoolRegisters, GraphVmLimits.MaxBoolRegisters),
                    _entityRegisters.AsSpan(agent * GraphVmLimits.MaxEntityRegisters, GraphVmLimits.MaxEntityRegisters),
                    _targetRegisters.AsSpan(agent * GraphVmLimits.MaxTargets, GraphVmLimits.MaxTargets),
                    _callStacks.AsSpan(agent * GraphVmLimits.MaxCallStackDepth, GraphVmLimits.MaxCallStackDepth),
                    ref cursor,
                    budgetSteps,
                    GraphKind.Script,
                    mapScope: _mapIds[agent]);
                steps += result.Steps;

                if (!result.Halted)
                {
                    throw new InvalidOperationException(
                        $"FSM graph {_graphId} did not halt within budget (Yield/budget suspension is not supported on FSM dispatch slices).");
                }

                _lastReturns[agent] = result.ReturnInt;
                halted++;
            }

            return new GraphFsmThinkStats(_count, halted, steps);
        }

        public void Dispose()
        {
            _world.Dispose();
        }

        private void ClearRegisters(int agent)
        {
            Array.Clear(_intRegisters, agent * GraphVmLimits.MaxIntRegisters, GraphVmLimits.MaxIntRegisters);
            Array.Clear(_boolRegisters, agent * GraphVmLimits.MaxBoolRegisters, GraphVmLimits.MaxBoolRegisters);
            Array.Clear(_floatRegisters, agent * GraphVmLimits.MaxFloatRegisters, GraphVmLimits.MaxFloatRegisters);
            Array.Clear(_targetRegisters, agent * GraphVmLimits.MaxTargets, GraphVmLimits.MaxTargets);
            Array.Clear(_callStacks, agent * GraphVmLimits.MaxCallStackDepth, GraphVmLimits.MaxCallStackDepth);
        }

        private void ValidateAgent(int agent)
        {
            if ((uint)agent >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(agent));
            }
        }
    }

    public readonly struct GraphFsmThinkStats
    {
        public GraphFsmThinkStats(int agents, int halted, int steps)
        {
            Agents = agents;
            Halted = halted;
            Steps = steps;
        }

        public int Agents { get; }
        public int Halted { get; }
        public int Steps { get; }
    }
}
