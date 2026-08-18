using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace Ludots.Core.UI.PanelActivation
{
    /// <summary>
    /// Visibility orchestration graph entry (#1014): one panelType decided by one
    /// compiled Script program. Context signals reach the graph as blackboard values
    /// on the context entity (ReadBlackboardInt); the program halts with a nonzero
    /// return meaning visible.
    /// </summary>
    public sealed class PanelOrchestrationEntry
    {
        public PanelOrchestrationEntry(string panelType, GraphInstruction[] program, string[]? symbols = null)
        {
            if (string.IsNullOrWhiteSpace(panelType))
            {
                throw new ArgumentException("Panel type is required.", nameof(panelType));
            }

            ArgumentNullException.ThrowIfNull(program);
            if (program.Length == 0)
            {
                throw new ArgumentException($"Orchestration graph for '{panelType}' is empty.", nameof(program));
            }

            PanelType = panelType.Trim();
            Program = program;
            Symbols = symbols ?? Array.Empty<string>();
        }

        public string PanelType { get; }
        public GraphInstruction[] Program { get; }
        public string[] Symbols { get; }
    }

    /// <summary>
    /// The single writer of <see cref="UiPanelActivationStore"/> (constitution contract
    /// five). Runs each entry's Script graph to halt against the context entity and
    /// applies the resulting activation set. Panels and interaction code never touch
    /// activation — they emit events that eventually feed the blackboard signals.
    /// </summary>
    public sealed class PanelOrchestrationRuntime
    {
        private static readonly PanelActivationWriteToken WriteToken = new(0);
        private static readonly ConfigOnlySymbolResolver ConfigResolver = new();

        private readonly List<PanelOrchestrationEntry> _entries;
        private readonly UiPanelActivationStore _store;
        private readonly Dictionary<string, GraphInstruction[]> _patched = new(StringComparer.Ordinal);

        public PanelOrchestrationRuntime(IEnumerable<PanelOrchestrationEntry> entries, UiPanelActivationStore store)
        {
            ArgumentNullException.ThrowIfNull(entries);
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _entries = new List<PanelOrchestrationEntry>(entries);
            foreach (PanelOrchestrationEntry entry in _entries)
            {
                GraphInstruction[] copy = (GraphInstruction[])entry.Program.Clone();
                if (entry.Symbols.Length > 0)
                {
                    GraphProgramSymbolPatcher.Patch(entry.Symbols, copy, ConfigResolver);
                }

                _patched.Add(entry.PanelType, copy);
            }
        }

        public UiPanelActivationStore Store => _store;

        public PanelActivationDiff EvaluateAll(World world, IGraphRuntimeApi api, Entity contextEntity)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(api);
            if (contextEntity == Entity.Null || !world.IsAlive(contextEntity))
            {
                throw new InvalidOperationException("Panel orchestration requires a live context entity.");
            }

            var desired = new Dictionary<string, bool>(_patched.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, GraphInstruction[]> patched in _patched)
            {
                desired[patched.Key] = ExecuteToHalt(world, api, contextEntity, patched.Value);
            }

            return _store.Apply(WriteToken, desired);
        }

        private static bool ExecuteToHalt(World world, IGraphRuntimeApi api, Entity contextEntity, GraphInstruction[] program)
        {
            var floats = new float[GraphVmLimits.MaxFloatRegisters];
            var ints = new int[GraphVmLimits.MaxIntRegisters];
            var entities = new Entity[GraphVmLimits.MaxEntityRegisters];
            var bools = new byte[GraphVmLimits.MaxBoolRegisters];
            var targets = new Entity[GraphVmLimits.MaxTargets];
            entities[0] = contextEntity;
            entities[1] = contextEntity;
            var state = new GraphExecutionState
            {
                World = world,
                Api = api,
                Caster = contextEntity,
                ExplicitTarget = contextEntity,
                F = floats,
                I = ints,
                E = entities,
                B = bools,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
                CallStack = new int[GraphVmLimits.MaxCallStackDepth],
                CallStackCount = 0,
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
            return state.ReturnInt != 0;
        }

        /// <summary>
        /// Blackboard/config symbols resolve through their registries; anything else has no
        /// business inside a visibility orchestration graph and fails loudly.
        /// </summary>
        private sealed class ConfigOnlySymbolResolver : IGraphSymbolResolver
        {
            public int ResolveTag(string name) => Fail(name, "tag");
            public int ResolveAttribute(string name) => Fail(name, "attribute");
            public int ResolveEffectTemplate(string name) => Fail(name, "effect template");
            public int ResolveRelationshipType(string name) => Fail(name, "relationship type");
            public int ResolveRelationshipMetric(string name) => Fail(name, "relationship metric");
            public int ResolveRelationshipFlag(string name) => Fail(name, "relationship flag");
            public int ResolveRelationshipReason(string name) => Fail(name, "relationship reason");
            public int ResolveTargetDispatchPreset(string name) => Fail(name, "target dispatch preset");
            public int ResolveEntityTemplate(string name) => Fail(name, "entity template");

            private static int Fail(string name, string kind)
            {
                throw new InvalidOperationException(
                    $"Panel orchestration graphs must not reference {kind} '{name}'; context signals enter as blackboard keys.");
            }
        }
    }
}
