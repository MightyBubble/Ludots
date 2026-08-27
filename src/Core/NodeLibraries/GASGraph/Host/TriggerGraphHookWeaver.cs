using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Scripting;

namespace Ludots.Core.NodeLibraries.GASGraph.Host
{
    /// <summary>
    /// Route A compile-time hook weaver (#1124): after every graph is registered and
    /// before any map mounts, TriggerGraph entries carrying a hook target
    /// (<c>hookAnchor</c> / <c>hookNodeBefore</c> / <c>hookNodeAfter</c>) have their
    /// entry body spliced into the target graph at the anchor node. The splice is a
    /// document merge followed by one recompile, so register isolation, jump targets,
    /// and symbol interning are produced by the compiler itself instead of a second
    /// instruction encoder; the result lands through
    /// <see cref="GraphProgramRegistry.ReplaceProgram"/>, which re-validates and rolls
    /// back on failure.
    ///
    /// Contracts (all fail closed at load):
    /// - Hook bodies must be control-self-contained: no control edge may enter the
    ///   fragment (other than at the entry start) or leave it. Value producers feeding
    ///   a reached node are pulled into the fragment wholesale — data crosses graphs
    ///   only through the hook point, never implicitly.
    /// - A fragment terminal must be a HaltReturnInt; its halt is dropped and control
    ///   continues at the anchor's original flow. Yield/Return terminals are rejected.
    /// - "before" needs at least one incoming control edge on the anchor (entry-root
    ///   anchors cannot be re-rooted); "after" needs exactly one successor edge.
    /// - Multiple hooks on one anchor chain by entry Priority ascending, ties broken
    ///   by the loader's deterministic compile order (mod merge order proxy).
    /// - Caller→target hook cycles (including self-hooks) fail closed via DFS, matching
    ///   the GraphYieldPurityValidator.InvokeCycleError code pattern.
    ///
    /// Placement note: lives in GASGraph.Host (not GraphRuntime) because weaving needs
    /// BuiltinHandlerRegistry / GraphProgramSymbolPatcher from the GAS namespaces, and
    /// the GraphRuntime architecture guard forbids "Gameplay.GAS" references there.
    /// </summary>
    public static class TriggerGraphHookWeaver
    {
        public const string HookCycleError = "GAS.GRAPH.ERR.HookInvokeCycle";

        private sealed class HookRecord
        {
            public HookRecord(
                string callerGraphId,
                GraphControlFlowDocument callerDocument,
                TriggerGraphEntryConfig entry,
                TriggerGraphHookTargetConfig target,
                string resolvedTargetNodeId,
                int order)
            {
                CallerGraphId = callerGraphId;
                CallerDocument = callerDocument;
                Entry = entry;
                Target = target;
                ResolvedTargetNodeId = resolvedTargetNodeId;
                Order = order;
            }

            public string CallerGraphId { get; }
            public GraphControlFlowDocument CallerDocument { get; }
            public TriggerGraphEntryConfig Entry { get; }
            public TriggerGraphHookTargetConfig Target { get; }
            public string ResolvedTargetNodeId { get; }
            public int Order { get; }
        }

        private sealed class Fragment
        {
            public HashSet<string> NodeIds { get; } = new(StringComparer.Ordinal);
            public List<GraphControlFlowEdge> ControlEdges { get; } = new();
            public List<GraphControlFlowValueEdge> ValueEdges { get; } = new();
            public Dictionary<string, GraphControlFlowNode> NodesById { get; } = new(StringComparer.Ordinal);
            public string EntryStartNodeId { get; set; } = string.Empty;
            public List<string> DroppedHaltNodes { get; } = new();
        }

