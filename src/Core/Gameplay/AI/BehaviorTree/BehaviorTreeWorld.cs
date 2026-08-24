using System;
using System.Runtime.CompilerServices;
using Arch.Core;
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
        private readonly int[] _lastScriptReturns;
        private readonly GraphExecutionCursor[] _scriptCursors;
        private readonly int[] _scriptResumeGraphIds;
        private readonly int[] _scriptIntRegs;
        private readonly byte[] _scriptBoolRegs;
        private readonly float[] _scriptFloatRegs;
        private readonly Entity[] _scriptEntityRegs;
        private readonly Entity[] _scriptTargetRegs;
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
            _lastScriptReturns = new int[capacity];
            _scriptCursors = new GraphExecutionCursor[capacity];
            _scriptResumeGraphIds = new int[capacity];
            _scriptIntRegs = new int[capacity * GraphVmLimits.MaxIntRegisters];
            _scriptBoolRegs = new byte[capacity * GraphVmLimits.MaxBoolRegisters];
            _scriptFloatRegs = new float[capacity * GraphVmLimits.MaxFloatRegisters];
            _scriptEntityRegs = new Entity[capacity * GraphVmLimits.MaxEntityRegisters];
            _scriptTargetRegs = new Entity[capacity * GraphVmLimits.MaxTargets];
            _scriptCallStacks = new int[capacity * GraphVmLimits.MaxCallStackDepth];
        }

        public BehaviorTreeDefinition Tree => _tree;
        public int Capacity { get; }
        public int Count => _count;
        public BehaviorTreeStatus[] Statuses => _status;
        /// <summary>Last HaltReturnInt from a ScriptSlice action leaf (intent codes for showcases).</summary>
        public int[] LastScriptReturns => _lastScriptReturns;

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
            RestartThinking(agent);
            _scriptCursors[agent].Reset();
            _scriptResumeGraphIds[agent] = 0;
            int intBase = agent * GraphVmLimits.MaxIntRegisters;
            int boolBase = agent * GraphVmLimits.MaxBoolRegisters;
            int floatBase = agent * GraphVmLimits.MaxFloatRegisters;
            int entityBase = agent * GraphVmLimits.MaxEntityRegisters;
            int targetBase = agent * GraphVmLimits.MaxTargets;
            Array.Clear(_scriptIntRegs, intBase, GraphVmLimits.MaxIntRegisters);
            Array.Clear(_scriptBoolRegs, boolBase, GraphVmLimits.MaxBoolRegisters);
            Array.Clear(_scriptFloatRegs, floatBase, GraphVmLimits.MaxFloatRegisters);
            Array.Clear(_scriptEntityRegs, entityBase, GraphVmLimits.MaxEntityRegisters);
            Array.Clear(_scriptTargetRegs, targetBase, GraphVmLimits.MaxTargets);
            Array.Clear(_scriptCallStacks, agent * GraphVmLimits.MaxCallStackDepth, GraphVmLimits.MaxCallStackDepth);
        }

        /// <summary>Cheap topology restart for a new think wave (no Script register wipes).</summary>
        public void RestartThinking(int agent)
        {
            ValidateAgent(agent);
            RestartThinkingUnchecked(agent);
        }

        /// <summary>Restarts every agent topology stack without clearing ScriptSlice registers.</summary>
        public void RestartAllThinking()
        {
            int root = _tree.RootIndex;
            int stackBase = 0;
            int[] stack = _stack;
            byte[] stackCount = _stackCount;
            byte[] childCursor = _childCursor;
            BehaviorTreeStatus[] status = _status;
            for (int agent = 0; agent < _count; agent++)
            {
                stackCount[agent] = 1;
                stack[stackBase] = root;
                childCursor[stackBase] = 0;
                status[agent] = BehaviorTreeStatus.Running;
                stackBase += BehaviorTreeLimits.MaxStackDepth;
            }
        }

        /// <summary>Restarts agents that reached Success/Failure without clearing ScriptSlice registers.</summary>
        public int RestartFinishedThinking()
        {
            int root = _tree.RootIndex;
            int stackBase = 0;
            int[] stack = _stack;
            byte[] stackCount = _stackCount;
            byte[] childCursor = _childCursor;
            BehaviorTreeStatus[] status = _status;
            int restarted = 0;
            for (int agent = 0; agent < _count; agent++)
            {
                BehaviorTreeStatus current = status[agent];
                if (current != BehaviorTreeStatus.Success && current != BehaviorTreeStatus.Failure)
                {
                    stackBase += BehaviorTreeLimits.MaxStackDepth;
                    continue;
                }

                stackCount[agent] = 1;
                stack[stackBase] = root;
                childCursor[stackBase] = 0;
                status[agent] = BehaviorTreeStatus.Running;
                restarted++;
                stackBase += BehaviorTreeLimits.MaxStackDepth;
            }

            return restarted;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RestartThinkingUnchecked(int agent)
        {
            int stackBase = agent * BehaviorTreeLimits.MaxStackDepth;
            _stackCount[agent] = 1;
            _stack[stackBase] = _tree.RootIndex;
            _childCursor[stackBase] = 0;
            _status[agent] = BehaviorTreeStatus.Running;
        }

        /// <summary>
        /// Topology-only tick (AlwaysSuccess / HoldRunning trees). ScriptSlice leaves are forbidden.
        /// </summary>
        public BehaviorTreeThinkStats TickAll(int scriptBudgetSteps = 32)
            => TickAll(programs: null, scriptBudgetSteps, sensors: null);

        /// <summary>
        /// Runs one think wave. ScriptSlice leaves resolve <see cref="BehaviorTreeNode.GraphId"/> from
        /// <paramref name="programs"/> (engine <see cref="GraphProgramRegistry"/>); sensors write I[0].
        /// </summary>
        public BehaviorTreeThinkStats TickAll(
            GraphProgramRegistry? programs,
            int scriptBudgetSteps,
            IBehaviorTreeSensorFeed? sensors,
            World? world = null,
            Entity caster = default,
            Entity explicitTarget = default,
            IGraphRuntimeApi? api = null)
        {
            int visited = 0;
            int scriptSlices = 0;
            int scriptSteps = 0;
            for (int agent = 0; agent < _count; agent++)
            {
                TickAgent(
                    agent,
                    programs,
                    scriptBudgetSteps,
                    sensors,
                    world,
                    caster,
                    explicitTarget,
                    api,
                    ref visited,
                    ref scriptSlices,
                    ref scriptSteps);
            }

            return new BehaviorTreeThinkStats(_count, visited, scriptSlices, scriptSteps);
        }

        private void TickAgent(
            int agent,
            GraphProgramRegistry? programs,
            int scriptBudgetSteps,
            IBehaviorTreeSensorFeed? sensors,
            World? world,
            Entity caster,
            Entity explicitTarget,
            IGraphRuntimeApi? api,
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
                _stackCount[agent] = 0;
                _status[agent] = BehaviorTreeStatus.Success;
                return;
            }

            int stackBase = agent * BehaviorTreeLimits.MaxStackDepth;
            BehaviorTreeNode[] nodes = _tree.Nodes;
            if (depth == 1 &&
                _stack[stackBase] == _tree.RootIndex &&
                TryTickFlatRootSequence(agent, nodes, stackBase, ref visited))
            {
                return;
            }

            while (depth > 0)
            {
                int nodeIndex = _stack[stackBase + depth - 1];
                ref readonly BehaviorTreeNode node = ref nodes[nodeIndex];
                visited++;

                if (node.Kind == BehaviorTreeNodeKind.Condition || node.Kind == BehaviorTreeNodeKind.Action)
                {
                    BehaviorTreeLeafBinding binding = node.Leaf;
                    BehaviorTreeStatus leaf;
                    if (binding == BehaviorTreeLeafBinding.AlwaysSuccess)
                    {
                        leaf = BehaviorTreeStatus.Success;
                    }
                    else if (binding == BehaviorTreeLeafBinding.AlwaysFailure)
                    {
                        leaf = BehaviorTreeStatus.Failure;
                    }
                    else if (binding == BehaviorTreeLeafBinding.HoldRunning)
                    {
                        leaf = BehaviorTreeStatus.Running;
                    }
                    else
                    {
                        leaf = EvalLeaf(
                            agent,
                            node,
                            programs,
                            scriptBudgetSteps,
                            sensors,
                            world,
                            caster,
                            explicitTarget,
                            api,
                            ref scriptSlices,
                            ref scriptSteps);
                    }

                    if (leaf == BehaviorTreeStatus.Running)
                    {
                        _stackCount[agent] = (byte)depth;
                        _status[agent] = BehaviorTreeStatus.Running;
                        return;
                    }

                    depth = PopAndPropagate(agent, stackBase, depth, leaf);
                    if (depth == 0)
                    {
                        _stackCount[agent] = 0;
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
                    depth = PopAndPropagate(agent, stackBase, depth, done);
                    if (depth == 0)
                    {
                        _stackCount[agent] = 0;
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
            }
        }

        private bool TryTickFlatRootSequence(
            int agent,
            BehaviorTreeNode[] nodes,
            int stackBase,
            ref int visited)
        {
            ref readonly BehaviorTreeNode root = ref nodes[_tree.RootIndex];
            if (root.Kind != BehaviorTreeNodeKind.Sequence ||
                root.Leaf != BehaviorTreeLeafBinding.None ||
                root.ChildCount <= 0)
            {
                return false;
            }

            int childStart = root.ChildStart;
            int childEnd = childStart + root.ChildCount;
            if ((uint)childStart >= (uint)nodes.Length || childEnd > nodes.Length)
            {
                return false;
            }

            int cursor = _childCursor[stackBase];
            if (cursor > root.ChildCount)
            {
                return false;
            }

            int localVisited = 1;
            for (; cursor < root.ChildCount; cursor++)
            {
                int childIndex = childStart + cursor;
                ref readonly BehaviorTreeNode child = ref nodes[childIndex];
                if ((child.Kind != BehaviorTreeNodeKind.Condition && child.Kind != BehaviorTreeNodeKind.Action) ||
                    child.ChildCount != 0)
                {
                    return false;
                }

                localVisited++;
                switch (child.Leaf)
                {
                    case BehaviorTreeLeafBinding.AlwaysSuccess:
                        break;

                    case BehaviorTreeLeafBinding.AlwaysFailure:
                        _childCursor[stackBase] = (byte)(cursor + 1);
                        _stackCount[agent] = 0;
                        _status[agent] = BehaviorTreeStatus.Failure;
                        visited += localVisited;
                        return true;

                    case BehaviorTreeLeafBinding.HoldRunning:
                        _childCursor[stackBase] = (byte)(cursor + 1);
                        _stack[stackBase + 1] = childIndex;
                        _childCursor[stackBase + 1] = 0;
                        _stackCount[agent] = 2;
                        _status[agent] = BehaviorTreeStatus.Running;
                        visited += localVisited;
                        return true;

                    default:
                        return false;
                }
            }

            _childCursor[stackBase] = (byte)root.ChildCount;
            _stackCount[agent] = 0;
            _status[agent] = BehaviorTreeStatus.Success;
            visited += localVisited;
            return true;
        }

        private int PopAndPropagate(int agent, int stackBase, int depth, BehaviorTreeStatus childStatus)
        {
            depth--;
            if (depth <= 0)
            {
                return 0;
            }

            int parentIndex = _stack[stackBase + depth - 1];
            BehaviorTreeNode parent = _tree.Nodes[parentIndex];

            if (parent.Kind == BehaviorTreeNodeKind.Sequence && childStatus == BehaviorTreeStatus.Failure)
            {
                return PopAndPropagate(agent, stackBase, depth, BehaviorTreeStatus.Failure);
            }

            if (parent.Kind == BehaviorTreeNodeKind.Selector && childStatus == BehaviorTreeStatus.Success)
            {
                return PopAndPropagate(agent, stackBase, depth, BehaviorTreeStatus.Success);
            }

            // Sequence success or Selector failure → try next sibling on subsequent loop.
            return depth;
        }

        private BehaviorTreeStatus EvalLeaf(
            int agent,
            in BehaviorTreeNode node,
            GraphProgramRegistry? programs,
            int scriptBudgetSteps,
            IBehaviorTreeSensorFeed? sensors,
            World? world,
            Entity caster,
            Entity explicitTarget,
            IGraphRuntimeApi? api,
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
                    if (node.GraphId <= 0)
                    {
                        throw new InvalidOperationException("ScriptSlice leaf requires a positive GraphId.");
                    }

                    if (programs == null)
                    {
                        throw new InvalidOperationException(
                            $"ScriptSlice GraphId={node.GraphId} requires GraphProgramRegistry.");
                    }

                    ReadOnlySpan<GraphInstruction> program = programs.RequireProgram(node.GraphId, GraphKind.Script, "行为树叶子");

                    scriptSlices++;
                    Span<int> ints = _scriptIntRegs.AsSpan(agent * GraphVmLimits.MaxIntRegisters, GraphVmLimits.MaxIntRegisters);
                    Span<byte> bools = _scriptBoolRegs.AsSpan(agent * GraphVmLimits.MaxBoolRegisters, GraphVmLimits.MaxBoolRegisters);
                    Span<float> floats = _scriptFloatRegs.AsSpan(agent * GraphVmLimits.MaxFloatRegisters, GraphVmLimits.MaxFloatRegisters);
                    Span<Entity> entities = _scriptEntityRegs.AsSpan(agent * GraphVmLimits.MaxEntityRegisters, GraphVmLimits.MaxEntityRegisters);
                    Span<Entity> targets = _scriptTargetRegs.AsSpan(agent * GraphVmLimits.MaxTargets, GraphVmLimits.MaxTargets);
                    Span<int> callStack = _scriptCallStacks.AsSpan(agent * GraphVmLimits.MaxCallStackDepth, GraphVmLimits.MaxCallStackDepth);

                    ref GraphExecutionCursor cursor = ref _scriptCursors[agent];
                    bool resume = cursor.IsSuspended && _scriptResumeGraphIds[agent] == node.GraphId;
                    if (!resume)
                    {
                        ints.Clear();
                        bools.Clear();
                        callStack.Clear();
                        sensors?.WriteSensors(agent, node.GraphId, ints, bools);
                        cursor.Reset();
                    }

                    _scriptResumeGraphIds[agent] = node.GraphId;

                    GraphSliceResult result = GraphExecutor.ExecuteResolvedRegisteredScriptSlice(
                        programs, program, floats, ints, bools, entities, targets, callStack,
                        ref cursor,
                        scriptBudgetSteps,
                        world,
                        caster,
                        explicitTarget,
                        api);
                    scriptSteps += result.Steps;

                    if (node.Kind == BehaviorTreeNodeKind.Condition)
                    {
                        if (!result.Halted)
                        {
                            throw new InvalidOperationException(
                                $"Condition Script GraphId={node.GraphId} must halt (got {result.Status}).");
                        }

                        return result.ReturnInt != 0 ? BehaviorTreeStatus.Success : BehaviorTreeStatus.Failure;
                    }

                    if (result.Yielded || result.BudgetSuspended)
                    {
                        return BehaviorTreeStatus.Running;
                    }

                    if (!result.Halted)
                    {
                        throw new InvalidOperationException(
                            $"Action Script GraphId={node.GraphId} returned unexpected status {result.Status}.");
                    }

                    _lastScriptReturns[agent] = result.ReturnInt;
                    cursor.Reset();
                    _scriptResumeGraphIds[agent] = 0;
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
