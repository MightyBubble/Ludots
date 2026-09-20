using System;
using System.Collections.Generic;

namespace Ludots.Core.GraphRuntime
{
    public readonly struct GraphVmCompileResult
    {
        public GraphVmCompileResult(
            GraphInstruction[] program,
            GraphInstructionSourceMap sourceMap,
            GraphVmDiagnostic[] diagnostics)
        {
            Program = program ?? Array.Empty<GraphInstruction>();
            SourceMap = sourceMap;
            Diagnostics = diagnostics ?? Array.Empty<GraphVmDiagnostic>();
        }

        public GraphInstruction[] Program { get; }
        public GraphInstructionSourceMap SourceMap { get; }
        public GraphVmDiagnostic[] Diagnostics { get; }
        public bool Succeeded => Diagnostics.Length == 0;
    }

    public static class GraphVmCompiler
    {
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

            public override bool Equals(object? obj)
                => obj is ControlKey other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(
                    StringComparer.Ordinal.GetHashCode(NodeId),
                    StringComparer.Ordinal.GetHashCode(Port));
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

            public override bool Equals(object? obj)
                => obj is ValueInputKey other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(
                    StringComparer.Ordinal.GetHashCode(NodeId),
                    StringComparer.Ordinal.GetHashCode(Port));
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

        public static GraphVmCompileResult Compile(GraphVmDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var diagnostics = new List<GraphVmDiagnostic>();
            ValidateHeader(document, diagnostics);

            List<GraphVmNode> nodes = document.Nodes ?? new List<GraphVmNode>();
            Dictionary<string, int> nodeIndices = BuildNodeIndex(nodes, diagnostics);

            var ops = new GraphVmOpcode[nodes.Count];
            ParseOps(nodes, ops, diagnostics);

            if (!string.IsNullOrWhiteSpace(document.Entry) &&
                !nodeIndices.ContainsKey(document.Entry))
            {
                diagnostics.Add(new GraphVmDiagnostic(
                    GraphVmDiagnosticSeverity.Error,
                    GraphVmDiagnosticCodes.MissingTargetNode,
                    $"GraphVM entry node '{document.Entry}' does not exist.",
                    document.Entry));
            }

            Dictionary<ControlKey, string> controlEdges = BuildControlEdges(document, nodeIndices, ops, diagnostics);
            Dictionary<ValueInputKey, GraphVmValueEdge> valueEdges = BuildValueEdges(document, nodeIndices, ops, diagnostics);

            var outputTypes = new GraphVmValueType[nodes.Count];
            var outputRegisters = new byte[nodes.Count];
            AllocateOutputs(nodes, ops, outputTypes, outputRegisters, diagnostics);
            ValidateRequiredEdges(nodes, ops, controlEdges, valueEdges, nodeIndices, outputTypes, diagnostics);

            NodeLayout[] layouts = BuildLayouts(nodes, ops, diagnostics);

            if (diagnostics.Count > 0)
            {
                return new GraphVmCompileResult(
                    Array.Empty<GraphInstruction>(),
                    GraphInstructionSourceMap.Empty,
                    diagnostics.ToArray());
            }

            int instructionCount = 1;
            for (int i = 0; i < layouts.Length; i++)
            {
                instructionCount += layouts[i].InstructionCount;
            }

            if (instructionCount > GraphVmRuntimeLimits.MaxInstructions)
            {
                diagnostics.Add(new GraphVmDiagnostic(
                    GraphVmDiagnosticSeverity.Error,
                    GraphVmDiagnosticCodes.BudgetExceeded,
                    $"GraphVM compiled instruction count {instructionCount} exceeds max {GraphVmRuntimeLimits.MaxInstructions}."));

                return new GraphVmCompileResult(
                    Array.Empty<GraphInstruction>(),
                    GraphInstructionSourceMap.Empty,
                    diagnostics.ToArray());
            }

            var program = new GraphInstruction[instructionCount];
            var sources = new GraphInstructionSource[instructionCount];

            program[0] = new GraphInstruction
            {
                Op = (ushort)GraphVmOpcode.Jump,
                Imm = layouts[nodeIndices[document.Entry]].BodyIndex
            };
            sources[0] = new GraphInstructionSource(
                document.Id,
                document.Entry,
                "Entry",
                GraphVmControlPorts.Enter);

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
                    diagnostics);
            }

