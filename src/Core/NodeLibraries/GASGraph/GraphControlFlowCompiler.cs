using System;
using System.Collections.Generic;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public readonly struct GraphControlFlowCompileResult
    {
        public GraphControlFlowCompileResult(
            GraphInstruction[] program,
            GraphInstructionSourceMap sourceMap,
            List<GraphDiagnostic> diagnostics)
        {
            Program = program ?? Array.Empty<GraphInstruction>();
            SourceMap = sourceMap;
            Diagnostics = diagnostics ?? new List<GraphDiagnostic>();
        }

        public GraphInstruction[] Program { get; }
        public GraphInstructionSourceMap SourceMap { get; }
        public List<GraphDiagnostic> Diagnostics { get; }
        public bool Succeeded => !HasErrors(Diagnostics);

        private static bool HasErrors(List<GraphDiagnostic> diagnostics)
        {
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Severity == GraphDiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Compiles L1 Script control-flow documents into <see cref="GraphInstruction"/> using GraphNodeOp.
    /// BranchBool is compile-time sugar only (not a GraphNodeOp).
    /// </summary>
    public static class GraphControlFlowCompiler
    {
        public const string BranchBoolOp = "BranchBool";

        private readonly struct ControlKey : IEquatable<ControlKey>
        {
            public ControlKey(string nodeId, string port)
            {
                NodeId = nodeId;
                Port = port;
            }

            public string NodeId { get; }
            public string Port { get; }

            public bool Equals(ControlKey other)
                => string.Equals(NodeId, other.NodeId, StringComparison.Ordinal) &&
                   string.Equals(Port, other.Port, StringComparison.Ordinal);

            public override bool Equals(object? obj) => obj is ControlKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(NodeId, Port);
        }

        private readonly struct ValueInputKey : IEquatable<ValueInputKey>
        {
            public ValueInputKey(string nodeId, string port)
            {
                NodeId = nodeId;
                Port = port;
            }

            public string NodeId { get; }
            public string Port { get; }

            public bool Equals(ValueInputKey other)
                => string.Equals(NodeId, other.NodeId, StringComparison.Ordinal) &&
                   string.Equals(Port, other.Port, StringComparison.Ordinal);

            public override bool Equals(object? obj) => obj is ValueInputKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(NodeId, Port);
        }

        private readonly struct NodeLayout
        {
            public NodeLayout(int bodyIndex, int instructionCount)
            {
                BodyIndex = bodyIndex;
                InstructionCount = instructionCount;
            }

            public int BodyIndex { get; }
            public int InstructionCount { get; }
        }

        private enum AuthoredOpKind : byte
        {
            GraphNodeOp = 0,
            BranchBool = 1
        }

        private readonly struct AuthoredOp
        {
            public AuthoredOp(AuthoredOpKind kind, GraphNodeOp nodeOp)
            {
                Kind = kind;
                NodeOp = nodeOp;
            }

            public AuthoredOpKind Kind { get; }
            public GraphNodeOp NodeOp { get; }
        }

        public static GraphControlFlowCompileResult Compile(GraphControlFlowDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var diagnostics = new List<GraphDiagnostic>();
            string graphId = document.Id ?? string.Empty;
            ValidateHeader(document, graphId, diagnostics);

            List<GraphControlFlowNode> nodes = document.Nodes ?? new List<GraphControlFlowNode>();
            Dictionary<string, int> nodeIndices = BuildNodeIndex(nodes, graphId, diagnostics);
            var ops = new AuthoredOp[nodes.Count];
            ParseOps(nodes, ops, graphId, diagnostics);

            if (!string.IsNullOrWhiteSpace(document.Entry) &&
                !nodeIndices.ContainsKey(document.Entry))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                    $"Script entry node '{document.Entry}' does not exist.", document.Entry));
            }

            Dictionary<ControlKey, string> controlEdges = BuildControlEdges(document, nodeIndices, ops, graphId, diagnostics);
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges = BuildValueEdges(document, nodeIndices, graphId, diagnostics);

            var outputTypes = new GraphValueType[nodes.Count];
            var outputRegisters = new byte[nodes.Count];
            AllocateOutputs(nodes, ops, outputTypes, outputRegisters, graphId, diagnostics);
            ValidateRequiredEdges(nodes, ops, controlEdges, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
            DetectUnreachable(document.Entry, nodes, document.ControlEdges, graphId, diagnostics);

            NodeLayout[] layouts = BuildLayouts(nodes, ops, diagnostics);
            if (HasErrors(diagnostics))
            {
                return new GraphControlFlowCompileResult(Array.Empty<GraphInstruction>(), GraphInstructionSourceMap.Empty, diagnostics);
            }

            int totalInstructions = 0;
            for (int i = 0; i < layouts.Length; i++)
            {
                totalInstructions += layouts[i].InstructionCount;
            }

            if (totalInstructions > GraphVmLimits.MaxInstructionsPerExecution)
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.BudgetExceeded,
                    $"Script exceeds instruction budget ({GraphVmLimits.MaxInstructionsPerExecution})."));
                return new GraphControlFlowCompileResult(Array.Empty<GraphInstruction>(), GraphInstructionSourceMap.Empty, diagnostics);
            }

            // Prefix jump to entry so entry need not be first authored node.
            int entryBody = layouts[nodeIndices[document.Entry]].BodyIndex + 1; // +1 for prefix jump slot
            for (int i = 0; i < layouts.Length; i++)
            {
                layouts[i] = new NodeLayout(layouts[i].BodyIndex + 1, layouts[i].InstructionCount);
            }

            var program = new GraphInstruction[totalInstructions + 1];
            var sources = new GraphInstructionSource[program.Length];
            program[0] = new GraphInstruction
            {
                Op = (ushort)GraphNodeOp.Jump,
                Imm = entryBody - 1 // relative: after fetch pc=1, 1+Imm = entryBody
            };
            sources[0] = new GraphInstructionSource(graphId, document.Entry, nameof(GraphNodeOp.Jump), GraphControlFlowPorts.Enter);

            var definedInts = new bool[GraphVmLimits.MaxIntRegisters];
            var definedBools = new bool[GraphVmLimits.MaxBoolRegisters];
            // ConstInt pins are entry/loop-carried int cells; treat as defined for SSA edge checks.
            for (int i = 0; i < nodes.Count; i++)
            {
                if (ops[i].NodeOp == GraphNodeOp.ConstInt && nodes[i].PinRegister >= 0 &&
                    nodes[i].PinRegister < GraphVmLimits.MaxIntRegisters)
                {
                    definedInts[nodes[i].PinRegister] = true;
                }
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                CompileNode(
                    document,
                    nodes[i],
                    ops[i],
                    outputRegisters,
                    outputTypes,
                    controlEdges,
                    valueEdges,
                    nodeIndices,
                    layouts,
                    program,
                    sources,
                    definedInts,
                    definedBools,
                    graphId,
                    diagnostics);
            }

            if (HasErrors(diagnostics))
            {
                return new GraphControlFlowCompileResult(Array.Empty<GraphInstruction>(), GraphInstructionSourceMap.Empty, diagnostics);
            }

            return new GraphControlFlowCompileResult(
                program,
                new GraphInstructionSourceMap(graphId, sources),
                diagnostics);
        }

        private static void ValidateHeader(GraphControlFlowDocument document, string graphId, List<GraphDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(document.Id))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingGraphId, "Script document requires a non-empty id."));
            }

            if (string.IsNullOrWhiteSpace(document.Entry))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingEntry, "Script document requires an entry node id."));
            }

            if (document.Nodes == null || document.Nodes.Count == 0)
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.EmptyGraph, "Script document requires at least one node."));
            }
        }

        private static Dictionary<string, int> BuildNodeIndex(
            List<GraphControlFlowNode> nodes,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < nodes.Count; i++)
            {
                GraphControlFlowNode node = nodes[i];
                if (string.IsNullOrWhiteSpace(node.Id))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeId, "Script node requires a non-empty id."));
                    continue;
                }

                if (!indices.TryAdd(node.Id, i))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.DuplicateNodeId,
                        $"Duplicate Script node id '{node.Id}'.", node.Id));
                }
            }

            return indices;
        }

        private static void ParseOps(
            List<GraphControlFlowNode> nodes,
            AuthoredOp[] ops,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                GraphControlFlowNode node = nodes[i];
                if (string.Equals(node.Op, BranchBoolOp, StringComparison.Ordinal))
                {
                    ops[i] = new AuthoredOp(AuthoredOpKind.BranchBool, GraphNodeOp.None);
                    continue;
                }

                if (!GraphNodeOpParser.TryParse(node.Op, out GraphNodeOp nodeOp) ||
                    !IsControlFlowAuthorable(nodeOp))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                        $"Unknown or non-Script-authorable op '{node.Op}'.", node.Id));
                    ops[i] = new AuthoredOp(AuthoredOpKind.GraphNodeOp, GraphNodeOp.None);
                    continue;
                }

                ops[i] = new AuthoredOp(AuthoredOpKind.GraphNodeOp, nodeOp);
            }
        }

        private static bool IsControlFlowAuthorable(GraphNodeOp op)
            => op is GraphNodeOp.ConstInt or
                     GraphNodeOp.AddInt or
                     GraphNodeOp.CompareLtInt or
                     GraphNodeOp.Jump or
                     GraphNodeOp.JumpIfFalse or
                     GraphNodeOp.Call or
                     GraphNodeOp.Return or
                     GraphNodeOp.Yield or
                     GraphNodeOp.HaltReturnInt or
                     GraphNodeOp.InvokeScript or
                     GraphNodeOp.MoveInt;

        private static Dictionary<ControlKey, string> BuildControlEdges(
            GraphControlFlowDocument document,
            Dictionary<string, int> nodeIndices,
            AuthoredOp[] ops,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            var result = new Dictionary<ControlKey, string>();
            List<GraphControlFlowEdge> edges = document.ControlEdges ?? new List<GraphControlFlowEdge>();
            for (int i = 0; i < edges.Count; i++)
            {
                GraphControlFlowEdge edge = edges[i];
                if (string.IsNullOrWhiteSpace(edge.From) ||
                    string.IsNullOrWhiteSpace(edge.FromPort) ||
                    string.IsNullOrWhiteSpace(edge.To))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingControlEdge,
                        "Control edge requires From, FromPort, and To."));
                    continue;
                }

                if (!nodeIndices.ContainsKey(edge.From) || !nodeIndices.ContainsKey(edge.To))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                        $"Control edge references missing node '{edge.From}' -> '{edge.To}'.", edge.From));
                    continue;
                }

                var key = new ControlKey(edge.From, edge.FromPort);
                if (!result.TryAdd(key, edge.To))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.DuplicateControlEdge,
                        $"Duplicate control edge from '{edge.From}'.{edge.FromPort}.", edge.From));
                }
            }

            return result;
        }

        private static Dictionary<ValueInputKey, GraphControlFlowValueEdge> BuildValueEdges(
            GraphControlFlowDocument document,
            Dictionary<string, int> nodeIndices,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            var result = new Dictionary<ValueInputKey, GraphControlFlowValueEdge>();
            List<GraphControlFlowValueEdge> edges = document.ValueEdges ?? new List<GraphControlFlowValueEdge>();
            for (int i = 0; i < edges.Count; i++)
            {
                GraphControlFlowValueEdge edge = edges[i];
                if (string.IsNullOrWhiteSpace(edge.From) ||
                    string.IsNullOrWhiteSpace(edge.To) ||
                    string.IsNullOrWhiteSpace(edge.ToPort))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingValueInput,
                        "Value edge requires From, To, and ToPort."));
                    continue;
                }

                if (!nodeIndices.ContainsKey(edge.From) || !nodeIndices.ContainsKey(edge.To))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                        $"Value edge references missing node '{edge.From}' -> '{edge.To}'.", edge.To));
                    continue;
                }

                var key = new ValueInputKey(edge.To, edge.ToPort);
                if (!result.TryAdd(key, edge))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.DuplicateValueEdge,
                        $"Duplicate value edge into '{edge.To}'.{edge.ToPort}.", edge.To));
                }
            }

            return result;
        }

        private static void AllocateOutputs(
            List<GraphControlFlowNode> nodes,
            AuthoredOp[] ops,
            GraphValueType[] outputTypes,
            byte[] outputRegisters,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            int intNext = 0;
            int boolNext = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                GraphValueType outputType = GetOutputType(ops[i]);
                outputTypes[i] = outputType;
                if (outputType == GraphValueType.Void)
                {
                    outputRegisters[i] = 0;
                    continue;
                }

                if (nodes[i].PinRegister >= 0)
                {
                    if (outputType != GraphValueType.Int)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                            "PinRegister is only supported for int-producing nodes.", nodes[i].Id));
                        continue;
                    }

                    if (nodes[i].PinRegister >= GraphVmLimits.MaxIntRegisters)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.RegisterOutOfRange,
                            $"PinRegister {nodes[i].PinRegister} exceeds MaxIntRegisters.", nodes[i].Id));
                        continue;
                    }

                    outputRegisters[i] = (byte)nodes[i].PinRegister;
                    if (intNext <= nodes[i].PinRegister)
                    {
                        intNext = nodes[i].PinRegister + 1;
                    }

                    continue;
                }

                outputRegisters[i] = outputType switch
                {
                    GraphValueType.Int => Alloc(ref intNext, GraphVmLimits.MaxIntRegisters, graphId, nodes[i].Id, diagnostics),
                    GraphValueType.Bool => Alloc(ref boolNext, GraphVmLimits.MaxBoolRegisters, graphId, nodes[i].Id, diagnostics),
                    _ => (byte)0
                };
            }
        }

        private static byte Alloc(ref int next, int max, string graphId, string nodeId, List<GraphDiagnostic> diagnostics)
        {
            if (next >= max)
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.RegisterOutOfRange,
                    $"Register budget exceeded ({max}).", nodeId));
                return 0;
            }

            return (byte)next++;
        }

        private static GraphValueType GetOutputType(AuthoredOp op)
        {
            if (op.Kind == AuthoredOpKind.BranchBool)
            {
                return GraphValueType.Void;
            }

            return op.NodeOp switch
            {
                GraphNodeOp.ConstInt or GraphNodeOp.AddInt or GraphNodeOp.MoveInt or GraphNodeOp.InvokeScript
                    => GraphValueType.Int,
                GraphNodeOp.CompareLtInt => GraphValueType.Bool,
                _ => GraphValueType.Void
            };
        }

        private static void ValidateRequiredEdges(
            List<GraphControlFlowNode> nodes,
            AuthoredOp[] ops,
            Dictionary<ControlKey, string> controlEdges,
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges,
            Dictionary<string, int> nodeIndices,
            GraphValueType[] outputTypes,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                GraphControlFlowNode node = nodes[i];
                AuthoredOp op = ops[i];

                if (op.Kind == AuthoredOpKind.BranchBool || op.NodeOp == GraphNodeOp.JumpIfFalse)
                {
                    RequireControlEdge(node, GraphControlFlowPorts.True, controlEdges, graphId, diagnostics);
                    RequireControlEdge(node, GraphControlFlowPorts.False, controlEdges, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Condition, GraphValueType.Bool, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    continue;
                }

                switch (op.NodeOp)
                {
                    case GraphNodeOp.ConstInt:
                    case GraphNodeOp.AddInt:
                    case GraphNodeOp.CompareLtInt:
                    case GraphNodeOp.MoveInt:
                    case GraphNodeOp.Yield:
                    case GraphNodeOp.InvokeScript:
                        RequireControlEdge(node, GraphControlFlowPorts.Next, controlEdges, graphId, diagnostics);
                        break;
                    case GraphNodeOp.Call:
                        RequireControlEdge(node, GraphControlFlowPorts.Call, controlEdges, graphId, diagnostics);
                        RequireControlEdge(node, GraphControlFlowPorts.Next, controlEdges, graphId, diagnostics);
                        break;
                    case GraphNodeOp.Jump:
                        RequireControlEdge(node, GraphControlFlowPorts.Target, controlEdges, graphId, diagnostics);
                        break;
                    case GraphNodeOp.Return:
                    case GraphNodeOp.HaltReturnInt:
                        break;
                }

                switch (op.NodeOp)
                {
                    case GraphNodeOp.AddInt:
                    case GraphNodeOp.CompareLtInt:
                        RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                        RequireValueInput(node, GraphControlFlowPorts.B, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                        break;
                    case GraphNodeOp.MoveInt:
                    case GraphNodeOp.HaltReturnInt:
                        RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                        break;
                }
            }
        }

        private static void DetectUnreachable(
            string entry,
            List<GraphControlFlowNode> nodes,
            List<GraphControlFlowEdge>? controlEdges,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                return;
            }

            var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            List<GraphControlFlowEdge> edges = controlEdges ?? new List<GraphControlFlowEdge>();
            for (int i = 0; i < edges.Count; i++)
            {
                GraphControlFlowEdge edge = edges[i];
                if (string.IsNullOrWhiteSpace(edge.From) || string.IsNullOrWhiteSpace(edge.To))
                {
                    continue;
                }

                if (!adjacency.TryGetValue(edge.From, out List<string>? tos))
                {
                    tos = new List<string>();
                    adjacency[edge.From] = tos;
                }

                tos.Add(edge.To);
            }

            var reachable = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<string>();
            if (!reachable.Add(entry))
            {
                return;
            }

            stack.Push(entry);
            while (stack.Count > 0)
            {
                string current = stack.Pop();
                if (!adjacency.TryGetValue(current, out List<string>? tos))
                {
                    continue;
                }

                for (int t = 0; t < tos.Count; t++)
                {
                    if (reachable.Add(tos[t]))
                    {
                        stack.Push(tos[t]);
                    }
                }
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                if (!reachable.Contains(nodes[i].Id))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnreachableNode,
                        $"Node '{nodes[i].Id}' is unreachable from entry.", nodes[i].Id));
                }
            }
        }

        private static NodeLayout[] BuildLayouts(
            List<GraphControlFlowNode> nodes,
            AuthoredOp[] ops,
            List<GraphDiagnostic> diagnostics)
        {
            var layouts = new NodeLayout[nodes.Count];
            int cursor = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                int count = InstructionCount(ops[i]);
                layouts[i] = new NodeLayout(cursor, count);
                cursor += count;
            }

            return layouts;
        }

        private static int InstructionCount(AuthoredOp op)
        {
            if (op.Kind == AuthoredOpKind.BranchBool || op.NodeOp == GraphNodeOp.JumpIfFalse)
            {
                return 2; // JumpIfFalse + Jump(true)
            }

            return op.NodeOp switch
            {
                GraphNodeOp.Return or GraphNodeOp.HaltReturnInt or GraphNodeOp.Jump => 1,
                GraphNodeOp.Call or GraphNodeOp.ConstInt or GraphNodeOp.AddInt or GraphNodeOp.CompareLtInt
                    or GraphNodeOp.MoveInt or GraphNodeOp.Yield or GraphNodeOp.InvokeScript => 2, // body + jump next
                _ => 1
            };
        }

        private static void CompileNode(
            GraphControlFlowDocument document,
            GraphControlFlowNode node,
            AuthoredOp op,
            byte[] outputRegisters,
            GraphValueType[] outputTypes,
            Dictionary<ControlKey, string> controlEdges,
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges,
            Dictionary<string, int> nodeIndices,
            NodeLayout[] layouts,
            GraphInstruction[] program,
            GraphInstructionSource[] sources,
            bool[] definedInts,
            bool[] definedBools,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            int nodeIndex = nodeIndices[node.Id];
            int bodyIndex = layouts[nodeIndex].BodyIndex;

            if (op.Kind == AuthoredOpKind.BranchBool || op.NodeOp == GraphNodeOp.JumpIfFalse)
            {
                byte cond = ResolveValueInput(
                    node, GraphControlFlowPorts.Condition, GraphValueType.Bool,
                    valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                int falseAbs = ResolveControlTarget(node, GraphControlFlowPorts.False, controlEdges, nodeIndices, layouts);
                program[bodyIndex] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.JumpIfFalse,
                    A = cond,
                    Imm = RelativeOffset(bodyIndex, falseAbs)
                };
                SetSource(sources, bodyIndex, graphId, node, BranchBoolOp, GraphControlFlowPorts.Enter);
                EmitRelativeJump(
                    document, node, GraphControlFlowPorts.True, bodyIndex + 1,
                    controlEdges, nodeIndices, layouts, program, sources, graphId);
                return;
            }

            switch (op.NodeOp)
            {
                case GraphNodeOp.ConstInt:
                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphNodeOp.ConstInt,
                        Dst = outputRegisters[nodeIndex],
                        Imm = node.IntValue
                    };
                    definedInts[outputRegisters[nodeIndex]] = true;
                    SetSource(sources, bodyIndex, graphId, node, nameof(GraphNodeOp.ConstInt), GraphControlFlowPorts.Enter);
                    EmitRelativeJump(document, node, GraphControlFlowPorts.Next, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources, graphId);
                    break;

                case GraphNodeOp.AddInt:
                {
                    byte a = ResolveValueInput(node, GraphControlFlowPorts.A, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    byte b = ResolveValueInput(node, GraphControlFlowPorts.B, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphNodeOp.AddInt,
                        Dst = outputRegisters[nodeIndex],
                        A = a,
                        B = b
                    };
                    definedInts[outputRegisters[nodeIndex]] = true;
                    SetSource(sources, bodyIndex, graphId, node, nameof(GraphNodeOp.AddInt), GraphControlFlowPorts.Enter);
                    EmitRelativeJump(document, node, GraphControlFlowPorts.Next, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources, graphId);
                    break;
                }

                case GraphNodeOp.CompareLtInt:
                {
                    byte a = ResolveValueInput(node, GraphControlFlowPorts.A, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    byte b = ResolveValueInput(node, GraphControlFlowPorts.B, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphNodeOp.CompareLtInt,
                        Dst = outputRegisters[nodeIndex],
                        A = a,
                        B = b
                    };
                    definedBools[outputRegisters[nodeIndex]] = true;
                    SetSource(sources, bodyIndex, graphId, node, nameof(GraphNodeOp.CompareLtInt), GraphControlFlowPorts.Enter);
                    EmitRelativeJump(document, node, GraphControlFlowPorts.Next, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources, graphId);
                    break;
                }

                case GraphNodeOp.MoveInt:
                {
                    byte a = ResolveValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphNodeOp.MoveInt,
                        Dst = outputRegisters[nodeIndex],
                        A = a
                    };
                    definedInts[outputRegisters[nodeIndex]] = true;
                    SetSource(sources, bodyIndex, graphId, node, nameof(GraphNodeOp.MoveInt), GraphControlFlowPorts.Enter);
                    EmitRelativeJump(document, node, GraphControlFlowPorts.Next, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources, graphId);
                    break;
                }

                case GraphNodeOp.Call:
                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphNodeOp.Call,
                        Imm = ResolveControlTarget(node, GraphControlFlowPorts.Call, controlEdges, nodeIndices, layouts)
                    };
                    SetSource(sources, bodyIndex, graphId, node, nameof(GraphNodeOp.Call), GraphControlFlowPorts.Call);
                    EmitRelativeJump(document, node, GraphControlFlowPorts.Next, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources, graphId);
                    break;

                case GraphNodeOp.Return:
                    program[bodyIndex] = new GraphInstruction { Op = (ushort)GraphNodeOp.Return };
                    SetSource(sources, bodyIndex, graphId, node, nameof(GraphNodeOp.Return), GraphControlFlowPorts.Enter);
                    break;

                case GraphNodeOp.HaltReturnInt:
                {
                    byte a = ResolveValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphNodeOp.HaltReturnInt,
                        A = a
                    };
                    SetSource(sources, bodyIndex, graphId, node, nameof(GraphNodeOp.HaltReturnInt), GraphControlFlowPorts.Enter);
                    break;
                }

                case GraphNodeOp.Yield:
                    program[bodyIndex] = new GraphInstruction { Op = (ushort)GraphNodeOp.Yield };
                    SetSource(sources, bodyIndex, graphId, node, nameof(GraphNodeOp.Yield), GraphControlFlowPorts.Enter);
                    EmitRelativeJump(document, node, GraphControlFlowPorts.Next, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources, graphId);
                    break;

                case GraphNodeOp.Jump:
                    EmitRelativeJump(document, node, GraphControlFlowPorts.Target, bodyIndex, controlEdges, nodeIndices, layouts, program, sources, graphId);
                    break;

                case GraphNodeOp.InvokeScript:
                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphNodeOp.InvokeScript,
                        Dst = outputRegisters[nodeIndex],
                        Imm = node.GraphId
                    };
                    definedInts[outputRegisters[nodeIndex]] = true;
                    SetSource(sources, bodyIndex, graphId, node, nameof(GraphNodeOp.InvokeScript), GraphControlFlowPorts.Enter);
                    EmitRelativeJump(document, node, GraphControlFlowPorts.Next, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources, graphId);
                    break;

                default:
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                        $"Op '{op.NodeOp}' is not supported by GraphControlFlowCompiler.", node.Id));
                    break;
            }
        }

        private static void EmitRelativeJump(
            GraphControlFlowDocument document,
            GraphControlFlowNode node,
            string port,
            int instructionIndex,
            Dictionary<ControlKey, string> controlEdges,
            Dictionary<string, int> nodeIndices,
            NodeLayout[] layouts,
            GraphInstruction[] program,
            GraphInstructionSource[] sources,
            string graphId)
        {
            int absolute = ResolveControlTarget(node, port, controlEdges, nodeIndices, layouts);
            program[instructionIndex] = new GraphInstruction
            {
                Op = (ushort)GraphNodeOp.Jump,
                Imm = RelativeOffset(instructionIndex, absolute)
            };
            SetSource(sources, instructionIndex, graphId, node, nameof(GraphNodeOp.Jump), port);
        }

        private static int RelativeOffset(int jumpInstructionIndex, int absoluteTarget)
            => absoluteTarget - (jumpInstructionIndex + 1);

        private static int ResolveControlTarget(
            GraphControlFlowNode node,
            string port,
            Dictionary<ControlKey, string> controlEdges,
            Dictionary<string, int> nodeIndices,
            NodeLayout[] layouts)
        {
            string targetNode = controlEdges[new ControlKey(node.Id, port)];
            return layouts[nodeIndices[targetNode]].BodyIndex;
        }

        private static byte ResolveValueInput(
            GraphControlFlowNode node,
            string port,
            GraphValueType expectedType,
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges,
            Dictionary<string, int> nodeIndices,
            GraphValueType[] outputTypes,
            byte[] outputRegisters,
            bool[] definedInts,
            bool[] definedBools,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            if (!valueEdges.TryGetValue(new ValueInputKey(node.Id, port), out GraphControlFlowValueEdge edge))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingValueInput,
                    $"Missing value input '{port}' on node '{node.Id}'.", node.Id));
                return 0;
            }

            int sourceIndex = nodeIndices[edge.From];
            if (outputTypes[sourceIndex] != expectedType)
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                    $"Value input '{port}' on '{node.Id}' expects {expectedType} but '{edge.From}' produces {outputTypes[sourceIndex]}.",
                    node.Id));
                return 0;
            }

            byte reg = outputRegisters[sourceIndex];
            if (expectedType == GraphValueType.Int)
            {
                if (!definedInts[reg])
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UninitializedRegisterRead,
                        $"Int register {reg} read before assignment via '{edge.From}' -> '{node.Id}'.{port}.",
                        node.Id));
                }
            }
            else if (expectedType == GraphValueType.Bool)
            {
                if (!definedBools[reg])
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UninitializedRegisterRead,
                        $"Bool register {reg} read before assignment via '{edge.From}' -> '{node.Id}'.{port}.",
                        node.Id));
                }
            }

            return reg;
        }

        private static void RequireControlEdge(
            GraphControlFlowNode node,
            string port,
            Dictionary<ControlKey, string> controlEdges,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            if (!controlEdges.ContainsKey(new ControlKey(node.Id, port)))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingControlEdge,
                    $"Missing control edge '{port}' on node '{node.Id}'.", node.Id));
            }
        }

        private static void RequireValueInput(
            GraphControlFlowNode node,
            string port,
            GraphValueType expectedType,
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges,
            Dictionary<string, int> nodeIndices,
            GraphValueType[] outputTypes,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            if (!valueEdges.TryGetValue(new ValueInputKey(node.Id, port), out GraphControlFlowValueEdge edge))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingValueInput,
                    $"Missing value input '{port}' on node '{node.Id}'.", node.Id));
                return;
            }

            int sourceIndex = nodeIndices[edge.From];
            if (outputTypes[sourceIndex] != expectedType && outputTypes[sourceIndex] != GraphValueType.Void)
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                    $"Value input '{port}' on '{node.Id}' expects {expectedType} but '{edge.From}' produces {outputTypes[sourceIndex]}.",
                    node.Id));
            }
        }

        private static void SetSource(
            GraphInstructionSource[] sources,
            int index,
            string graphId,
            GraphControlFlowNode node,
            string op,
            string port)
        {
            sources[index] = new GraphInstructionSource(graphId, node.Id, op, port);
        }

        private static GraphDiagnostic Error(string graphId, string code, string message, string? nodeId = null)
            => new(GraphDiagnosticSeverity.Error, code, message, graphId, nodeId);

        private static bool HasErrors(List<GraphDiagnostic> diagnostics)
        {
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Severity == GraphDiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
