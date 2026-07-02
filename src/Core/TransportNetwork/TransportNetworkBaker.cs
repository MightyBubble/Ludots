using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.GraphSemantics.GAS;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Presentation.Surfaces;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.TransportNetwork
{
    public sealed class TransportNetworkBaker
    {
        private const string NodeKindTagPrefix = "Transport.NodeKind.";
        private const string FlowDownstreamTag = "Transport.Flow.Downstream";
        private const string FlowUpstreamTag = "Transport.Flow.Upstream";

        private static readonly List<LocalEdge> EmptyLocalEdges = new();
        private static readonly List<GraphCrossEdge> EmptyCrossEdges = new();

        public TransportNetworkBakedAsset Bake(TransportNetworkAsset asset, int chunkSizeCm)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            if (chunkSizeCm <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSizeCm));
            asset.Validate();

            var authoredNodes = BuildAuthoredNodeLookup(asset);
            var globalNodes = new List<SampledNode>(128);
            var nodeIdsByPoint = new Dictionary<long, int>(128);
            var uniqueEdges = new HashSet<ulong>();
            var edges = new List<GlobalEdge>(256);

            for (int i = 0; i < asset.Segments.Count; i++)
            {
                TransportNetworkSegment segment = asset.Segments[i];
                var points = ResolvePoints(segment, authoredNodes);
                List<int> sampled = SampleSegmentNodes(asset, segment, points, authoredNodes, globalNodes, nodeIdsByPoint);
                for (int edgeIndex = 0; edgeIndex < sampled.Count - 1; edgeIndex++)
                {
                    int from = sampled[edgeIndex];
                    int to = sampled[edgeIndex + 1];
                    float distanceCm = DistanceCm(globalNodes[from].Point, globalNodes[to].Point);

                    if (segment.Direction != TransportNetworkDirection.ReverseOnly)
                    {
                        AddDirectedGlobalEdge(uniqueEdges, edges, segment, from, to, distanceCm, reverseFlow: false);
                    }

                    if (segment.Direction != TransportNetworkDirection.ForwardOnly)
                    {
                        AddDirectedGlobalEdge(uniqueEdges, edges, segment, to, from, distanceCm, reverseFlow: true);
                    }
                }
            }

            Dictionary<long, GraphChunkData> graphChunks = BuildGraphChunks(globalNodes, edges, chunkSizeCm);
            Dictionary<long, SurfaceSplineSegment[]> ribbonChunks = BuildRibbonChunks(asset, authoredNodes, chunkSizeCm);
            return new TransportNetworkBakedAsset(graphChunks, ribbonChunks, globalNodes.Count, edges.Count);
        }

        private static Dictionary<string, AuthoredNode> BuildAuthoredNodeLookup(TransportNetworkAsset asset)
        {
            var result = new Dictionary<string, AuthoredNode>(asset.Nodes.Count, StringComparer.Ordinal);
            for (int i = 0; i < asset.Nodes.Count; i++)
            {
                TransportNetworkNode node = asset.Nodes[i];
                var point = new WorldPoint(node.Xcm, node.Ycm);
                TagBits256 tags = CompileNodeTags(node);
                result.Add(node.Id, new AuthoredNode(point, tags));
            }

            return result;
        }

        private static List<WorldPoint> ResolvePoints(
            TransportNetworkSegment segment,
            IReadOnlyDictionary<string, AuthoredNode> authoredNodes)
        {
            var result = new List<WorldPoint>(segment.Points.Count);
            for (int i = 0; i < segment.Points.Count; i++)
            {
                TransportNetworkPoint point = segment.Points[i];
                if (!string.IsNullOrWhiteSpace(point.NodeId))
                {
                    result.Add(authoredNodes[point.NodeId].Point);
                }
                else
                {
                    result.Add(new WorldPoint(point.Xcm, point.Ycm));
                }
            }

            return result;
        }

        private static List<int> SampleSegmentNodes(
            TransportNetworkAsset asset,
            TransportNetworkSegment segment,
            IReadOnlyList<WorldPoint> points,
            IReadOnlyDictionary<string, AuthoredNode> authoredNodes,
            List<SampledNode> globalNodes,
            Dictionary<long, int> nodeIdsByPoint)
        {
            var result = new List<int>(points.Count * 2);
            int sampleStepCm = segment.SampleStepCm > 0 ? segment.SampleStepCm : asset.SampleStepCm;

            for (int segmentIndex = 0; segmentIndex < points.Count - 1; segmentIndex++)
            {
                WorldPoint previous = segmentIndex > 0 ? points[segmentIndex - 1] : points[segmentIndex];
                WorldPoint start = points[segmentIndex];
                WorldPoint end = points[segmentIndex + 1];
                WorldPoint next = segmentIndex + 2 < points.Count ? points[segmentIndex + 2] : points[segmentIndex + 1];
                CurveGeometry curve = BuildCurve(previous, start, end, next);
                float curveLengthCm = EstimateCurveLengthCm(in curve);
                int steps = Math.Max(1, (int)MathF.Ceiling(curveLengthCm / sampleStepCm));
                int firstStep = segmentIndex == 0 ? 0 : 1;

                for (int step = firstStep; step <= steps; step++)
                {
                    float t = step / (float)steps;
                    WorldPoint sampled = EvaluatePoint(in curve, t);
                    TagBits256 nodeTags = ResolveAuthoredPointTags(sampled, authoredNodes);
                    int nodeId = GetOrAddNode(sampled, nodeTags, globalNodes, nodeIdsByPoint);
                    if (result.Count == 0 || result[result.Count - 1] != nodeId)
                    {
                        result.Add(nodeId);
                    }
                }
            }

            return result;
        }

        private static void AddDirectedGlobalEdge(
            HashSet<ulong> uniqueEdges,
            List<GlobalEdge> edges,
            TransportNetworkSegment segment,
            int from,
            int to,
            float distanceCm,
            bool reverseFlow)
        {
            ulong edgeKey = PackDirectedEdge(from, to);
            if (!uniqueEdges.Add(edgeKey))
            {
                return;
            }

            edges.Add(new GlobalEdge(
                from,
                to,
                distanceCm,
                CompileEdgeTags(segment, reverseFlow),
                segment.DepthCm,
                segment.WidthCm));
        }

        private static Dictionary<long, GraphChunkData> BuildGraphChunks(
            IReadOnlyList<SampledNode> globalNodes,
            IReadOnlyList<GlobalEdge> edges,
            int chunkSizeCm)
        {
            var nodeChunkKeys = new long[globalNodes.Count];
            var nodesByChunk = new Dictionary<long, List<int>>();
            for (int nodeId = 0; nodeId < globalNodes.Count; nodeId++)
            {
                long chunkKey = GraphChunkKey.FromWorld(new WorldCmInt2(globalNodes[nodeId].Point.Xcm, globalNodes[nodeId].Point.Ycm), chunkSizeCm);
                nodeChunkKeys[nodeId] = chunkKey;
                if (!nodesByChunk.TryGetValue(chunkKey, out List<int> nodes))
                {
                    nodes = new List<int>();
                    nodesByChunk.Add(chunkKey, nodes);
                }

                nodes.Add(nodeId);
            }

            var localNodeIdsByChunk = new Dictionary<long, Dictionary<int, int>>(nodesByChunk.Count);
            foreach ((long chunkKey, List<int> chunkNodes) in nodesByChunk)
            {
                var localIds = new Dictionary<int, int>(chunkNodes.Count);
                for (int i = 0; i < chunkNodes.Count; i++)
                {
                    localIds[chunkNodes[i]] = i;
                }

                localNodeIdsByChunk.Add(chunkKey, localIds);
            }

            var localEdgesByChunk = new Dictionary<long, List<LocalEdge>>(nodesByChunk.Count);
            var crossEdgesByChunk = new Dictionary<long, List<GraphCrossEdge>>(nodesByChunk.Count);
            for (int i = 0; i < edges.Count; i++)
            {
                GlobalEdge edge = edges[i];
                AddChunkEdge(edge, nodeChunkKeys, localNodeIdsByChunk, localEdgesByChunk, crossEdgesByChunk);
            }

            var graphChunks = new Dictionary<long, GraphChunkData>(nodesByChunk.Count);
            foreach ((long chunkKey, List<int> chunkNodes) in nodesByChunk)
            {
                List<LocalEdge> localEdges = localEdgesByChunk.TryGetValue(chunkKey, out List<LocalEdge> storedLocalEdges)
                    ? storedLocalEdges
                    : EmptyLocalEdges;
                List<GraphCrossEdge> crossEdges = crossEdgesByChunk.TryGetValue(chunkKey, out List<GraphCrossEdge> storedCrossEdges)
                    ? storedCrossEdges
                    : EmptyCrossEdges;

                var builder = new NodeGraphBuilder(chunkNodes.Count, localEdges.Count);
                for (int i = 0; i < chunkNodes.Count; i++)
                {
                    SampledNode node = globalNodes[chunkNodes[i]];
                    ushort tagSetId = builder.AddTagSet(in node.Tags);
                    builder.AddNode(node.Point.Xcm, node.Point.Ycm, tagSetId);
                }

                for (int i = 0; i < localEdges.Count; i++)
                {
                    LocalEdge edge = localEdges[i];
                    ushort tagSetId = builder.AddTagSet(in edge.Tags);
                    builder.AddEdge(edge.FromLocalNodeId, edge.ToLocalNodeId, edge.Cost, tagSetId, edge.DepthCm, edge.WidthCm);
                }

                graphChunks.Add(chunkKey, new GraphChunkData(builder.Build(), crossEdges.ToArray()));
            }

            return graphChunks;
        }

        private static void AddChunkEdge(
            in GlobalEdge edge,
            IReadOnlyList<long> nodeChunkKeys,
            IReadOnlyDictionary<long, Dictionary<int, int>> localNodeIdsByChunk,
            IDictionary<long, List<LocalEdge>> localEdgesByChunk,
            IDictionary<long, List<GraphCrossEdge>> crossEdgesByChunk)
        {
            long fromChunkKey = nodeChunkKeys[edge.FromNodeId];
            long toChunkKey = nodeChunkKeys[edge.ToNodeId];
            int fromLocalNodeId = localNodeIdsByChunk[fromChunkKey][edge.FromNodeId];
            int toLocalNodeId = localNodeIdsByChunk[toChunkKey][edge.ToNodeId];

            if (fromChunkKey == toChunkKey)
            {
                if (!localEdgesByChunk.TryGetValue(fromChunkKey, out List<LocalEdge> localEdges))
                {
                    localEdges = new List<LocalEdge>();
                    localEdgesByChunk.Add(fromChunkKey, localEdges);
                }

                localEdges.Add(new LocalEdge(fromLocalNodeId, toLocalNodeId, edge.Cost, edge.Tags, edge.DepthCm, edge.WidthCm));
                return;
            }

            if (!crossEdgesByChunk.TryGetValue(fromChunkKey, out List<GraphCrossEdge> crossEdges))
            {
                crossEdges = new List<GraphCrossEdge>();
                crossEdgesByChunk.Add(fromChunkKey, crossEdges);
            }

            crossEdges.Add(new GraphCrossEdge(
                (ushort)fromLocalNodeId,
                toChunkKey,
                (ushort)toLocalNodeId,
                edge.Cost,
                tagSetId: 0,
                edge.DepthCm,
                edge.WidthCm,
                edge.Tags));
        }

        private static Dictionary<long, SurfaceSplineSegment[]> BuildRibbonChunks(
            TransportNetworkAsset asset,
            IReadOnlyDictionary<string, AuthoredNode> authoredNodes,
            int chunkSizeCm)
        {
            var ribbonChunks = new Dictionary<long, List<SurfaceSplineSegment>>();
            for (int i = 0; i < asset.Segments.Count; i++)
            {
                TransportNetworkSegment segment = asset.Segments[i];
                var points = ResolvePoints(segment, authoredNodes);
                float visualWidth = segment.VisualWidthMeters > 0f ? segment.VisualWidthMeters : asset.DefaultVisualWidthMeters;

                for (int segmentIndex = 0; segmentIndex < points.Count - 1; segmentIndex++)
                {
                    WorldPoint previous = segmentIndex > 0 ? points[segmentIndex - 1] : points[segmentIndex];
                    WorldPoint start = points[segmentIndex];
                    WorldPoint end = points[segmentIndex + 1];
                    WorldPoint next = segmentIndex + 2 < points.Count ? points[segmentIndex + 2] : points[segmentIndex + 1];
                    CurveGeometry curve = BuildCurve(previous, start, end, next);
                    var ribbon = new SurfaceSplineSegment(
                        ToSplinePoint(curve.P0),
                        ToSplinePoint(curve.P1),
                        ToSplinePoint(curve.P2),
                        ToSplinePoint(curve.P3),
                        visualWidth);
                    WorldPoint midpoint = EvaluatePoint(in curve, 0.5f);
                    long ownerChunkKey = GraphChunkKey.FromWorld(new WorldCmInt2(midpoint.Xcm, midpoint.Ycm), chunkSizeCm);
                    if (!ribbonChunks.TryGetValue(ownerChunkKey, out List<SurfaceSplineSegment> chunkRibbons))
                    {
                        chunkRibbons = new List<SurfaceSplineSegment>();
                        ribbonChunks.Add(ownerChunkKey, chunkRibbons);
                    }

                    chunkRibbons.Add(ribbon);
                }
            }

            var frozen = new Dictionary<long, SurfaceSplineSegment[]>(ribbonChunks.Count);
            foreach ((long chunkKey, List<SurfaceSplineSegment> segments) in ribbonChunks)
            {
                frozen.Add(chunkKey, segments.ToArray());
            }

            return frozen;
        }

        private static TagBits256 CompileNodeTags(TransportNetworkNode node)
        {
            int capacity = (node.Tags?.Count ?? 0) + 1;
            Span<int> ids = capacity <= 32 ? stackalloc int[capacity] : new int[capacity];
            int count = 0;
            if (node.Tags != null)
            {
                for (int i = 0; i < node.Tags.Count; i++)
                {
                    ids[count++] = GetTagId(node.Tags[i]);
                }
            }

            if (node.Kind != TransportNetworkNodeKind.Normal)
            {
                ids[count++] = GetTagId(NodeKindTagPrefix + node.Kind);
            }

            return GraphTagSetRegistry.TagBitsFromIds(ids.Slice(0, count));
        }

        private static TagBits256 CompileEdgeTags(TransportNetworkSegment segment, bool reverseFlow)
        {
            int capacity = (segment.Tags?.Count ?? 0) + 2;
            Span<int> ids = capacity <= 32 ? stackalloc int[capacity] : new int[capacity];
            int count = 0;
            if (segment.Tags != null)
            {
                for (int i = 0; i < segment.Tags.Count; i++)
                {
                    ids[count++] = GetTagId(segment.Tags[i]);
                }
            }

            if (!string.IsNullOrWhiteSpace(segment.AreaId))
            {
                ids[count++] = GetTagId(segment.AreaId);
            }

            if (segment.FlowDirection != TransportNetworkFlowDirection.None)
            {
                bool downstream = (segment.FlowDirection == TransportNetworkFlowDirection.Forward && !reverseFlow) ||
                    (segment.FlowDirection == TransportNetworkFlowDirection.Reverse && reverseFlow);
                ids[count++] = GetTagId(downstream ? FlowDownstreamTag : FlowUpstreamTag);
            }

            return GraphTagSetRegistry.TagBitsFromIds(ids.Slice(0, count));
        }

        private static int GetTagId(string tag)
        {
            int tagId = TagRegistry.GetId(tag);
            return tagId == TagRegistry.InvalidId ? TagRegistry.Register(tag) : tagId;
        }

        private static TagBits256 ResolveAuthoredPointTags(
            in WorldPoint point,
            IReadOnlyDictionary<string, AuthoredNode> authoredNodes)
        {
            foreach (AuthoredNode node in authoredNodes.Values)
            {
                if (node.Point.Xcm == point.Xcm && node.Point.Ycm == point.Ycm)
                {
                    return node.Tags;
                }
            }

            return default;
        }

        private static int GetOrAddNode(
            WorldPoint point,
            in TagBits256 tags,
            IList<SampledNode> globalNodes,
            IDictionary<long, int> nodeIdsByPoint)
        {
            long key = PackPoint(point.Xcm, point.Ycm);
            if (nodeIdsByPoint.TryGetValue(key, out int existing))
            {
                return existing;
            }

            int nodeId = globalNodes.Count;
            globalNodes.Add(new SampledNode(point, tags));
            nodeIdsByPoint.Add(key, nodeId);
            return nodeId;
        }

        private static CurveGeometry BuildCurve(WorldPoint previous, WorldPoint start, WorldPoint end, WorldPoint next)
        {
            Vector2 prev = previous.ToVector2();
            Vector2 p0 = start.ToVector2();
            Vector2 p3 = end.ToVector2();
            Vector2 nextPoint = next.ToVector2();
            Vector2 control0 = p0 + ((p3 - prev) / 6f);
            Vector2 control1 = p3 - ((nextPoint - p0) / 6f);
            return new CurveGeometry(
                new WorldPoint(Round(p0.X), Round(p0.Y)),
                new WorldPoint(Round(control0.X), Round(control0.Y)),
                new WorldPoint(Round(control1.X), Round(control1.Y)),
                new WorldPoint(Round(p3.X), Round(p3.Y)));
        }

        private static WorldPoint EvaluatePoint(in CurveGeometry curve, float t)
        {
            float omt = 1f - t;
            float omt2 = omt * omt;
            float omt3 = omt2 * omt;
            float t2 = t * t;
            float t3 = t2 * t;

            float x =
                (curve.P0.Xcm * omt3) +
                (3f * curve.P1.Xcm * omt2 * t) +
                (3f * curve.P2.Xcm * omt * t2) +
                (curve.P3.Xcm * t3);
            float y =
                (curve.P0.Ycm * omt3) +
                (3f * curve.P1.Ycm * omt2 * t) +
                (3f * curve.P2.Ycm * omt * t2) +
                (curve.P3.Ycm * t3);
            return new WorldPoint(Round(x), Round(y));
        }

        private static float EstimateCurveLengthCm(in CurveGeometry curve)
        {
            float length = 0f;
            WorldPoint previous = curve.P0;
            const int samples = 12;
            for (int i = 1; i <= samples; i++)
            {
                WorldPoint current = EvaluatePoint(in curve, i / (float)samples);
                length += DistanceCm(previous, current);
                previous = current;
            }

            return length;
        }

        private static Vector3 ToSplinePoint(in WorldPoint point)
        {
            return new Vector3(WorldUnits.CmToM(point.Xcm), 0.03f, WorldUnits.CmToM(point.Ycm));
        }

        private static float DistanceCm(WorldPoint a, WorldPoint b)
        {
            float dx = b.Xcm - a.Xcm;
            float dy = b.Ycm - a.Ycm;
            return MathF.Sqrt((dx * dx) + (dy * dy));
        }

        private static int Round(float value)
        {
            return (int)MathF.Round(value, MidpointRounding.AwayFromZero);
        }

        private static ulong PackDirectedEdge(int from, int to)
        {
            return (uint)from | ((ulong)(uint)to << 32);
        }

        private static long PackPoint(int xcm, int ycm)
        {
            return ((long)xcm << 32) ^ (uint)ycm;
        }

        private readonly struct AuthoredNode
        {
            public readonly WorldPoint Point;
            public readonly TagBits256 Tags;

            public AuthoredNode(in WorldPoint point, in TagBits256 tags)
            {
                Point = point;
                Tags = tags;
            }
        }

        private readonly struct SampledNode
        {
            public readonly WorldPoint Point;
            public readonly TagBits256 Tags;

            public SampledNode(in WorldPoint point, in TagBits256 tags)
            {
                Point = point;
                Tags = tags;
            }
        }

        private readonly struct GlobalEdge
        {
            public readonly int FromNodeId;
            public readonly int ToNodeId;
            public readonly float Cost;
            public readonly TagBits256 Tags;
            public readonly int DepthCm;
            public readonly int WidthCm;

            public GlobalEdge(int fromNodeId, int toNodeId, float cost, in TagBits256 tags, int depthCm, int widthCm)
            {
                FromNodeId = fromNodeId;
                ToNodeId = toNodeId;
                Cost = cost;
                Tags = tags;
                DepthCm = depthCm;
                WidthCm = widthCm;
            }
        }

        private readonly struct LocalEdge
        {
            public readonly int FromLocalNodeId;
            public readonly int ToLocalNodeId;
            public readonly float Cost;
            public readonly TagBits256 Tags;
            public readonly int DepthCm;
            public readonly int WidthCm;

            public LocalEdge(int fromLocalNodeId, int toLocalNodeId, float cost, in TagBits256 tags, int depthCm, int widthCm)
            {
                FromLocalNodeId = fromLocalNodeId;
                ToLocalNodeId = toLocalNodeId;
                Cost = cost;
                Tags = tags;
                DepthCm = depthCm;
                WidthCm = widthCm;
            }
        }

        private readonly struct CurveGeometry
        {
            public readonly WorldPoint P0;
            public readonly WorldPoint P1;
            public readonly WorldPoint P2;
            public readonly WorldPoint P3;

            public CurveGeometry(WorldPoint p0, WorldPoint p1, WorldPoint p2, WorldPoint p3)
            {
                P0 = p0;
                P1 = p1;
                P2 = p2;
                P3 = p3;
            }
        }

        private readonly struct WorldPoint
        {
            public readonly int Xcm;
            public readonly int Ycm;

            public WorldPoint(int xcm, int ycm)
            {
                Xcm = xcm;
                Ycm = ycm;
            }

            public Vector2 ToVector2()
            {
                return new Vector2(Xcm, Ycm);
            }
        }
    }
}
