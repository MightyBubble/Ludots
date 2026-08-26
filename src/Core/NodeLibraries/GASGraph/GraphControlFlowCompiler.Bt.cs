using System;
using System.Collections.Generic;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    /// <summary>
    /// BT tick status contract shared by the compiled tree program and its host:
    /// int status channel register + HaltReturnInt value. 0=Failure, 1=Success, 2=Running.
    /// </summary>
    public static class GraphBtStatusCodes
    {
        public const int Failure = 0;
        public const int Success = 1;
        public const int Running = 2;
    }

    public static partial class GraphControlFlowCompiler
    {
        public const string BtSequenceOp = GraphAuthoringSugar.BtSequence;
        public const string BtSelectorOp = GraphAuthoringSugar.BtSelector;
        public const string BtDecoratorOp = GraphAuthoringSugar.BtDecorator;

        internal const string BtDecoratorInverter = "inverter";
        internal const string BtDecoratorForceSuccess = "forceSuccess";
        internal const string BtDecoratorForceFailure = "forceFailure";

        /// <summary>
        /// Compile-time plan for a behavior tree authored as BtSequence/BtSelector/BtDecorator sugar.
        /// The whole tree inlines into one Script program: composite bodies Call children, children
        /// report status in one shared int register (scratch liveness never spans a Call, so a single
        /// status/const/bool scratch triple serves every composite), terminals of leaf chains lower to
        /// a status-writing epilogue + Return instead of the implicit halt. Only the root's exits halt;
        /// a Running child halts the tick from any depth (next think wave re-evaluates from the root).
        /// </summary>
        private sealed class BtSugarPlan
        {
            internal bool[] Composite;
            internal bool[] ChainNode;
            internal bool[] ChainTerminal;
            internal List<BtChildArm>[] Children;
            internal int RootNodeIndex;
            internal string RootNodeId = string.Empty;
            internal byte StatusReg;
            internal byte ConstReg;
            internal byte BoolReg;

            internal bool IsComposite(int nodeIndex) => Composite[nodeIndex];
            internal bool IsChainTerminal(int nodeIndex) => ChainTerminal[nodeIndex];
            internal bool IsRoot(int nodeIndex) => nodeIndex == RootNodeIndex;
        }

        internal readonly struct BtChildArm
        {
            public BtChildArm(int ordinal, string targetNodeId)
            {
                Ordinal = ordinal;
                TargetNodeId = targetNodeId;
            }

            public int Ordinal { get; }
            public string TargetNodeId { get; }
        }

        private static BtSugarPlan? AnalyzeBtSugar(
            GraphControlFlowDocument document,
            List<GraphControlFlowNode> nodes,
            AuthoredOp[] ops,
            Dictionary<string, int> nodeIndices,
            Dictionary<ControlKey, string> controlEdges,
            GraphValueType[] outputTypes,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            bool hasBt = false;
            for (int i = 0; i < ops.Length; i++)
            {
                if (ops[i].Kind is AuthoredOpKind.BtSequence or AuthoredOpKind.BtSelector or AuthoredOpKind.BtDecorator)
                {
                    hasBt = true;
                    break;
                }
            }

            if (!hasBt)
            {
                return null;
            }

            var plan = new BtSugarPlan
            {
                Composite = new bool[nodes.Count],
                ChainNode = new bool[nodes.Count],
                ChainTerminal = new bool[nodes.Count],
                Children = new List<BtChildArm>[nodes.Count]
            };

            for (int i = 0; i < nodes.Count; i++)
            {
                if (ops[i].Kind is AuthoredOpKind.BtSequence or AuthoredOpKind.BtSelector or AuthoredOpKind.BtDecorator)
                {
                    plan.Composite[i] = true;
                    plan.Children[i] = CollectBtChildArms(nodes[i], controlEdges);
                }
            }

            if (string.IsNullOrWhiteSpace(document.Entry) || !nodeIndices.TryGetValue(document.Entry, out int rootIndex))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingEntry,
                    $"Script graph with BT sugar requires the entry node to be one of {GraphAuthoringSugar.DescribeBtSugar()}."));
                return plan;
            }

            plan.RootNodeIndex = rootIndex;
            plan.RootNodeId = document.Entry;
            if (!plan.Composite[rootIndex])
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingEntry,
                    $"BT sugar root must be one of {GraphAuthoringSugar.DescribeBtSugar()}; entry '{document.Entry}' is not.", document.Entry));
                return plan;
            }

            ValidateBtComposites(plan, nodes, ops, nodeIndices, controlEdges, graphId, diagnostics);
            if (HasErrors(diagnostics))
            {
                return plan;
            }

            ClassifyBtLeafChains(plan, nodes, ops, nodeIndices, controlEdges, outputTypes, graphId, diagnostics);
            ValidateBtCompositeDepth(plan, nodeIndices, graphId, diagnostics);
            return plan;
        }

        private static List<BtChildArm> CollectBtChildArms(
            GraphControlFlowNode node,
            Dictionary<ControlKey, string> controlEdges)
        {
            var arms = new List<BtChildArm>();
            foreach (ControlKey key in controlEdges.Keys)
            {
                if (string.Equals(key.NodeId, node.Id, StringComparison.Ordinal) &&
                    GraphControlFlowPorts.TryParseChildPort(key.Port, out int ordinal))
                {
                    arms.Add(new BtChildArm(ordinal, controlEdges[key]));
                }
            }

            arms.Sort((a, b) => a.Ordinal.CompareTo(b.Ordinal));
            return arms;
        }

        private static void ValidateBtComposites(
            BtSugarPlan plan,
            List<GraphControlFlowNode> nodes,
            AuthoredOp[] ops,
            Dictionary<string, int> nodeIndices,
            Dictionary<ControlKey, string> controlEdges,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!plan.Composite[i])
                {
                    continue;
                }

                GraphControlFlowNode node = nodes[i];
                if (ops[i].Kind == AuthoredOpKind.BtDecorator)
                {
                    string kind = (node.DecoratorKind ?? string.Empty).Trim();
                    if (kind != BtDecoratorInverter && kind != BtDecoratorForceSuccess && kind != BtDecoratorForceFailure)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                            $"BtDecorator node '{node.Id}' requires decoratorKind \"{BtDecoratorInverter}\", \"{BtDecoratorForceSuccess}\", or \"{BtDecoratorForceFailure}\" (got '{node.DecoratorKind}').", node.Id));
                    }

                    if (plan.Children[i].Count != 1 || plan.Children[i][0].Ordinal != 0)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingControlEdge,
                            $"BtDecorator node '{node.Id}' requires exactly one 'child:0' control edge.", node.Id));
                    }
                }
                else if (plan.Children[i].Count == 0)
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingControlEdge,
                        $"{(ops[i].Kind == AuthoredOpKind.BtSequence ? BtSequenceOp : BtSelectorOp)} node '{node.Id}' requires at least one child:{{n}} control edge.", node.Id));
                }
            }

            // Composites are subroutine entries: only a parent's child:{n} edge (or the document
            // entry prefix jump) may enter them. A next/case/true arm falling into a composite
            // would hit its Return without a matching Call frame.
            foreach (KeyValuePair<ControlKey, string> edge in controlEdges)
            {
                if (!nodeIndices.TryGetValue(edge.Value, out int targetIndex) || !plan.Composite[targetIndex])
                {
                    continue;
                }

                if (!GraphControlFlowPorts.TryParseChildPort(edge.Key.Port, out _))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnexpectedControlEdge,
                        $"BT composite node '{edge.Value}' may only be entered through a child:{{n}} edge; found '{edge.Key.NodeId}.{edge.Key.Port}'.", edge.Value));
                }
            }
        }

        private static void ClassifyBtLeafChains(
            BtSugarPlan plan,
            List<GraphControlFlowNode> nodes,
            AuthoredOp[] ops,
            Dictionary<string, int> nodeIndices,
            Dictionary<ControlKey, string> controlEdges,
            GraphValueType[] outputTypes,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            var frontier = new Queue<int>();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!plan.Composite[i])
                {
                    continue;
                }

                for (int a = 0; a < plan.Children[i].Count; a++)
                {
                    string targetId = plan.Children[i][a].TargetNodeId;
                    if (nodeIndices.TryGetValue(targetId, out int targetIndex) && !plan.Composite[targetIndex])
                    {
                        frontier.Enqueue(targetIndex);
                    }
                }
            }

            bool HasOutgoingControlEdge(int nodeIndex)
            {
                string nodeId = nodes[nodeIndex].Id;
                foreach (ControlKey key in controlEdges.Keys)
                {
                    if (string.Equals(key.NodeId, nodeId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }

            while (frontier.Count > 0)
            {
                int nodeIndex = frontier.Dequeue();
                if (plan.Composite[nodeIndex] || plan.ChainNode[nodeIndex])
                {
                    continue;
                }

                plan.ChainNode[nodeIndex] = true;
                GraphControlFlowNode node = nodes[nodeIndex];

                if (ops[nodeIndex].NodeOp == GraphNodeOp.HaltReturnInt)
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnexpectedControlEdge,
                        $"BT leaf chain node '{node.Id}' must not author HaltReturnInt; the tree root owns the tick halt and leaves report status through their chain terminal.", node.Id));
                }

                if (ops[nodeIndex].NodeOp == GraphNodeOp.Return)
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnexpectedControlEdge,
                        $"BT leaf chain node '{node.Id}' must not author Return; the chain terminal lowers the status epilogue that returns to the parent.", node.Id));
                }

                if (!HasOutgoingControlEdge(nodeIndex))
                {
                    plan.ChainTerminal[nodeIndex] = true;
                    GraphValueType terminalType = outputTypes[nodeIndex];
                    if (terminalType is not (GraphValueType.Int or GraphValueType.Bool or GraphValueType.Void))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                            $"BT leaf terminal '{node.Id}' must produce Int, Bool, or Void to report a BT status; produces {terminalType}.", node.Id));
                    }

                    continue;
                }

                foreach (ControlKey key in controlEdges.Keys)
                {
                    if (!string.Equals(key.NodeId, node.Id, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (nodeIndices.TryGetValue(controlEdges[key], out int nextIndex) && !plan.Composite[nextIndex])
                    {
                        frontier.Enqueue(nextIndex);
                    }
                }
            }
        }

        private static void ValidateBtCompositeDepth(
            BtSugarPlan plan,
            Dictionary<string, int> nodeIndices,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            // Each nesting level pushes exactly one Call frame; a chain deeper than
            // MaxCallStackDepth would overflow the VM call stack mid-tick.
            int max = 0;
            var stack = new Stack<(int NodeIndex, int Depth)>();
            stack.Push((plan.RootNodeIndex, 1));
            var visited = new HashSet<int>();
            while (stack.Count > 0)
            {
                (int nodeIndex, int depth) = stack.Pop();
                if (!visited.Add(nodeIndex))
                {
                    continue;
                }

                max = Math.Max(max, depth);
                List<BtChildArm> children = plan.Children[nodeIndex];
                for (int a = 0; a < children.Count; a++)
                {
                    if (nodeIndices.TryGetValue(children[a].TargetNodeId, out int targetIndex) && plan.Composite[targetIndex])
                    {
                        stack.Push((targetIndex, depth + 1));
                    }
                }
            }

            if (max > GraphVmLimits.MaxCallStackDepth)
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.BudgetExceeded,
                    $"BT composite nesting depth {max} exceeds the VM call stack depth ({GraphVmLimits.MaxCallStackDepth}); each composite level spends one Call frame."));
            }
        }

        private static int CountBtCompositeInstructions(AuthoredOp op, BtSugarPlan plan, int nodeIndex, string? decoratorKind)
        {
            if (op.Kind == AuthoredOpKind.BtDecorator)
            {
                bool inverter = string.Equals((decoratorKind ?? string.Empty).Trim(), BtDecoratorInverter, StringComparison.Ordinal);
                return inverter ? 11 : 6;
            }

            return (plan.Children[nodeIndex].Count * 9) + 6;
        }

        internal static int CountBtLeafEpilogueInstructions(GraphValueType terminalType)
        {
            return terminalType switch
            {
                GraphValueType.Int => 2,   // MoveInt st, R ; Return
                GraphValueType.Bool => 5,  // JumpIfFalse ; ConstInt st 1 ; Jump ; ConstInt st 0 ; Return
                _ => 2                    // ConstInt st 1 ; Return
            };
        }

        private static void CompileBtComposite(
            BtSugarPlan plan,
            GraphControlFlowNode node,
            AuthoredOp op,
            Dictionary<string, int> nodeIndices,
            NodeLayout[] layouts,
            GraphInstruction[] program,
            GraphInstructionSource[] sources,
            string graphId)
        {
            int nodeIndex = nodeIndices[node.Id];
            int body = layouts[nodeIndex].BodyIndex;
            bool isRoot = plan.IsRoot(nodeIndex);
            byte st = plan.StatusReg;
            byte k = plan.ConstReg;
            byte b = plan.BoolReg;

            if (op.Kind == AuthoredOpKind.BtDecorator)
            {
                CompileBtDecorator(plan, node, nodeIndex, body, isRoot, st, k, b, nodeIndices, layouts, program, sources, graphId);
                return;
            }

            bool selector = op.Kind == AuthoredOpKind.BtSelector;
            List<BtChildArm> children = plan.Children[nodeIndex];
            int childCount = children.Count;
            int succAbs = body + (childCount * 9);
            int failAbs = succAbs + 2;
            int runningAbs = succAbs + 4;

            for (int i = 0; i < childCount; i++)
            {
                int s = body + (i * 9);
                int childAbs = layouts[nodeIndices[children[i].TargetNodeId]].BodyIndex;
                string armPort = GraphControlFlowPorts.Child(children[i].Ordinal);

                program[s] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.Call,
                    Imm = childAbs
                };
                SetSource(sources, s, graphId, node, nameof(GraphNodeOp.Call), armPort);

                // Short-circuit status: sequence jumps away on Failure(0), selector on Success(1).
                program[s + 1] = new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = k, Imm = selector ? GraphBtStatusCodes.Success : GraphBtStatusCodes.Failure };
                program[s + 2] = new GraphInstruction { Op = (ushort)GraphNodeOp.CompareEqInt, Dst = b, A = st, B = k };
                program[s + 3] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.JumpIfFalse,
                    A = b,
                    Imm = RelativeOffset(s + 3, s + 5)
                };
                program[s + 4] = new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = RelativeOffset(s + 4, selector ? succAbs : failAbs) };
                SetSource(sources, s + 1, graphId, node, selector ? BtSelectorOp : BtSequenceOp, armPort);
                SetSource(sources, s + 2, graphId, node, selector ? BtSelectorOp : BtSequenceOp, armPort);
                SetSource(sources, s + 3, graphId, node, selector ? BtSelectorOp : BtSequenceOp, armPort);
                SetSource(sources, s + 4, graphId, node, selector ? BtSelectorOp : BtSequenceOp, armPort);

                // Running(2) ends the tick from any depth; otherwise fall through to the next child
                // (after the last child: sequence falls to Success, selector to Failure).
                program[s + 5] = new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = k, Imm = GraphBtStatusCodes.Running };
                program[s + 6] = new GraphInstruction { Op = (ushort)GraphNodeOp.CompareEqInt, Dst = b, A = st, B = k };
                program[s + 7] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.JumpIfFalse,
                    A = b,
                    Imm = RelativeOffset(s + 7, i + 1 < childCount ? s + 9 : (selector ? failAbs : succAbs))
                };
                program[s + 8] = new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = RelativeOffset(s + 8, runningAbs) };
                SetSource(sources, s + 5, graphId, node, selector ? BtSelectorOp : BtSequenceOp, armPort);
                SetSource(sources, s + 6, graphId, node, selector ? BtSelectorOp : BtSequenceOp, armPort);
                SetSource(sources, s + 7, graphId, node, selector ? BtSelectorOp : BtSequenceOp, armPort);
                SetSource(sources, s + 8, graphId, node, selector ? BtSelectorOp : BtSequenceOp, armPort);
            }

            EmitBtExit(program, sources, succAbs, graphId, node, st, GraphBtStatusCodes.Success, isRoot);
            EmitBtExit(program, sources, failAbs, graphId, node, st, GraphBtStatusCodes.Failure, isRoot);
            EmitBtExit(program, sources, runningAbs, graphId, node, st, GraphBtStatusCodes.Running, isRoot);
        }

        private static void CompileBtDecorator(
            BtSugarPlan plan,
            GraphControlFlowNode node,
            int nodeIndex,
            int body,
            bool isRoot,
            byte st,
            byte k,
            byte b,
            Dictionary<string, int> nodeIndices,
            NodeLayout[] layouts,
            GraphInstruction[] program,
            GraphInstructionSource[] sources,
            string graphId)
        {
            string kind = (node.DecoratorKind ?? string.Empty).Trim();
            int childAbs = layouts[nodeIndices[plan.Children[nodeIndex][0].TargetNodeId]].BodyIndex;
            string armPort = GraphControlFlowPorts.Child(0);

            program[body] = new GraphInstruction { Op = (ushort)GraphNodeOp.Call, Imm = childAbs };
            SetSource(sources, body, graphId, node, nameof(GraphNodeOp.Call), armPort);

            if (kind == BtDecoratorInverter)
            {
                // Failure(0) → Success, Success(1) → Failure, Running(2) passes through.
                program[body + 1] = new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = k, Imm = GraphBtStatusCodes.Failure };
                program[body + 2] = new GraphInstruction { Op = (ushort)GraphNodeOp.CompareEqInt, Dst = b, A = st, B = k };
                program[body + 3] = new GraphInstruction { Op = (ushort)GraphNodeOp.JumpIfFalse, A = b, Imm = RelativeOffset(body + 3, body + 6) };
                program[body + 4] = new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = st, Imm = GraphBtStatusCodes.Success };
                program[body + 5] = new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = RelativeOffset(body + 5, body + 10) };
                program[body + 6] = new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = k, Imm = GraphBtStatusCodes.Success };
                program[body + 7] = new GraphInstruction { Op = (ushort)GraphNodeOp.CompareEqInt, Dst = b, A = st, B = k };
                program[body + 8] = new GraphInstruction { Op = (ushort)GraphNodeOp.JumpIfFalse, A = b, Imm = RelativeOffset(body + 8, body + 10) };
                program[body + 9] = new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = st, Imm = GraphBtStatusCodes.Failure };
                for (int i = 1; i <= 9; i++)
                {
                    SetSource(sources, body + i, graphId, node, BtDecoratorOp, armPort);
                }

                EmitBtTail(program, sources, body + 10, graphId, node, st, isRoot, "bt:return");
                return;
            }

            bool forceSuccess = kind == BtDecoratorForceSuccess;
            program[body + 1] = new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = k, Imm = forceSuccess ? GraphBtStatusCodes.Failure : GraphBtStatusCodes.Success };
            program[body + 2] = new GraphInstruction { Op = (ushort)GraphNodeOp.CompareEqInt, Dst = b, A = st, B = k };
            program[body + 3] = new GraphInstruction { Op = (ushort)GraphNodeOp.JumpIfFalse, A = b, Imm = RelativeOffset(body + 3, body + 5) };
            program[body + 4] = new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = st, Imm = forceSuccess ? GraphBtStatusCodes.Success : GraphBtStatusCodes.Failure };
            for (int i = 1; i <= 4; i++)
            {
                SetSource(sources, body + i, graphId, node, BtDecoratorOp, armPort);
            }

            EmitBtTail(program, sources, body + 5, graphId, node, st, isRoot, "bt:return");
        }

        /// <summary>Shared exit: write the status channel, then Return (or halt when this is the tree root).</summary>
        private static void EmitBtExit(
            GraphInstruction[] program,
            GraphInstructionSource[] sources,
            int index,
            string graphId,
            GraphControlFlowNode node,
            byte st,
            int status,
            bool isRoot)
        {
            program[index] = new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = st, Imm = status };
            SetSource(sources, index, graphId, node, node.Op, BtExitPort(status));
            EmitBtTail(program, sources, index + 1, graphId, node, st, isRoot, BtExitPort(status));
        }

        private static void EmitBtTail(
            GraphInstruction[] program,
            GraphInstructionSource[] sources,
            int index,
            string graphId,
            GraphControlFlowNode node,
            byte st,
            bool isRoot,
            string port)
        {
            if (isRoot)
            {
                program[index] = new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = st };
                SetSource(sources, index, graphId, node, nameof(GraphNodeOp.HaltReturnInt), port);
                return;
            }

            program[index] = new GraphInstruction { Op = (ushort)GraphNodeOp.Return };
            SetSource(sources, index, graphId, node, nameof(GraphNodeOp.Return), port);
        }

        private static string BtExitPort(int status)
            => status switch
            {
                GraphBtStatusCodes.Failure => "bt:failure",
                GraphBtStatusCodes.Success => "bt:success",
                _ => "bt:running"
            };

        /// <summary>
        /// Emits the trailing slot after a node body: the authored next jump, or — when the node is
        /// a BT leaf-chain terminal without a next edge — the status epilogue that Returns to the
        /// parent. Shared by the Script-native switch and the linear emitter so every authorable
        /// op can terminate a BT leaf chain.
        /// </summary>
        private static void EmitNextJumpOrBtEpilogue(
            GraphControlFlowDocument document,
            GraphControlFlowNode node,
            int emitIndex,
            Dictionary<ControlKey, string> controlEdges,
            Dictionary<string, int> nodeIndices,
            NodeLayout[] layouts,
            GraphInstruction[] program,
            GraphInstructionSource[] sources,
            string graphId,
            BtSugarPlan? btPlan,
            int nodeIndex,
            GraphValueType[] outputTypes,
            byte[] outputRegisters)
        {
            if (btPlan != null &&
                nodeIndex >= 0 &&
                btPlan.IsChainTerminal(nodeIndex) &&
                !controlEdges.ContainsKey(new ControlKey(node.Id, GraphControlFlowPorts.Next)))
            {
                EmitBtLeafEpilogue(
                    btPlan,
                    node,
                    nodeIndex,
                    outputTypes[nodeIndex],
                    outputRegisters[nodeIndex],
                    emitIndex,
                    program,
                    sources,
                    graphId);
                return;
            }

            EmitRelativeJump(
                document, node, GraphControlFlowPorts.Next, emitIndex,
                controlEdges, nodeIndices, layouts, program, sources, graphId);
        }

        /// <summary>
        /// Leaf-chain terminal epilogue: report the chain's final value as the child status
        /// (Int moves into the channel, Bool branches 1/0, Void succeeds) and Return to the parent.
        /// </summary>
        private static void EmitBtLeafEpilogue(
            BtSugarPlan plan,
            GraphControlFlowNode node,
            int nodeIndex,
            GraphValueType terminalType,
            byte terminalRegister,
            int epilogueIndex,
            GraphInstruction[] program,
            GraphInstructionSource[] sources,
            string graphId)
        {
            byte st = plan.StatusReg;
            switch (terminalType)
            {
                case GraphValueType.Int:
                    program[epilogueIndex] = new GraphInstruction { Op = (ushort)GraphNodeOp.MoveInt, Dst = st, A = terminalRegister };
                    SetSource(sources, epilogueIndex, graphId, node, nameof(GraphNodeOp.MoveInt), "bt:return");
                    EmitBtTail(program, sources, epilogueIndex + 1, graphId, node, st, isRoot: false, "bt:return");
                    break;

                case GraphValueType.Bool:
                    program[epilogueIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphNodeOp.JumpIfFalse,
                        A = terminalRegister,
                        Imm = RelativeOffset(epilogueIndex, epilogueIndex + 3)
                    };
                    program[epilogueIndex + 1] = new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = st, Imm = GraphBtStatusCodes.Success };
                    program[epilogueIndex + 2] = new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = RelativeOffset(epilogueIndex + 2, epilogueIndex + 4) };
                    program[epilogueIndex + 3] = new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = st, Imm = GraphBtStatusCodes.Failure };
                    for (int i = 0; i < 4; i++)
                    {
                        SetSource(sources, epilogueIndex + i, graphId, node, node.Op, "bt:return");
                    }

                    EmitBtTail(program, sources, epilogueIndex + 4, graphId, node, st, isRoot: false, "bt:return");
                    break;

                default:
                    program[epilogueIndex] = new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = st, Imm = GraphBtStatusCodes.Success };
                    SetSource(sources, epilogueIndex, graphId, node, node.Op, "bt:return");
                    EmitBtTail(program, sources, epilogueIndex + 1, graphId, node, st, isRoot: false, "bt:return");
                    break;
            }
        }
    }
}
