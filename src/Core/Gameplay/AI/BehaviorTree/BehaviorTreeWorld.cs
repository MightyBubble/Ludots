using System;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.Gameplay.AI.BehaviorTree
{
    /// <summary>
    /// Dense SoA BT agent store for a single shared tree. Graph layer stays unaware of staggering.
    /// Think cadence is owned by the caller (e.g. every 0.2s).
    /// </summary>
    public sealed class BehaviorTreeWorld
    {
        private readonly BehaviorTreeDefinition _tree;
        private readonly int[] _stack;
        private readonly byte[] _stackCount;
        private readonly byte[] _childCursor;
        private readonly BehaviorTreeStatus[] _status;
        private readonly GraphExecutionCursor[] _scriptCursors;
        private readonly int[] _scriptIntRegs;
        private readonly byte[] _scriptBoolRegs;
        private readonly int[] _scriptCallStacks;
        private int _count;

        public BehaviorTreeWorld(BehaviorTreeDefinition tree, int capacity)
        {
            _tree = tree ?? throw new ArgumentNullException(nameof(tree));
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (tree.NodeCount > BehaviorTreeLimits.MaxNodesPerTree)
            {
                throw new InvalidOperationException(
                    $"Tree '{tree.Id}' has {tree.NodeCount} nodes; max is {BehaviorTreeLimits.MaxNodesPerTree}.");
            }

            Capacity = capacity;
            _stack = new int[capacity * BehaviorTreeLimits.MaxStackDepth];
            _stackCount = new byte[capacity];
            _childCursor = new byte[capacity * BehaviorTreeLimits.MaxStackDepth];
            _status = new BehaviorTreeStatus[capacity];
            _scriptCursors = new GraphExecutionCursor[capacity];
            _scriptIntRegs = new int[capacity * GraphVmLimits.MaxIntRegisters];
            _scriptBoolRegs = new byte[capacity * GraphVmLimits.MaxBoolRegisters];
            _scriptCallStacks = new int[capacity * GraphVmLimits.MaxCallStackDepth];
        }

        public BehaviorTreeDefinition Tree => _tree;
        public int Capacity { get; }
        public int Count => _count;
        public BehaviorTreeStatus[] Statuses => _status;

        public int AddAgent()
        {
            if (_count >= Capacity)
            {
                throw new InvalidOperationException("BehaviorTreeWorld is at capacity.");
            }

            int index = _count++;
            ResetAgent(index);
            return index;
        }

        public void ResetAgent(int agent)
        {
            ValidateAgent(agent);
            _stackCount[agent] = 1;
            _stack[agent * BehaviorTreeLimits.MaxStackDepth] = _tree.RootIndex;
            _childCursor[agent * BehaviorTreeLimits.MaxStackDepth] = 0;
            _status[agent] = BehaviorTreeStatus.Running;
            _scriptCursors[agent].Reset();
            int intBase = agent * GraphVmLimits.MaxIntRegisters;
            int boolBase = agent * GraphVmLimits.MaxBoolRegisters;
            Array.Clear(_scriptIntRegs, intBase, GraphVmLimits.MaxIntRegisters);
            Array.Clear(_scriptBoolRegs, boolBase, GraphVmLimits.MaxBoolRegisters);
            Array.Clear(_scriptCallStacks, agent * GraphVmLimits.MaxCallStackDepth, GraphVmLimits.MaxCallStackDepth);
        }

        /// <summary>
        /// Runs one think wave over [0, Count). Returns wall-time via <paramref name="elapsedMs"/> when measured by caller.
        /// </summary>
        public BehaviorTreeThinkStats TickAll(
            ReadOnlySpan<GraphInstruction> scriptProgram,
            int scriptBudgetSteps)
        {
            int visited = 0;
            int scriptSlices = 0;
            int scriptSteps = 0;
            for (int agent = 0; agent < _count; agent++)
            {
                TickAgent(agent, scriptProgram, scriptBudgetSteps, ref visited, ref scriptSlices, ref scriptSteps);
            }

            return new BehaviorTreeThinkStats(_count, visited, scriptSlices, scriptSteps);
        }

        private void TickAgent(
            int agent,
            ReadOnlySpan<GraphInstruction> scriptProgram,
            int scriptBudgetSteps,
            ref int visited,
            ref int scriptSlices,
            ref int scriptSteps)
        {
            if (_status[agent] is BehaviorTreeStatus.Success or BehaviorTreeStatus.Failure)
            {
                // Cheap path: finished agents stay latched until host ResetAgent.
                return;
            }

            int depth = _stackCount[agent];
            if (depth <= 0)
            {
                _status[agent] = BehaviorTreeStatus.Success;
                return;
            }

            while (depth > 0)
            {
                int stackBase = agent * BehaviorTreeLimits.MaxStackDepth;
                int nodeIndex = _stack[stackBase + depth - 1];
                BehaviorTreeNode node = _tree.Nodes[nodeIndex];
                visited++;

                if (node.Kind is BehaviorTreeNodeKind.Condition or BehaviorTreeNodeKind.Action)
                {
                    BehaviorTreeStatus leaf = EvalLeaf(
                        agent,
                        node,
                        scriptProgram,
                        scriptBudgetSteps,
                        ref scriptSlices,
                        ref scriptSteps);
                    if (leaf == BehaviorTreeStatus.Running)
                    {
                        _status[agent] = BehaviorTreeStatus.Running;
                        return;
                    }

                    depth = PopAndPropagate(agent, depth, leaf);
                    _stackCount[agent] = (byte)depth;
                    if (depth == 0)
                    {
                        _status[agent] = leaf;
                        return;
                    }

                    continue;
                }

                // Composite: push next child or finish.
                int cursor = _childCursor[stackBase + depth - 1];
                if (cursor >= node.ChildCount)
                {
                    BehaviorTreeStatus done = node.Kind == BehaviorTreeNodeKind.Sequence
                        ? BehaviorTreeStatus.Success
                        : BehaviorTreeStatus.Failure;
                    depth = PopAndPropagate(agent, depth, done);
                    _stackCount[agent] = (byte)depth;
                    if (depth == 0)
                    {
                        _status[agent] = done;
                        return;
                    }

                    continue;
                }

                int child = node.ChildStart + cursor;
                _childCursor[stackBase + depth - 1] = (byte)(cursor + 1);
                if (depth >= BehaviorTreeLimits.MaxStackDepth)
                {
                    throw new InvalidOperationException("BT stack depth exceeded.");
                }

                _stack[stackBase + depth] = child;
                _childCursor[stackBase + depth] = 0;
                depth++;
                _stackCount[agent] = (byte)depth;
            }
        }

        private int PopAndPropagate(int agent, int depth, BehaviorTreeStatus childStatus)
        {
            depth--;
            _stackCount[agent] = (byte)depth;
            if (depth <= 0)
            {
                return 0;
            }

            int stackBase = agent * BehaviorTreeLimits.MaxStackDepth;
            int parentIndex = _stack[stackBase + depth - 1];
            BehaviorTreeNode parent = _tree.Nodes[parentIndex];

            if (parent.Kind == BehaviorTreeNodeKind.Sequence && childStatus == BehaviorTreeStatus.Failure)
            {
                return PopAndPropagate(agent, depth, BehaviorTreeStatus.Failure);
            }

            if (parent.Kind == BehaviorTreeNodeKind.Selector && childStatus == BehaviorTreeStatus.Success)
            {
                return PopAndPropagate(agent, depth, BehaviorTreeStatus.Success);
            }

            // Sequence success or Selector failure → try next sibling on subsequent loop.
            return depth;
        }

        private BehaviorTreeStatus EvalLeaf(
            int agent,
            in BehaviorTreeNode node,
            ReadOnlySpan<GraphInstruction> scriptProgram,
            int scriptBudgetSteps,
            ref int scriptSlices,
            ref int scriptSteps)
        {
            switch (node.Leaf)
            {
                case BehaviorTreeLeafBinding.AlwaysSuccess:
                    return BehaviorTreeStatus.Success;
                case BehaviorTreeLeafBinding.AlwaysFailure:
                    return BehaviorTreeStatus.Failure;
                case BehaviorTreeLeafBinding.HoldRunning:
                    return BehaviorTreeStatus.Running;
                case BehaviorTreeLeafBinding.ScriptSlice:
                {
                    if (scriptProgram.Length == 0)
                    {
                        throw new InvalidOperationException("ScriptSlice leaf requires a compiled program span.");
                    }

                    scriptSlices++;
                    Span<int> ints = _scriptIntRegs.AsSpan(agent * GraphVmLimits.MaxIntRegisters, GraphVmLimits.MaxIntRegisters);
                    Span<byte> bools = _scriptBoolRegs.AsSpan(agent * GraphVmLimits.MaxBoolRegisters, GraphVmLimits.MaxBoolRegisters);
                    Span<int> callStack = _scriptCallStacks.AsSpan(agent * GraphVmLimits.MaxCallStackDepth, GraphVmLimits.MaxCallStackDepth);
                    ref GraphExecutionCursor cursor = ref _scriptCursors[agent];
                    var state = new GraphExecutionState
                    {
                        I = ints,
                        B = bools,
                        CallStack = callStack,
                        CallStackCount = cursor.CallStackCount,
                        ReturnInt = cursor.ReturnInt,
                        Status = GraphExecutionStatus.Running
                    };
                    GraphSliceResult result = GasGraphOpHandlerTable.ExecuteSlice(
                        ref state,
                        scriptProgram,
                        GasGraphOpHandlerTable.Instance,
                        ref cursor,
                        scriptBudgetSteps);
                    scriptSteps += result.Steps;
                    if (result.Yielded || result.Running)
                    {
                        return BehaviorTreeStatus.Running;
                    }

                    cursor.Reset();
                    Array.Clear(_scriptCallStacks, agent * GraphVmLimits.MaxCallStackDepth, GraphVmLimits.MaxCallStackDepth);
                    return BehaviorTreeStatus.Success;
                }
                default:
                    throw new InvalidOperationException($"Unsupported BT leaf binding '{node.Leaf}'.");
            }
        }

        private void ValidateAgent(int agent)
        {
            if ((uint)agent >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(agent));
            }
        }
    }

    public readonly struct BehaviorTreeThinkStats
    {
        public BehaviorTreeThinkStats(int agents, int nodesVisited, int scriptSlices, int scriptSteps)
        {
            Agents = agents;
            NodesVisited = nodesVisited;
            ScriptSlices = scriptSlices;
            ScriptSteps = scriptSteps;
        }

        public int Agents { get; }
        public int NodesVisited { get; }
        public int ScriptSlices { get; }
        public int ScriptSteps { get; }
    }
}
