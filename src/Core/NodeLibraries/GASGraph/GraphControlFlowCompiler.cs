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
        /// <summary>The authored document this result compiled from (#1124 weave input).</summary>
        public GraphControlFlowDocument? Document { get; init; }
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
    /// Wait is an author alias for <see cref="GraphNodeOp.Yield"/> (Script and TriggerGraph).
    /// </summary>
    public static partial class GraphControlFlowCompiler
    {
        public const string BranchBoolOp = GraphAuthoringSugar.BranchBool;
        public const string SwitchIntOp = GraphAuthoringSugar.SwitchInt;
        public const string SelectByEnumOp = GraphAuthoringSugar.SelectByEnum;
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
            public SwitchCaseArm(int caseValue, string authoredPort, string targetNodeId)
            {
                CaseValue = caseValue;
                AuthoredPort = authoredPort;
                TargetNodeId = targetNodeId;
            }

            public int CaseValue { get; }
            /// <summary>The authored port spelling ("case:Combat" when enum-bound, else "case:{n}") — source-map label.</summary>
            public string AuthoredPort { get; }
            public string TargetNodeId { get; }
        }

        /// <summary>One SelectByEnum candidate: the bound member's value plus the authored case port it wires from.</summary>
        private readonly struct SelectCandidateArm
        {
            public SelectCandidateArm(int memberValue, string authoredPort)
            {
                MemberValue = memberValue;
                AuthoredPort = authoredPort;
            }

            public int MemberValue { get; }
            public string AuthoredPort { get; }
        }

        /// <summary>
        /// Per-compilation resolution of enum-bound case ports (#1125): authored
        /// case:{memberName} ports → declaration-order ints, plus the reverse map that keeps
        /// the authored member spelling for instruction source labels. Built once from the
        /// document + EnumCatalog; unregistered enumType, unknown member names, and raw int
        /// literals under an enumType all fail closed here.
        /// </summary>
        private sealed class EnumCaseTable
        {
            private readonly Dictionary<ControlKey, int> _memberValues = new();
            private readonly Dictionary<(string NodeId, int Value), string> _authoredPorts = new();
            private readonly HashSet<string> _enumBoundNodes = new(StringComparer.Ordinal);

            public static readonly EnumCaseTable Empty = new();

            public bool IsEnumBound(string nodeId)
                => _enumBoundNodes.Contains(nodeId);

            public void MarkEnumBound(string nodeId)
                => _enumBoundNodes.Add(nodeId);

            public void Record(string nodeId, string authoredPort, int memberValue)
            {
                _memberValues[new ControlKey(nodeId, authoredPort)] = memberValue;
                _authoredPorts[(nodeId, memberValue)] = authoredPort;
            }

            /// <summary>Authored case port → member int. Int-literal ports parse before this table.</summary>
            public bool TryResolveMemberPort(string nodeId, string port, out int memberValue)
                => _memberValues.TryGetValue(new ControlKey(nodeId, port), out memberValue);

            /// <summary>Source-map label for a resolved case value: the authored member port when enum-bound.</summary>
            public string AuthoredPortOrNumeric(string nodeId, int caseValue)
                => _authoredPorts.TryGetValue((nodeId, caseValue), out string? authored)
                    ? authored
                    : GraphControlFlowPorts.Case(caseValue);
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
            Until = 4,
            BtSequence = 5,
            BtSelector = 6,
            BtDecorator = 7,
            SelectByEnum = 8,
            FsmState = 9
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
            => CompileCore(document, eventSchemas: null, enums: null) with { Document = document };

        /// <summary>
        /// Compiles with the event schema SSOT in scope (#1115): DispatchMapEvent nodes
        /// validate their event name, dispatch scope, and per-parameter payload ports
        /// against it. Hosts that never author DispatchMapEvent may compile without it.
        /// </summary>
        public static GraphControlFlowCompileResult Compile(GraphControlFlowDocument document, Ludots.Core.Scripting.EventSchemaRegistry? eventSchemas)
            => CompileCore(document, eventSchemas, enums: null) with { Document = document };

        /// <summary>
        /// Compiles with the enum catalog in scope (#1125): SwitchInt nodes bound to an
        /// <c>enumType</c> and SelectByEnum nodes resolve their case:{memberName} ports to
        /// declaration-order ints here; unregistered types and unknown members fail closed.
        /// Hosts that never author enum-bound sugar may compile without it — but an authored
        /// enumType without a catalog is itself fail-closed, not silently ignored.
        /// </summary>
        public static GraphControlFlowCompileResult Compile(
            GraphControlFlowDocument document,
            Ludots.Core.Scripting.EventSchemaRegistry? eventSchemas,
            Ludots.Core.Scripting.EnumCatalog? enums)
            => CompileCore(document, eventSchemas, enums) with { Document = document };

        public static (GraphProgramPackage? Package, GraphOutputSchema OutputSchema, List<GraphDiagnostic> Diagnostics) CompileWithOutputs(
            GraphControlFlowDocument document)
        {
            GraphControlFlowCompileResult result = CompileCore(document, eventSchemas: null, enums: null);
            return (result.Package, result.OutputSchema, result.Diagnostics);
        }

        private static GraphControlFlowCompileResult CompileCore(
            GraphControlFlowDocument document,
            Ludots.Core.Scripting.EventSchemaRegistry? eventSchemas,
            Ludots.Core.Scripting.EnumCatalog? enums)
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
            EnumCaseTable enumCases = BuildEnumCaseTable(document, nodes, ops, enums, graphId, diagnostics);

            Dictionary<string, Ludots.Core.Scripting.EventSchema> dispatchSchemas =
                BuildDispatchEventSchemas(nodes, graphKind, eventSchemas, graphId, diagnostics);

            List<TriggerGraphEntryConfig> triggerGraphEntries = ValidateTriggerGraphEntries(
                document, nodeIndices, graphKind, graphId, diagnostics);

            if (graphKind != GraphKind.TriggerGraph &&
                !string.IsNullOrWhiteSpace(document.Entry) &&
                !nodeIndices.ContainsKey(document.Entry))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                    $"{GraphKindLabel(graphKind)} entry node '{document.Entry}' does not exist.", document.Entry));
            }

            Dictionary<ControlKey, string> controlEdges = BuildControlEdges(document, nodeIndices, ops, graphId, diagnostics, enumCases);
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges = BuildValueEdges(document, nodeIndices, graphId, diagnostics);

            var outputTypes = new GraphValueType[nodes.Count];
            var outputRegisters = new byte[nodes.Count];
            var boolScratches = new byte[nodes.Count];
            var droppedRegisters = new byte[nodes.Count];
            GraphRegisterFile registers = GraphRegisterFile.Create(graphKind);
            AllocateOutputs(nodes, ops, outputTypes, outputRegisters, registers, graphId, diagnostics);
            AllocateOpScratches(nodes, ops, boolScratches, registers, graphId, diagnostics);
            AllocateDroppedOutputs(nodes, ops, outputRegisters, droppedRegisters, registers, graphId, diagnostics);
            BtSugarPlan? btPlan = AnalyzeBtSugar(
                document, nodes, ops, nodeIndices, controlEdges, outputTypes, graphId, diagnostics);
            ValidateRequiredEdges(nodes, ops, graphKind, controlEdges, valueEdges, nodeIndices, outputTypes, graphId, diagnostics, dispatchSchemas, btPlan, enumCases);
            DetectUnreachable(EntryRoots(document, graphKind, triggerGraphEntries), nodes, document.ControlEdges, graphId, diagnostics);

            var sugarScratches = new SugarScratch[nodes.Count];
            AllocateSugarScratches(nodes, ops, sugarScratches, registers, graphId, diagnostics, btPlan);
            NodeLayout[] layouts = BuildLayouts(nodes, ops, graphKind, controlEdges, valueEdges, dispatchSchemas, diagnostics, btPlan, enumCases);
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
            // TriggerGraph graphs dispatch through per-entry PCs; the prefix targets the first entry's start node.
            string primaryEntryNodeId = graphKind == GraphKind.TriggerGraph ? triggerGraphEntries[0].Start : document.Entry;
            int entryBody = layouts[nodeIndices[primaryEntryNodeId]].BodyIndex + 1; // +1 for prefix jump slot
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
            sources[0] = new GraphInstructionSource(graphId, primaryEntryNodeId, nameof(GraphNodeOp.Jump), GraphControlFlowPorts.Enter);

            TriggerGraphEntry[] compiledEntries = CompileTriggerGraphEntryTable(
                triggerGraphEntries, nodeIndices, layouts, graphKind);

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
                    boolScratches,
                    droppedRegisters,
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
                    diagnostics,
                    dispatchSchemas,
                    btPlan,
                    enumCases);
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
            var package = new GraphProgramPackage(graphId, symbols.ToArray(), program, graphKind, compiledEntries);
            return new GraphControlFlowCompileResult(
                program,
                sourceMap,
                diagnostics,
                package,
                outputSchema);
        }

        private static TriggerGraphEntry[] CompileTriggerGraphEntryTable(
            List<TriggerGraphEntryConfig> validatedEntries,
            Dictionary<string, int> nodeIndices,
            NodeLayout[] layouts,
            GraphKind graphKind)
        {
            if (graphKind != GraphKind.TriggerGraph)
            {
                return Array.Empty<TriggerGraphEntry>();
            }

            var compiled = new TriggerGraphEntry[validatedEntries.Count];
            for (int i = 0; i < validatedEntries.Count; i++)
            {
                TriggerGraphEntryConfig entry = validatedEntries[i];
                compiled[i] = new TriggerGraphEntry(
                    entry.Label,
                    entry.Event,
                    layouts[nodeIndices[entry.Start]].BodyIndex,
                    entry.Once,
                    entry.ParsedFilters,
                    entry.NormalizedRefire,
                    entry.Priority,
                    entry.ParsedHook != null);
            }

            return compiled;
        }

        /// <summary>
        /// Resolves every DispatchMapEvent node's event against the EventSchemaRegistry and
        /// checks its dispatch scope (#1115 / #1123): "map" requires a Map-scope schema,
        /// "self" an Entity-scope schema, "global" a Global-scope schema; any mismatch
        /// fails closed. Returns the nodeId → schema map used for port validation and emit.
        /// </summary>
        private static Dictionary<string, Ludots.Core.Scripting.EventSchema> BuildDispatchEventSchemas(
            List<GraphControlFlowNode> nodes,
            GraphKind graphKind,
            Ludots.Core.Scripting.EventSchemaRegistry? eventSchemas,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            var schemas = new Dictionary<string, Ludots.Core.Scripting.EventSchema>(StringComparer.Ordinal);
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!string.Equals(nodes[i].Op, nameof(GraphNodeOp.DispatchMapEvent), StringComparison.Ordinal))
                {
                    continue;
                }

                if (graphKind != GraphKind.TriggerGraph)
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                        $"DispatchMapEvent node '{nodes[i].Id}' is TriggerGraph-only; found in kind '{graphKind}'.", nodes[i].Id));
                    continue;
                }

                if (eventSchemas == null)
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                        $"DispatchMapEvent node '{nodes[i].Id}' requires an EventSchemaRegistry compile context; this host did not provide one.", nodes[i].Id));
                    continue;
                }

                string eventName = (nodes[i].Event ?? string.Empty).Trim();
                if (eventName.Length == 0)
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                        $"DispatchMapEvent node '{nodes[i].Id}' requires a non-empty event.", nodes[i].Id));
                    continue;
                }

                if (!eventSchemas.TryGet(eventName, out Ludots.Core.Scripting.EventSchema schema))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                        $"DispatchMapEvent node '{nodes[i].Id}' event '{eventName}' has no registered schema.", nodes[i].Id));
                    continue;
                }

                string scope = (nodes[i].Scope ?? "map").Trim().ToLowerInvariant();
                if (scope != "map" && scope != "self" && scope != "global")
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                        $"DispatchMapEvent node '{nodes[i].Id}' scope '{nodes[i].Scope}' must be \"map\", \"self\", or \"global\".", nodes[i].Id));
                    continue;
                }

                Ludots.Core.Scripting.EventScope expected = scope switch
                {
                    "self" => Ludots.Core.Scripting.EventScope.Entity,
                    "global" => Ludots.Core.Scripting.EventScope.Global,
                    _ => Ludots.Core.Scripting.EventScope.Map,
                };
                if (schema.Scope != expected)
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                        $"DispatchMapEvent node '{nodes[i].Id}' scope '{scope}' does not match event '{eventName}' declared scope '{schema.Scope}'.", nodes[i].Id));
                    continue;
                }

                schemas[nodes[i].Id] = schema;
            }

            return schemas;
        }

        private static string[] EntryRoots(
            GraphControlFlowDocument document,
            GraphKind graphKind,
            List<TriggerGraphEntryConfig> validatedEntries)
        {
            if (graphKind != GraphKind.TriggerGraph)
            {
                return string.IsNullOrWhiteSpace(document.Entry)
                    ? Array.Empty<string>()
                    : new[] { document.Entry };
            }

            var roots = new string[validatedEntries.Count];
            for (int i = 0; i < validatedEntries.Count; i++)
            {
                roots[i] = validatedEntries[i].Start;
            }

            return roots;
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

            if (graphKind == GraphKind.TriggerGraph)
            {
                if (!string.IsNullOrWhiteSpace(document.Entry))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.ForbiddenEntryTable,
                        $"TriggerGraph graph '{graphId}' must not declare a single 'entry' start node; author the 'entries' table instead.",
                        document.Entry));
                }

                if (document.Entries == null || document.Entries.Count == 0)
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingEntry,
                        $"TriggerGraph graph '{graphId}' requires a non-empty 'entries' table."));
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(document.Entry))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingEntry,
                        $"{GraphKindLabel(graphKind)} document requires an entry node id."));
                }

                if (document.Entries != null && document.Entries.Count > 0)
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.ForbiddenEntryTable,
                        $"Graph '{graphId}' kind '{graphKind}' must not declare 'entries'; the entry table is TriggerGraph-only."));
                }
            }

            if (document.Nodes == null || document.Nodes.Count == 0)
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.EmptyGraph,
                    $"{GraphKindLabel(graphKind)} document requires at least one node."));
            }
        }

        private static List<TriggerGraphEntryConfig> ValidateTriggerGraphEntries(
            GraphControlFlowDocument document,
            Dictionary<string, int> nodeIndices,
            GraphKind graphKind,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            if (graphKind != GraphKind.TriggerGraph)
            {
                return new List<TriggerGraphEntryConfig>();
            }

            List<TriggerGraphEntryConfig> authored = document.Entries ?? new List<TriggerGraphEntryConfig>();
            var validated = new List<TriggerGraphEntryConfig>(authored.Count);
            var seenLabels = new HashSet<string>(StringComparer.Ordinal);
            ValidateNodeAnchors(document.Nodes ?? new List<GraphControlFlowNode>(), graphId, diagnostics);
            for (int i = 0; i < authored.Count; i++)
            {
                if (authored[i] == null)
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingEntry,
                        $"TriggerGraph graph '{graphId}' entries[{i}] is null."));
                    continue;
                }

                string label = (authored[i].Label ?? string.Empty).Trim();
                string shown = label.Length > 0 ? label : $"entries[{i}]";
                if (label.Length == 0)
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingEntry,
                        $"TriggerGraph graph '{graphId}' entries[{i}] requires a non-empty 'label'."));
                }
                else if (!seenLabels.Add(label))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.DuplicateEntryLabel,
                        $"TriggerGraph graph '{graphId}' has duplicate entry label '{label}'.", label));
                }

                string eventName = (authored[i].Event ?? string.Empty).Trim();
                if (eventName.Length == 0)
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingEntry,
                        $"TriggerGraph graph '{graphId}' entry '{shown}' requires a non-empty 'event' string."));
                }

                string start = (authored[i].Start ?? string.Empty).Trim();
                if (start.Length == 0)
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                        $"TriggerGraph graph '{graphId}' entry '{shown}' requires a 'start' node id."));
                }
                else if (!nodeIndices.ContainsKey(start))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                        $"TriggerGraph graph '{graphId}' entry '{shown}' start node '{start}' does not exist.", start));
                }

                validated.Add(new TriggerGraphEntryConfig
                {
                    Label = label,
                    Event = eventName,
                    Start = start,
                    Once = authored[i].Once,
                    Priority = authored[i].Priority,
                    NormalizedRefire = NormalizeEntryRefire(authored[i].Refire, graphId, shown, diagnostics),
                    ParsedFilters = ParseEntryFilters(authored[i].Filters, graphId, shown, diagnostics),
                    ParsedHook = ParseEntryHook(authored[i], graphId, shown, diagnostics)
                });
            }

            return validated;
        }


        /// <summary>
        /// Normalizes a hook target (#1124): an entry with a hookAnchor / hookNodeBefore /
        /// hookNodeAfter block is a fragment woven into the target graph at compile time;
        /// at most one hook block per entry fails closed. Anchor-name conflicts across a
        /// graph's nodes are diagnosed here as well — anchor ids are the cross-mod contract.
        /// </summary>
        private static TriggerGraphHookTargetConfig? ParseEntryHook(
            TriggerGraphEntryConfig authored,
            string graphId,
            string shown,
            List<GraphDiagnostic> diagnostics)
        {
            int hookBlocks = (authored.HookAnchor != null ? 1 : 0) +
                (authored.HookNodeBefore != null ? 1 : 0) +
                (authored.HookNodeAfter != null ? 1 : 0);
            if (hookBlocks > 1)
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.InvalidEntryFilters,
                    $"TriggerGraph graph '{graphId}' entry '{shown}' declares more than one hook block; " +
                    "combine exactly one of hookAnchor / hookNodeBefore / hookNodeAfter.", shown));
                return null;
            }

            string context = $"TriggerGraph graph '{graphId}' entry '{shown}'";
            if (authored.HookAnchor != null)
            {
                if (!TriggerGraphHookTargetConfig.TryParseAnchor(
                        authored.HookAnchor.GraphId,
                        authored.HookAnchor.Anchor,
                        authored.HookAnchor.Position,
                        context,
                        out TriggerGraphHookTargetConfig? parsed,
                        out string? error))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.InvalidEntryHook, error!, shown));
                    return null;
                }

                return parsed;
            }

            if (authored.HookNodeBefore != null || authored.HookNodeAfter != null)
            {
                TriggerGraphHookNodeConfig nodeHook = authored.HookNodeBefore ?? authored.HookNodeAfter!;
                if (!TriggerGraphHookTargetConfig.TryParseNode(
                        nodeHook.GraphId,
                        nodeHook.NodeId,
                        authored.HookNodeBefore != null ? "before" : "after",
                        context,
                        out TriggerGraphHookTargetConfig? parsed,
                        out string? error))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.InvalidEntryHook, error!, shown));
                    return null;
                }

                return parsed;
            }

            return null;
        }

        private static void ValidateNodeAnchors(
            List<GraphControlFlowNode> nodes,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < nodes.Count; i++)
            {
                string? anchor = nodes[i].Anchor;
                if (string.IsNullOrEmpty(anchor))
                {
                    continue;
                }

                string trimmed = anchor.Trim();
                if (trimmed.Length == 0)
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.InvalidEntryHook,
                        $"Graph '{graphId}' node '{nodes[i].Id}' anchor must be a non-empty string.", nodes[i].Id));
                    continue;
                }

                if (!seen.Add(trimmed))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.DuplicateAnchor,
                        $"Graph '{graphId}' has duplicate anchor '{trimmed}'; anchor names must be unique " +
                        "because they are the cross-mod hook contract (#1124).", nodes[i].Id));
                }
            }
        }

        private static string NormalizeEntryRefire(
            string? authored,
            string graphId,
            string shown,
            List<GraphDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(authored))
            {
                return TriggerGraphEntry.RefireIgnore;
            }

            string trimmed = authored.Trim();
            if (trimmed != TriggerGraphEntry.RefireIgnore && trimmed != TriggerGraphEntry.RefireRestart)
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.InvalidEntryRefire,
                    $"TriggerGraph graph '{graphId}' entry '{shown}' field 'refire' must be \"ignore\" or \"restart\".", authored));
                return TriggerGraphEntry.RefireIgnore;
            }

            return trimmed;
        }

        private static TriggerGraphEntryFilters ParseEntryFilters(
            TriggerGraphEntryFiltersConfig? filters,
            string graphId,
            string shown,
            List<GraphDiagnostic> diagnostics)
        {
            if (filters == null)
            {
                return default;
            }

            string? region = null;
            if (filters.Region != null)
            {
                region = filters.Region.Trim();
                if (region.Length == 0)
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.InvalidEntryFilters,
                        $"TriggerGraph graph '{graphId}' entry '{shown}' filters field 'region' requires a non-empty string.", filters.Region));
                }
            }

            string? tag = null;
            if (filters.Tag != null)
            {
                tag = filters.Tag.Trim();
                if (tag.Length == 0)
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.InvalidEntryFilters,
                        $"TriggerGraph graph '{graphId}' entry '{shown}' filters field 'tag' requires a non-empty string.", filters.Tag));
                }
            }

            TriggerGraphEntryFilterDirection? direction = null;
            if (filters.Direction != null)
            {
                string directionText = filters.Direction.Trim();
                if (string.Equals(directionText, "cross_above", StringComparison.Ordinal))
                {
                    direction = TriggerGraphEntryFilterDirection.CrossAbove;
                }
                else if (string.Equals(directionText, "cross_below", StringComparison.Ordinal))
                {
                    direction = TriggerGraphEntryFilterDirection.CrossBelow;
                }
                else
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.InvalidEntryFilters,
                        $"TriggerGraph graph '{graphId}' entry '{shown}' filters field 'direction' must be 'cross_above' or 'cross_below' (got '{directionText}').", filters.Direction));
                }
            }

            if (filters.Threshold.HasValue != direction.HasValue)
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.InvalidEntryFilters,
                    $"TriggerGraph graph '{graphId}' entry '{shown}' filters fields 'threshold' and 'direction' must be declared together."));
            }

            string? action = null;
            if (filters.Action != null)
            {
                action = filters.Action.Trim();
                if (action.Length == 0)
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.InvalidEntryFilters,
                        $"TriggerGraph graph '{graphId}' entry '{shown}' filters field 'action' must be a non-empty action id.", filters.Action));
                }
            }

            string? instanceId = null;
            if (filters.InstanceId != null)
            {
                instanceId = filters.InstanceId.Trim();
                if (instanceId.Length == 0)
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.InvalidEntryFilters,
                        $"TriggerGraph graph '{graphId}' entry '{shown}' filters field 'instanceId' requires a non-empty placed instance id.", filters.InstanceId));
                }
            }

            string? varName = null;
            if (filters.VarName != null)
            {
                varName = filters.VarName.Trim();
                if (varName.Length == 0)
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.InvalidEntryFilters,
                        $"TriggerGraph graph '{graphId}' entry '{shown}' filters field 'varName' requires a non-empty map variable name.", filters.VarName));
                }
            }

            return new TriggerGraphEntryFilters(region, tag, filters.Team, filters.Threshold, direction, action, instanceId, null, varName);
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
                    if (!IsBranchBoolAuthorable(graphKind))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                            $"{BranchBoolOp} is Script/Effect/TriggerGraph compile-time sugar only.", node.Id));
                        ops[i] = new AuthoredOp(AuthoredOpKind.GraphNodeOp, GraphNodeOp.None);
                        continue;
                    }

                    ops[i] = new AuthoredOp(AuthoredOpKind.BranchBool, GraphNodeOp.None);
                    continue;
                }

                if (string.Equals(node.Op, SwitchIntOp, StringComparison.Ordinal))
                {
                    if (graphKind is not (GraphKind.Script or GraphKind.TriggerGraph))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                            $"{SwitchIntOp} is Script/TriggerGraph compile-time sugar only.", node.Id));
                        ops[i] = new AuthoredOp(AuthoredOpKind.GraphNodeOp, GraphNodeOp.None);
                        continue;
                    }

                    ops[i] = new AuthoredOp(AuthoredOpKind.SwitchInt, GraphNodeOp.None);
                    continue;
                }

                if (string.Equals(node.Op, SelectByEnumOp, StringComparison.Ordinal))
                {
                    if (graphKind is not (GraphKind.Script or GraphKind.TriggerGraph))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                            $"{SelectByEnumOp} is Script/TriggerGraph compile-time sugar only.", node.Id));
                        ops[i] = new AuthoredOp(AuthoredOpKind.GraphNodeOp, GraphNodeOp.None);
                        continue;
                    }

                    ops[i] = new AuthoredOp(AuthoredOpKind.SelectByEnum, GraphNodeOp.None);
                    continue;
                }

                // FSM dispatch container sugar (GraphControlFlowCompiler.Fsm.cs): same
                // Script/TriggerGraph gating as SwitchInt; never a GraphNodeOp value.
                if (string.Equals(node.Op, GraphAuthoringSugar.FsmState, StringComparison.Ordinal))
                {
                    if (graphKind is not (GraphKind.Script or GraphKind.TriggerGraph))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                            $"{GraphAuthoringSugar.FsmState} is Script/TriggerGraph compile-time sugar only.", node.Id));
                        ops[i] = new AuthoredOp(AuthoredOpKind.GraphNodeOp, GraphNodeOp.None);
                        continue;
                    }

                    ops[i] = new AuthoredOp(AuthoredOpKind.FsmState, GraphNodeOp.None);
                    continue;
                }

                if (string.Equals(node.Op, WhileOp, StringComparison.Ordinal))
                {
                    if (graphKind is not (GraphKind.Script or GraphKind.TriggerGraph))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                            $"While is Script/TriggerGraph-only author sugar (kind='{graphKind}').", node.Id));
                        ops[i] = new AuthoredOp(AuthoredOpKind.GraphNodeOp, GraphNodeOp.None);
                        continue;
                    }

                    ops[i] = new AuthoredOp(AuthoredOpKind.While, GraphNodeOp.None);
                    continue;
                }

                if (string.Equals(node.Op, UntilOp, StringComparison.Ordinal))
                {
                    if (graphKind is not (GraphKind.Script or GraphKind.TriggerGraph))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                            $"Until is Script/TriggerGraph-only author sugar (kind='{graphKind}').", node.Id));
                        ops[i] = new AuthoredOp(AuthoredOpKind.GraphNodeOp, GraphNodeOp.None);
                        continue;
                    }

                    ops[i] = new AuthoredOp(AuthoredOpKind.Until, GraphNodeOp.None);
                    continue;
                }

                if (string.Equals(node.Op, GraphAuthoringSugar.Break, StringComparison.Ordinal))
                {
                    if (graphKind is not (GraphKind.Script or GraphKind.TriggerGraph))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                            $"{GraphAuthoringSugar.Break} is Script/TriggerGraph compile-time sugar only.", node.Id));
                        ops[i] = new AuthoredOp(AuthoredOpKind.GraphNodeOp, GraphNodeOp.None);
                        continue;
                    }

                    // Break is an author-facing name for an unconditional jump to an
                    // explicitly authored exit. There is no implicit loop-scope lookup;
                    // the target edge keeps control flow visible and rejects dangling breaks.
                    ops[i] = new AuthoredOp(AuthoredOpKind.GraphNodeOp, GraphNodeOp.Jump);
                    continue;
                }

                // Wait is author alias for Yield — Script/TriggerGraph CF only; never a second waiter opcode.
                if (string.Equals(node.Op, WaitOp, StringComparison.Ordinal))
                {
                    if (graphKind is not (GraphKind.Script or GraphKind.TriggerGraph))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                            $"Wait is Script/TriggerGraph-only author alias for Yield (kind='{graphKind}').", node.Id));
                        ops[i] = new AuthoredOp(AuthoredOpKind.GraphNodeOp, GraphNodeOp.None);
                        continue;
                    }

                    ops[i] = new AuthoredOp(AuthoredOpKind.GraphNodeOp, GraphNodeOp.Yield);
                    continue;
                }

                // BT composition sugar: Script-only. The whole tree inlines into one program
                // (see GraphControlFlowCompiler.Bt.cs); these names never become GraphNodeOp values.
                if (GraphAuthoringSugar.IsBtSugar(node.Op))
                {
                    if (graphKind != GraphKind.Script)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                            $"{node.Op} is Script-only BT compile-time sugar (kind='{graphKind}').", node.Id));
                        ops[i] = new AuthoredOp(AuthoredOpKind.GraphNodeOp, GraphNodeOp.None);
                        continue;
                    }

                    ops[i] = new AuthoredOp(
                        node.Op switch
                        {
                            GraphAuthoringSugar.BtSequence => AuthoredOpKind.BtSequence,
                            GraphAuthoringSugar.BtSelector => AuthoredOpKind.BtSelector,
                            _ => AuthoredOpKind.BtDecorator
                        },
                        GraphNodeOp.None);
                    continue;
                }

                bool parsedNodeOp = GraphNodeOpParser.TryParse(node.Op, out GraphNodeOp nodeOp);
                if (!parsedNodeOp ||
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

        private static bool IsBranchBoolAuthorable(GraphKind graphKind)
            => graphKind is GraphKind.Script or GraphKind.Effect or GraphKind.TriggerGraph;

        private static bool IsControlFlowAuthorable(GraphKind graphKind, GraphNodeOp op)
        {
            return GraphOpDescriptorTable.IsAuthorable(graphKind, op);
        }

        private static string GraphKindLabel(GraphKind graphKind)
            => graphKind.ToString();

        /// <summary>
        /// Resolves enum bindings for SwitchInt case arms (control edges) and SelectByEnum
        /// candidates (value inputs) before the generic edge passes run. Ports keep their
        /// authored spelling in the document; the table is the only place name→int happens.
        /// </summary>
        private static EnumCaseTable BuildEnumCaseTable(
            GraphControlFlowDocument document,
            List<GraphControlFlowNode> nodes,
            AuthoredOp[] ops,
            Ludots.Core.Scripting.EnumCatalog? enums,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            bool anyBound = false;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (ops[i].Kind is not (AuthoredOpKind.SwitchInt or AuthoredOpKind.SelectByEnum or AuthoredOpKind.FsmState) ||
                    string.IsNullOrWhiteSpace(nodes[i].EnumType))
                {
                    continue;
                }

                anyBound = true;
                break;
            }

            if (!anyBound)
            {
                return EnumCaseTable.Empty;
            }

            var table = new EnumCaseTable();
            for (int i = 0; i < nodes.Count; i++)
            {
                AuthoredOpKind kind = ops[i].Kind;
                if (kind is not (AuthoredOpKind.SwitchInt or AuthoredOpKind.SelectByEnum or AuthoredOpKind.FsmState) ||
                    string.IsNullOrWhiteSpace(nodes[i].EnumType))
                {
                    continue;
                }

                GraphControlFlowNode node = nodes[i];
                string enumType = node.EnumType!.Trim();
                table.MarkEnumBound(node.Id);
                if (enums == null || !enums.TryGet(enumType, out Ludots.Core.Scripting.EnumSchema? schema))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                        $"Node '{node.Id}' enumType '{enumType}' is not registered in the enum catalog " +
                        $"({Ludots.Core.Scripting.EnumCatalogLoader.ConfigPath}).", node.Id));
                    continue;
                }

                if (kind == AuthoredOpKind.SwitchInt || kind == AuthoredOpKind.FsmState)
                {
                    List<GraphControlFlowEdge> edges = document.ControlEdges ?? new List<GraphControlFlowEdge>();
                    for (int e = 0; e < edges.Count; e++)
                    {
                        if (!string.Equals(edges[e].From, node.Id, StringComparison.Ordinal) ||
                            !edges[e].FromPort.StartsWith(GraphControlFlowPorts.CasePrefix, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        ResolveEnumCasePort(edges[e].FromPort, schema, node, graphId, diagnostics, table);
                    }
                }
                else
                {
                    List<GraphControlFlowValueEdge> edges = document.ValueEdges ?? new List<GraphControlFlowValueEdge>();
                    for (int e = 0; e < edges.Count; e++)
                    {
                        if (!string.Equals(edges[e].To, node.Id, StringComparison.Ordinal) ||
                            !edges[e].ToPort.StartsWith(GraphControlFlowPorts.CasePrefix, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        ResolveEnumCasePort(edges[e].ToPort, schema, node, graphId, diagnostics, table);
                    }
                }
            }

            return table;
        }

        private static void ResolveEnumCasePort(
            string port,
            Ludots.Core.Scripting.EnumSchema schema,
            GraphControlFlowNode node,
            string graphId,
            List<GraphDiagnostic> diagnostics,
            EnumCaseTable table)
        {
            string member = port[GraphControlFlowPorts.CasePrefix.Length..];
            if (int.TryParse(member, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                    $"Node '{node.Id}' is bound to enumType '{schema.TypeName}'; case ports must name members " +
                    $"(case:{schema.Members[0]}...), not raw ints — member values are declaration-order and drifting " +
                    "literals are exactly what the binding prevents.", node.Id));
                return;
            }

            if (!schema.TryGetValue(member, out int value))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                    $"Node '{node.Id}' case '{member}' is not a member of enum '{schema.TypeName}'.", node.Id));
                return;
            }

            table.Record(node.Id, port, value);
        }

        private static Dictionary<ControlKey, string> BuildControlEdges(
            GraphControlFlowDocument document,
            Dictionary<string, int> nodeIndices,
            AuthoredOp[] ops,
            string graphId,
            List<GraphDiagnostic> diagnostics,
            EnumCaseTable? enumCases = null)
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

                // Enum-bound case:{memberName} enters the generic dictionary as case:{int} so
                // validation, counting, and lowering below stay the SwitchInt path unchanged.
                string port = edge.FromPort;
                if (enumCases != null &&
                    enumCases.TryResolveMemberPort(edge.From, port, out int memberValue))
                {
                    port = GraphControlFlowPorts.Case(memberValue);
                }

                var key = new ControlKey(edge.From, port);
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
            GraphRegisterFile registers,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            // Pinned registers reserve before sequential allocation so a pin declared
            // anywhere in the node list cannot collide with an earlier auto-allocated
            // slot; declaration order must not decide pin validity.
            for (int i = 0; i < nodes.Count; i++)
            {
                GraphValueType outputType = GetOutputType(ops[i], registers.Kind);
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

                    outputRegisters[i] = registers.PinInt(nodes[i].PinRegister, graphId, nodes[i].Id, diagnostics);
                }
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                GraphValueType outputType = outputTypes[i];
                if (outputType == GraphValueType.Void || outputType == GraphValueType.TargetList || nodes[i].PinRegister >= 0)
                {
                    continue;
                }

                outputRegisters[i] = outputType switch
                {
                    GraphValueType.Int => registers.Alloc(GraphValueType.Int, graphId, nodes[i].Id, diagnostics),
                    GraphValueType.Bool => registers.Alloc(GraphValueType.Bool, graphId, nodes[i].Id, diagnostics),
                    GraphValueType.Float => registers.Alloc(GraphValueType.Float, graphId, nodes[i].Id, diagnostics),
                    GraphValueType.Entity when ops[i].NodeOp is GraphNodeOp.LoadCaster
                        or GraphNodeOp.LoadExplicitTarget
                        or GraphNodeOp.LoadViewer
                        => registers.BindEntityPreset(ops[i].NodeOp),
                    GraphValueType.Entity => registers.Alloc(GraphValueType.Entity, graphId, nodes[i].Id, diagnostics),
                    _ => (byte)0
                };
            }
        }

        private static GraphValueType GetOutputType(AuthoredOp op, GraphKind graphKind)
        {
            if (op.Kind is AuthoredOpKind.BranchBool or AuthoredOpKind.SwitchInt
                or AuthoredOpKind.While or AuthoredOpKind.Until
                or AuthoredOpKind.BtSequence or AuthoredOpKind.BtSelector or AuthoredOpKind.BtDecorator)
            {
                return GraphValueType.Void;
            }

            // SelectByEnum is sugar that produces a value: the picked candidate's int.
            if (op.Kind == AuthoredOpKind.SelectByEnum)
            {
                return GraphValueType.Int;
            }

            // FsmState also produces a value: the state int read from stateVar, so the
            // register doubles as the arm-check selector (ablation parity with a
            // handwritten ReadMapVarInt→SwitchInt chain).
            if (op.Kind == AuthoredOpKind.FsmState)
            {
                return GraphValueType.Int;
            }

            if (graphKind == GraphKind.Query)
            {
                return GraphOpDescriptorTable.GetQueryOutputType(op.NodeOp);
            }

            if (GraphAuthoringKindPolicy.IsLinearAuthoringKind(graphKind) ||
                UsesLinearDescriptorEmit(graphKind, op))
            {
                return GraphOpDescriptorTable.GetLinearOutputType(op.NodeOp);
            }

            return op.NodeOp switch
            {
                GraphNodeOp.ConstInt or GraphNodeOp.AddInt or GraphNodeOp.MoveInt or GraphNodeOp.InvokeScript
                    => GraphValueType.Int,
                GraphNodeOp.CompareLtInt => GraphValueType.Bool,
                _ => GraphValueType.Void
            };
        }

        private static bool IsScriptNativeDialectOp(GraphNodeOp op)
            => op is GraphNodeOp.ConstInt
                or GraphNodeOp.AddInt
                or GraphNodeOp.CompareLtInt
                or GraphNodeOp.MoveInt
                or GraphNodeOp.Call
                or GraphNodeOp.Return
                or GraphNodeOp.HaltReturnInt
                or GraphNodeOp.Yield
                or GraphNodeOp.Jump
                or GraphNodeOp.JumpIfFalse
                or GraphNodeOp.InvokeScript;

        private static bool UsesLinearDescriptorEmit(GraphKind graphKind, AuthoredOp op)
            => graphKind is GraphKind.Script or GraphKind.TriggerGraph &&
               op.Kind == AuthoredOpKind.GraphNodeOp &&
               !IsScriptNativeDialectOp(op.NodeOp) &&
               GraphOpDescriptorTable.IsAuthorable(graphKind, op.NodeOp);

        private static bool NeedsDroppedOutput(GraphNodeOp op)
            => IsSpatialCapacityQuery(op) || IsRelationshipCapacityQuery(op);

        private static bool IsAllowTruncated(GraphControlFlowNode node)
            => string.Equals(node.QueryCapacityPolicy, "AllowTruncated", StringComparison.Ordinal);

        private static bool IsRequireComplete(GraphControlFlowNode node)
            => string.Equals(node.QueryCapacityPolicy, "RequireComplete", StringComparison.Ordinal);

        private static void ApplySpatialCapacityPolicy(
            GraphControlFlowNode node,
            byte droppedRegister,
            ref GraphInstruction instruction)
        {
            if (!IsAllowTruncated(node))
            {
                instruction.Flags = 0;
                return;
            }

            instruction.Flags = 1;
            instruction.Dst = droppedRegister;
        }

        private static void ApplyRelationshipCapacityPolicy(
            GraphControlFlowNode node,
            byte droppedRegister,
            ref GraphInstruction instruction)
        {
            if (!IsAllowTruncated(node))
            {
                instruction.Flags = 0;
                return;
            }

            instruction.Flags = 1;
            instruction.C = droppedRegister;
        }

        private static bool IsSpatialCapacityQuery(GraphNodeOp op)
            => op is GraphNodeOp.QueryRadius
                or GraphNodeOp.QueryCone
                or GraphNodeOp.QueryRectangle
                or GraphNodeOp.QueryLine
                or GraphNodeOp.QueryHexRange
                or GraphNodeOp.QueryHexRing
                or GraphNodeOp.QueryHexNeighbors;

        private static bool IsRelationshipCapacityQuery(GraphNodeOp op)
            => op is GraphNodeOp.RelationshipQueryOutgoing
                or GraphNodeOp.RelationshipQueryIncoming
                or GraphNodeOp.RelationshipQueryMutual
                or GraphNodeOp.RelationshipQueryBetweenPair;

        private static void ValidateRequiredEdges(
            List<GraphControlFlowNode> nodes,
            AuthoredOp[] ops,
            GraphKind graphKind,
            Dictionary<ControlKey, string> controlEdges,
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges,
            Dictionary<string, int> nodeIndices,
            GraphValueType[] outputTypes,
            string graphId,
            List<GraphDiagnostic> diagnostics,
            Dictionary<string, Ludots.Core.Scripting.EventSchema>? dispatchSchemas = null,
            BtSugarPlan? btPlan = null,
            EnumCaseTable? enumCases = null)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                GraphControlFlowNode node = nodes[i];
                AuthoredOp op = ops[i];

                if (btPlan != null && btPlan.IsComposite(i))
                {
                    // BT composites have no linear Next/value contract; AnalyzeBtSugar owns their
                    // child-arm and decorator-kind rules, the generic port whitelist still applies.
                    ValidateAllowedPorts(node, op, graphKind, controlEdges, valueEdges, graphId, diagnostics, dispatchSchemas, enumCases);
                    continue;
                }

                ValidateAuxiliaryOutputs(node, op, graphKind, graphId, diagnostics);
                ValidateAllowedPorts(node, op, graphKind, controlEdges, valueEdges, graphId, diagnostics, dispatchSchemas, enumCases);

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

                if (op.Kind == AuthoredOpKind.FsmState)
                {
                    ValidateFsmStateEdges(node, controlEdges, graphId, diagnostics);
                    continue;
                }

                if (op.Kind == AuthoredOpKind.SelectByEnum)
                {
                    ValidateSelectByEnumEdges(node, controlEdges, valueEdges, nodeIndices, outputTypes, graphId, diagnostics, enumCases);
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

                if (GraphAuthoringKindPolicy.IsLinearAuthoringKind(graphKind) ||
                    UsesLinearDescriptorEmit(graphKind, op))
                {
                    ValidateLinearNode(
                        node,
                        op,
                        controlEdges,
                        valueEdges,
                        nodeIndices,
                        outputTypes,
                        graphId,
                        diagnostics,
                        dispatchSchemas);
                    continue;
                }

                // BT chain terminals report status instead of chaining to a next node.
                bool btChainTerminal = btPlan != null && btPlan.IsChainTerminal(i);
                switch (op.NodeOp)
                {
                    case GraphNodeOp.ConstInt:
                    case GraphNodeOp.AddInt:
                    case GraphNodeOp.CompareLtInt:
                    case GraphNodeOp.MoveInt:
                    case GraphNodeOp.Yield:
                    case GraphNodeOp.AwaitCallback:
                    case GraphNodeOp.InvokeScript:
                        if (!btChainTerminal)
                        {
                            RequireControlEdge(node, GraphControlFlowPorts.Next, controlEdges, graphId, diagnostics);
                        }

                        break;
                    case GraphNodeOp.Call:
                        RequireControlEdge(node, GraphControlFlowPorts.Call, controlEdges, graphId, diagnostics);
                        if (!btChainTerminal)
                        {
                            RequireControlEdge(node, GraphControlFlowPorts.Next, controlEdges, graphId, diagnostics);
                        }

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
                        // Value pin optional: absent means I[0] (sensor / ambient register contract).
                        if (valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.Value)))
                        {
                            RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                        }

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
            List<GraphDiagnostic> diagnostics,
            Dictionary<string, Ludots.Core.Scripting.EventSchema>? dispatchSchemas = null,
            EnumCaseTable? enumCases = null)
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
                    !IsAllowedOutputPort(node, op, graphKind, edge.FromPort))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                        $"Unexpected value output '{edge.FromPort}' on node '{node.Id}'.", node.Id));
                }

                if (string.Equals(edge.To, node.Id, StringComparison.Ordinal) &&
                    !IsAllowedInputPort(node, op, graphKind, edge.ToPort, dispatchSchemas, enumCases))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingValueInput,
                        $"Unexpected value input '{edge.ToPort}' on node '{node.Id}'.", node.Id));
                }
            }
        }

        private static void ValidateAuxiliaryOutputs(
            GraphControlFlowNode node,
            AuthoredOp op,
            GraphKind graphKind,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            if (!string.IsNullOrWhiteSpace(node.ValidOutput) &&
                op.NodeOp != GraphNodeOp.SnapToNearestInCollection)
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                    $"Node '{node.Id}' validOutput is only valid on SnapToNearestInCollection.",
                    node.Id));
            }

            if (!string.IsNullOrWhiteSpace(node.DroppedOutput) &&
                !NeedsDroppedOutput(op.NodeOp))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                    $"Node '{node.Id}' droppedOutput is only valid on capacity-limited query nodes.",
                    node.Id));
            }

            if (!string.IsNullOrWhiteSpace(node.DroppedOutput) &&
                !IsAllowTruncated(node) &&
                NeedsDroppedOutput(op.NodeOp))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                    $"Node '{node.Id}' droppedOutput requires queryCapacityPolicy 'AllowTruncated'.",
                    node.Id));
            }
        }

        private static bool IsAllowedControlPort(AuthoredOp op, GraphKind graphKind, string port)
        {
            if (op.Kind is AuthoredOpKind.BtSequence or AuthoredOpKind.BtSelector or AuthoredOpKind.BtDecorator)
            {
                return GraphControlFlowPorts.TryParseChildPort(port, out _);
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

            if (op.Kind == AuthoredOpKind.FsmState)
            {
                return port == GraphControlFlowPorts.Default ||
                       GraphControlFlowPorts.TryParseCasePort(port, out _);
            }

            if (op.Kind is AuthoredOpKind.While or AuthoredOpKind.Until)
            {
                return port is GraphControlFlowPorts.Body or GraphControlFlowPorts.Next;
            }

            if (op.NodeOp == GraphNodeOp.HaltReturnInt)
            {
                return false;
            }

            if (graphKind == GraphKind.Query)
            {
                return IsAllowedQueryControlPort(port);
            }

            if (GraphAuthoringKindPolicy.IsLinearAuthoringKind(graphKind))
            {
                return IsAllowedLinearControlPort(port);
            }

            return op.NodeOp switch
            {
                GraphNodeOp.Call => port is GraphControlFlowPorts.Call or GraphControlFlowPorts.Next,
                GraphNodeOp.Jump => port == GraphControlFlowPorts.Target,
                GraphNodeOp.Return or GraphNodeOp.HaltReturnInt => false,
                _ => port == GraphControlFlowPorts.Next
            };
        }

        private static bool IsAllowedInputPort(
            GraphControlFlowNode node,
            AuthoredOp op,
            GraphKind graphKind,
            string port,
            Dictionary<string, Ludots.Core.Scripting.EventSchema>? dispatchSchemas = null,
            EnumCaseTable? enumCases = null)
        {
            // DispatchMapEvent payload ports are dynamic (schema parameter names); the
            // per-port name/type contract is enforced in ValidateLinearNode.
            if (op.NodeOp == GraphNodeOp.DispatchMapEvent && dispatchSchemas != null)
            {
                return true;
            }

            if (op.Kind == AuthoredOpKind.BranchBool ||
                op.Kind == AuthoredOpKind.While ||
                op.Kind == AuthoredOpKind.Until ||
                op.NodeOp == GraphNodeOp.JumpIfFalse)
            {
                return port == GraphControlFlowPorts.Condition;
            }

            if (op.Kind is AuthoredOpKind.BtSequence or AuthoredOpKind.BtSelector or AuthoredOpKind.BtDecorator)
            {
                return false;
            }

            if (op.Kind == AuthoredOpKind.SwitchInt)
            {
                return port == GraphControlFlowPorts.Selector;
            }

            // FsmState has no value inputs: the selector is the stateVar map read baked
            // into the expansion, so there is nothing an edge could legitimately feed.
            if (op.Kind == AuthoredOpKind.FsmState)
            {
                return false;
            }

            if (op.Kind == AuthoredOpKind.SelectByEnum)
            {
                // Candidate ports are dynamic (case:{memberName} per enum binding); member
                // existence was already fail-closed in BuildEnumCaseTable.
                return port == GraphControlFlowPorts.Selector ||
                       port == GraphControlFlowPorts.Default ||
                       (enumCases != null && enumCases.TryResolveMemberPort(node.Id, port, out _));
            }

            if (graphKind == GraphKind.Query)
            {
                return GraphOpDescriptorTable.IsAllowedQueryInputPort(op.NodeOp, port);
            }

            if (GraphAuthoringKindPolicy.IsLinearAuthoringKind(graphKind))
            {
                return GraphOpDescriptorTable.IsAllowedLinearInputPort(op.NodeOp, port);
            }

            return GraphOpDescriptorTable.IsAllowedScriptInputPort(op.NodeOp, port);
        }

        private static bool IsAllowedOutputPort(GraphControlFlowNode node, AuthoredOp op, GraphKind graphKind, string port)
        {
            if (!string.IsNullOrWhiteSpace(node.ValidOutput) &&
                string.Equals(port, node.ValidOutput, StringComparison.Ordinal))
            {
                return op.NodeOp == GraphNodeOp.SnapToNearestInCollection;
            }

            if (!string.IsNullOrWhiteSpace(node.DroppedOutput) &&
                string.Equals(port, node.DroppedOutput, StringComparison.Ordinal))
            {
                return NeedsDroppedOutput(op.NodeOp) && IsAllowTruncated(node);
            }

            if (graphKind == GraphKind.Query)
            {
                return GraphOpDescriptorTable.IsAllowedQueryOutputPort(op.NodeOp, port);
            }

            if (GraphAuthoringKindPolicy.IsLinearAuthoringKind(graphKind))
            {
                return GraphOpDescriptorTable.IsAllowedLinearOutputPort(op.NodeOp, port);
            }

            return GetOutputType(op, graphKind) != GraphValueType.Void && port == GraphControlFlowPorts.Value;
        }

        private static void DetectUnreachable(
            string[] entryRoots,
            List<GraphControlFlowNode> nodes,
            List<GraphControlFlowEdge>? controlEdges,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
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
            for (int i = 0; i < entryRoots.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(entryRoots[i]))
                {
                    continue;
                }

                if (reachable.Add(entryRoots[i]))
                {
                    stack.Push(entryRoots[i]);
                }
            }

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
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges,
            Dictionary<string, Ludots.Core.Scripting.EventSchema>? dispatchSchemas,
            List<GraphDiagnostic> diagnostics,
            BtSugarPlan? btPlan = null,
            EnumCaseTable? enumCases = null)
        {
            var layouts = new NodeLayout[nodes.Count];
            int cursor = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                int count = InstructionCount(nodes[i], ops[i], graphKind, controlEdges, valueEdges, dispatchSchemas, btPlan, i, enumCases);
                layouts[i] = new NodeLayout(cursor, count);
                cursor += count;
            }

            return layouts;
        }

        private static int InstructionCount(
            GraphControlFlowNode node,
            AuthoredOp op,
            GraphKind graphKind,
            Dictionary<ControlKey, string> controlEdges,
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges,
            Dictionary<string, Ludots.Core.Scripting.EventSchema>? dispatchSchemas = null,
            BtSugarPlan? btPlan = null,
            int nodeIndex = -1,
            EnumCaseTable? enumCases = null)
        {
            if (btPlan != null && nodeIndex >= 0 && btPlan.IsComposite(nodeIndex))
            {
                return CountBtCompositeInstructions(op, btPlan, nodeIndex, node.DecoratorKind);
            }

            // BT leaf-chain terminal: the implicit halt slot is replaced by the status epilogue.
            if (btPlan != null && nodeIndex >= 0 && btPlan.IsChainTerminal(nodeIndex) &&
                !controlEdges.ContainsKey(new ControlKey(node.Id, GraphControlFlowPorts.Next)))
            {
                GraphValueType terminalType = op.Kind == AuthoredOpKind.GraphNodeOp
                    ? GetOutputType(op, graphKind)
                    : GraphValueType.Void;
                int epilogue = CountBtLeafEpilogueInstructions(terminalType);
                int opCount = op.NodeOp == GraphNodeOp.DispatchMapEvent && dispatchSchemas != null &&
                              dispatchSchemas.TryGetValue(node.Id, out Ludots.Core.Scripting.EventSchema schema)
                    ? CountWiredDispatchParams(node, valueEdges, schema) + 1
                    : 1;
                return opCount + epilogue;
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

            if (op.Kind == AuthoredOpKind.FsmState)
            {
                int cases = CountSwitchCaseArms(node.Id, controlEdges);
                // ReadMapVarInt + next-aligned Jump; then the SwitchInt-shaped arm chain + Jump(default)
                return (cases * 4) + 3;
            }

            if (op.Kind == AuthoredOpKind.SelectByEnum)
            {
                int candidates = CountSelectCandidates(node.Id, valueEdges, enumCases);
                bool hasDefault = valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.Default));
                // per candidate: ConstInt(memberValue) + CompareEqInt + JumpIfFalse(next) + MoveInt + Jump(end);
                // optional default MoveInt; the shared slot is the linear next-jump.
                return (candidates * 5) + (hasDefault ? 1 : 0) + 1;
            }

            // DispatchMapEvent emits one StoreArg* per wired schema parameter, then the fire,
            // then the shared next-jump / explicit-halt slot.
            if (op.NodeOp == GraphNodeOp.DispatchMapEvent)
            {
                return dispatchSchemas != null && dispatchSchemas.TryGetValue(node.Id, out Ludots.Core.Scripting.EventSchema schema)
                    ? CountWiredDispatchParams(node, valueEdges, schema) + 2
                    : 2;
            }

            if (graphKind == GraphKind.Query ||
                GraphAuthoringKindPolicy.IsLinearAuthoringKind(graphKind) ||
                UsesLinearDescriptorEmit(graphKind, op))
            {
                if (controlEdges.ContainsKey(new ControlKey(node.Id, GraphControlFlowPorts.Next)))
                {
                    return 2;
                }

                return op.NodeOp == GraphNodeOp.HaltReturnInt ? 1 : 2;
            }

            return op.NodeOp switch
            {
                GraphNodeOp.Return or GraphNodeOp.HaltReturnInt or GraphNodeOp.Jump => 1,
                GraphNodeOp.Call or GraphNodeOp.ConstInt or GraphNodeOp.AddInt or GraphNodeOp.CompareLtInt
                    or GraphNodeOp.MoveInt or GraphNodeOp.Yield or GraphNodeOp.InvokeScript => 2, // body + jump next
                _ => 1
            };
        }

        private static int CountWiredDispatchParams(
            GraphControlFlowNode node,
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges,
            Ludots.Core.Scripting.EventSchema schema)
        {
            int wired = 0;
            for (int i = 0; i < schema.Params.Count; i++)
            {
                if (schema.Params[i].Type == Ludots.Core.Scripting.EventParamType.String)
                {
                    continue;
                }

                if (valueEdges.ContainsKey(new ValueInputKey(node.Id, schema.Params[i].Name)))
                {
                    wired++;
                }
            }

            return wired;
        }

        private static void CompileNode(
            GraphControlFlowDocument document,
            GraphControlFlowNode node,
            AuthoredOp op,
            byte[] outputRegisters,
            GraphValueType[] outputTypes,
            byte[] boolScratches,
            byte[] droppedRegisters,
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
            List<GraphDiagnostic> diagnostics,
            Dictionary<string, Ludots.Core.Scripting.EventSchema>? dispatchSchemas = null,
            BtSugarPlan? btPlan = null,
            EnumCaseTable? enumCases = null)
        {
            int nodeIndex = nodeIndices[node.Id];
            int bodyIndex = layouts[nodeIndex].BodyIndex;

            if (op.Kind == AuthoredOpKind.BranchBool || op.NodeOp == GraphNodeOp.JumpIfFalse)
            {
                byte cond = ResolveValueInput(
                    node, GraphControlFlowPorts.Condition, GraphValueType.Bool,
                    valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
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

            if (btPlan != null && btPlan.IsComposite(nodeIndex))
            {
                CompileBtComposite(btPlan, node, op, nodeIndices, layouts, program, sources, graphId);
                return;
            }

            if (graphKind == GraphKind.Query)
            {
                CompileQueryNode(
                    document,
                    node,
                    op,
                    outputRegisters,
                    outputTypes,
                    boolScratches,
                    droppedRegisters,
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

            if (GraphAuthoringKindPolicy.IsLinearAuthoringKind(graphKind) ||
                UsesLinearDescriptorEmit(graphKind, op))
            {
                CompileLinearNode(
                    document,
                    node,
                    op,
                    outputRegisters,
                    outputTypes,
                    boolScratches,
                    droppedRegisters,
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
                    diagnostics,
                    dispatchSchemas,
                    btPlan,
                    nodeIndex);
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
                    boolScratches,
                    droppedRegisters,
                    definedInts,
                    definedBools,
                    graphId,
                    diagnostics,
                    enumCases);
                return;
            }

            if (op.Kind == AuthoredOpKind.FsmState)
            {
                CompileFsmState(
                    document,
                    node,
                    sugarScratches[nodeIndex],
                    controlEdges,
                    nodeIndices,
                    layouts,
                    program,
                    sources,
                    outputRegisters,
                    definedInts,
                    symbolToIndex,
                    symbols,
                    graphId,
                    diagnostics,
                    enumCases);
                return;
            }

            if (op.Kind == AuthoredOpKind.SelectByEnum)
            {
                CompileSelectByEnum(
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
                    boolScratches,
                    droppedRegisters,
                    definedInts,
                    definedBools,
                    graphId,
                    diagnostics,
                    enumCases,
                    btPlan,
                    nodeIndex);
                return;
            }

            if (op.Kind == AuthoredOpKind.While)
            {
                // while (cond) body;  =>  JumpIfFalse(cond)->next; Jump->body
                byte cond = ResolveValueInput(
                    node, GraphControlFlowPorts.Condition, GraphValueType.Bool,
                    valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
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
                    valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
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
                    EmitNextJumpOrBtEpilogue(document, node, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources, graphId, btPlan, nodeIndex, outputTypes, outputRegisters);
                    break;

                case GraphNodeOp.AddInt:
                {
                    byte a = ResolveValueInput(node, GraphControlFlowPorts.A, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    byte b = ResolveValueInput(node, GraphControlFlowPorts.B, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphNodeOp.AddInt,
                        Dst = outputRegisters[nodeIndex],
                        A = a,
                        B = b
                    };
                    definedInts[outputRegisters[nodeIndex]] = true;
                    SetSource(sources, bodyIndex, graphId, node, nameof(GraphNodeOp.AddInt), GraphControlFlowPorts.Enter);
                    EmitNextJumpOrBtEpilogue(document, node, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources, graphId, btPlan, nodeIndex, outputTypes, outputRegisters);
                    break;
                }

                case GraphNodeOp.CompareLtInt:
                {
                    byte a = ResolveValueInput(node, GraphControlFlowPorts.A, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    byte b = ResolveValueInput(node, GraphControlFlowPorts.B, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphNodeOp.CompareLtInt,
                        Dst = outputRegisters[nodeIndex],
                        A = a,
                        B = b
                    };
                    definedBools[outputRegisters[nodeIndex]] = true;
                    SetSource(sources, bodyIndex, graphId, node, nameof(GraphNodeOp.CompareLtInt), GraphControlFlowPorts.Enter);
                    EmitNextJumpOrBtEpilogue(document, node, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources, graphId, btPlan, nodeIndex, outputTypes, outputRegisters);
                    break;
                }

                case GraphNodeOp.MoveInt:
                {
                    // Absent value edge keeps A=0: MoveInt re-publishes the ambient I[0] sensor slot
                    // into the dataflow so mid-chain compares can branch on host-fed measurements.
                    byte a = 0;
                    if (valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.Value)))
                    {
                        a = ResolveValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    }

                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphNodeOp.MoveInt,
                        Dst = outputRegisters[nodeIndex],
                        A = a
                    };
                    definedInts[outputRegisters[nodeIndex]] = true;
                    SetSource(sources, bodyIndex, graphId, node, nameof(GraphNodeOp.MoveInt), GraphControlFlowPorts.Enter);
                    EmitNextJumpOrBtEpilogue(document, node, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources, graphId, btPlan, nodeIndex, outputTypes, outputRegisters);
                    break;
                }

                case GraphNodeOp.Call:
                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphNodeOp.Call,
                        Imm = ResolveControlTarget(node, GraphControlFlowPorts.Call, controlEdges, nodeIndices, layouts)
                    };
                    SetSource(sources, bodyIndex, graphId, node, nameof(GraphNodeOp.Call), GraphControlFlowPorts.Call);
                    EmitNextJumpOrBtEpilogue(document, node, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources, graphId, btPlan, nodeIndex, outputTypes, outputRegisters);
                    break;

                case GraphNodeOp.Return:
                    program[bodyIndex] = new GraphInstruction { Op = (ushort)GraphNodeOp.Return };
                    SetSource(sources, bodyIndex, graphId, node, nameof(GraphNodeOp.Return), GraphControlFlowPorts.Enter);
                    break;

                case GraphNodeOp.HaltReturnInt:
                {
                    byte a = 0;
                    if (valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.Value)))
                    {
                        a = ResolveValueInput(
                            node,
                            GraphControlFlowPorts.Value,
                            GraphValueType.Int,
                            valueEdges,
                            nodeIndices,
                            outputTypes,
                            outputRegisters,
                            boolScratches,
                            droppedRegisters,
                            definedInts,
                            definedBools,
                            graphId,
                            diagnostics);
                    }

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
                    EmitNextJumpOrBtEpilogue(document, node, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources, graphId, btPlan, nodeIndex, outputTypes, outputRegisters);
                    break;

                case GraphNodeOp.Jump:
                    EmitRelativeJump(document, node, GraphControlFlowPorts.Target, bodyIndex, controlEdges, nodeIndices, layouts, program, sources, graphId);
                    break;

                case GraphNodeOp.InvokeScript:
                {
                    bool hasName = !string.IsNullOrWhiteSpace(node.FunctionName);
                    bool hasId = node.GraphId > 0;
                    if (hasName && hasId)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                            $"InvokeScript node '{node.Id}' cannot set both functionName and graphId.", node.Id));
                        break;
                    }

                    if (!hasName && !hasId)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                            $"InvokeScript node '{node.Id}' requires functionName (Func Lib) or graphId.", node.Id));
                        break;
                    }

                    int imm;
                    byte flags;
                    if (hasName)
                    {
                        imm = Intern(symbolToIndex, symbols, node.FunctionName!.Trim());
                        flags = GraphInstructionFlags.FuncLibName;
                    }
                    else
                    {
                        imm = node.GraphId;
                        flags = 0;
                    }

                    program[bodyIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphNodeOp.InvokeScript,
                        Dst = outputRegisters[nodeIndex],
                        Imm = imm,
                        Flags = flags
                    };
                    definedInts[outputRegisters[nodeIndex]] = true;
                    SetSource(sources, bodyIndex, graphId, node, nameof(GraphNodeOp.InvokeScript), GraphControlFlowPorts.Enter);
                    EmitNextJumpOrBtEpilogue(document, node, bodyIndex + 1, controlEdges, nodeIndices, layouts, program, sources, graphId, btPlan, nodeIndex, outputTypes, outputRegisters);
                    break;
                }

                default:
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                        $"Op '{op.NodeOp}' is not supported by GraphControlFlowCompiler.", node.Id));
                    break;
            }
        }


        private static void AllocateOpScratches(
            List<GraphControlFlowNode> nodes,
            AuthoredOp[] ops,
            byte[] boolScratches,
            GraphRegisterFile registers,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (ops[i].NodeOp == GraphNodeOp.TargetListGet)
                {
                    boolScratches[i] = registers.AllocScratch(GraphValueType.Bool, graphId, nodes[i].Id, diagnostics);
                    continue;
                }

                if (ops[i].NodeOp == GraphNodeOp.SnapToNearestInCollection &&
                    !string.IsNullOrWhiteSpace(nodes[i].ValidOutput))
                {
                    boolScratches[i] = registers.AllocScratch(GraphValueType.Bool, graphId, nodes[i].Id, diagnostics);
                    continue;
                }

                boolScratches[i] = byte.MaxValue;
            }
        }

        private static void AllocateDroppedOutputs(
            List<GraphControlFlowNode> nodes,
            AuthoredOp[] ops,
            byte[] outputRegisters,
            byte[] droppedRegisters,
            GraphRegisterFile registers,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!NeedsDroppedOutput(ops[i].NodeOp) ||
                    !string.Equals(nodes[i].QueryCapacityPolicy, "AllowTruncated", StringComparison.Ordinal))
                {
                    droppedRegisters[i] = byte.MaxValue;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(nodes[i].DroppedOutput))
                {
                    droppedRegisters[i] = byte.MaxValue;
                    continue;
                }

                droppedRegisters[i] = registers.AllocScratch(GraphValueType.Int, graphId, nodes[i].Id, diagnostics);
                outputRegisters[i] = droppedRegisters[i];
            }
        }

        private static void AllocateSugarScratches(
            List<GraphControlFlowNode> nodes,
            AuthoredOp[] ops,
            SugarScratch[] sugarScratches,
            GraphRegisterFile registers,
            string graphId,
            List<GraphDiagnostic> diagnostics,
            BtSugarPlan? btPlan = null)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (ops[i].Kind is not (AuthoredOpKind.SwitchInt or AuthoredOpKind.SelectByEnum or AuthoredOpKind.FsmState))
                {
                    continue;
                }

                byte intReg = registers.AllocScratch(GraphValueType.Int, graphId, nodes[i].Id, diagnostics);
                byte boolReg = registers.AllocScratch(GraphValueType.Bool, graphId, nodes[i].Id, diagnostics);
                sugarScratches[i] = new SugarScratch(intReg, boolReg);
            }

            if (btPlan != null)
            {
                // One shared scratch triple for the whole tree: scratch liveness never spans a
                // Call boundary, so composites never observe each other's compare cells.
                btPlan.StatusReg = registers.AllocScratch(GraphValueType.Int, graphId, btPlan.RootNodeId, diagnostics);
                btPlan.ConstReg = registers.AllocScratch(GraphValueType.Int, graphId, btPlan.RootNodeId, diagnostics);
                btPlan.BoolReg = registers.AllocScratch(GraphValueType.Bool, graphId, btPlan.RootNodeId, diagnostics);
            }
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

        private static void ValidateSelectByEnumEdges(
            GraphControlFlowNode node,
            Dictionary<ControlKey, string> controlEdges,
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges,
            Dictionary<string, int> nodeIndices,
            GraphValueType[] outputTypes,
            string graphId,
            List<GraphDiagnostic> diagnostics,
            EnumCaseTable? enumCases = null)
        {
            RequireControlEdge(node, GraphControlFlowPorts.Next, controlEdges, graphId, diagnostics);
            RequireValueInput(node, GraphControlFlowPorts.Selector, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);

            if (string.IsNullOrWhiteSpace(node.EnumType))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                    $"{SelectByEnumOp} node '{node.Id}' requires an enumType binding.", node.Id));
                return;
            }

            var seenValues = new HashSet<int>();
            int candidateCount = 0;
            foreach (KeyValuePair<ValueInputKey, GraphControlFlowValueEdge> pair in valueEdges)
            {
                if (!string.Equals(pair.Key.NodeId, node.Id, StringComparison.Ordinal) ||
                    !pair.Key.Port.StartsWith(GraphControlFlowPorts.CasePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                int memberValue = ResolveAuthoredCaseValue(node.Id, pair.Key.Port, enumCases);
                if (memberValue < 0)
                {
                    // Name resolution failures were already reported by BuildEnumCaseTable.
                    continue;
                }

                if (!seenValues.Add(memberValue))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.DuplicateValueEdge,
                        $"{SelectByEnumOp} node '{node.Id}' binds member value {memberValue} more than once.", node.Id));
                }

                RequireValueInput(node, pair.Key.Port, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                candidateCount++;
            }

            if (candidateCount == 0)
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingValueInput,
                    $"{SelectByEnumOp} node '{node.Id}' requires at least one case:{{member}} candidate value input.", node.Id));
            }

            if (valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.Default)))
            {
                RequireValueInput(node, GraphControlFlowPorts.Default, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
            }
        }

        /// <summary>Authored case port → int: literal first (unbound SwitchInt), then enum member name.</summary>
        private static int ResolveAuthoredCaseValue(string nodeId, string port, EnumCaseTable? enumCases)
        {
            if (GraphControlFlowPorts.TryParseCasePort(port, out int literal))
            {
                return literal;
            }

            return enumCases != null && enumCases.TryResolveMemberPort(nodeId, port, out int memberValue)
                ? memberValue
                : -1;
        }

        private static List<SwitchCaseArm> CollectSwitchCaseArms(
            GraphControlFlowDocument document,
            GraphControlFlowNode node,
            EnumCaseTable? enumCases = null)
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
                    if (enumCases == null || !enumCases.TryResolveMemberPort(node.Id, edge.FromPort, out caseValue))
                    {
                        continue;
                    }
                }

                arms.Add(new SwitchCaseArm(caseValue, edge.FromPort, edge.To));
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
            byte[] boolScratches,
            byte[] droppedRegisters,
            bool[] definedInts,
            bool[] definedBools,
            string graphId,
            List<GraphDiagnostic> diagnostics,
            EnumCaseTable? enumCases = null)
        {
            int nodeIndex = nodeIndices[node.Id];
            int bodyIndex = layouts[nodeIndex].BodyIndex;
            List<SwitchCaseArm> arms = CollectSwitchCaseArms(document, node, enumCases);
            byte selector = ResolveValueInput(
                node, GraphControlFlowPorts.Selector, GraphValueType.Int,
                valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
            int defaultAbs = ResolveControlTarget(node, GraphControlFlowPorts.Default, controlEdges, nodeIndices, layouts);
            int defaultJumpIndex = bodyIndex + (arms.Count * 4);

            for (int i = 0; i < arms.Count; i++)
            {
                SwitchCaseArm arm = arms[i];
                int armBase = bodyIndex + (i * 4);
                int nextCheck = (i + 1 < arms.Count) ? bodyIndex + ((i + 1) * 4) : defaultJumpIndex;
                int armAbs = layouts[nodeIndices[arm.TargetNodeId]].BodyIndex;
                string armPort = enumCases != null
                    ? enumCases.AuthoredPortOrNumeric(node.Id, arm.CaseValue)
                    : GraphControlFlowPorts.Case(arm.CaseValue);

                program[armBase] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.ConstInt,
                    Dst = scratch.IntReg,
                    Imm = arm.CaseValue
                };
                SetSource(sources, armBase, graphId, node, SwitchIntOp, armPort);

                program[armBase + 1] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.CompareEqInt,
                    Dst = scratch.BoolReg,
                    A = selector,
                    B = scratch.IntReg
                };
                SetSource(sources, armBase + 1, graphId, node, SwitchIntOp, armPort);

                program[armBase + 2] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.JumpIfFalse,
                    A = scratch.BoolReg,
                    Imm = RelativeOffset(armBase + 2, nextCheck)
                };
                SetSource(sources, armBase + 2, graphId, node, SwitchIntOp, armPort);

                program[armBase + 3] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.Jump,
                    Imm = RelativeOffset(armBase + 3, armAbs)
                };
                SetSource(sources, armBase + 3, graphId, node, SwitchIntOp, armPort);
            }

            program[defaultJumpIndex] = new GraphInstruction
            {
                Op = (ushort)GraphNodeOp.Jump,
                Imm = RelativeOffset(defaultJumpIndex, defaultAbs)
            };
            SetSource(sources, defaultJumpIndex, graphId, node, SwitchIntOp, GraphControlFlowPorts.Default);
        }

        private static int CountSelectCandidates(
            string nodeId,
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges,
            EnumCaseTable? enumCases)
        {
            int count = 0;
            foreach (KeyValuePair<ValueInputKey, GraphControlFlowValueEdge> pair in valueEdges)
            {
                if (string.Equals(pair.Key.NodeId, nodeId, StringComparison.Ordinal) &&
                    ResolveAuthoredCaseValue(nodeId, pair.Key.Port, enumCases) >= 0)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Lowers SelectByEnum (#1125): per candidate a ConstInt(memberValue) +
        /// CompareEqInt(selector) + JumpIfFalse(next check) + MoveInt(candidate) +
        /// Jump(end) chain, an optional default MoveInt, and the shared linear next-jump.
        /// No new executor or opcode — the running VM only ever sees the existing int ops.
        /// </summary>
        private static void CompileSelectByEnum(
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
            byte[] boolScratches,
            byte[] droppedRegisters,
            bool[] definedInts,
            bool[] definedBools,
            string graphId,
            List<GraphDiagnostic> diagnostics,
            EnumCaseTable? enumCases = null,
            BtSugarPlan? btPlan = null,
            int nodeIndex = -1)
        {
            int bodyIndex = layouts[nodeIndices[node.Id]].BodyIndex;
            List<SelectCandidateArm> arms = CollectSelectCandidateArms(document, node, enumCases);
            byte selector = ResolveValueInput(
                node, GraphControlFlowPorts.Selector, GraphValueType.Int,
                valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
            bool hasDefault = valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.Default));
            int defaultIndex = bodyIndex + (arms.Count * 5);
            int endSlot = defaultIndex + (hasDefault ? 1 : 0);
            byte output = outputRegisters[nodeIndices[node.Id]];

            for (int i = 0; i < arms.Count; i++)
            {
                SelectCandidateArm arm = arms[i];
                int armBase = bodyIndex + (i * 5);
                int nextCheck = (i + 1 < arms.Count) ? bodyIndex + ((i + 1) * 5) : defaultIndex;

                program[armBase] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.ConstInt,
                    Dst = scratch.IntReg,
                    Imm = arm.MemberValue
                };
                SetSource(sources, armBase, graphId, node, SelectByEnumOp, arm.AuthoredPort);

                program[armBase + 1] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.CompareEqInt,
                    Dst = scratch.BoolReg,
                    A = selector,
                    B = scratch.IntReg
                };
                SetSource(sources, armBase + 1, graphId, node, SelectByEnumOp, arm.AuthoredPort);

                program[armBase + 2] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.JumpIfFalse,
                    A = scratch.BoolReg,
                    Imm = RelativeOffset(armBase + 2, nextCheck)
                };
                SetSource(sources, armBase + 2, graphId, node, SelectByEnumOp, arm.AuthoredPort);

                program[armBase + 3] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.MoveInt,
                    Dst = output,
                    A = ResolveValueInput(
                        node, arm.AuthoredPort, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics)
                };
                definedInts[output] = true;
                SetSource(sources, armBase + 3, graphId, node, SelectByEnumOp, arm.AuthoredPort);

                program[armBase + 4] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.Jump,
                    Imm = RelativeOffset(armBase + 4, endSlot)
                };
                SetSource(sources, armBase + 4, graphId, node, SelectByEnumOp, arm.AuthoredPort);
            }

            if (hasDefault)
            {
                program[defaultIndex] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.MoveInt,
                    Dst = output,
                    A = ResolveValueInput(
                        node, GraphControlFlowPorts.Default, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics)
                };
                definedInts[output] = true;
                SetSource(sources, defaultIndex, graphId, node, SelectByEnumOp, GraphControlFlowPorts.Default);
            }

            EmitNextJumpOrBtEpilogue(
                document, node, endSlot, controlEdges, nodeIndices, layouts, program, sources, graphId, btPlan, nodeIndex, outputTypes, outputRegisters);
        }

        private static List<SelectCandidateArm> CollectSelectCandidateArms(
            GraphControlFlowDocument document,
            GraphControlFlowNode node,
            EnumCaseTable? enumCases)
        {
            var arms = new List<SelectCandidateArm>();
            List<GraphControlFlowValueEdge> edges = document.ValueEdges ?? new List<GraphControlFlowValueEdge>();
            for (int i = 0; i < edges.Count; i++)
            {
                GraphControlFlowValueEdge edge = edges[i];
                if (!string.Equals(edge.To, node.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                int memberValue = ResolveAuthoredCaseValue(node.Id, edge.ToPort, enumCases);
                if (memberValue < 0)
                {
                    continue;
                }

                arms.Add(new SelectCandidateArm(memberValue, edge.ToPort));
            }

            return arms;
        }

        private static void EmitExplicitHalt(
            GraphInstruction[] program,
            GraphInstructionSource[] sources,
            int instructionIndex,
            string graphId,
            GraphControlFlowNode node)
        {
            program[instructionIndex] = new GraphInstruction
            {
                Op = (ushort)GraphNodeOp.HaltReturnInt,
                A = 0
            };
            SetSource(sources, instructionIndex, graphId, node, nameof(GraphNodeOp.HaltReturnInt), GraphControlFlowPorts.Next);
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
            byte[] boolScratches,
            byte[] droppedRegisters,
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
                bool isAuxiliaryPort = !string.Equals(edge.FromPort, GraphControlFlowPorts.Value, StringComparison.Ordinal) &&
                                       !string.Equals(edge.FromPort, GraphControlFlowPorts.List, StringComparison.Ordinal);
                if (expectedType == GraphValueType.Bool && isAuxiliaryPort)
                {
                    byte validOutputRegister = boolScratches[sourceIndex];
                    if (validOutputRegister == byte.MaxValue)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingValueInput,
                            $"Value input '{port}' on '{node.Id}' requires a validOutput on '{edge.From}'.",
                            node.Id));
                        return 0;
                    }

                    if (!definedBools[validOutputRegister])
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UninitializedRegisterRead,
                            $"Bool register {validOutputRegister} read before assignment via '{edge.From}' -> '{node.Id}'.{port}.",
                            node.Id));
                    }

                    return validOutputRegister;
                }

                if (expectedType == GraphValueType.Int && isAuxiliaryPort)
                {
                    byte droppedOutputRegister = droppedRegisters[sourceIndex];
                    if (droppedOutputRegister == byte.MaxValue)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingValueInput,
                            $"Value input '{port}' on '{node.Id}' requires a droppedOutput on '{edge.From}'.",
                            node.Id));
                        return 0;
                    }

                    if (!definedInts[droppedOutputRegister])
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UninitializedRegisterRead,
                            $"Int register {droppedOutputRegister} read before assignment via '{edge.From}' -> '{node.Id}'.{port}.",
                            node.Id));
                    }

                    return droppedOutputRegister;
                }

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
                bool isAuxiliaryPort = !string.Equals(edge.FromPort, GraphControlFlowPorts.Value, StringComparison.Ordinal) &&
                                       !string.Equals(edge.FromPort, GraphControlFlowPorts.List, StringComparison.Ordinal);
                if (isAuxiliaryPort && expectedType is GraphValueType.Int or GraphValueType.Bool)
                {
                    return;
                }

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
