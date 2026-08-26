using System;
using Arch.Core;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.Gameplay.AI
{
    /// <summary>
    /// Graph-path behavior tree host: one shared Script tree program (compiled from
    /// BtSequence/BtSelector/BtDecorator sugar), one resident execution frame per agent
    /// (registers + call stack + cursor), one ExecuteSlice per agent per think wave.
    /// A Yield or slice-budget suspension parks the frame and the next wave resumes it
    /// (pc inside the leaf, call stack holding the parents' resume addresses); a halted
    /// tick reports status through HaltReturnInt per <see cref="GraphBtStatusCodes"/>.
    /// Finished (Success/Failure) agents stay latched until the host restarts them —
    /// the C# interpreter (<see cref="BehaviorTreeWorld"/>) is never consulted.
    /// </summary>
    public sealed class GraphBehaviorTreeHost
    {
        private readonly GraphProgramRegistry _programs;
        private readonly int _graphId;
        private readonly GraphInstruction[] _program;
        private readonly GraphExecutionCursor[] _cursors;
        private readonly int[] _intRegisters;
        private readonly byte[] _boolRegisters;
        private readonly float[] _floatRegisters;
        private readonly Entity[] _entityRegisters;
        private readonly Entity[] _targetRegisters;
        private readonly int[] _callStacks;
        private readonly BehaviorTreeStatus[] _statuses;
        private readonly int[] _lastReturns;
        private int _count;

        public GraphBehaviorTreeHost(GraphProgramRegistry programs, int graphId, int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _programs = programs ?? throw new ArgumentNullException(nameof(programs));
            _graphId = graphId;
            _program = programs.RequireProgramArray(graphId, GraphKind.Script, "GraphBehaviorTreeHost");
            Capacity = capacity;
            _cursors = new GraphExecutionCursor[capacity];
            _intRegisters = new int[capacity * GraphVmLimits.MaxIntRegisters];
            _boolRegisters = new byte[capacity * GraphVmLimits.MaxBoolRegisters];
            _floatRegisters = new float[capacity * GraphVmLimits.MaxFloatRegisters];
            _entityRegisters = new Entity[capacity * GraphVmLimits.MaxEntityRegisters];
            _targetRegisters = new Entity[capacity * GraphVmLimits.MaxTargets];
            _callStacks = new int[capacity * GraphVmLimits.MaxCallStackDepth];
            _statuses = new BehaviorTreeStatus[capacity];
            _lastReturns = new int[capacity];
        }

        public int Capacity { get; }
        public int Count => _count;
        public int GraphId => _graphId;
        public BehaviorTreeStatus[] Statuses => _statuses;
        /// <summary>Last HaltReturnInt per agent (BT status codes 0/1/2; intent codes stay inside leaf graphs).</summary>
        public int[] LastReturns => _lastReturns;

        public int AddAgent()
        {
            if (_count >= Capacity)
            {
                throw new InvalidOperationException("GraphBehaviorTreeHost is at capacity.");
            }

            int index = _count++;
            ResetAgent(index);
            return index;
        }

        /// <summary>Full wipe: cursor, registers, call stack, latched status.</summary>
        public void ResetAgent(int agent)
        {
            ValidateAgent(agent);
            _cursors[agent].Reset();
            ClearRegisters(agent);
            _statuses[agent] = BehaviorTreeStatus.Running;
            _lastReturns[agent] = 0;
        }

        public bool IsSuspended(int agent)
        {
            ValidateAgent(agent);
            return _cursors[agent].IsSuspended;
        }

        public GraphExecutionCursor CursorOf(int agent)
        {
            ValidateAgent(agent);
            return _cursors[agent];
        }

        /// <summary>
        /// Glue drain for pinned leaf outputs (intent codes written by leaf chains into a pinned
        /// register, e.g. I[3]). Read after a think wave; the next RestartFinishedAgents wipes it.
        /// </summary>
        public int ReadInt(int agent, int index)
        {
            ValidateAgent(agent);
            if ((uint)index >= (uint)GraphVmLimits.MaxIntRegisters)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _intRegisters[(agent * GraphVmLimits.MaxIntRegisters) + index];
        }

        public BehaviorTreeStatus StatusOf(int agent)
        {
            ValidateAgent(agent);
            return _statuses[agent];
        }

        /// <summary>
        /// Restarts every Success/Failure agent from the tree root for the next think wave.
        /// Register wipe is deferred: ThinkWave already clears + re-feeds every non-resumed
        /// frame before execution, so clearing here would double-memset the SoA banks
        /// (the target bank alone is 2KB per agent).
        /// </summary>
        public int RestartFinishedAgents()
        {
            int restarted = 0;
            for (int agent = 0; agent < _count; agent++)
            {
                if (_statuses[agent] is BehaviorTreeStatus.Success or BehaviorTreeStatus.Failure)
                {
                    _cursors[agent].Reset();
                    _statuses[agent] = BehaviorTreeStatus.Running;
                    _lastReturns[agent] = 0;
                    restarted++;
                }
            }

            return restarted;
        }

        /// <summary>
        /// One think wave: every agent executes (or resumes) one slice of the tree program.
        /// Latched Success/Failure agents are skipped until <see cref="RestartFinishedAgents"/>.
        /// </summary>
        public GraphBehaviorTreeThinkStats ThinkWave(
            int budgetSteps,
            IBehaviorTreeSensorFeed? sensors = null,
            World? world = null,
            Entity caster = default,
            Entity explicitTarget = default,
            IGraphRuntimeApi? api = null)
        {
            if (budgetSteps <= 0) throw new ArgumentOutOfRangeException(nameof(budgetSteps));

            int resumed = 0;
            int halted = 0;
            int steps = 0;
            for (int agent = 0; agent < _count; agent++)
            {
                if (_statuses[agent] is BehaviorTreeStatus.Success or BehaviorTreeStatus.Failure)
                {
                    continue;
                }

                ref GraphExecutionCursor cursor = ref _cursors[agent];
                bool resume = cursor.IsSuspended;
                if (!resume)
                {
                    cursor.Reset();
                    ClearRegisters(agent);
                    sensors?.WriteSensors(agent, _graphId,
                        _intRegisters.AsSpan(agent * GraphVmLimits.MaxIntRegisters, GraphVmLimits.MaxIntRegisters),
                        _boolRegisters.AsSpan(agent * GraphVmLimits.MaxBoolRegisters, GraphVmLimits.MaxBoolRegisters));
                }
                else
                {
                    resumed++;
                }

                GraphSliceResult result = GraphExecutor.ExecuteResolvedRegisteredScriptSlice(
                    _programs,
                    _program,
                    _floatRegisters.AsSpan(agent * GraphVmLimits.MaxFloatRegisters, GraphVmLimits.MaxFloatRegisters),
                    _intRegisters.AsSpan(agent * GraphVmLimits.MaxIntRegisters, GraphVmLimits.MaxIntRegisters),
                    _boolRegisters.AsSpan(agent * GraphVmLimits.MaxBoolRegisters, GraphVmLimits.MaxBoolRegisters),
                    _entityRegisters.AsSpan(agent * GraphVmLimits.MaxEntityRegisters, GraphVmLimits.MaxEntityRegisters),
                    _targetRegisters.AsSpan(agent * GraphVmLimits.MaxTargets, GraphVmLimits.MaxTargets),
                    _callStacks.AsSpan(agent * GraphVmLimits.MaxCallStackDepth, GraphVmLimits.MaxCallStackDepth),
                    ref cursor,
                    budgetSteps,
                    world,
                    caster,
                    explicitTarget,
                    api);
                steps += result.Steps;

                if (result.Halted)
                {
                    _statuses[agent] = MapTickStatus(result.ReturnInt);
                    _lastReturns[agent] = result.ReturnInt;
                    halted++;
                }
                else
                {
                    _statuses[agent] = BehaviorTreeStatus.Running;
                }
            }

            return new GraphBehaviorTreeThinkStats(_count, resumed, halted, steps);
        }

        private static BehaviorTreeStatus MapTickStatus(int returnInt)
            => returnInt switch
            {
                GraphBtStatusCodes.Failure => BehaviorTreeStatus.Failure,
                GraphBtStatusCodes.Success => BehaviorTreeStatus.Success,
                GraphBtStatusCodes.Running => BehaviorTreeStatus.Running,
                _ => throw new InvalidOperationException(
                    $"BT tree program halted with ReturnInt={returnInt}; the BT status contract is 0=Failure/1=Success/2=Running ({nameof(GraphBtStatusCodes)}).")
            };

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

    public readonly struct GraphBehaviorTreeThinkStats
    {
        public GraphBehaviorTreeThinkStats(int agents, int resumed, int halted, int steps)
        {
            Agents = agents;
            Resumed = resumed;
            Halted = halted;
            Steps = steps;
        }

        public int Agents { get; }
        public int Resumed { get; }
        public int Halted { get; }
        public int Steps { get; }
    }
}