        public static void Weave(
            GraphProgramRegistry registry,
            IReadOnlyList<KeyValuePair<string, GraphControlFlowDocument>> documents,
            IGraphSymbolResolver symbolResolver,
            EventSchemaRegistry? eventSchemas,
            EntityCollectionStore? entityCollections = null,
            BuiltinHandlerRegistry? builtinHandlers = null,
            Ludots.Core.Scripting.EnumCatalog? enums = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (documents == null) throw new ArgumentNullException(nameof(documents));
            if (symbolResolver == null) throw new ArgumentNullException(nameof(symbolResolver));

            var byId = new Dictionary<string, GraphControlFlowDocument>(StringComparer.OrdinalIgnoreCase);
            var anchors = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var triggerDocIds = new List<string>();
            for (int i = 0; i < documents.Count; i++)
            {
                string id = documents[i].Key;
                GraphControlFlowDocument doc = documents[i].Value;
                if (!GraphKindParser.TryParse(doc.Kind, out GraphKind kind) || kind != GraphKind.TriggerGraph)
                {
                    continue;
                }

                byId[id] = doc;
                anchors[id] = BuildAnchorMap(id, doc);
                triggerDocIds.Add(id);
            }

            List<HookRecord> hooks = CollectHooks(triggerDocIds, byId, anchors);
            if (hooks.Count == 0)
            {
                return;
            }

            DetectCycles(hooks);

            var byTarget = new Dictionary<string, List<HookRecord>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < hooks.Count; i++)
            {
                HookRecord hook = hooks[i];
                if (!byTarget.TryGetValue(hook.Target.TargetGraphId, out List<HookRecord> group))
                {
                    group = new List<HookRecord>();
                    byTarget[hook.Target.TargetGraphId] = group;
                }

                group.Add(hook);
            }

            JsonSerializerOptions cloneOptions = Ludots.Core.Config.StrictJsonOptions.CreateCamelCase(includeFields: true);
            foreach (KeyValuePair<string, List<HookRecord>> pair in byTarget)
            {
                pair.Value.Sort(CompareHookOrder);
                WeaveTarget(
                    registry, pair.Key, byId[pair.Key], pair.Value,
                    symbolResolver, eventSchemas, entityCollections, builtinHandlers, cloneOptions, enums);
            }
        }

        private static int CompareHookOrder(HookRecord left, HookRecord right)
        {
            int byPriority = left.Entry.Priority.CompareTo(right.Entry.Priority);
            return byPriority != 0 ? byPriority : left.Order.CompareTo(right.Order);
        }

