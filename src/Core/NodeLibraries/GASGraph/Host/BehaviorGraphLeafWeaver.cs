using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph.Host
{
    /// <summary>
    /// Compile-time leaf portals for BT/FSM outer topology: splice a reusable Script
    /// function graph into the host before compile. BtLeaf strips HaltReturnInt/Return so
    /// sugar-BT epilogues own status; FsmAction keeps HaltReturnInt (host requires halt).
    /// Editor double-click opens the function graph; weave is load-time only.
    /// </summary>
    public static class BehaviorGraphLeafWeaver
    {
        public const int MaxInlineDepth = 8;
        public const string LeafCycleError = "GAS.GRAPH.ERR.BehaviorLeafCycle";

        private enum LeafKind : byte
        {
            BtLeaf = 1,
            FsmAction = 2,
        }

        private sealed class LeafSite
        {
            public LeafSite(string hostGraphId, string siteNodeId, string leafGraphId, LeafKind kind, int depth)
            {
                HostGraphId = hostGraphId;
                SiteNodeId = siteNodeId;
                LeafGraphId = leafGraphId;
                Kind = kind;
                Depth = depth;
            }

            public string HostGraphId { get; }
            public string SiteNodeId { get; }
            public string LeafGraphId { get; }
            public LeafKind Kind { get; }
            public int Depth { get; }
        }

        private sealed class Fragment
        {
            public HashSet<string> NodeIds { get; } = new(StringComparer.Ordinal);
            public List<GraphControlFlowEdge> ControlEdges { get; } = new();
            public List<GraphControlFlowValueEdge> ValueEdges { get; } = new();
            public Dictionary<string, GraphControlFlowNode> NodesById { get; } = new(StringComparer.Ordinal);
            public string EntryStartNodeId { get; set; } = string.Empty;
            public HashSet<string> DroppedExitNodes { get; } = new(StringComparer.Ordinal);
        }

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
                        $"BehaviorGraphLeafWeaver: expand did not converge within {pass} passes.");
                }

                var hosts = documents.Keys.ToList();
                for (int h = 0; h < hosts.Count; h++)
                {
                    string hostId = hosts[h];
                    GraphControlFlowDocument host = documents[hostId];
                    if (!GraphKindParser.TryParse(host.Kind, out GraphKind hostKind) || hostKind != GraphKind.Script)
                    {
                        continue;
                    }

                    List<LeafSite> sites = CollectSites(hostId, host, depth: 0);
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

            foreach (KeyValuePair<string, GraphControlFlowDocument> pair in documents)
            {
                List<GraphControlFlowNode> nodes = pair.Value.Nodes ?? new List<GraphControlFlowNode>();
                for (int i = 0; i < nodes.Count; i++)
                {
                    string op = (nodes[i].Op ?? string.Empty).Trim();
                    if (GraphAuthoringSugar.IsBtLeafPortal(op) || GraphAuthoringSugar.IsFsmActionPortal(op))
                    {
                        throw new InvalidOperationException(
                            $"BehaviorGraphLeafWeaver: graph '{pair.Key}' still contains {op} node '{nodes[i].Id}' " +
                            "after expand; leftover sites fail closed.");
                    }
                }
            }
        }

        private static List<LeafSite> CollectSites(string hostGraphId, GraphControlFlowDocument host, int depth)
        {
            var sites = new List<LeafSite>();
            List<GraphControlFlowNode> nodes = host.Nodes ?? new List<GraphControlFlowNode>();
            for (int i = 0; i < nodes.Count; i++)
            {
                GraphControlFlowNode node = nodes[i];
                string op = (node.Op ?? string.Empty).Trim();
                LeafKind kind;
                if (GraphAuthoringSugar.IsBtLeafPortal(op))
                {
                    kind = LeafKind.BtLeaf;
                }
                else if (GraphAuthoringSugar.IsFsmActionPortal(op))
                {
                    kind = LeafKind.FsmAction;
                }
                else
                {
                    continue;
                }

                string leafId = (node.FunctionName ?? string.Empty).Trim();
                if (leafId.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"BehaviorGraphLeafWeaver: graph '{hostGraphId}' {op} node '{node.Id}' requires functionName " +
                        "(Script function graph id).");
                }

                if (node.GraphId > 0)
                {
                    throw new InvalidOperationException(
                        $"BehaviorGraphLeafWeaver: graph '{hostGraphId}' {op} node '{node.Id}' must use functionName, " +
                        "not graphId.");
                }

                sites.Add(new LeafSite(hostGraphId, node.Id, leafId, kind, depth));
            }

            return sites;
        }

        private static void ExpandSite(
            IDictionary<string, GraphControlFlowDocument> documents,
            string hostGraphId,
            LeafSite site,
            JsonSerializerOptions cloneOptions,
            HashSet<string> stack)
        {
            if (site.Depth >= MaxInlineDepth)
            {
                throw new InvalidOperationException(
                    $"BehaviorGraphLeafWeaver: leaf depth exceeded {MaxInlineDepth} at host '{hostGraphId}' " +
                    $"site '{site.SiteNodeId}' → '{site.LeafGraphId}'.");
            }

            if (!stack.Add(site.LeafGraphId))
            {
                throw new InvalidOperationException(
                    $"{LeafCycleError}: host '{hostGraphId}' site '{site.SiteNodeId}' closes a leaf cycle through '{site.LeafGraphId}'.");
            }

            if (!documents.TryGetValue(site.LeafGraphId, out GraphControlFlowDocument? leaf) || leaf == null)
            {
                throw new InvalidOperationException(
                    $"BehaviorGraphLeafWeaver: graph '{hostGraphId}' {Describe(site.Kind)} '{site.SiteNodeId}' references unknown " +
                    $"function graph '{site.LeafGraphId}'.");
            }

            if (!GraphKindParser.TryParse(leaf.Kind, out GraphKind leafKind) || leafKind != GraphKind.Script)
            {
                throw new InvalidOperationException(
                    $"BehaviorGraphLeafWeaver: function graph '{site.LeafGraphId}' must be Script (got '{leaf.Kind}').");
            }

            // Nested portals inside the leaf expand first.
            List<LeafSite> nested = CollectSites(site.LeafGraphId, leaf, site.Depth + 1);
            for (int i = 0; i < nested.Count; i++)
            {
                ExpandSite(documents, site.LeafGraphId, nested[i], cloneOptions, stack);
            }

            string start = ResolveEntry(leaf, site.LeafGraphId);
            Fragment fragment = ExtractFragment(site.LeafGraphId, leaf, start, site.Kind);
            SpliceIntoHost(documents[hostGraphId], hostGraphId, site, fragment, cloneOptions);
            stack.Remove(site.LeafGraphId);
        }

        private static string ResolveEntry(GraphControlFlowDocument leaf, string leafGraphId)
        {
            string entry = (leaf.Entry ?? string.Empty).Trim();
            if (entry.Length == 0)
            {
                throw new InvalidOperationException(
                    $"BehaviorGraphLeafWeaver: function graph '{leafGraphId}' has empty entry.");
            }

            return entry;
        }

        private static Fragment ExtractFragment(
            string leafGraphId,
            GraphControlFlowDocument leaf,
            string start,
            LeafKind kind)
        {
            List<GraphControlFlowNode> nodes = leaf.Nodes ?? new List<GraphControlFlowNode>();
            bool startExists = nodes.Any(n => string.Equals(n.Id, start, StringComparison.Ordinal));
            if (!startExists)
            {
                throw new InvalidOperationException(
                    $"BehaviorGraphLeafWeaver: function graph '{leafGraphId}' start node '{start}' does not exist.");
            }

            List<GraphControlFlowEdge> control = leaf.ControlEdges ?? new List<GraphControlFlowEdge>();
            List<GraphControlFlowValueEdge> values = leaf.ValueEdges ?? new List<GraphControlFlowValueEdge>();

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

            var carriesControl = new HashSet<string>(StringComparer.Ordinal);
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
                        $"BehaviorGraphLeafWeaver: function graph '{leafGraphId}' has control edge from '{control[i].From}' " +
                        $"into non-entry '{control[i].To}'; leaf fragments must be control-self-contained.");
                }

                if (fromIn)
                {
                    throw new InvalidOperationException(
                        $"BehaviorGraphLeafWeaver: function graph '{leafGraphId}' has control edge leaving to " +
                        $"'{control[i].To}'; leaf fragments must be control-self-contained.");
                }
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (reachable.Contains(values[i].From) && reachable.Contains(values[i].To))
                {
                    fragment.ValueEdges.Add(values[i]);
                }
            }

            bool stripExits = kind == LeafKind.BtLeaf;
            foreach (string nodeId in reachable)
            {
                string op = (fragment.NodesById[nodeId].Op ?? string.Empty).Trim();
                if (stripExits && (op == nameof(GraphNodeOp.HaltReturnInt) || op == nameof(GraphNodeOp.Return)))
                {
                    fragment.DroppedExitNodes.Add(nodeId);
                    continue;
                }

                if (!stripExits && op == nameof(GraphNodeOp.HaltReturnInt))
                {
                    // FsmAction keeps Halt terminals.
                    continue;
                }
            }

            if (stripExits)
            {
                if (fragment.DroppedExitNodes.Contains(start))
                {
                    throw new InvalidOperationException(
                        $"BehaviorGraphLeafWeaver: function graph '{leafGraphId}' fragment is empty (start is an exit).");
                }

                // Every HaltReturnInt / Return drop must leave predecessors as terminals.
                // Dangling branch arms that only targeted Halt get a synthetic ConstInt from the
                // Halt's value producer when the producer is not already the control predecessor.
                PromoteHaltValueProducers(fragment, leafGraphId);
            }
            else
            {
                bool hasHalt = fragment.NodesById.Values.Any(n =>
                    string.Equals((n.Op ?? string.Empty).Trim(), nameof(GraphNodeOp.HaltReturnInt), StringComparison.Ordinal)
                    && !fragment.DroppedExitNodes.Contains(n.Id));
                if (!hasHalt)
                {
                    throw new InvalidOperationException(
                        $"BehaviorGraphLeafWeaver: FsmAction function graph '{leafGraphId}' must end in HaltReturnInt.");
                }
            }

            return fragment;
        }

        private static void PromoteHaltValueProducers(Fragment fragment, string leafGraphId)
        {
            // When control pred P → Halt and value V → Halt, and P != V, retarget P → V so the
            // Int/Bool producer becomes the BT chain terminal after Halt is dropped.
            var retargets = new List<(string From, string FromPort, string To)>();
            for (int i = 0; i < fragment.ControlEdges.Count; i++)
            {
                GraphControlFlowEdge edge = fragment.ControlEdges[i];
                if (!fragment.DroppedExitNodes.Contains(edge.To))
                {
                    continue;
                }

                string? valueFrom = null;
                for (int v = 0; v < fragment.ValueEdges.Count; v++)
                {
                    if (string.Equals(fragment.ValueEdges[v].To, edge.To, StringComparison.Ordinal)
                        && string.Equals(fragment.ValueEdges[v].ToPort, "value", StringComparison.Ordinal))
                    {
                        valueFrom = fragment.ValueEdges[v].From;
                        break;
                    }
                }

                if (valueFrom != null
                    && !string.Equals(valueFrom, edge.From, StringComparison.Ordinal)
                    && fragment.NodeIds.Contains(valueFrom)
                    && !fragment.DroppedExitNodes.Contains(valueFrom))
                {
                    retargets.Add((edge.From, edge.FromPort, valueFrom));
                }
            }

            for (int i = 0; i < retargets.Count; i++)
            {
                (string from, string fromPort, string to) = retargets[i];
                // Replace the edge to Halt with edge to value producer.
                fragment.ControlEdges.RemoveAll(e =>
                    string.Equals(e.From, from, StringComparison.Ordinal)
                    && string.Equals(e.FromPort, fromPort, StringComparison.Ordinal)
                    && fragment.DroppedExitNodes.Contains(e.To));
                fragment.ControlEdges.Add(new GraphControlFlowEdge(from, fromPort, to));
            }

            // Drop value edges that only fed removed exits.
            fragment.ValueEdges.RemoveAll(e => fragment.DroppedExitNodes.Contains(e.To));
        }

        private static void SpliceIntoHost(
            GraphControlFlowDocument host,
            string hostGraphId,
            LeafSite site,
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
                if (string.Equals(edge.To, site.SiteNodeId, StringComparison.Ordinal))
                {
                    incoming.Add(edge);
                }
                else if (string.Equals(edge.From, site.SiteNodeId, StringComparison.Ordinal))
                {
                    outgoing.Add(edge);
                }
            }

            if (outgoing.Count != 0)
            {
                throw new InvalidOperationException(
                    $"BehaviorGraphLeafWeaver: host '{hostGraphId}' {Describe(site.Kind)} '{site.SiteNodeId}' must not have " +
                    $"outgoing control edges (found {outgoing.Count}); portals are leaf sites.");
            }

            if (incoming.Count == 0)
            {
                throw new InvalidOperationException(
                    $"BehaviorGraphLeafWeaver: host '{hostGraphId}' {Describe(site.Kind)} '{site.SiteNodeId}' has no incoming " +
                    "control edge.");
            }

            string prefixedStart = PrefixedId(site.SiteNodeId, fragment.EntryStartNodeId);

            if (string.Equals(host.Entry, site.SiteNodeId, StringComparison.Ordinal))
            {
                host.Entry = prefixedStart;
            }

            host.Nodes.RemoveAll(n => string.Equals(n.Id, site.SiteNodeId, StringComparison.Ordinal));
            host.ControlEdges.RemoveAll(e =>
                string.Equals(e.From, site.SiteNodeId, StringComparison.Ordinal) ||
                string.Equals(e.To, site.SiteNodeId, StringComparison.Ordinal));
            host.ValueEdges.RemoveAll(e =>
                string.Equals(e.From, site.SiteNodeId, StringComparison.Ordinal) ||
                string.Equals(e.To, site.SiteNodeId, StringComparison.Ordinal));

            var nodeIds = new HashSet<string>(host.Nodes.Select(n => n.Id), StringComparer.Ordinal);
            foreach (string nodeId in fragment.NodeIds)
            {
                if (fragment.DroppedExitNodes.Contains(nodeId))
                {
                    continue;
                }

                GraphControlFlowNode clone = CloneNode(fragment.NodesById[nodeId], cloneOptions);
                clone.Id = PrefixedId(site.SiteNodeId, nodeId);
                if (!nodeIds.Add(clone.Id))
                {
                    throw new InvalidOperationException(
                        $"BehaviorGraphLeafWeaver: woven node id '{clone.Id}' collides inside host '{hostGraphId}' " +
                        $"(leaf '{site.LeafGraphId}').");
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
                if (fragment.DroppedExitNodes.Contains(edge.To))
                {
                    // Pred becomes a chain / Call-body terminal.
                    continue;
                }

                if (fragment.DroppedExitNodes.Contains(edge.From))
                {
                    continue;
                }

                host.ControlEdges.Add(new GraphControlFlowEdge(
                    PrefixedId(site.SiteNodeId, edge.From),
                    edge.FromPort,
                    PrefixedId(site.SiteNodeId, edge.To)));
            }

            foreach (GraphControlFlowValueEdge edge in fragment.ValueEdges)
            {
                if (fragment.DroppedExitNodes.Contains(edge.From) || fragment.DroppedExitNodes.Contains(edge.To))
                {
                    continue;
                }

                host.ValueEdges.Add(new GraphControlFlowValueEdge(
                    PrefixedId(site.SiteNodeId, edge.From),
                    edge.FromPort,
                    PrefixedId(site.SiteNodeId, edge.To),
                    edge.ToPort));
            }
        }

        private static string PrefixedId(string siteNodeId, string nodeId)
            => $"{siteNodeId}__{nodeId}";

        private static string Describe(LeafKind kind)
            => kind == LeafKind.BtLeaf ? GraphAuthoringSugar.BtLeaf : GraphAuthoringSugar.FsmAction;

        private static GraphControlFlowNode CloneNode(GraphControlFlowNode node, JsonSerializerOptions options)
        {
            string json = JsonSerializer.Serialize(node, options);
            GraphControlFlowNode? clone = JsonSerializer.Deserialize<GraphControlFlowNode>(json, options);
            if (clone == null)
            {
                throw new InvalidOperationException(
                    $"BehaviorGraphLeafWeaver: node '{node.Id}' failed to round-trip clone.");
            }

            return clone;
        }
    }
}