            return diagnostics.Count == 0
                ? new GraphVmCompileResult(program, new GraphInstructionSourceMap(document.Id, sources), Array.Empty<GraphVmDiagnostic>())
                : new GraphVmCompileResult(Array.Empty<GraphInstruction>(), GraphInstructionSourceMap.Empty, diagnostics.ToArray());
        }

        private static void ValidateHeader(GraphVmDocument document, List<GraphVmDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(document.Id))
            {
                diagnostics.Add(new GraphVmDiagnostic(
                    GraphVmDiagnosticSeverity.Error,
                    GraphVmDiagnosticCodes.MissingGraphId,
                    "GraphVM document requires a non-empty id."));
            }

            if (string.IsNullOrWhiteSpace(document.Entry))
            {
                diagnostics.Add(new GraphVmDiagnostic(
                    GraphVmDiagnosticSeverity.Error,
                    GraphVmDiagnosticCodes.MissingEntry,
                    "GraphVM document requires a non-empty entry node id."));
            }

            if (document.Nodes == null || document.Nodes.Count == 0)
            {
                diagnostics.Add(new GraphVmDiagnostic(
                    GraphVmDiagnosticSeverity.Error,
                    GraphVmDiagnosticCodes.EmptyGraph,
                    "GraphVM document requires at least one node."));
                return;
            }

            if (document.Nodes.Count > GraphVmRuntimeLimits.MaxInstructions)
            {
                diagnostics.Add(new GraphVmDiagnostic(
                    GraphVmDiagnosticSeverity.Error,
                    GraphVmDiagnosticCodes.BudgetExceeded,
                    $"GraphVM document exceeds max node count ({GraphVmRuntimeLimits.MaxInstructions})."));
            }
        }

        private static Dictionary<string, int> BuildNodeIndex(
            List<GraphVmNode> nodes,
            List<GraphVmDiagnostic> diagnostics)
        {
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < nodes.Count; i++)
            {
                GraphVmNode node = nodes[i];
                if (string.IsNullOrWhiteSpace(node.Id))
                {
                    diagnostics.Add(new GraphVmDiagnostic(
                        GraphVmDiagnosticSeverity.Error,
                        GraphVmDiagnosticCodes.MissingNodeId,
                        "GraphVM node requires a non-empty id."));
                    continue;
                }

                if (!indices.TryAdd(node.Id, i))
                {
                    diagnostics.Add(new GraphVmDiagnostic(
                        GraphVmDiagnosticSeverity.Error,
                        GraphVmDiagnosticCodes.DuplicateNodeId,
                        $"Duplicate GraphVM node id '{node.Id}'.",
                        node.Id));
                }
            }

            return indices;
        }

        private static void ParseOps(
            List<GraphVmNode> nodes,
            GraphVmOpcode[] ops,
            List<GraphVmDiagnostic> diagnostics)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                GraphVmNode node = nodes[i];
                if (!Enum.TryParse(node.Op, ignoreCase: true, out GraphVmOpcode op) ||
                    !Enum.IsDefined(typeof(GraphVmOpcode), op))
                {
                    diagnostics.Add(new GraphVmDiagnostic(
                        GraphVmDiagnosticSeverity.Error,
                        GraphVmDiagnosticCodes.UnknownOp,
                        $"Unknown GraphVM op '{node.Op}'.",
                        node.Id));
                    continue;
                }

                ops[i] = op;
            }
        }

        private static Dictionary<ControlKey, string> BuildControlEdges(
            GraphVmDocument document,
            Dictionary<string, int> nodeIndices,
            GraphVmOpcode[] ops,
            List<GraphVmDiagnostic> diagnostics)
        {
            var result = new Dictionary<ControlKey, string>();
            if (document.ControlEdges == null)
            {
                return result;
            }

            for (int i = 0; i < document.ControlEdges.Count; i++)
            {
                GraphVmControlEdge edge = document.ControlEdges[i];
                if (edge == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(edge.From) ||
                    string.IsNullOrWhiteSpace(edge.FromPort) ||
                    string.IsNullOrWhiteSpace(edge.To))
                {
                    diagnostics.Add(new GraphVmDiagnostic(
                        GraphVmDiagnosticSeverity.Error,
                        GraphVmDiagnosticCodes.MissingTarget,
                        $"GraphVM control edge[{i}] requires from, fromPort, and to."));
                    continue;
                }

                if (!nodeIndices.TryGetValue(edge.From, out int fromIndex))
                {
                    diagnostics.Add(new GraphVmDiagnostic(
                        GraphVmDiagnosticSeverity.Error,
                        GraphVmDiagnosticCodes.MissingTargetNode,
                        $"GraphVM control edge[{i}] references missing source node '{edge.From}'.",
                        edge.From));
                    continue;
                }

                if (!nodeIndices.ContainsKey(edge.To))
                {
                    diagnostics.Add(new GraphVmDiagnostic(
                        GraphVmDiagnosticSeverity.Error,
                        GraphVmDiagnosticCodes.MissingTargetNode,
                        $"GraphVM control edge[{i}] references missing target node '{edge.To}'.",
                        edge.From));
                    continue;
                }

                if (!IsValidControlPort(ops[fromIndex], edge.FromPort))
                {
                    diagnostics.Add(new GraphVmDiagnostic(
                        GraphVmDiagnosticSeverity.Error,
                        GraphVmDiagnosticCodes.UnexpectedControlEdge,
                        $"GraphVM node '{edge.From}' op '{ops[fromIndex]}' does not support control port '{edge.FromPort}'.",
                        edge.From));
                    continue;
                }

                var key = new ControlKey(edge.From, edge.FromPort);
                if (result.ContainsKey(key))
                {
                    diagnostics.Add(new GraphVmDiagnostic(
                        GraphVmDiagnosticSeverity.Error,
                        GraphVmDiagnosticCodes.DuplicateControlEdge,
                        $"GraphVM node '{edge.From}' has duplicate control edge for port '{edge.FromPort}'.",
                        edge.From));
                    continue;
                }

                result[key] = edge.To;
            }

            return result;
        }

        private static Dictionary<ValueInputKey, GraphVmValueEdge> BuildValueEdges(
            GraphVmDocument document,
            Dictionary<string, int> nodeIndices,
            GraphVmOpcode[] ops,
            List<GraphVmDiagnostic> diagnostics)
        {
            var result = new Dictionary<ValueInputKey, GraphVmValueEdge>();
            if (document.ValueEdges == null)
            {
                return result;
            }

            for (int i = 0; i < document.ValueEdges.Count; i++)
            {
                GraphVmValueEdge edge = document.ValueEdges[i];
                if (edge == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(edge.From) ||
                    string.IsNullOrWhiteSpace(edge.FromPort) ||
                    string.IsNullOrWhiteSpace(edge.To) ||
                    string.IsNullOrWhiteSpace(edge.ToPort))
                {
                    diagnostics.Add(new GraphVmDiagnostic(
                        GraphVmDiagnosticSeverity.Error,
                        GraphVmDiagnosticCodes.MissingValueInput,
                        $"GraphVM value edge[{i}] requires from, fromPort, to, and toPort."));
                    continue;
                }

                if (!nodeIndices.TryGetValue(edge.From, out int fromIndex))
                {
                    diagnostics.Add(new GraphVmDiagnostic(
                        GraphVmDiagnosticSeverity.Error,
                        GraphVmDiagnosticCodes.MissingValueSource,
                        $"GraphVM value edge[{i}] references missing source node '{edge.From}'.",
                        edge.To));
                    continue;
                }

                if (!nodeIndices.TryGetValue(edge.To, out int toIndex))
                {
                    diagnostics.Add(new GraphVmDiagnostic(
                        GraphVmDiagnosticSeverity.Error,
                        GraphVmDiagnosticCodes.MissingTargetNode,
                        $"GraphVM value edge[{i}] references missing target node '{edge.To}'.",
                        edge.To));
                    continue;
                }

                if (!string.Equals(edge.FromPort, GraphVmValuePorts.Value, StringComparison.Ordinal))
                {
                    diagnostics.Add(new GraphVmDiagnostic(
                        GraphVmDiagnosticSeverity.Error,
                        GraphVmDiagnosticCodes.MissingValueSource,
                        $"GraphVM node '{edge.From}' exposes only value output port '{GraphVmValuePorts.Value}'.",
                        edge.From));
                    continue;
                }

                if (GetOutputType(ops[fromIndex]) == GraphVmValueType.Void)
                {
                    diagnostics.Add(new GraphVmDiagnostic(
                        GraphVmDiagnosticSeverity.Error,
                        GraphVmDiagnosticCodes.MissingValueSource,
                        $"GraphVM node '{edge.From}' op '{ops[fromIndex]}' does not produce a value.",
                        edge.To));
                    continue;
                }

                if (!IsValidValueInputPort(ops[toIndex], edge.ToPort))
                {
                    diagnostics.Add(new GraphVmDiagnostic(
                        GraphVmDiagnosticSeverity.Error,
                        GraphVmDiagnosticCodes.MissingValueInput,
                        $"GraphVM node '{edge.To}' op '{ops[toIndex]}' does not support value input port '{edge.ToPort}'.",
                        edge.To));
                    continue;
                }

                var key = new ValueInputKey(edge.To, edge.ToPort);
                if (result.ContainsKey(key))
                {
                    diagnostics.Add(new GraphVmDiagnostic(
                        GraphVmDiagnosticSeverity.Error,
                        GraphVmDiagnosticCodes.DuplicateValueEdge,
                        $"GraphVM node '{edge.To}' has duplicate value edge for port '{edge.ToPort}'.",
                        edge.To));
                    continue;
                }

                result[key] = edge;
            }

            return result;
        }

        private static void AllocateOutputs(
            List<GraphVmNode> nodes,
            GraphVmOpcode[] ops,
            GraphVmValueType[] outputTypes,
            byte[] outputRegisters,
            List<GraphVmDiagnostic> diagnostics)
        {
            int intNext = 0;
            int boolNext = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                GraphVmNode node = nodes[i];
                GraphVmOpcode op = ops[i];
                if (op == GraphVmOpcode.StoreInt)
                {
                    ReserveIntSlot(node.Slot, ref intNext, node.Id, diagnostics);
                }
                else if (op == GraphVmOpcode.LoadInt)
                {
                    ReserveIntSlot(node.Slot, ref intNext, node.Id, diagnostics);
                }
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                GraphVmValueType outputType = GetOutputType(ops[i]);
                outputTypes[i] = outputType;
                outputRegisters[i] = outputType switch
                {
                    GraphVmValueType.Int => AllocInt(ref intNext, nodes[i].Id, diagnostics),
                    GraphVmValueType.Bool => AllocBool(ref boolNext, nodes[i].Id, diagnostics),
                    _ => 0
                };
            }
        }

        private static void ValidateRequiredEdges(
            List<GraphVmNode> nodes,
            GraphVmOpcode[] ops,
            Dictionary<ControlKey, string> controlEdges,
            Dictionary<ValueInputKey, GraphVmValueEdge> valueEdges,
            Dictionary<string, int> nodeIndices,
            GraphVmValueType[] outputTypes,
            List<GraphVmDiagnostic> diagnostics)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                GraphVmNode node = nodes[i];
                GraphVmOpcode op = ops[i];

                switch (op)
                {
                    case GraphVmOpcode.ConstInt:
                    case GraphVmOpcode.LoadInt:
                    case GraphVmOpcode.StoreInt:
                    case GraphVmOpcode.AddInt:
                    case GraphVmOpcode.LessThanInt:
                    case GraphVmOpcode.Nop:
                    case GraphVmOpcode.Yield:
                        RequireControlEdge(node, GraphVmControlPorts.Next, controlEdges, diagnostics);
                        break;
                    case GraphVmOpcode.BranchBool:
                    case GraphVmOpcode.JumpIfFalse:
                        RequireControlEdge(node, GraphVmControlPorts.True, controlEdges, diagnostics);
                        RequireControlEdge(node, GraphVmControlPorts.False, controlEdges, diagnostics);
                        break;
                    case GraphVmOpcode.Call:
                        RequireControlEdge(node, GraphVmControlPorts.Call, controlEdges, diagnostics);
                        RequireControlEdge(node, GraphVmControlPorts.Next, controlEdges, diagnostics);
                        break;
                    case GraphVmOpcode.Jump:
                        RequireControlEdge(node, GraphVmControlPorts.Target, controlEdges, diagnostics);
                        break;
                    case GraphVmOpcode.Return:
                    case GraphVmOpcode.ReturnInt:
                        break;
                }

                switch (op)
                {
                    case GraphVmOpcode.StoreInt:
                    case GraphVmOpcode.ReturnInt:
                        RequireValueInput(node, GraphVmValuePorts.Value, GraphVmValueType.Int, valueEdges, nodeIndices, outputTypes, diagnostics);
                        break;
                    case GraphVmOpcode.AddInt:
                    case GraphVmOpcode.LessThanInt:
                        RequireValueInput(node, GraphVmValuePorts.A, GraphVmValueType.Int, valueEdges, nodeIndices, outputTypes, diagnostics);
                        RequireValueInput(node, GraphVmValuePorts.B, GraphVmValueType.Int, valueEdges, nodeIndices, outputTypes, diagnostics);
                        break;
                    case GraphVmOpcode.BranchBool:
                    case GraphVmOpcode.JumpIfFalse:
                        RequireValueInput(node, GraphVmValuePorts.Condition, GraphVmValueType.Bool, valueEdges, nodeIndices, outputTypes, diagnostics);
                        break;
                }
            }
        }

        private static NodeLayout[] BuildLayouts(
            List<GraphVmNode> nodes,
            GraphVmOpcode[] ops,
            List<GraphVmDiagnostic> diagnostics)
        {
            var layouts = new NodeLayout[nodes.Count];
            int nextInstruction = 1;

            for (int i = 0; i < nodes.Count; i++)
            {
                int count = GetInstructionCount(ops[i]);
                layouts[i] = new NodeLayout(nextInstruction, count);
                nextInstruction += count;
            }

            if (nextInstruction > GraphVmRuntimeLimits.MaxInstructions)
            {
                diagnostics.Add(new GraphVmDiagnostic(
                    GraphVmDiagnosticSeverity.Error,
                    GraphVmDiagnosticCodes.BudgetExceeded,
                    $"GraphVM compiled instruction count {nextInstruction} exceeds max {GraphVmRuntimeLimits.MaxInstructions}."));
            }

            return layouts;
        }

        private static void CompileNode(
            GraphVmDocument document,
            GraphVmNode node,
            GraphVmOpcode op,
            byte[] outputRegisters,
            GraphVmValueType[] outputTypes,
            Dictionary<ControlKey, string> controlEdges,
            Dictionary<ValueInputKey, GraphVmValueEdge> valueEdges,
            Dictionary<string, int> nodeIndices,
            NodeLayout[] layouts,
            GraphInstruction[] program,
            GraphInstructionSource[] sources,
            List<GraphVmDiagnostic> diagnostics)
        {
            int nodeIndex = nodeIndices[node.Id];
            int bodyIndex = layouts[nodeIndex].BodyIndex;

            switch (op)
            {
                case GraphVmOpcode.ConstInt:
                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphVmOpcode.ConstInt,
                        Dst = outputRegisters[nodeIndex],
                        Imm = node.IntValue
                    };
                    SetSource(document, sources, bodyIndex, node, op, GraphVmControlPorts.Enter);
                    EmitJumpToControlTarget(document, node, GraphVmControlPorts.Next, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources);
                    break;
                case GraphVmOpcode.LoadInt:
                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphVmOpcode.LoadInt,
                        Dst = outputRegisters[nodeIndex],
                        A = node.Slot
                    };
                    SetSource(document, sources, bodyIndex, node, op, GraphVmControlPorts.Enter);
                    EmitJumpToControlTarget(document, node, GraphVmControlPorts.Next, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources);
                    break;
                case GraphVmOpcode.StoreInt:
                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphVmOpcode.StoreInt,
                        Dst = node.Slot,
                        A = ResolveValueInput(node, GraphVmValuePorts.Value, GraphVmValueType.Int, valueEdges, nodeIndices, outputTypes, outputRegisters, diagnostics)
                    };
                    SetSource(document, sources, bodyIndex, node, op, GraphVmControlPorts.Enter);
                    EmitJumpToControlTarget(document, node, GraphVmControlPorts.Next, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources);
                    break;
                case GraphVmOpcode.AddInt:
                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphVmOpcode.AddInt,
                        Dst = outputRegisters[nodeIndex],
                        A = ResolveValueInput(node, GraphVmValuePorts.A, GraphVmValueType.Int, valueEdges, nodeIndices, outputTypes, outputRegisters, diagnostics),
                        B = ResolveValueInput(node, GraphVmValuePorts.B, GraphVmValueType.Int, valueEdges, nodeIndices, outputTypes, outputRegisters, diagnostics)
                    };
                    SetSource(document, sources, bodyIndex, node, op, GraphVmControlPorts.Enter);
                    EmitJumpToControlTarget(document, node, GraphVmControlPorts.Next, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources);
                    break;
                case GraphVmOpcode.LessThanInt:
                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphVmOpcode.LessThanInt,
                        Dst = outputRegisters[nodeIndex],
                        A = ResolveValueInput(node, GraphVmValuePorts.A, GraphVmValueType.Int, valueEdges, nodeIndices, outputTypes, outputRegisters, diagnostics),
                        B = ResolveValueInput(node, GraphVmValuePorts.B, GraphVmValueType.Int, valueEdges, nodeIndices, outputTypes, outputRegisters, diagnostics)
                    };
                    SetSource(document, sources, bodyIndex, node, op, GraphVmControlPorts.Enter);
                    EmitJumpToControlTarget(document, node, GraphVmControlPorts.Next, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources);
                    break;
                case GraphVmOpcode.BranchBool:
                case GraphVmOpcode.JumpIfFalse:
                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphVmOpcode.JumpIfFalse,
                        A = ResolveValueInput(node, GraphVmValuePorts.Condition, GraphVmValueType.Bool, valueEdges, nodeIndices, outputTypes, outputRegisters, diagnostics),
                        Imm = ResolveControlTarget(node, GraphVmControlPorts.False, controlEdges, nodeIndices, layouts)
                    };
                    SetSource(document, sources, bodyIndex, node, op, GraphVmControlPorts.Enter);
                    EmitJumpToControlTarget(document, node, GraphVmControlPorts.True, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources);
                    break;
                case GraphVmOpcode.Jump:
                    EmitJumpToControlTarget(document, node, GraphVmControlPorts.Target, bodyIndex, controlEdges, nodeIndices, layouts, program, sources);
                    break;
                case GraphVmOpcode.Call:
                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphVmOpcode.Call,
                        Imm = ResolveControlTarget(node, GraphVmControlPorts.Call, controlEdges, nodeIndices, layouts)
                    };
                    SetSource(document, sources, bodyIndex, node, op, GraphVmControlPorts.Call);
                    EmitJumpToControlTarget(document, node, GraphVmControlPorts.Next, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources);
                    break;
                case GraphVmOpcode.Return:
                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphVmOpcode.Return
                    };
                    SetSource(document, sources, bodyIndex, node, op, GraphVmControlPorts.Enter);
                    break;
                case GraphVmOpcode.ReturnInt:
                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphVmOpcode.ReturnInt,
                        A = ResolveValueInput(node, GraphVmValuePorts.Value, GraphVmValueType.Int, valueEdges, nodeIndices, outputTypes, outputRegisters, diagnostics)
                    };
                    SetSource(document, sources, bodyIndex, node, op, GraphVmControlPorts.Enter);
                    break;
                case GraphVmOpcode.Yield:
                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphVmOpcode.Yield
                    };
                    SetSource(document, sources, bodyIndex, node, op, GraphVmControlPorts.Enter);
                    EmitJumpToControlTarget(document, node, GraphVmControlPorts.Next, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources);
                    break;
                case GraphVmOpcode.Nop:
                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphVmOpcode.Nop
                    };
                    SetSource(document, sources, bodyIndex, node, op, GraphVmControlPorts.Enter);
                    EmitJumpToControlTarget(document, node, GraphVmControlPorts.Next, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources);
                    break;
                default:
                    diagnostics.Add(new GraphVmDiagnostic(
                        GraphVmDiagnosticSeverity.Error,
                        GraphVmDiagnosticCodes.UnknownOp,
                        $"GraphVM op '{op}' is not supported by the compiler.",
                        node.Id));
                    break;
            }
        }

        private static void EmitJumpToControlTarget(
            GraphVmDocument document,
            GraphVmNode node,
            string port,
            int instructionIndex,
            Dictionary<ControlKey, string> controlEdges,
            Dictionary<string, int> nodeIndices,
            NodeLayout[] layouts,
            GraphInstruction[] program,
            GraphInstructionSource[] sources)
        {
            program[instructionIndex] = new GraphInstruction
            {
                Op = (ushort)GraphVmOpcode.Jump,
                Imm = ResolveControlTarget(node, port, controlEdges, nodeIndices, layouts)
            };
            SetSource(document, sources, instructionIndex, node, GraphVmOpcode.Jump, port);
        }

        private static int ResolveControlTarget(
            GraphVmNode node,
            string port,
            Dictionary<ControlKey, string> controlEdges,
            Dictionary<string, int> nodeIndices,
            NodeLayout[] layouts)
        {
            string targetNode = controlEdges[new ControlKey(node.Id, port)];
            return layouts[nodeIndices[targetNode]].BodyIndex;
        }

        private static byte ResolveValueInput(
            GraphVmNode node,
            string port,
            GraphVmValueType expectedType,
            Dictionary<ValueInputKey, GraphVmValueEdge> valueEdges,
            Dictionary<string, int> nodeIndices,
            GraphVmValueType[] outputTypes,
            byte[] outputRegisters,
            List<GraphVmDiagnostic> diagnostics)
        {
            if (!valueEdges.TryGetValue(new ValueInputKey(node.Id, port), out GraphVmValueEdge edge))
            {
                diagnostics.Add(new GraphVmDiagnostic(
                    GraphVmDiagnosticSeverity.Error,
                    GraphVmDiagnosticCodes.MissingValueInput,
                    $"GraphVM node '{node.Id}' requires value input '{port}'.",
                    node.Id));
                return 0;
            }

            int sourceIndex = nodeIndices[edge.From];
            GraphVmValueType actualType = outputTypes[sourceIndex];
            if (actualType != expectedType)
            {
                diagnostics.Add(new GraphVmDiagnostic(
                    GraphVmDiagnosticSeverity.Error,
                    GraphVmDiagnosticCodes.TypeMismatch,
                    $"GraphVM node '{node.Id}' input '{port}' expected {expectedType} but got {actualType} from '{edge.From}'.",
                    node.Id));
                return 0;
            }

            return outputRegisters[sourceIndex];
        }

        private static void RequireControlEdge(
            GraphVmNode node,
            string port,
            Dictionary<ControlKey, string> controlEdges,
            List<GraphVmDiagnostic> diagnostics)
        {
            if (!controlEdges.ContainsKey(new ControlKey(node.Id, port)))
            {
                diagnostics.Add(new GraphVmDiagnostic(
                    GraphVmDiagnosticSeverity.Error,
                    GraphVmDiagnosticCodes.MissingControlEdge,
                    $"GraphVM node '{node.Id}' requires control edge '{port}'.",
                    node.Id));
            }
        }

        private static void RequireValueInput(
            GraphVmNode node,
            string port,
            GraphVmValueType expectedType,
            Dictionary<ValueInputKey, GraphVmValueEdge> valueEdges,
            Dictionary<string, int> nodeIndices,
            GraphVmValueType[] outputTypes,
            List<GraphVmDiagnostic> diagnostics)
        {
            if (!valueEdges.TryGetValue(new ValueInputKey(node.Id, port), out GraphVmValueEdge edge))
            {
                diagnostics.Add(new GraphVmDiagnostic(
                    GraphVmDiagnosticSeverity.Error,
                    GraphVmDiagnosticCodes.MissingValueInput,
                    $"GraphVM node '{node.Id}' requires value input '{port}'.",
                    node.Id));
                return;
            }

            int sourceIndex = nodeIndices[edge.From];
            GraphVmValueType actualType = outputTypes[sourceIndex];
            if (actualType != expectedType)
            {
                diagnostics.Add(new GraphVmDiagnostic(
                    GraphVmDiagnosticSeverity.Error,
                    GraphVmDiagnosticCodes.TypeMismatch,
                    $"GraphVM node '{node.Id}' input '{port}' expected {expectedType} but got {actualType} from '{edge.From}'.",
                    node.Id));
            }
        }

        private static void SetSource(
            GraphVmDocument document,
            GraphInstructionSource[] sources,
            int instructionIndex,
            GraphVmNode node,
            GraphVmOpcode op,
            string controlPort)
        {
            sources[instructionIndex] = new GraphInstructionSource(document.Id, node.Id, op.ToString(), controlPort);
        }

        private static int GetInstructionCount(GraphVmOpcode op)
        {
            return op switch
            {
                GraphVmOpcode.Return => 1,
                GraphVmOpcode.ReturnInt => 1,
                GraphVmOpcode.Jump => 1,
                GraphVmOpcode.BranchBool => 2,
                GraphVmOpcode.JumpIfFalse => 2,
                GraphVmOpcode.Call => 2,
                _ => 2
            };
        }

        private static GraphVmValueType GetOutputType(GraphVmOpcode op)
        {
            return op switch
            {
                GraphVmOpcode.ConstInt => GraphVmValueType.Int,
                GraphVmOpcode.LoadInt => GraphVmValueType.Int,
                GraphVmOpcode.AddInt => GraphVmValueType.Int,
                GraphVmOpcode.LessThanInt => GraphVmValueType.Bool,
                _ => GraphVmValueType.Void
            };
        }

        private static bool IsValidControlPort(GraphVmOpcode op, string port)
        {
            return op switch
            {
                GraphVmOpcode.Return or GraphVmOpcode.ReturnInt => false,
                GraphVmOpcode.Jump => string.Equals(port, GraphVmControlPorts.Target, StringComparison.Ordinal),
                GraphVmOpcode.BranchBool or GraphVmOpcode.JumpIfFalse =>
                    string.Equals(port, GraphVmControlPorts.True, StringComparison.Ordinal) ||
                    string.Equals(port, GraphVmControlPorts.False, StringComparison.Ordinal),
                GraphVmOpcode.Call =>
                    string.Equals(port, GraphVmControlPorts.Call, StringComparison.Ordinal) ||
                    string.Equals(port, GraphVmControlPorts.Next, StringComparison.Ordinal),
                _ => string.Equals(port, GraphVmControlPorts.Next, StringComparison.Ordinal)
            };
        }

        private static bool IsValidValueInputPort(GraphVmOpcode op, string port)
        {
            return op switch
            {
                GraphVmOpcode.StoreInt or GraphVmOpcode.ReturnInt =>
                    string.Equals(port, GraphVmValuePorts.Value, StringComparison.Ordinal),
                GraphVmOpcode.AddInt or GraphVmOpcode.LessThanInt =>
                    string.Equals(port, GraphVmValuePorts.A, StringComparison.Ordinal) ||
                    string.Equals(port, GraphVmValuePorts.B, StringComparison.Ordinal),
                GraphVmOpcode.BranchBool or GraphVmOpcode.JumpIfFalse =>
                    string.Equals(port, GraphVmValuePorts.Condition, StringComparison.Ordinal),
                _ => false
            };
        }

        private static byte AllocInt(ref int next, string nodeId, List<GraphVmDiagnostic> diagnostics)
        {
            if (next >= GraphVmRuntimeLimits.MaxIntRegisters)
            {
                diagnostics.Add(new GraphVmDiagnostic(
                    GraphVmDiagnosticSeverity.Error,
                    GraphVmDiagnosticCodes.RegisterOutOfRange,
                    $"GraphVM int register budget exceeded ({GraphVmRuntimeLimits.MaxIntRegisters}).",
                    nodeId));
                return 0;
            }

            return (byte)next++;
        }

        private static byte AllocBool(ref int next, string nodeId, List<GraphVmDiagnostic> diagnostics)
        {
            if (next >= GraphVmRuntimeLimits.MaxBoolRegisters)
            {
                diagnostics.Add(new GraphVmDiagnostic(
                    GraphVmDiagnosticSeverity.Error,
                    GraphVmDiagnosticCodes.RegisterOutOfRange,
                    $"GraphVM bool register budget exceeded ({GraphVmRuntimeLimits.MaxBoolRegisters}).",
                    nodeId));
                return 0;
            }

            return (byte)next++;
        }

        private static void ReserveIntSlot(byte slot, ref int next, string nodeId, List<GraphVmDiagnostic> diagnostics)
        {
            if (slot >= GraphVmRuntimeLimits.MaxIntRegisters)
            {
                diagnostics.Add(new GraphVmDiagnostic(
                    GraphVmDiagnosticSeverity.Error,
                    GraphVmDiagnosticCodes.RegisterOutOfRange,
                    $"GraphVM int register slot {slot} is out of range.",
                    nodeId));
                return;
            }

            if (slot >= next)
            {
                next = slot + 1;
            }
        }
    }
}