        private static Dictionary<string, string> BuildAnchorMap(string graphId, GraphControlFlowDocument doc)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            List<GraphControlFlowNode> nodes = doc.Nodes ?? new List<GraphControlFlowNode>();
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
                    throw new InvalidOperationException(
                        $"TriggerGraphHookWeaver: graph '{graphId}' node '{nodes[i].Id}' declares an empty anchor.");
                }

                if (map.TryGetValue(trimmed, out string existing))
                {
                    throw new InvalidOperationException(
                        $"TriggerGraphHookWeaver: graph '{graphId}' declares anchor '{trimmed}' on both node '{existing}' " +
                        $"and node '{nodes[i].Id}'; anchor names are the cross-mod hook contract and must be unique.");
                }

                map[trimmed] = nodes[i].Id;
            }

            return map;
        }

        private static List<HookRecord> CollectHooks(
            List<string> triggerDocIds,
            Dictionary<string, GraphControlFlowDocument> byId,
            Dictionary<string, Dictionary<string, string>> anchors)
        {
            var hooks = new List<HookRecord>();
            int order = 0;
            for (int d = 0; d < triggerDocIds.Count; d++)
            {
                string callerId = triggerDocIds[d];
                GraphControlFlowDocument caller = byId[callerId];
                List<TriggerGraphEntryConfig> entries = caller.Entries ?? new List<TriggerGraphEntryConfig>();
                for (int e = 0; e < entries.Count; e++)
                {
                    TriggerGraphEntryConfig entry = entries[e];
                    if (!TryReadHookTarget(entry, out string targetGraphId, out string targetNodeRef, out bool viaAnchor, out bool before, out string? error))
                    {
                        if (error != null)
                        {
                            throw new InvalidOperationException($"TriggerGraphHookWeaver: {error}");
                        }

                        continue;
                    }

                    if (!byId.TryGetValue(targetGraphId, out GraphControlFlowDocument targetDoc))
                    {
                        throw new InvalidOperationException(
                            $"TriggerGraphHookWeaver: graph '{callerId}' entry '{entry.Label}' hooks graph " +
                            $"'{targetGraphId}' which is not a registered TriggerGraph authoring document.");
                    }

                    string resolvedNodeId;
                    if (viaAnchor)
                    {
                        if (!anchors[targetGraphId].TryGetValue(targetNodeRef, out resolvedNodeId))
                        {
                            throw new InvalidOperationException(
                                $"TriggerGraphHookWeaver: graph '{callerId}' entry '{entry.Label}' hooks anchor " +
                                $"'{targetNodeRef}' which graph '{targetGraphId}' does not declare.");
                        }
                    }
                    else
                    {
                        resolvedNodeId = targetNodeRef;
                        bool nodeExists = targetDoc.Nodes?.Any(n => string.Equals(n.Id, resolvedNodeId, StringComparison.Ordinal)) == true;
                        if (!nodeExists)
                        {
                            throw new InvalidOperationException(
                                $"TriggerGraphHookWeaver: graph '{callerId}' entry '{entry.Label}' hooks node " +
                                $"'{resolvedNodeId}' which does not exist in graph '{targetGraphId}'.");
                        }
                    }

                    var target = new TriggerGraphHookTargetConfig(targetGraphId, resolvedNodeId, before);
                    hooks.Add(new HookRecord(callerId, caller, entry, target, resolvedNodeId, order++));
                }
            }

            return hooks;
        }

        private static bool TryReadHookTarget(
            TriggerGraphEntryConfig entry,
            out string targetGraphId,
            out string targetNodeRef,
            out bool viaAnchor,
            out bool before,
            out string? error)
        {
            targetGraphId = string.Empty;
            targetNodeRef = string.Empty;
            viaAnchor = false;
            before = true;
            int blocks = (entry.HookAnchor != null ? 1 : 0) +
                (entry.HookNodeBefore != null ? 1 : 0) +
                (entry.HookNodeAfter != null ? 1 : 0);
            if (blocks == 0)
            {
                error = null;
                return false;
            }

            if (blocks > 1)
            {
                error = $"graph entry '{entry.Label}' declares more than one hook block; combine exactly one of " +
                    "hookAnchor / hookNodeBefore / hookNodeAfter.";
                return false;
            }

            string position;
            if (entry.HookAnchor != null)
            {
                targetGraphId = (entry.HookAnchor.GraphId ?? string.Empty).Trim();
                targetNodeRef = (entry.HookAnchor.Anchor ?? string.Empty).Trim();
                position = (entry.HookAnchor.Position ?? "before").Trim();
                viaAnchor = true;
            }
            else
            {
                TriggerGraphHookNodeConfig nodeHook = entry.HookNodeBefore ?? entry.HookNodeAfter!;
                targetGraphId = (nodeHook.GraphId ?? string.Empty).Trim();
                targetNodeRef = (nodeHook.NodeId ?? string.Empty).Trim();
                position = entry.HookNodeBefore != null ? "before" : "after";
            }

            string context = $"entry '{entry.Label}'";
            if (targetGraphId.Length == 0)
            {
                error = $"{context} hook target requires a non-empty 'graphId'.";
                return false;
            }

            if (targetNodeRef.Length == 0)
            {
                error = $"{context} hook target requires a non-empty '{(viaAnchor ? "anchor" : "nodeId")}'.";
                return false;
            }

            if (position == "before")
            {
                before = true;
            }
            else if (position == "after")
            {
                before = false;
            }
            else
            {
                error = $"{context} hook 'position' must be \"before\" or \"after\" (got '{position}').";
                return false;
            }

            error = null;
            return true;
        }

        private static void DetectCycles(List<HookRecord> hooks)
        {
            var edges = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < hooks.Count; i++)
            {
                if (!edges.TryGetValue(hooks[i].CallerGraphId, out List<string> targets))
                {
                    targets = new List<string>();
                    edges[hooks[i].CallerGraphId] = targets;
                }

                targets.Add(hooks[i].Target.TargetGraphId);
            }

            var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var path = new List<string>();
            foreach (string start in edges.Keys.ToList())
            {
                Walk(start);
            }

            void Walk(string node)
            {
                state[node] = 1;
                path.Add(node);
                if (edges.TryGetValue(node, out List<string> targets))
                {
                    for (int i = 0; i < targets.Count; i++)
                    {
                        string next = targets[i];
                        if (!state.TryGetValue(next, out int nextState))
                        {
                            Walk(next);
                            continue;
                        }

                        if (nextState == 1)
                        {
                            throw new InvalidOperationException(
                                $"{HookCycleError}: hook dependency cycle {string.Join(" -> ", path)} -> {next}; " +
                                "hook chains must be acyclic because weaving inlines bodies at compile time.");
                        }
                    }
                }

                path.RemoveAt(path.Count - 1);
                state[node] = 2;
            }
        }

        private static void WeaveTarget(
            GraphProgramRegistry registry,
            string targetGraphId,
            GraphControlFlowDocument target,
            List<HookRecord> hooks,
            IGraphSymbolResolver symbolResolver,
            EventSchemaRegistry? eventSchemas,
            EntityCollectionStore? entityCollections,
            BuiltinHandlerRegistry? builtinHandlers,
            JsonSerializerOptions cloneOptions,
            Ludots.Core.Scripting.EnumCatalog? enums = null)
        {
            // Group hooks by (resolved node, position): every group is one chain on the
            // anchor, ordered by (priority, compile order) as sorted by the caller.
            var groups = new List<(string NodeId, bool Before, List<HookRecord> Records)>();
            for (int i = 0; i < hooks.Count; i++)
            {
                int index = groups.FindIndex(g => g.NodeId == hooks[i].ResolvedTargetNodeId && g.Before == hooks[i].Target.Before);
                if (index < 0)
                {
                    groups.Add((hooks[i].ResolvedTargetNodeId, hooks[i].Target.Before, new List<HookRecord> { hooks[i] }));
                }
                else
                {
                    groups[index].Records.Add(hooks[i]);
                }
            }

            // Pass 1: extract every fragment and compute chain entries/exits.
            var groupPlans = new List<(string ChainEntry, string[] FragmentEntries, string Continuation, Fragment[] Fragments, List<HookRecord> Chain, bool Before, string NodeId)>();
            for (int g = 0; g < groups.Count; g++)
            {
                List<HookRecord> chain = groups[g].Records;
                string nodeId = groups[g].NodeId;
                bool before = groups[g].Before;

                List<GraphControlFlowEdge> targetEdges = target.ControlEdges ?? new List<GraphControlFlowEdge>();
                string continuation;
                if (before)
                {
                    if (targetEdges.Count(edge => edge.To == nodeId) == 0)
                    {
                        throw new InvalidOperationException(
                            $"TriggerGraphHookWeaver: 'before' hook chain into graph '{targetGraphId}' node '{nodeId}' " +
                            "has no incoming control edge; entry-root anchors cannot be re-rooted (hook the predecessor instead).");
                    }

                    continuation = nodeId;
                }
                else
                {
                    List<GraphControlFlowEdge> outgoing = targetEdges.Where(edge => edge.From == nodeId).ToList();
                    if (outgoing.Count != 1)
                    {
                        throw new InvalidOperationException(
                            $"TriggerGraphHookWeaver: 'after' hook chain into graph '{targetGraphId}' node '{nodeId}' " +
                            $"requires exactly one successor control edge (found {outgoing.Count}); branch nodes need explicit anchor placement.");
                    }

                    continuation = outgoing[0].To;
                }

                var fragments = new Fragment[chain.Count];
                var fragmentEntries = new string[chain.Count];
                for (int i = 0; i < chain.Count; i++)
                {
                    fragments[i] = ExtractFragment(chain[i]);
                    fragmentEntries[i] = PrefixedId(chain[i].CallerGraphId, fragments[i].EntryStartNodeId);
                }

                groupPlans.Add((fragmentEntries[0], fragmentEntries, continuation, fragments, chain, before, nodeId));
            }

            // Build the merged document from the untouched target shape.
            var merged = new GraphControlFlowDocument
            {
                Id = target.Id,
                Kind = target.Kind,
                Entry = target.Entry,
                Entries = new List<TriggerGraphEntryConfig>(target.Entries ?? new List<TriggerGraphEntryConfig>()),
                Nodes = new List<GraphControlFlowNode>(target.Nodes ?? new List<GraphControlFlowNode>()),
                ControlEdges = new List<GraphControlFlowEdge>(target.ControlEdges ?? new List<GraphControlFlowEdge>()),
                ValueEdges = new List<GraphControlFlowValueEdge>(target.ValueEdges ?? new List<GraphControlFlowValueEdge>()),
                Outputs = target.Outputs,
            };

            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < merged.Nodes.Count; i++)
            {
                nodeIds.Add(merged.Nodes[i].Id);
            }

            // Anchor-side rewiring FIRST so later-added fragment edges to the anchor are
            // never clobbered: original predecessors (before) / the successor edge (after)
            // now enter the chain head instead of the anchor flow directly.
            for (int p = 0; p < groupPlans.Count; p++)
            {
                var plan = groupPlans[p];
                if (plan.Before)
                {
                    for (int i = 0; i < merged.ControlEdges.Count; i++)
                    {
                        GraphControlFlowEdge edge = merged.ControlEdges[i];
                        if (edge.To == plan.NodeId && !IsWovenId(edge.From))
                        {
                            edge.To = plan.ChainEntry;
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < merged.ControlEdges.Count; i++)
                    {
                        GraphControlFlowEdge edge = merged.ControlEdges[i];
                        if (edge.From == plan.NodeId && edge.To == plan.Continuation && !IsWovenId(edge.From))
                        {
                            edge.To = plan.ChainEntry;
                            break;
                        }
                    }
                }
            }

            // Pass 2: append fragments; each fragment's dropped-halt terminals hand
            // control to the next fragment entry, the last to the continuation.
            for (int p = 0; p < groupPlans.Count; p++)
            {
                var plan = groupPlans[p];
                for (int i = 0; i < plan.Chain.Count; i++)
                {
                    string exit = i + 1 < plan.FragmentEntries.Length ? plan.FragmentEntries[i + 1] : plan.Continuation;
                    AppendFragment(merged, plan.Chain[i], plan.Fragments[i], exit, cloneOptions, nodeIds);
                }
            }

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(merged, eventSchemas, enums);
            List<GraphDiagnostic> errors = compiled.Diagnostics.Where(d => d.Severity == GraphDiagnosticSeverity.Error).ToList();
            if (errors.Count > 0 || compiled.Package == null)
            {
                throw new InvalidOperationException(
                    $"TriggerGraphHookWeaver: woven graph '{targetGraphId}' failed to compile: " +
                    string.Join(" | ", errors.Select(d => $"{d.Code} {d.Message}")));
            }

            GraphProgramPackage package = compiled.Package.Value;
            GraphProgramSymbolPatcher.Patch(package.Symbols, package.Program, symbolResolver, entityCollections, builtinHandlers);

            int graphId = GraphIdRegistry.GetId(targetGraphId);
            registry.ReplaceProgram(
                graphId,
                package.Program,
                GraphKind.TriggerGraph,
                compiled.SourceMap,
                package.Symbols,
                package.TriggerGraphEntries);
        }

        private static bool IsWovenId(string nodeId)
        {
            return nodeId.IndexOf("::", StringComparison.Ordinal) >= 0;
        }

        private static Fragment ExtractFragment(HookRecord hook)
        {
            GraphControlFlowDocument caller = hook.CallerDocument;
            string start = (hook.Entry.Start ?? string.Empty).Trim();
            List<GraphControlFlowNode> nodes = caller.Nodes ?? new List<GraphControlFlowNode>();
            bool startExists = nodes.Any(n => string.Equals(n.Id, start, StringComparison.Ordinal));
            if (start.Length == 0 || !startExists)
            {
                throw new InvalidOperationException(
                    $"TriggerGraphHookWeaver: graph '{hook.CallerGraphId}' hook entry '{hook.Entry.Label}' start node " +
                    $"'{start}' does not exist.");
            }

            List<GraphControlFlowEdge> control = caller.ControlEdges ?? new List<GraphControlFlowEdge>();
            List<GraphControlFlowValueEdge> values = caller.ValueEdges ?? new List<GraphControlFlowValueEdge>();

            // Reachability: control flow forward from the entry start, plus value
            // producers feeding any reached node (pulled in wholesale — a fragment may
            // not read values authored outside itself).
            var reachable = new HashSet<string>(StringComparer.Ordinal);
            reachable.Add(start);
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < control.Count; i++)
                {
                    if (reachable.Contains(control[i].From) && reachable.Add(control[i].To))
                    {
                        changed = true;
                    }
                }

                for (int i = 0; i < values.Count; i++)
                {
                    if (reachable.Contains(values[i].To) && reachable.Add(values[i].From))
                    {
                        changed = true;
                    }
                }
            }

            var fragment = new Fragment { EntryStartNodeId = start };
            var carriesControl = new HashSet<string>(StringComparer.Ordinal);
            foreach (string nodeId in reachable)
            {
                fragment.NodeIds.Add(nodeId);
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                if (reachable.Contains(nodes[i].Id))
                {
                    fragment.NodesById[nodes[i].Id] = nodes[i];
                }
            }

            for (int i = 0; i < control.Count; i++)
            {
                bool fromIn = reachable.Contains(control[i].From);
                bool toIn = reachable.Contains(control[i].To);
                if (fromIn && toIn)
                {
                    fragment.ControlEdges.Add(control[i]);
                    carriesControl.Add(control[i].From);
                    carriesControl.Add(control[i].To);
                    continue;
                }

                if (toIn && control[i].To != start)
                {
                    throw new InvalidOperationException(
                        $"TriggerGraphHookWeaver: hook body '{hook.CallerGraphId}'#{hook.Entry.Label} has a control edge from " +
                        $"'{control[i].From}' into non-entry node '{control[i].To}'; fragments must be control-self-contained.");
                }

                if (fromIn)
                {
                    throw new InvalidOperationException(
                        $"TriggerGraphHookWeaver: hook body '{hook.CallerGraphId}'#{hook.Entry.Label} has a control edge leaving " +
                        $"to '{control[i].To}'; fragments must be control-self-contained.");
                }
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (reachable.Contains(values[i].From) && reachable.Contains(values[i].To))
                {
                    fragment.ValueEdges.Add(values[i]);
                }
            }

            // Terminals: nodes that carry control but have no outgoing control edge must
            // be Halt returns — the halt is dropped so the target's flow continues.
            foreach (string nodeId in reachable)
            {
                if (!carriesControl.Contains(nodeId))
                {
                    continue;
                }

                if (ControlOutDegree(nodeId, control) > 0)
                {
                    continue;
                }

                string op = (fragment.NodesById[nodeId].Op ?? string.Empty).Trim();
                if (op == "HaltReturnInt")
                {
                    fragment.DroppedHaltNodes.Add(nodeId);
                    continue;
                }

                throw new InvalidOperationException(
                    $"TriggerGraphHookWeaver: hook body '{hook.CallerGraphId}'#{hook.Entry.Label} terminal node '{nodeId}' " +
                    $"has op '{op}'; hook fragments must end in HaltReturnInt (Yield/Return terminals are rejected).");
            }

            if (fragment.DroppedHaltNodes.Contains(start))
            {
                throw new InvalidOperationException(
                    $"TriggerGraphHookWeaver: hook body '{hook.CallerGraphId}'#{hook.Entry.Label} is empty " +
                    "(the entry start is itself the terminal halt).");
            }

            return fragment;
        }

        private static int ControlOutDegree(string nodeId, List<GraphControlFlowEdge> control)
        {
            int degree = 0;
            for (int i = 0; i < control.Count; i++)
            {
                if (control[i].From == nodeId)
                {
                    degree++;
                }
            }

            return degree;
        }

        private static void AppendFragment(
            GraphControlFlowDocument merged,
            HookRecord hook,
            Fragment fragment,
            string exitContinuation,
            JsonSerializerOptions cloneOptions,
            HashSet<string> nodeIds)
        {
            foreach (string nodeId in fragment.NodeIds)
            {
                if (fragment.DroppedHaltNodes.Contains(nodeId))
                {
                    continue;
                }

                GraphControlFlowNode clone = CloneNode(fragment.NodesById[nodeId], cloneOptions);
                clone.Id = PrefixedId(hook.CallerGraphId, nodeId);
                if (!nodeIds.Add(clone.Id))
                {
                    throw new InvalidOperationException(
                        $"TriggerGraphHookWeaver: woven node id '{clone.Id}' collides inside graph '{merged.Id}'.");
                }

                merged.Nodes.Add(clone);
            }

            foreach (GraphControlFlowEdge edge in fragment.ControlEdges)
            {
                string to = fragment.DroppedHaltNodes.Contains(edge.To)
                    ? exitContinuation
                    : PrefixedId(hook.CallerGraphId, edge.To);
                merged.ControlEdges.Add(new GraphControlFlowEdge(
                    PrefixedId(hook.CallerGraphId, edge.From),
                    edge.FromPort,
                    to));
            }

            foreach (GraphControlFlowValueEdge edge in fragment.ValueEdges)
            {
                merged.ValueEdges.Add(new GraphControlFlowValueEdge(
                    PrefixedId(hook.CallerGraphId, edge.From),
                    edge.FromPort,
                    PrefixedId(hook.CallerGraphId, edge.To),
                    edge.ToPort));
            }
        }

        private static string PrefixedId(string callerGraphId, string nodeId)
        {
            return $"{callerGraphId}::{nodeId}";
        }

        private static GraphControlFlowNode CloneNode(GraphControlFlowNode node, JsonSerializerOptions options)
        {
            string json = JsonSerializer.Serialize(node, options);
            return JsonSerializer.Deserialize<GraphControlFlowNode>(json, options)
                ?? throw new InvalidOperationException($"TriggerGraphHookWeaver: node '{node.Id}' failed to round-trip clone.");
        }
    }
}
