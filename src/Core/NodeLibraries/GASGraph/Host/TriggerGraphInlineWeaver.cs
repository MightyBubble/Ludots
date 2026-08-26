using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph.Host
{
    /// <summary>
    /// Compile-time InlineGraph (Unreal-macro style): splice a reusable TriggerGraph
    /// fragment into the host document before compile so AwaitCallback/Yield may live
    /// inside the reusable body. Runtime <c>InvokeGraph</c> stays sync-only
    /// (<c>ContainsYield</c> unchanged). Zero new opcodes.
    /// </summary>
    public static class TriggerGraphInlineWeaver
    {
        public const int MaxInlineDepth = 8;
        public const string InlineCycleError = "GAS.GRAPH.ERR.InlineGraphCycle";

        private sealed class InlineSite
        {
            public InlineSite(string hostGraphId, string siteNodeId, string macroGraphId, string entryLabel, int depth)
            {
                HostGraphId = hostGraphId;
                SiteNodeId = siteNodeId;
                MacroGraphId = macroGraphId;
                EntryLabel = entryLabel;
                Depth = depth;
            }

            public string HostGraphId { get; }
            public string SiteNodeId { get; }
            public string MacroGraphId { get; }
            public string EntryLabel { get; }
            public int Depth { get; }
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

        /// <summary>
        /// Expands every <see cref="GraphAuthoringSugar.InlineGraph"/> site in TriggerGraph
        /// documents in place. Nested inlines expand until fixed point or depth budget.
        /// </summary>
        public static void ExpandDocuments(IDictionary<string, GraphControlFlowDocument> documents)
        {
            if (documents == null) throw new ArgumentNullException(nameof(documents));

            JsonSerializerOptions cloneOptions = StrictJsonOptions.CreateCamelCase(includeFields: true);
            bool progressed = true;
            int pass = 0;
            while (progressed)
            {
                progressed = false;
                pass++;
                if (pass > MaxInlineDepth * documents.Count + 1)
                {
                    throw new InvalidOperationException(
                        $"TriggerGraphInlineWeaver: expand did not converge within {pass} passes.");
                }

                var hosts = documents.Keys.ToList();
                for (int h = 0; h < hosts.Count; h++)
                {
                    string hostId = hosts[h];
                    GraphControlFlowDocument host = documents[hostId];
                    if (!GraphKindParser.TryParse(host.Kind, out GraphKind hostKind) || hostKind != GraphKind.TriggerGraph)
                    {
                        continue;
                    }

                    List<InlineSite> sites = CollectSites(hostId, host, depth: 0);
                    if (sites.Count == 0)
                    {
                        continue;
                    }

                    for (int s = 0; s < sites.Count; s++)
                    {
                        ExpandSite(documents, hostId, sites[s], cloneOptions, stack: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            hostId
                        });
                        progressed = true;
                    }
                }
            }

            // Fail closed if any InlineGraph sugar remains (nested expand miss / wrong kind).
            foreach (KeyValuePair<string, GraphControlFlowDocument> pair in documents)
            {
                List<GraphControlFlowNode> nodes = pair.Value.Nodes ?? new List<GraphControlFlowNode>();
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (string.Equals(nodes[i].Op, GraphAuthoringSugar.InlineGraph, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"TriggerGraphInlineWeaver: graph '{pair.Key}' still contains InlineGraph node '{nodes[i].Id}' " +
                            "after expand; InlineGraph is TriggerGraph-only compile-time sugar.");
                    }
                }
            }
        }

        private static List<InlineSite> CollectSites(string hostGraphId, GraphControlFlowDocument host, int depth)
        {
            var sites = new List<InlineSite>();
            List<GraphControlFlowNode> nodes = host.Nodes ?? new List<GraphControlFlowNode>();
            for (int i = 0; i < nodes.Count; i++)
            {
                GraphControlFlowNode node = nodes[i];
                if (!string.Equals(node.Op, GraphAuthoringSugar.InlineGraph, StringComparison.Ordinal))
                {
                    continue;
                }

                string macroId = (node.FunctionName ?? string.Empty).Trim();
                if (macroId.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"TriggerGraphInlineWeaver: graph '{hostGraphId}' InlineGraph node '{node.Id}' requires functionName " +
                        "(macro graph id).");
                }

                if (node.GraphId > 0)
                {
                    throw new InvalidOperationException(
                        $"TriggerGraphInlineWeaver: graph '{hostGraphId}' InlineGraph node '{node.Id}' must use functionName, " +
                        "not load-order graphId.");
                }

                sites.Add(new InlineSite(
                    hostGraphId,
                    node.Id,
                    macroId,
                    (node.EntryLabel ?? string.Empty).Trim(),
                    depth));
            }

            return sites;
        }

        private static void ExpandSite(
            IDictionary<string, GraphControlFlowDocument> documents,
            string hostGraphId,
            InlineSite site,
            JsonSerializerOptions cloneOptions,
            HashSet<string> stack)
        {
            if (site.Depth >= MaxInlineDepth)
            {
                throw new InvalidOperationException(
                    $"TriggerGraphInlineWeaver: InlineGraph depth exceeded {MaxInlineDepth} at host '{hostGraphId}' " +
                    $"site '{site.SiteNodeId}' → '{site.MacroGraphId}'.");
            }

            if (!documents.TryGetValue(site.MacroGraphId, out GraphControlFlowDocument? macro) || macro == null)
            {
                // Case-insensitive fallback — registry keys are ordinal-ignore-case elsewhere.
                foreach (KeyValuePair<string, GraphControlFlowDocument> pair in documents)
                {
                    if (string.Equals(pair.Key, site.MacroGraphId, StringComparison.OrdinalIgnoreCase))
                    {
                        macro = pair.Value;
                        break;
                    }
                }
            }

            if (macro == null)
            {
                throw new InvalidOperationException(
                    $"TriggerGraphInlineWeaver: graph '{hostGraphId}' InlineGraph '{site.SiteNodeId}' references unknown " +
                    $"macro graph '{site.MacroGraphId}'.");
            }

            if (!GraphKindParser.TryParse(macro.Kind, out GraphKind macroKind) || macroKind != GraphKind.TriggerGraph)
            {
                throw new InvalidOperationException(
                    $"TriggerGraphInlineWeaver: macro '{site.MacroGraphId}' must be TriggerGraph (got '{macro.Kind}').");
            }

            string macroKey = documents.Keys.First(k =>
                string.Equals(k, site.MacroGraphId, StringComparison.OrdinalIgnoreCase));
            if (!stack.Add(macroKey))
            {
                throw new InvalidOperationException(
                    $"{InlineCycleError}: InlineGraph cycle involving '{hostGraphId}' → '{macroKey}'.");
            }

            // Expand nested InlineGraph inside the macro document first (in place).
            List<InlineSite> nested = CollectSites(macroKey, macro, site.Depth + 1);
            for (int i = 0; i < nested.Count; i++)
            {
                ExpandSite(documents, macroKey, nested[i], cloneOptions, stack);
            }

            stack.Remove(macroKey);

            GraphControlFlowDocument host = documents[hostGraphId];
            string start = ResolveMacroStart(macroKey, macro, site.EntryLabel);
            Fragment fragment = ExtractFragment(macroKey, macro, start);
            SpliceIntoHost(host, hostGraphId, site.SiteNodeId, macroKey, fragment, cloneOptions);
        }

        private static string ResolveMacroStart(string macroGraphId, GraphControlFlowDocument macro, string entryLabel)
        {
            List<TriggerGraphEntryConfig> entries = macro.Entries ?? new List<TriggerGraphEntryConfig>();
            if (entryLabel.Length > 0)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (string.Equals(entries[i].Label, entryLabel, StringComparison.Ordinal))
                    {
                        string labeled = (entries[i].Start ?? string.Empty).Trim();
                        if (labeled.Length == 0)
                        {
                            throw new InvalidOperationException(
                                $"TriggerGraphInlineWeaver: macro '{macroGraphId}' entry '{entryLabel}' has empty start.");
                        }

                        return labeled;
                    }
                }

                throw new InvalidOperationException(
                    $"TriggerGraphInlineWeaver: macro '{macroGraphId}' has no entry label '{entryLabel}'.");
            }

            if (entries.Count > 0)
            {
                string first = (entries[0].Start ?? string.Empty).Trim();
                if (first.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"TriggerGraphInlineWeaver: macro '{macroGraphId}' first entry has empty start.");
                }

                return first;
            }

            string entry = (macro.Entry ?? string.Empty).Trim();
            if (entry.Length == 0)
            {
                throw new InvalidOperationException(
                    $"TriggerGraphInlineWeaver: macro '{macroGraphId}' has no Entries/Entry start for InlineGraph.");
            }

            return entry;
        }

        private static Fragment ExtractFragment(string macroGraphId, GraphControlFlowDocument macro, string start)
        {
            List<GraphControlFlowNode> nodes = macro.Nodes ?? new List<GraphControlFlowNode>();
            bool startExists = nodes.Any(n => string.Equals(n.Id, start, StringComparison.Ordinal));
            if (!startExists)
            {
                throw new InvalidOperationException(
                    $"TriggerGraphInlineWeaver: macro '{macroGraphId}' start node '{start}' does not exist.");
            }

            List<GraphControlFlowEdge> control = macro.ControlEdges ?? new List<GraphControlFlowEdge>();
            List<GraphControlFlowValueEdge> values = macro.ValueEdges ?? new List<GraphControlFlowValueEdge>();

            var reachable = new HashSet<string>(StringComparer.Ordinal) { start };
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
                        $"TriggerGraphInlineWeaver: macro '{macroGraphId}' fragment has control edge from '{control[i].From}' " +
                        $"into non-entry '{control[i].To}'; fragments must be control-self-contained.");
                }

                if (fromIn)
                {
                    throw new InvalidOperationException(
                        $"TriggerGraphInlineWeaver: macro '{macroGraphId}' fragment has control edge leaving to " +
                        $"'{control[i].To}'; fragments must be control-self-contained.");
                }
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (reachable.Contains(values[i].From) && reachable.Contains(values[i].To))
                {
                    fragment.ValueEdges.Add(values[i]);
                }
            }

            foreach (string nodeId in reachable)
            {
                if (!carriesControl.Contains(nodeId) && nodeId != start)
                {
                    continue;
                }

                if (ControlOutDegree(nodeId, control) > 0)
                {
                    continue;
                }

                // Pure value-only nodes pulled in as producers are not terminals.
                if (!carriesControl.Contains(nodeId) && nodeId != start)
                {
                    continue;
                }

                string op = (fragment.NodesById[nodeId].Op ?? string.Empty).Trim();
                if (op == "HaltReturnInt")
                {
                    fragment.DroppedHaltNodes.Add(nodeId);
                    continue;
                }

                // Mid-fragment AwaitCallback/Yield continue after resume; they are not exits.
                // A fragment that ends on Yield/Await without a Halt is rejected — parent needs
                // a defined continuation edge after the macro returns.
                throw new InvalidOperationException(
                    $"TriggerGraphInlineWeaver: macro '{macroGraphId}' terminal node '{nodeId}' has op '{op}'; " +
                    "InlineGraph fragments must end in HaltReturnInt (AwaitCallback/Yield may appear mid-fragment).");
            }

            if (fragment.DroppedHaltNodes.Contains(start))
            {
                throw new InvalidOperationException(
                    $"TriggerGraphInlineWeaver: macro '{macroGraphId}' fragment is empty (start is the terminal halt).");
            }

            if (fragment.DroppedHaltNodes.Count == 0)
            {
                throw new InvalidOperationException(
                    $"TriggerGraphInlineWeaver: macro '{macroGraphId}' fragment has no HaltReturnInt terminal.");
            }

            return fragment;
        }

        private static void SpliceIntoHost(
            GraphControlFlowDocument host,
            string hostGraphId,
            string siteNodeId,
            string macroGraphId,
            Fragment fragment,
            JsonSerializerOptions cloneOptions)
        {
            host.Nodes ??= new List<GraphControlFlowNode>();
            host.ControlEdges ??= new List<GraphControlFlowEdge>();
            host.ValueEdges ??= new List<GraphControlFlowValueEdge>();

            var incoming = new List<GraphControlFlowEdge>();
            var outgoing = new List<GraphControlFlowEdge>();
            for (int i = 0; i < host.ControlEdges.Count; i++)
            {
                GraphControlFlowEdge edge = host.ControlEdges[i];
                if (string.Equals(edge.To, siteNodeId, StringComparison.Ordinal))
                {
                    incoming.Add(edge);
                }
                else if (string.Equals(edge.From, siteNodeId, StringComparison.Ordinal))
                {
                    outgoing.Add(edge);
                }
            }

            if (outgoing.Count != 1)
            {
                throw new InvalidOperationException(
                    $"TriggerGraphInlineWeaver: host '{hostGraphId}' InlineGraph '{siteNodeId}' must have exactly one " +
                    $"outgoing control edge (found {outgoing.Count}).");
            }

            string exitTarget = outgoing[0].To;
            string prefixedStart = PrefixedId(siteNodeId, fragment.EntryStartNodeId);

            // Retarget Entry / Entries that started at the InlineGraph site.
            if (string.Equals(host.Entry, siteNodeId, StringComparison.Ordinal))
            {
                host.Entry = prefixedStart;
            }

            List<TriggerGraphEntryConfig> entries = host.Entries ?? new List<TriggerGraphEntryConfig>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].Start, siteNodeId, StringComparison.Ordinal))
                {
                    entries[i].Start = prefixedStart;
                }
            }

            // Drop site node and its incident control edges.
            host.Nodes.RemoveAll(n => string.Equals(n.Id, siteNodeId, StringComparison.Ordinal));
            host.ControlEdges.RemoveAll(e =>
                string.Equals(e.From, siteNodeId, StringComparison.Ordinal) ||
                string.Equals(e.To, siteNodeId, StringComparison.Ordinal));
            host.ValueEdges.RemoveAll(e =>
                string.Equals(e.From, siteNodeId, StringComparison.Ordinal) ||
                string.Equals(e.To, siteNodeId, StringComparison.Ordinal));

            var nodeIds = new HashSet<string>(host.Nodes.Select(n => n.Id), StringComparer.Ordinal);
            foreach (string nodeId in fragment.NodeIds)
            {
                if (fragment.DroppedHaltNodes.Contains(nodeId))
                {
                    continue;
                }

                GraphControlFlowNode clone = CloneNode(fragment.NodesById[nodeId], cloneOptions);
                clone.Id = PrefixedId(siteNodeId, nodeId);
                if (!nodeIds.Add(clone.Id))
                {
                    throw new InvalidOperationException(
                        $"TriggerGraphInlineWeaver: woven node id '{clone.Id}' collides inside host '{hostGraphId}' " +
                        $"(macro '{macroGraphId}').");
                }

                host.Nodes.Add(clone);
            }

            for (int i = 0; i < incoming.Count; i++)
            {
                host.ControlEdges.Add(new GraphControlFlowEdge(
                    incoming[i].From,
                    incoming[i].FromPort,
                    prefixedStart));
            }

            foreach (GraphControlFlowEdge edge in fragment.ControlEdges)
            {
                string to = fragment.DroppedHaltNodes.Contains(edge.To)
                    ? exitTarget
                    : PrefixedId(siteNodeId, edge.To);
                host.ControlEdges.Add(new GraphControlFlowEdge(
                    PrefixedId(siteNodeId, edge.From),
                    edge.FromPort,
                    to));
            }

            foreach (GraphControlFlowValueEdge edge in fragment.ValueEdges)
            {
                host.ValueEdges.Add(new GraphControlFlowValueEdge(
                    PrefixedId(siteNodeId, edge.From),
                    edge.FromPort,
                    PrefixedId(siteNodeId, edge.To),
                    edge.ToPort));
            }
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

        private static string PrefixedId(string siteNodeId, string nodeId)
            => $"inline_{siteNodeId}::{nodeId}";

        private static GraphControlFlowNode CloneNode(GraphControlFlowNode node, JsonSerializerOptions options)
        {
            string json = JsonSerializer.Serialize(node, options);
            return JsonSerializer.Deserialize<GraphControlFlowNode>(json, options)
                ?? throw new InvalidOperationException(
                    $"TriggerGraphInlineWeaver: node '{node.Id}' failed to round-trip clone.");
        }
    }
}
