using System;
using System.Collections.Generic;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace Ludots.Core.GraphRuntime
{
    public readonly struct GraphProgramRegistration
    {
        public GraphProgramRegistration(GraphInstruction[] program, GraphKind kind)
            : this(program, kind, Array.Empty<string>(), Array.Empty<MapTriggerGraphEntry>())
        {
        }

        public GraphProgramRegistration(GraphInstruction[] program, GraphKind kind, string[]? symbols)
            : this(program, kind, symbols, Array.Empty<MapTriggerGraphEntry>())
        {
        }

        public GraphProgramRegistration(
            GraphInstruction[] program,
            GraphKind kind,
            string[]? symbols,
            MapTriggerGraphEntry[]? mapTriggerEntries)
        {
            Program = program ?? Array.Empty<GraphInstruction>();
            Kind = kind;
            Symbols = symbols ?? Array.Empty<string>();
            MapTriggerEntries = mapTriggerEntries ?? Array.Empty<MapTriggerGraphEntry>();
            ContainsYield = ProgramContainsYield(Program);
        }

        public GraphInstruction[] Program { get; }
        public GraphKind Kind { get; }
        public string[] Symbols { get; }
        public IReadOnlyList<MapTriggerGraphEntry> MapTriggerEntries { get; }
        public bool ContainsYield { get; }

        private static bool ProgramContainsYield(GraphInstruction[] program)
        {
            for (int i = 0; i < program.Length; i++)
            {
                if (program[i].Op == (ushort)GraphNodeOp.Yield)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class GraphProgramRegistry
    {
        private readonly Dictionary<int, GraphProgramRegistration> _programs = new();
        private readonly Dictionary<int, GraphInstructionSourceMap> _sourceMaps = new();
        private int _version;

        public int Version => _version;

        public void Clear()
        {
            _programs.Clear();
            _sourceMaps.Clear();
            _version++;
        }

        public void Register(int graphId, GraphInstruction[] program, GraphKind kind)
            => Register(graphId, program, kind, GraphInstructionSourceMap.Empty);

        public void Register(int graphId, GraphInstruction[] program, GraphKind kind, GraphInstructionSourceMap sourceMap)
            => Register(graphId, program, kind, sourceMap, Array.Empty<string>());

        public void Register(int graphId, GraphInstruction[] program, GraphKind kind, GraphInstructionSourceMap sourceMap, string[]? symbols)
            => Register(graphId, program, kind, sourceMap, symbols, Array.Empty<MapTriggerGraphEntry>());

        public void Register(
            int graphId,
            GraphInstruction[] program,
            GraphKind kind,
            GraphInstructionSourceMap sourceMap,
            string[]? symbols,
            MapTriggerGraphEntry[]? mapTriggerEntries)
        {
            if (graphId <= 0) throw new ArgumentOutOfRangeException(nameof(graphId));
            if (program == null) throw new ArgumentNullException(nameof(program));
            if (kind == GraphKind.None || !Enum.IsDefined(typeof(GraphKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Graph registration requires an explicit supported kind.");
            }

            MapTriggerGraphEntry[] entries = NormalizeMapTriggerEntries(graphId, kind, mapTriggerEntries, program);

            if (!_programs.TryAdd(graphId, new GraphProgramRegistration(program, kind, symbols, entries)))
            {
                throw new InvalidOperationException(
                    $"Graph program id {graphId} is already registered; duplicate registration is not allowed.");
            }

            if (sourceMap.HasSources)
            {
                _sourceMaps[graphId] = sourceMap;
            }

            try
            {
                EnsureProgramValid(graphId, program, kind);
                EnsureInvokeTargetsAreScript(allowMissingTargets: true, allowUnpatchedFuncLibNames: true);
                EnsureNoInvokeCycle(graphId);
            }
            catch
            {
                _programs.Remove(graphId);
                _sourceMaps.Remove(graphId);
                throw;
            }

            _version++;
        }

        /// <summary>
        /// Hot-replaces program body for an already-registered graph id.
        /// Kind and id must stay identical; identity remap is forbidden (EngineRestartRequired).
        /// </summary>
        public void ReplaceProgram(int graphId, GraphInstruction[] program, GraphKind kind, GraphInstructionSourceMap sourceMap)
            => ReplaceProgram(graphId, program, kind, sourceMap, Array.Empty<string>());

        public void ReplaceProgram(int graphId, GraphInstruction[] program, GraphKind kind, GraphInstructionSourceMap sourceMap, string[]? symbols)
            => ReplaceProgram(graphId, program, kind, sourceMap, symbols, Array.Empty<MapTriggerGraphEntry>());

        public void ReplaceProgram(
            int graphId,
            GraphInstruction[] program,
            GraphKind kind,
            GraphInstructionSourceMap sourceMap,
            string[]? symbols,
            MapTriggerGraphEntry[]? mapTriggerEntries)
        {
            if (graphId <= 0) throw new ArgumentOutOfRangeException(nameof(graphId));
            if (program == null) throw new ArgumentNullException(nameof(program));
            if (kind == GraphKind.None || !Enum.IsDefined(typeof(GraphKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Graph replace requires an explicit supported kind.");
            }

            if (!_programs.TryGetValue(graphId, out GraphProgramRegistration existing))
            {
                throw new InvalidOperationException(
                    $"Graph program id {graphId} is not registered; cannot ReplaceProgram (new ids require EngineRestart).");
            }

            if (existing.Kind != kind)
            {
                throw new InvalidOperationException(
                    $"Graph program id {graphId} kind is '{existing.Kind}'; cannot replace with '{kind}' (identity change requires EngineRestart).");
            }

            MapTriggerGraphEntry[] entries = NormalizeMapTriggerEntries(graphId, kind, mapTriggerEntries, program);

            GraphProgramRegistration previous = existing;
            bool hadPreviousSourceMap = _sourceMaps.TryGetValue(graphId, out GraphInstructionSourceMap previousSourceMap);
            _programs[graphId] = new GraphProgramRegistration(program, kind, symbols, entries);
            if (sourceMap.HasSources)
            {
                _sourceMaps[graphId] = sourceMap;
            }
            else
            {
                _sourceMaps.Remove(graphId);
            }

            try
            {
                EnsureProgramValid(graphId, program, kind);
                EnsureInvokeTargetsAreScript(allowMissingTargets: true, allowUnpatchedFuncLibNames: true);
                EnsureNoInvokeCycle(graphId);
            }
            catch
            {
                _programs[graphId] = previous;
                if (hadPreviousSourceMap)
                {
                    _sourceMaps[graphId] = previousSourceMap;
                }
                else
                {
                    _sourceMaps.Remove(graphId);
                }

                throw;
            }

            _version++;
        }

        private static MapTriggerGraphEntry[] NormalizeMapTriggerEntries(
            int graphId,
            GraphKind kind,
            MapTriggerGraphEntry[]? entries,
            GraphInstruction[] program)
        {
            if (kind == GraphKind.MapTrigger)
            {
                if (entries == null || entries.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Graph program id {graphId} kind MapTrigger requires a non-empty MapTrigger entry table.");
                }

                var seenLabels = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < entries.Length; i++)
                {
                    string label = entries[i].Label ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(label))
                    {
                        throw new InvalidOperationException(
                            $"Graph program id {graphId} MapTrigger entry [{i}] requires a non-empty label.");
                    }

                    if (!seenLabels.Add(label.Trim()))
                    {
                        throw new InvalidOperationException(
                            $"Graph program id {graphId} has duplicate MapTrigger entry label '{label}'.");
                    }

                    if (string.IsNullOrWhiteSpace(entries[i].EventName))
                    {
                        throw new InvalidOperationException(
                            $"Graph program id {graphId} MapTrigger entry '{label}' requires a non-empty event name.");
                    }

                    int startPc = entries[i].StartPc;
                    if (startPc < 0 || startPc >= program.Length)
                    {
                        throw new InvalidOperationException(
                            $"Graph program id {graphId} MapTrigger entry '{label}' StartPc {startPc} is outside the program (length {program.Length}).");
                    }
                }

                return entries;
            }

            if (entries != null && entries.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Graph program id {graphId} kind '{kind}' must not carry MapTrigger entries; the entry table is MapTrigger-only.");
            }

            return Array.Empty<MapTriggerGraphEntry>();
        }

        private static void EnsureProgramValid(int graphId, GraphInstruction[] program, GraphKind kind)
        {
            GraphKindOperationPolicy.ValidateProgram(
                kind,
                program,
                GasGraphOpHandlerTable.Instance,
                graphId,
                nameof(GraphProgramRegistry));
        }

        public void RequireHostKind(int graphId, GraphKind expected, string hostLabel)
        {
            _ = RequireRegistration(graphId, expected, hostLabel, allowEmpty: true);
        }

        public ReadOnlySpan<GraphInstruction> RequireProgram(int graphId, GraphKind expected, string hostLabel)
            => RequireProgramArray(graphId, expected, hostLabel);

        public GraphInstruction[] RequireProgramArray(int graphId, GraphKind expected, string hostLabel)
            => RequireRegistration(graphId, expected, hostLabel, allowEmpty: false).Program;

        private GraphProgramRegistration RequireRegistration(
            int graphId,
            GraphKind expected,
            string hostLabel,
            bool allowEmpty)
        {
            if (!_programs.TryGetValue(graphId, out GraphProgramRegistration entry))
            {
                throw new InvalidOperationException($"Graph program id {graphId} is not registered.");
            }

            if (entry.Kind != expected)
            {
                throw new InvalidOperationException(
                    $"{GraphKindOperationPolicy.KindMismatchError}: 图 {graphId} 的种类是「{entry.Kind}」，不能挂在{hostLabel}上（这里只接受 {expected}）。");
            }

            if (!allowEmpty && entry.Program.Length == 0)
            {
                throw new InvalidOperationException($"Graph program id {graphId} is not registered.");
            }

            return entry;
        }

        private void EnsureNoInvokeCycle(int graphId)
            => EnsureNoInvokeCycle(graphId, allowMissingTargets: true);

        private void EnsureNoInvokeCycle(int graphId, bool allowMissingTargets)
        {
            if (!GraphYieldPurityValidator.TryValidateNoInvokeCycle(
                    this,
                    graphId,
                    GraphYieldPurityTarget.DescribeGraph(graphId),
                    out string diagnostic,
                    allowMissingTargets: allowMissingTargets))
            {
                throw new InvalidOperationException($"{GraphYieldPurityValidator.InvokeCycleError}: {diagnostic}");
            }
        }

        public void ValidateInvokeTargets()
        {
            EnsureInvokeTargetsAreScript(allowMissingTargets: false, allowUnpatchedFuncLibNames: false);
            foreach (int graphId in _programs.Keys)
            {
                EnsureNoInvokeCycle(graphId, allowMissingTargets: false);
            }
        }

        private void EnsureInvokeTargetsAreScript(bool allowMissingTargets, bool allowUnpatchedFuncLibNames)
        {
            foreach (KeyValuePair<int, GraphProgramRegistration> pair in _programs)
            {
                ValidateProgramInvokeTargets(
                    pair.Key,
                    pair.Value,
                    allowMissingTargets,
                    allowUnpatchedFuncLibNames);
            }
        }

        private void ValidateProgramInvokeTargets(
            int graphId,
            GraphProgramRegistration registration,
            bool allowMissingTargets,
            bool allowUnpatchedFuncLibNames)
        {
            ReadOnlySpan<GraphInstruction> program = registration.Program;
            for (int i = 0; i < program.Length; i++)
            {
                GraphInstruction ins = program[i];
                if (ins.Op != (ushort)GraphNodeOp.InvokeScript)
                {
                    continue;
                }

                if ((ins.Flags & GraphInstructionFlags.FuncLibName) != 0)
                {
                    if (allowUnpatchedFuncLibNames)
                    {
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"InvokeScript.functionName remains unresolved in graph id {graphId} at pc={i}.");
                }

                int targetGraphId = ins.Imm;
                if (targetGraphId <= 0)
                {
                    throw new InvalidOperationException(
                        $"InvokeScript.graphId in graph id {graphId} at pc={i} requires a positive graph id.");
                }

                if (!_programs.TryGetValue(targetGraphId, out GraphProgramRegistration target))
                {
                    if (allowMissingTargets)
                    {
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"InvokeScript target graph id {targetGraphId} is not registered.");
                }

                if (target.Kind != GraphKind.Script)
                {
                    throw new InvalidOperationException(
                        $"InvokeScript target graph id {targetGraphId} must be Script, but is '{target.Kind}'.");
                }
            }
        }

        public bool TryGetProgram(int graphId, out ReadOnlySpan<GraphInstruction> program)
        {
            if (_programs.TryGetValue(graphId, out GraphProgramRegistration entry))
            {
                program = entry.Program;
                return true;
            }

            program = default;
            return false;
        }

        public bool TryGetKind(int graphId, out GraphKind kind)
        {
            if (_programs.TryGetValue(graphId, out GraphProgramRegistration entry) && entry.Kind != GraphKind.None)
            {
                kind = entry.Kind;
                return true;
            }

            kind = GraphKind.None;
            return false;
        }

        public bool TryGetRegistration(int graphId, out GraphProgramRegistration registration)
            => _programs.TryGetValue(graphId, out registration);

        public GraphKind RequireKind(int graphId, GraphKind expected)
        {
            if (!_programs.TryGetValue(graphId, out GraphProgramRegistration entry))
            {
                throw new InvalidOperationException($"Graph program id {graphId} is not registered.");
            }

            if (entry.Kind == GraphKind.None)
            {
                throw new InvalidOperationException(
                    $"Graph program id {graphId} has no authored kind; expected '{expected}'.");
            }

            if (entry.Kind != expected)
            {
                throw new InvalidOperationException(
                    $"Graph program id {graphId} kind is '{entry.Kind}', but '{expected}' is required.");
            }

            return entry.Kind;
        }

        public bool TryGetSourceMap(int graphId, out GraphInstructionSourceMap sourceMap)
            => _sourceMaps.TryGetValue(graphId, out sourceMap);
    }
}
