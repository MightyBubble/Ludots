using System;
using System.Collections.Generic;
using Ludots.Core.EntityCollections;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public readonly struct GraphControlFlowCompileResult
    {
        public GraphControlFlowCompileResult(
            GraphInstruction[] program,
            GraphInstructionSourceMap sourceMap,
            List<GraphDiagnostic> diagnostics)
            : this(
                program,
                sourceMap,
                diagnostics,
                null,
                GraphOutputSchema.Empty)
        {
        }

        public GraphControlFlowCompileResult(
            GraphInstruction[] program,
            GraphInstructionSourceMap sourceMap,
            List<GraphDiagnostic> diagnostics,
            GraphProgramPackage? package,
            GraphOutputSchema outputSchema)
        {
            Program = program ?? Array.Empty<GraphInstruction>();
            SourceMap = sourceMap;
            Diagnostics = diagnostics ?? new List<GraphDiagnostic>();
            Package = package;
            OutputSchema = outputSchema ?? GraphOutputSchema.Empty;
        }

        public GraphInstruction[] Program { get; }
        public GraphInstructionSourceMap SourceMap { get; }
        public List<GraphDiagnostic> Diagnostics { get; }
        public GraphProgramPackage? Package { get; }
        public GraphOutputSchema OutputSchema { get; }
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
    /// Compiles L1 ControlFlow documents (all GraphKinds) into <see cref="GraphInstruction"/> using GraphNodeOp.
    /// BranchBool / SwitchInt / While / Until are compile-time sugar only (not GraphNodeOp values).
    /// Wait is an author alias for <see cref="GraphNodeOp.Yield"/> (Script only).
    /// </summary>
    public static partial class GraphControlFlowCompiler
    {
        public const string BranchBoolOp = GraphAuthoringSugar.BranchBool;
        public const string SwitchIntOp = GraphAuthoringSugar.SwitchInt;
        public const string WaitOp = GraphAuthoringSugar.Wait;
        public const string WhileOp = GraphAuthoringSugar.While;
        public const string UntilOp = GraphAuthoringSugar.Until;

        private readonly struct SugarScratch
        {
            public SugarScratch(byte intReg, byte boolReg)
            {
                IntReg = intReg;
                BoolReg = boolReg;
            }

            public byte IntReg { get; }
            public byte BoolReg { get; }
        }

        private readonly struct SwitchCaseArm
        {
            public SwitchCaseArm(int caseValue, string targetNodeId)
            {
                CaseValue = caseValue;
                TargetNodeId = targetNodeId;
            }

            public int CaseValue { get; }
            public string TargetNodeId { get; }
        }

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
            BranchBool = 1,
            SwitchInt = 2,
            While = 3,
            Until = 4
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
            => CompileCore(document);

        public static (GraphProgramPackage? Package, GraphOutputSchema OutputSchema, List<GraphDiagnostic> Diagnostics) CompileWithOutputs(
            GraphControlFlowDocument document)
        {
            GraphControlFlowCompileResult result = CompileCore(document);
            return (result.Package, result.OutputSchema, result.Diagnostics);
        }

        private static GraphControlFlowCompileResult CompileCore(GraphControlFlowDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var diagnostics = new List<GraphDiagnostic>();
            string graphId = document.Id ?? string.Empty;
            GraphKind graphKind = ParseControlFlowKind(document, graphId, diagnostics);
            ValidateHeader(document, graphId, graphKind, diagnostics);

            List<GraphControlFlowNode> nodes = document.Nodes ?? new List<GraphControlFlowNode>();
            Dictionary<string, int> nodeIndices = BuildNodeIndex(nodes, graphId, diagnostics);
            var ops = new AuthoredOp[nodes.Count];
            ParseOps(nodes, ops, graphKind, graphId, diagnostics);

            if (!string.IsNullOrWhiteSpace(document.Entry) &&
                !nodeIndices.ContainsKey(document.Entry))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                    $"{GraphKindLabel(graphKind)} entry node '{document.Entry}' does not exist.", document.Entry));
            }

            Dictionary<ControlKey, string> controlEdges = BuildControlEdges(document, nodeIndices, ops, graphId, diagnostics);
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges = BuildValueEdges(document, nodeIndices, graphId, diagnostics);

            var outputTypes = new GraphValueType[nodes.Count];
            var outputRegisters = new byte[nodes.Count];
            AllocateOutputs(nodes, ops, outputTypes, outputRegisters, graphId, graphKind, diagnostics);
            ValidateRequiredEdges(nodes, ops, graphKind, controlEdges, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
            DetectUnreachable(document.Entry, nodes, document.ControlEdges, graphId, diagnostics);

            var sugarScratches = new SugarScratch[nodes.Count];
            AllocateSugarScratches(nodes, ops, outputTypes, outputRegisters, sugarScratches, graphId, diagnostics);
            NodeLayout[] layouts = BuildLayouts(nodes, ops, graphKind, controlEdges, diagnostics);
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
                    $"{GraphKindLabel(graphKind)} exceeds instruction budget ({GraphVmLimits.MaxInstructionsPerExecution})."));
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
            var symbolToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var symbols = new List<string>();
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
                    sugarScratches,
                    controlEdges,
                    valueEdges,
                    nodeIndices,
                    layouts,
                    program,
                    sources,
                    definedInts,
                    definedBools,
                    symbolToIndex,
                    symbols,
                    graphKind,
                    graphId,
                    diagnostics);
            }

            if (HasErrors(diagnostics))
            {
                return new GraphControlFlowCompileResult(Array.Empty<GraphInstruction>(), GraphInstructionSourceMap.Empty, diagnostics);
            }

            GraphOutputSchema outputSchema = CompileOutputSchema(document, outputTypes, outputRegisters, nodeIndices, diagnostics);
            if (HasErrors(diagnostics))
            {
                return new GraphControlFlowCompileResult(Array.Empty<GraphInstruction>(), GraphInstructionSourceMap.Empty, diagnostics);
            }

            var sourceMap = new GraphInstructionSourceMap(graphId, sources);
            var package = new GraphProgramPackage(graphId, symbols.ToArray(), program, graphKind);
            return new GraphControlFlowCompileResult(
                program,
                sourceMap,
                diagnostics,
                package,
                outputSchema);
        }

        private static GraphKind ParseControlFlowKind(
            GraphControlFlowDocument document,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(document.Kind))
            {
                return GraphKind.Script;
            }

            if (!GraphKindParser.TryParse(document.Kind, out GraphKind graphKind) ||
                !GraphAuthoringKindPolicy.IsControlFlowAuthoringKind(graphKind))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnsupportedGraphKind,
                    $"ControlFlow document kind '{document.Kind}' is not supported. Supported kinds: {GraphAuthoringKindPolicy.DescribeSupportedKinds()}."));
                return GraphKind.Script;
            }

            return graphKind;
        }

        private static void ValidateHeader(
            GraphControlFlowDocument document,
            string graphId,
            GraphKind graphKind,
            List<GraphDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(document.Id))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingGraphId,
                    $"{GraphKindLabel(graphKind)} document requires a non-empty id."));
            }

            if (string.IsNullOrWhiteSpace(document.Entry))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingEntry,
                    $"{GraphKindLabel(graphKind)} document requires an entry node id."));
            }

            if (document.Nodes == null || document.Nodes.Count == 0)
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.EmptyGraph,
                    $"{GraphKindLabel(graphKind)} document requires at least one node."));
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
            GraphKind graphKind,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                GraphControlFlowNode node = nodes[i];
                if (string.Equals(node.Op, BranchBoolOp, StringComparison.Ordinal))
                {
                    if (graphKind != GraphKind.Script)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                            $"{BranchBoolOp} is Script compile-time sugar only.", node.Id));
                        ops[i] = new AuthoredOp(AuthoredOpKind.GraphNodeOp, GraphNodeOp.None);
                        continue;
                    }

                    ops[i] = new AuthoredOp(AuthoredOpKind.BranchBool, GraphNodeOp.None);
                    continue;
                }

                if (string.Equals(node.Op, SwitchIntOp, StringComparison.Ordinal))
                {
                    if (graphKind != GraphKind.Script)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                            $"{SwitchIntOp} is Script compile-time sugar only.", node.Id));
                        ops[i] = new AuthoredOp(AuthoredOpKind.GraphNodeOp, GraphNodeOp.None);
                        continue;
                    }

                    ops[i] = new AuthoredOp(AuthoredOpKind.SwitchInt, GraphNodeOp.None);
                    continue;
                }

                if (string.Equals(node.Op, WhileOp, StringComparison.Ordinal))
                {
                    if (graphKind != GraphKind.Script)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                            $"While is Script-only author sugar (kind='{graphKind}').", node.Id));
                        ops[i] = new AuthoredOp(AuthoredOpKind.GraphNodeOp, GraphNodeOp.None);
                        continue;
                    }

                    ops[i] = new AuthoredOp(AuthoredOpKind.While, GraphNodeOp.None);
                    continue;
                }

                if (string.Equals(node.Op, UntilOp, StringComparison.Ordinal))
                {
                    if (graphKind != GraphKind.Script)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                            $"Until is Script-only author sugar (kind='{graphKind}').", node.Id));
                        ops[i] = new AuthoredOp(AuthoredOpKind.GraphNodeOp, GraphNodeOp.None);
                        continue;
                    }

                    ops[i] = new AuthoredOp(AuthoredOpKind.Until, GraphNodeOp.None);
                    continue;
                }

                // Wait is author alias for Yield — Script CF only; never a second waiter opcode.
                if (string.Equals(node.Op, WaitOp, StringComparison.Ordinal))
                {
                    if (graphKind != GraphKind.Script)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                            $"Wait is Script-only author alias for Yield (kind='{graphKind}').", node.Id));
                        ops[i] = new AuthoredOp(AuthoredOpKind.GraphNodeOp, GraphNodeOp.None);
                        continue;
                    }

                    ops[i] = new AuthoredOp(AuthoredOpKind.GraphNodeOp, GraphNodeOp.Yield);
                    continue;
                }

                if (!GraphNodeOpParser.TryParse(node.Op, out GraphNodeOp nodeOp) ||
                    !IsControlFlowAuthorable(graphKind, nodeOp))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                        $"Unknown or non-{GraphKindLabel(graphKind)}-authorable op '{node.Op}'.", node.Id));
                    ops[i] = new AuthoredOp(AuthoredOpKind.GraphNodeOp, GraphNodeOp.None);
                    continue;
                }

                ops[i] = new AuthoredOp(AuthoredOpKind.GraphNodeOp, nodeOp);
            }
        }

        private static bool IsControlFlowAuthorable(GraphKind graphKind, GraphNodeOp op)
        {
            if (graphKind == GraphKind.Query)
            {
                return IsQueryControlFlowAuthorable(op);
            }

            if (GraphAuthoringKindPolicy.IsLinearAuthoringKind(graphKind))
            {
                return IsLinearControlFlowAuthorable(op);
            }

            return op is GraphNodeOp.ConstInt or
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
        }

        private static string GraphKindLabel(GraphKind graphKind)
            => graphKind.ToString();

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
                    string.IsNullOrWhiteSpace(edge.FromPort) ||
                    string.IsNullOrWhiteSpace(edge.To) ||
                    string.IsNullOrWhiteSpace(edge.ToPort))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingValueInput,
                        "Value edge requires From, FromPort, To, and ToPort."));
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
            GraphKind graphKind,
            List<GraphDiagnostic> diagnostics)
        {
            int intNext = 0;
            int boolNext = 0;
            int floatNext = 0;
            // E[0]=caster, E[1]=explicit target, E[2]=viewer are fixed context registers.
            int entityNext = 3;

            for (int i = 0; i < nodes.Count; i++)
            {
                GraphValueType outputType = GetOutputType(ops[i], graphKind);
                outputTypes[i] = outputType;
                if (outputType == GraphValueType.Void || outputType == GraphValueType.TargetList)
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
                    GraphValueType.Float => Alloc(ref floatNext, GraphVmLimits.MaxFloatRegisters, graphId, nodes[i].Id, diagnostics),
                    GraphValueType.Entity when ops[i].NodeOp == GraphNodeOp.LoadCaster => 0,
                    GraphValueType.Entity when ops[i].NodeOp == GraphNodeOp.LoadExplicitTarget => 1,
                    GraphValueType.Entity when ops[i].NodeOp == GraphNodeOp.LoadViewer => 2,
                    GraphValueType.Entity => Alloc(ref entityNext, GraphVmLimits.MaxEntityRegisters, graphId, nodes[i].Id, diagnostics),
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

        private static GraphValueType GetOutputType(AuthoredOp op, GraphKind graphKind)
        {
            if (op.Kind is AuthoredOpKind.BranchBool or AuthoredOpKind.SwitchInt
                or AuthoredOpKind.While or AuthoredOpKind.Until)
            {
                return GraphValueType.Void;
            }

            if (graphKind == GraphKind.Query)
            {
                return GetQueryOutputType(op.NodeOp);
            }

            if (GraphAuthoringKindPolicy.IsLinearAuthoringKind(graphKind))
            {
                return GetLinearOutputType(op.NodeOp);
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
            GraphKind graphKind,
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

                ValidateAllowedPorts(node, op, graphKind, controlEdges, valueEdges, graphId, diagnostics);

                if (graphKind == GraphKind.Query)
                {
                    ValidateQueryNode(
                        nodes,
                        i,
                        op,
                        controlEdges,
                        valueEdges,
                        nodeIndices,
                        outputTypes,
                        graphId,
                        diagnostics);
                    continue;
                }

                if (op.Kind == AuthoredOpKind.SwitchInt)
                {
                    ValidateSwitchIntEdges(node, controlEdges, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    continue;
                }

                if (op.Kind == AuthoredOpKind.BranchBool || op.NodeOp == GraphNodeOp.JumpIfFalse)
                {
                    RequireControlEdge(node, GraphControlFlowPorts.True, controlEdges, graphId, diagnostics);
                    RequireControlEdge(node, GraphControlFlowPorts.False, controlEdges, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Condition, GraphValueType.Bool, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    continue;
                }

                if (op.Kind is AuthoredOpKind.While or AuthoredOpKind.Until)
                {
                    RequireControlEdge(node, GraphControlFlowPorts.Body, controlEdges, graphId, diagnostics);
                    RequireControlEdge(node, GraphControlFlowPorts.Next, controlEdges, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Condition, GraphValueType.Bool, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    continue;
                }

                if (GraphAuthoringKindPolicy.IsLinearAuthoringKind(graphKind))
                {
                    ValidateLinearNode(
                        node,
                        op,
                        controlEdges,
                        valueEdges,
                        nodeIndices,
                        outputTypes,
                        graphId,
                        diagnostics);
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

        private static void ValidateAllowedPorts(
            GraphControlFlowNode node,
            AuthoredOp op,
            GraphKind graphKind,
            Dictionary<ControlKey, string> controlEdges,
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            foreach (ControlKey edge in controlEdges.Keys)
            {
                if (!string.Equals(edge.NodeId, node.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!IsAllowedControlPort(op, graphKind, edge.Port))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnexpectedControlEdge,
                        $"Unexpected control edge '{edge.Port}' on node '{node.Id}'.", node.Id));
                }
            }

            foreach (GraphControlFlowValueEdge edge in valueEdges.Values)
            {
                if (string.Equals(edge.From, node.Id, StringComparison.Ordinal) &&
                    !IsAllowedOutputPort(op, graphKind, edge.FromPort))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                        $"Unexpected value output '{edge.FromPort}' on node '{node.Id}'.", node.Id));
                }

                if (string.Equals(edge.To, node.Id, StringComparison.Ordinal) &&
                    !IsAllowedInputPort(op, graphKind, edge.ToPort))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingValueInput,
                        $"Unexpected value input '{edge.ToPort}' on node '{node.Id}'.", node.Id));
                }
            }
        }

        private static bool IsAllowedControlPort(AuthoredOp op, GraphKind graphKind, string port)
        {
            if (graphKind == GraphKind.Query)
            {
                return IsAllowedQueryControlPort(port);
            }

            if (GraphAuthoringKindPolicy.IsLinearAuthoringKind(graphKind))
            {
                return IsAllowedLinearControlPort(port);
            }

            if (op.Kind == AuthoredOpKind.BranchBool || op.NodeOp == GraphNodeOp.JumpIfFalse)
            {
                return port is GraphControlFlowPorts.True or GraphControlFlowPorts.False;
            }

            if (op.Kind == AuthoredOpKind.SwitchInt)
            {
                return port == GraphControlFlowPorts.Default ||
                       GraphControlFlowPorts.TryParseCasePort(port, out _);
            }

            if (op.Kind is AuthoredOpKind.While or AuthoredOpKind.Until)
            {
                return port is GraphControlFlowPorts.Body or GraphControlFlowPorts.Next;
            }

            return op.NodeOp switch
            {
                GraphNodeOp.Call => port is GraphControlFlowPorts.Call or GraphControlFlowPorts.Next,
                GraphNodeOp.Jump => port == GraphControlFlowPorts.Target,
                GraphNodeOp.Return or GraphNodeOp.HaltReturnInt => false,
                _ => port == GraphControlFlowPorts.Next
            };
        }

        private static bool IsAllowedInputPort(AuthoredOp op, GraphKind graphKind, string port)
        {
            if (graphKind == GraphKind.Query)
            {
                return IsAllowedQueryInputPort(op.NodeOp, port);
            }

            if (GraphAuthoringKindPolicy.IsLinearAuthoringKind(graphKind))
            {
                return IsAllowedLinearInputPort(op.NodeOp, port);
            }

            if (op.Kind == AuthoredOpKind.BranchBool ||
                op.Kind == AuthoredOpKind.While ||
                op.Kind == AuthoredOpKind.Until ||
                op.NodeOp == GraphNodeOp.JumpIfFalse)
            {
                return port == GraphControlFlowPorts.Condition;
            }

            if (op.Kind == AuthoredOpKind.SwitchInt)
            {
                return port == GraphControlFlowPorts.Selector;
            }

            return op.NodeOp switch
            {
                GraphNodeOp.AddInt or GraphNodeOp.CompareLtInt => port is GraphControlFlowPorts.A or GraphControlFlowPorts.B,
                GraphNodeOp.MoveInt or GraphNodeOp.HaltReturnInt => port == GraphControlFlowPorts.Value,
                _ => false
            };
        }

        private static bool IsAllowedOutputPort(AuthoredOp op, GraphKind graphKind, string port)
        {
            if (graphKind == GraphKind.Query)
            {
                return IsAllowedQueryOutputPort(op.NodeOp, port);
            }

            if (GraphAuthoringKindPolicy.IsLinearAuthoringKind(graphKind))
            {
                return IsAllowedLinearOutputPort(op.NodeOp, port);
            }

            return GetOutputType(op, graphKind) != GraphValueType.Void && port == GraphControlFlowPorts.Value;
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
            GraphKind graphKind,
            Dictionary<ControlKey, string> controlEdges,
            List<GraphDiagnostic> diagnostics)
        {
            var layouts = new NodeLayout[nodes.Count];
            int cursor = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                int count = InstructionCount(nodes[i], ops[i], graphKind, controlEdges);
                layouts[i] = new NodeLayout(cursor, count);
                cursor += count;
            }

            return layouts;
        }

        private static int InstructionCount(
            GraphControlFlowNode node,
            AuthoredOp op,
            GraphKind graphKind,
            Dictionary<ControlKey, string> controlEdges)
        {
            if (graphKind == GraphKind.Query || GraphAuthoringKindPolicy.IsLinearAuthoringKind(graphKind))
            {
                return controlEdges.ContainsKey(new ControlKey(node.Id, GraphControlFlowPorts.Next)) ? 2 : 1;
            }

            if (op.Kind == AuthoredOpKind.BranchBool ||
                op.Kind == AuthoredOpKind.While ||
                op.Kind == AuthoredOpKind.Until ||
                op.NodeOp == GraphNodeOp.JumpIfFalse)
            {
                return 2; // JumpIfFalse + Jump(arm)
            }

            if (op.Kind == AuthoredOpKind.SwitchInt)
            {
                int cases = CountSwitchCaseArms(node.Id, controlEdges);
                // per arm: ConstInt + CompareEqInt + JumpIfFalse + Jump(arm); then Jump(default)
                return (cases * 4) + 1;
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
            SugarScratch[] sugarScratches,
            Dictionary<ControlKey, string> controlEdges,
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges,
            Dictionary<string, int> nodeIndices,
            NodeLayout[] layouts,
            GraphInstruction[] program,
            GraphInstructionSource[] sources,
            bool[] definedInts,
            bool[] definedBools,
            Dictionary<string, int> symbolToIndex,
            List<string> symbols,
            GraphKind graphKind,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            int nodeIndex = nodeIndices[node.Id];
            int bodyIndex = layouts[nodeIndex].BodyIndex;

            if (graphKind == GraphKind.Query)
            {
                CompileQueryNode(
                    document,
                    node,
                    op,
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
                    symbolToIndex,
                    symbols,
                    graphId,
                    diagnostics);
                return;
            }

            if (GraphAuthoringKindPolicy.IsLinearAuthoringKind(graphKind))
            {
                CompileLinearNode(
                    document,
                    node,
                    op,
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
                    symbolToIndex,
                    symbols,
                    graphId,
                    diagnostics);
                return;
            }

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

            if (op.Kind == AuthoredOpKind.SwitchInt)
            {
                CompileSwitchInt(
                    document,
                    node,
                    sugarScratches[nodeIndex],
                    controlEdges,
                    valueEdges,
                    nodeIndices,
                    layouts,
                    program,
                    sources,
                    outputRegisters,
                    outputTypes,
                    definedInts,
                    definedBools,
                    graphId,
                    diagnostics);
                return;
            }

            if (op.Kind == AuthoredOpKind.While)
            {
                // while (cond) body;  =>  JumpIfFalse(cond)->next; Jump->body
                byte cond = ResolveValueInput(
                    node, GraphControlFlowPorts.Condition, GraphValueType.Bool,
                    valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                int nextAbs = ResolveControlTarget(node, GraphControlFlowPorts.Next, controlEdges, nodeIndices, layouts);
                program[bodyIndex] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.JumpIfFalse,
                    A = cond,
                    Imm = RelativeOffset(bodyIndex, nextAbs)
                };
                SetSource(sources, bodyIndex, graphId, node, WhileOp, GraphControlFlowPorts.Enter);
                EmitRelativeJump(
                    document, node, GraphControlFlowPorts.Body, bodyIndex + 1,
                    controlEdges, nodeIndices, layouts, program, sources, graphId);
                return;
            }

            if (op.Kind == AuthoredOpKind.Until)
            {
                // until (cond) body;  =>  JumpIfFalse(cond)->body; Jump->next  (exit when true)
                byte cond = ResolveValueInput(
                    node, GraphControlFlowPorts.Condition, GraphValueType.Bool,
                    valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                int bodyAbs = ResolveControlTarget(node, GraphControlFlowPorts.Body, controlEdges, nodeIndices, layouts);
                program[bodyIndex] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.JumpIfFalse,
                    A = cond,
                    Imm = RelativeOffset(bodyIndex, bodyAbs)
                };
                SetSource(sources, bodyIndex, graphId, node, UntilOp, GraphControlFlowPorts.Enter);
                EmitRelativeJump(
                    document, node, GraphControlFlowPorts.Next, bodyIndex + 1,
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
                    SetSource(
                        sources,
                        bodyIndex,
                        graphId,
                        node,
                        string.Equals(node.Op, WaitOp, StringComparison.Ordinal) ? WaitOp : nameof(GraphNodeOp.Yield),
                        GraphControlFlowPorts.Enter);
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


        private static void AllocateSugarScratches(
            List<GraphControlFlowNode> nodes,
            AuthoredOp[] ops,
            GraphValueType[] outputTypes,
            byte[] outputRegisters,
            SugarScratch[] sugarScratches,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            var usedInt = new bool[GraphVmLimits.MaxIntRegisters];
            var usedBool = new bool[GraphVmLimits.MaxBoolRegisters];
            for (int i = 0; i < nodes.Count; i++)
            {
                if (outputTypes[i] == GraphValueType.Int)
                {
                    usedInt[outputRegisters[i]] = true;
                }
                else if (outputTypes[i] == GraphValueType.Bool)
                {
                    usedBool[outputRegisters[i]] = true;
                }
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                if (ops[i].Kind != AuthoredOpKind.SwitchInt)
                {
                    continue;
                }

                byte intReg = AllocFree(usedInt, GraphVmLimits.MaxIntRegisters, graphId, nodes[i].Id, diagnostics);
                byte boolReg = AllocFree(usedBool, GraphVmLimits.MaxBoolRegisters, graphId, nodes[i].Id, diagnostics);
                sugarScratches[i] = new SugarScratch(intReg, boolReg);
            }
        }

        private static byte AllocFree(
            bool[] used,
            int max,
            string graphId,
            string nodeId,
            List<GraphDiagnostic> diagnostics)
        {
            for (int i = 0; i < max; i++)
            {
                if (!used[i])
                {
                    used[i] = true;
                    return (byte)i;
                }
            }

            diagnostics.Add(Error(graphId, GraphDiagnosticCodes.RegisterOutOfRange,
                $"Register budget exceeded ({max}).", nodeId));
            return 0;
        }

        private static void ValidateSwitchIntEdges(
            GraphControlFlowNode node,
            Dictionary<ControlKey, string> controlEdges,
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges,
            Dictionary<string, int> nodeIndices,
            GraphValueType[] outputTypes,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            RequireControlEdge(node, GraphControlFlowPorts.Default, controlEdges, graphId, diagnostics);
            RequireValueInput(node, GraphControlFlowPorts.Selector, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);

            var seenCases = new HashSet<int>();
            int caseCount = 0;
            foreach (ControlKey key in controlEdges.Keys)
            {
                if (!string.Equals(key.NodeId, node.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (key.Port == GraphControlFlowPorts.Default)
                {
                    continue;
                }

                if (!GraphControlFlowPorts.TryParseCasePort(key.Port, out int caseValue))
                {
                    continue;
                }

                if (!seenCases.Add(caseValue))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.DuplicateControlEdge,
                        $"Duplicate SwitchInt case value {caseValue} on node '{node.Id}'.", node.Id));
                }

                caseCount++;
            }

            if (caseCount == 0)
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingControlEdge,
                    $"SwitchInt node '{node.Id}' requires at least one case:{{n}} control edge.", node.Id));
            }
        }

        private static int CountSwitchCaseArms(string nodeId, Dictionary<ControlKey, string> controlEdges)
        {
            int count = 0;
            foreach (ControlKey key in controlEdges.Keys)
            {
                if (string.Equals(key.NodeId, nodeId, StringComparison.Ordinal) &&
                    GraphControlFlowPorts.TryParseCasePort(key.Port, out _))
                {
                    count++;
                }
            }

            return count;
        }

        private static List<SwitchCaseArm> CollectSwitchCaseArms(
            GraphControlFlowDocument document,
            GraphControlFlowNode node)
        {
            var arms = new List<SwitchCaseArm>();
            List<GraphControlFlowEdge> edges = document.ControlEdges ?? new List<GraphControlFlowEdge>();
            for (int i = 0; i < edges.Count; i++)
            {
                GraphControlFlowEdge edge = edges[i];
                if (!string.Equals(edge.From, node.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!GraphControlFlowPorts.TryParseCasePort(edge.FromPort, out int caseValue))
                {
                    continue;
                }

                arms.Add(new SwitchCaseArm(caseValue, edge.To));
            }

            return arms;
        }

        private static void CompileSwitchInt(
            GraphControlFlowDocument document,
            GraphControlFlowNode node,
            SugarScratch scratch,
            Dictionary<ControlKey, string> controlEdges,
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges,
            Dictionary<string, int> nodeIndices,
            NodeLayout[] layouts,
            GraphInstruction[] program,
            GraphInstructionSource[] sources,
            byte[] outputRegisters,
            GraphValueType[] outputTypes,
            bool[] definedInts,
            bool[] definedBools,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            int nodeIndex = nodeIndices[node.Id];
            int bodyIndex = layouts[nodeIndex].BodyIndex;
            List<SwitchCaseArm> arms = CollectSwitchCaseArms(document, node);
            byte selector = ResolveValueInput(
                node, GraphControlFlowPorts.Selector, GraphValueType.Int,
                valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
            int defaultAbs = ResolveControlTarget(node, GraphControlFlowPorts.Default, controlEdges, nodeIndices, layouts);
            int defaultJumpIndex = bodyIndex + (arms.Count * 4);

            for (int i = 0; i < arms.Count; i++)
            {
                SwitchCaseArm arm = arms[i];
                int armBase = bodyIndex + (i * 4);
                int nextCheck = (i + 1 < arms.Count) ? bodyIndex + ((i + 1) * 4) : defaultJumpIndex;
                int armAbs = layouts[nodeIndices[arm.TargetNodeId]].BodyIndex;

                program[armBase] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.ConstInt,
                    Dst = scratch.IntReg,
                    Imm = arm.CaseValue
                };
                SetSource(sources, armBase, graphId, node, SwitchIntOp, GraphControlFlowPorts.Case(arm.CaseValue));

                program[armBase + 1] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.CompareEqInt,
                    Dst = scratch.BoolReg,
                    A = selector,
                    B = scratch.IntReg
                };
                SetSource(sources, armBase + 1, graphId, node, SwitchIntOp, GraphControlFlowPorts.Case(arm.CaseValue));

                program[armBase + 2] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.JumpIfFalse,
                    A = scratch.BoolReg,
                    Imm = RelativeOffset(armBase + 2, nextCheck)
                };
                SetSource(sources, armBase + 2, graphId, node, SwitchIntOp, GraphControlFlowPorts.Case(arm.CaseValue));

                program[armBase + 3] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.Jump,
                    Imm = RelativeOffset(armBase + 3, armAbs)
                };
                SetSource(sources, armBase + 3, graphId, node, SwitchIntOp, GraphControlFlowPorts.Case(arm.CaseValue));
            }

            program[defaultJumpIndex] = new GraphInstruction
            {
                Op = (ushort)GraphNodeOp.Jump,
                Imm = RelativeOffset(defaultJumpIndex, defaultAbs)
            };
            SetSource(sources, defaultJumpIndex, graphId, node, SwitchIntOp, GraphControlFlowPorts.Default);
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

        private static GraphOutputSchema CompileOutputSchema(
            GraphControlFlowDocument document,
            GraphValueType[] outputTypes,
            byte[] outputRegisters,
            Dictionary<string, int> nodeIndices,
            List<GraphDiagnostic> diagnostics)
        {
            List<GraphOutputConfig> outputs = document.Outputs ?? new List<GraphOutputConfig>();
            if (outputs.Count == 0)
            {
                return GraphOutputSchema.Empty;
            }

            var bindings = new List<GraphOutputBinding>(outputs.Count);
            string graphId = document.Id ?? string.Empty;
            for (int i = 0; i < outputs.Count; i++)
            {
                GraphOutputConfig output = outputs[i];
                if (output == null)
                {
                    continue;
                }

                string outputId = string.IsNullOrWhiteSpace(output.Id)
                    ? $"output[{i}]"
                    : output.Id;

                if (!TryParseOutputDestination(output.Destination, out GraphOutputDestinationKind destination))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                        $"ControlFlow graph output '{outputId}' has unsupported destination '{output.Destination}'.", outputId));
                    continue;
                }

                if (!TryParseOutputValueKind(output.Type, out GraphOutputValueKind valueKind))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                        $"ControlFlow graph output '{outputId}' has unsupported type '{output.Type}'.", outputId));
                    continue;
                }

                if (destination == GraphOutputDestinationKind.EntityCollection)
                {
                    CompileCollectionOutput(graphId, output, outputId, valueKind, bindings, diagnostics);
                    continue;
                }

                if (destination != GraphOutputDestinationKind.Summary)
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                        $"ControlFlow graph output '{outputId}' has unsupported destination '{output.Destination}'.", outputId));
                    continue;
                }

                if (valueKind == GraphOutputValueKind.TargetList)
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                        $"ControlFlow summary output '{outputId}' has unsupported type '{output.Type}'.", outputId));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(output.Source) ||
                    !nodeIndices.TryGetValue(output.Source, out int sourceIndex))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                        $"ControlFlow summary output '{outputId}' references missing source '{output.Source}'.", outputId));
                    continue;
                }

                GraphValueType sourceType = outputTypes[sourceIndex];
                if (!MatchesOutputType(valueKind, sourceType))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                        $"ControlFlow summary output '{outputId}' type {valueKind} does not match source '{output.Source}' type {sourceType}.",
                        outputId));
                    continue;
                }

                string key = string.IsNullOrWhiteSpace(output.Key) ? outputId : output.Key.Trim();
                bindings.Add(new GraphOutputBinding(
                    outputId,
                    GraphOutputDestinationKind.Summary,
                    valueKind,
                    outputRegisters[sourceIndex],
                    keyId: 0,
                    key,
                    collectionKey: string.Empty,
                    collectionRole: EntityCollectionRoleKind.Display,
                    title: output.Title,
                    summary: output.Summary));
            }

            return bindings.Count == 0
                ? GraphOutputSchema.Empty
                : new GraphOutputSchema(bindings.ToArray());
        }

        private static void CompileCollectionOutput(
            string graphId,
            GraphOutputConfig output,
            string outputId,
            GraphOutputValueKind valueKind,
            List<GraphOutputBinding> bindings,
            List<GraphDiagnostic> diagnostics)
        {
            if (valueKind != GraphOutputValueKind.TargetList)
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                    $"ControlFlow collection output '{outputId}' must use type TargetList.", outputId));
                return;
            }

            if (string.IsNullOrWhiteSpace(output.CollectionKey))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                    $"ControlFlow collection output '{outputId}' requires collectionKey.", outputId));
                return;
            }

            EntityCollectionRoleKind role = EntityCollectionRoleKind.Display;
            if (!string.IsNullOrWhiteSpace(output.Role) &&
                !Enum.TryParse(output.Role, ignoreCase: false, out role))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                    $"ControlFlow collection output '{outputId}' has unsupported role '{output.Role}'.", outputId));
                return;
            }

            bindings.Add(new GraphOutputBinding(
                outputId,
                GraphOutputDestinationKind.EntityCollection,
                GraphOutputValueKind.TargetList,
                register: 0,
                keyId: 0,
                key: string.Empty,
                collectionKey: output.CollectionKey.Trim(),
                collectionRole: role,
                title: output.Title,
                summary: output.Summary));
        }

        private static bool TryParseOutputDestination(string value, out GraphOutputDestinationKind destination)
        {
            destination = GraphOutputDestinationKind.Summary;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return Enum.TryParse(value, ignoreCase: false, out destination) &&
                   Enum.IsDefined(typeof(GraphOutputDestinationKind), destination);
        }

        private static bool TryParseOutputValueKind(string value, out GraphOutputValueKind valueKind)
        {
            valueKind = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return Enum.TryParse(value, ignoreCase: false, out valueKind) &&
                   Enum.IsDefined(typeof(GraphOutputValueKind), valueKind);
        }

        private static bool MatchesOutputType(GraphOutputValueKind outputKind, GraphValueType graphType)
        {
            return outputKind switch
            {
                GraphOutputValueKind.Bool => graphType == GraphValueType.Bool,
                GraphOutputValueKind.Int => graphType == GraphValueType.Int,
                GraphOutputValueKind.Float => graphType == GraphValueType.Float,
                GraphOutputValueKind.Entity => graphType == GraphValueType.Entity,
                GraphOutputValueKind.TargetList => graphType == GraphValueType.TargetList,
                _ => false,
            };
        }

        private static int RequireSymbol(
            string? symbol,
            string fieldName,
            GraphControlFlowNode node,
            Dictionary<string, int> symbolToIndex,
            List<string> symbols,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                    $"Node '{node.Id}' requires a non-empty {fieldName}.", node.Id));
                return -1;
            }

            return Intern(symbolToIndex, symbols, symbol);
        }

        private static int RequireLookupFieldSymbol(
            GraphControlFlowNode node,
            Dictionary<string, int> symbolToIndex,
            List<string> symbols,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(node.LookupTable) || string.IsNullOrWhiteSpace(node.LookupField))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                    $"Node '{node.Id}' requires non-empty lookupTable and lookupField.", node.Id));
                return -1;
            }

            string fieldSymbol = Host.GraphLookupTableRegistry.EncodeFieldSymbol(node.LookupTable, node.LookupField);
            return Intern(symbolToIndex, symbols, fieldSymbol);
        }

        private static int Intern(Dictionary<string, int> symbolToIndex, List<string> symbols, string symbol)
        {
            if (symbolToIndex.TryGetValue(symbol, out int existing))
            {
                return existing;
            }

            int index = symbols.Count;
            symbolToIndex[symbol] = index;
            symbols.Add(symbol);
            return index;
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
