using System;
using System.Collections.Generic;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    /// <summary>
    /// Expands FormatText authoring sugar into ConstText + ConcatText before compile.
    /// Brace ports are Text; authors wire IntToText/FloatToText upstream when needed.
    /// Generated nodes are inserted on the control path so reachability stays fail-closed.
    /// </summary>
    public static class GraphFormatTextExpander
    {
        public static void ExpandInPlace(GraphControlFlowDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);
            if (document.Nodes == null || document.Nodes.Count == 0)
            {
                return;
            }

            var sites = new List<GraphControlFlowNode>();
            for (int i = 0; i < document.Nodes.Count; i++)
            {
                if (string.Equals(document.Nodes[i].Op, GraphAuthoringSugar.FormatText, StringComparison.Ordinal))
                {
                    sites.Add(document.Nodes[i]);
                }
            }

            for (int s = 0; s < sites.Count; s++)
            {
                ExpandSite(document, sites[s]);
            }
        }

        private static void ExpandSite(GraphControlFlowDocument document, GraphControlFlowNode site)
        {
            if (!GraphFormalTextTemplate.TryParse(site.Text, out GraphFormalTextTemplate.Part[] parts, out string? error))
            {
                throw new InvalidOperationException(
                    $"FormatText node '{site.Id}' in graph '{document.Id}': {error}");
            }

            var producers = new Dictionary<string, GraphControlFlowValueEdge>(StringComparer.Ordinal);
            for (int i = document.ValueEdges.Count - 1; i >= 0; i--)
            {
                GraphControlFlowValueEdge edge = document.ValueEdges[i];
                if (!string.Equals(edge.To, site.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                producers[edge.ToPort] = edge;
                document.ValueEdges.RemoveAt(i);
            }

            var incoming = new List<GraphControlFlowEdge>();
            var outgoing = new List<GraphControlFlowEdge>();
            for (int i = document.ControlEdges.Count - 1; i >= 0; i--)
            {
                GraphControlFlowEdge edge = document.ControlEdges[i];
                if (string.Equals(edge.To, site.Id, StringComparison.Ordinal))
                {
                    incoming.Add(edge);
                    document.ControlEdges.RemoveAt(i);
                }
                else if (string.Equals(edge.From, site.Id, StringComparison.Ordinal))
                {
                    outgoing.Add(edge);
                    document.ControlEdges.RemoveAt(i);
                }
            }

            document.Nodes.RemoveAll(n => string.Equals(n.Id, site.Id, StringComparison.Ordinal));

            var controlSequence = new List<string>();
            string? chain = null;
            int lit = 0;
            int cat = 0;

            if (parts.Length == 0)
            {
                document.Nodes.Add(new GraphControlFlowNode
                {
                    Id = site.Id,
                    Op = nameof(GraphNodeOp.ConstText),
                    Text = string.Empty
                });
                controlSequence.Add(site.Id);
            }
            else
            {
                for (int p = 0; p < parts.Length; p++)
                {
                    GraphFormalTextTemplate.Part part = parts[p];
                    string pieceNodeId;
                    string piecePort = GraphControlFlowPorts.Value;

                    if (part.IsLiteral)
                    {
                        pieceNodeId = $"{site.Id}__lit{lit++}";
                        document.Nodes.Add(new GraphControlFlowNode
                        {
                            Id = pieceNodeId,
                            Op = nameof(GraphNodeOp.ConstText),
                            Text = part.Literal ?? string.Empty
                        });
                        controlSequence.Add(pieceNodeId);
                    }
                    else
                    {
                        if (part.PortName == null || !producers.TryGetValue(part.PortName, out GraphControlFlowValueEdge producer))
                        {
                            throw new InvalidOperationException(
                                $"FormatText node '{site.Id}' missing Text value edge for '{part.PortName ?? "<null>"}'.");
                        }

                        pieceNodeId = producer.From;
                        piecePort = producer.FromPort;
                    }

                    if (chain == null)
                    {
                        if (part.IsLiteral)
                        {
                            chain = pieceNodeId;
                        }
                        else
                        {
                            string emptyId = $"{site.Id}__empty";
                            document.Nodes.Add(new GraphControlFlowNode
                            {
                                Id = emptyId,
                                Op = nameof(GraphNodeOp.ConstText),
                                Text = string.Empty
                            });
                            controlSequence.Add(emptyId);
                            string catId = $"{site.Id}__cat{cat++}";
                            document.Nodes.Add(new GraphControlFlowNode
                            {
                                Id = catId,
                                Op = nameof(GraphNodeOp.ConcatText)
                            });
                            controlSequence.Add(catId);
                            document.ValueEdges.Add(new GraphControlFlowValueEdge(emptyId, GraphControlFlowPorts.Value, catId, GraphControlFlowPorts.A));
                            document.ValueEdges.Add(new GraphControlFlowValueEdge(pieceNodeId, piecePort, catId, GraphControlFlowPorts.B));
                            chain = catId;
                        }

                        continue;
                    }

                    string nextCat = (p == parts.Length - 1) ? site.Id : $"{site.Id}__cat{cat++}";
                    document.Nodes.Add(new GraphControlFlowNode
                    {
                        Id = nextCat,
                        Op = nameof(GraphNodeOp.ConcatText)
                    });
                    controlSequence.Add(nextCat);
                    document.ValueEdges.Add(new GraphControlFlowValueEdge(chain, GraphControlFlowPorts.Value, nextCat, GraphControlFlowPorts.A));
                    document.ValueEdges.Add(new GraphControlFlowValueEdge(pieceNodeId, piecePort, nextCat, GraphControlFlowPorts.B));
                    chain = nextCat;
                }

                if (chain != null && !string.Equals(chain, site.Id, StringComparison.Ordinal))
                {
                    for (int i = 0; i < document.Nodes.Count; i++)
                    {
                        if (string.Equals(document.Nodes[i].Id, chain, StringComparison.Ordinal))
                        {
                            document.Nodes[i].Id = site.Id;
                            break;
                        }
                    }

                    for (int i = 0; i < controlSequence.Count; i++)
                    {
                        if (string.Equals(controlSequence[i], chain, StringComparison.Ordinal))
                        {
                            controlSequence[i] = site.Id;
                        }
                    }

                    for (int i = 0; i < document.ValueEdges.Count; i++)
                    {
                        GraphControlFlowValueEdge e = document.ValueEdges[i];
                        if (string.Equals(e.From, chain, StringComparison.Ordinal))
                        {
                            document.ValueEdges[i] = new GraphControlFlowValueEdge(site.Id, e.FromPort, e.To, e.ToPort);
                        }
                        else if (string.Equals(e.To, chain, StringComparison.Ordinal))
                        {
                            document.ValueEdges[i] = new GraphControlFlowValueEdge(e.From, e.FromPort, site.Id, e.ToPort);
                        }
                    }
                }
            }

            if (controlSequence.Count == 0)
            {
                controlSequence.Add(site.Id);
            }

            string first = controlSequence[0];
            for (int i = 0; i < incoming.Count; i++)
            {
                document.ControlEdges.Add(new GraphControlFlowEdge(incoming[i].From, incoming[i].FromPort, first));
            }

            for (int i = 0; i < controlSequence.Count - 1; i++)
            {
                document.ControlEdges.Add(new GraphControlFlowEdge(
                    controlSequence[i],
                    GraphControlFlowPorts.Next,
                    controlSequence[i + 1]));
            }

            string last = controlSequence[controlSequence.Count - 1];
            for (int i = 0; i < outgoing.Count; i++)
            {
                document.ControlEdges.Add(new GraphControlFlowEdge(last, outgoing[i].FromPort, outgoing[i].To));
            }

            if (string.Equals(document.Entry, site.Id, StringComparison.Ordinal))
            {
                document.Entry = first;
            }
        }
    }
}
