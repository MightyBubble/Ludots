using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Presentation.Rendering;

namespace RoadNetworkShowcaseMod.Runtime
{
    public sealed class RoadNetworkScenarioDefinition
    {
        private const float SampleStepCm = 600f;
        private static readonly Vector4 RoadFill = new(0.78f, 0.58f, 0.28f, 0.88f);
        private static readonly Vector4 RoadBorder = new(0.96f, 0.82f, 0.52f, 0.94f);

        private readonly Dictionary<long, GraphChunkData> _graphChunks;
        private readonly Dictionary<long, RoadSplineSpec[]> _roadSplineChunks;
        private readonly Dictionary<byte, WorldPoint> _landmarks;

        public int ChunkSizeCm { get; }
        public int StreamingRadiusCm { get; }

        private RoadNetworkScenarioDefinition(
            int chunkSizeCm,
            int streamingRadiusCm,
            Dictionary<long, GraphChunkData> graphChunks,
            Dictionary<long, RoadSplineSpec[]> roadSplineChunks,
            Dictionary<byte, WorldPoint> landmarks)
        {
            ChunkSizeCm = chunkSizeCm;
            StreamingRadiusCm = streamingRadiusCm;
            _graphChunks = graphChunks;
            _roadSplineChunks = roadSplineChunks;
            _landmarks = landmarks;
        }

        public bool TryGetGraphChunk(long chunkKey, out GraphChunkData chunk)
        {
            return _graphChunks.TryGetValue(chunkKey, out chunk!);
        }

        public bool TryGetRoadSplineChunk(long chunkKey, out RoadSplineSpec[] segments)
        {
            return _roadSplineChunks.TryGetValue(chunkKey, out segments!);
        }

        public bool TryGetLandmarkWorldCm(RoadLandmarkId id, out Vector3 worldCm)
        {
            worldCm = default;
            if (!_landmarks.TryGetValue((byte)id, out WorldPoint point))
            {
                return false;
            }

            worldCm = new Vector3(point.Xcm, 0f, point.Ycm);
            return true;
        }

        public static RoadNetworkScenarioDefinition Create(int chunkSizeCm)
        {
            var blueCapital = new WorldPoint(-18000, 0);
            var westGate = new WorldPoint(-9000, 0);
            var centralCrossing = new WorldPoint(0, 0);
            var eastGate = new WorldPoint(9000, 0);
            var redCapital = new WorldPoint(18000, 0);
            var northPass = new WorldPoint(0, 9000);
            var southFord = new WorldPoint(0, -9000);
            var northWatch = new WorldPoint(9000, 9000);
            var southWatch = new WorldPoint(9000, -9000);

            WorldPoint[][] roads =
            {
                new[] { blueCapital, westGate, centralCrossing, eastGate, redCapital },
                new[] { centralCrossing, northPass, northWatch, eastGate },
                new[] { centralCrossing, southFord, southWatch, eastGate },
                new[] { westGate, northPass },
                new[] { westGate, southFord },
            };

            var globalNodes = new List<WorldPoint>(64);
            var nodeIdsByPoint = new Dictionary<long, int>(64);
            var uniqueEdges = new HashSet<ulong>();
            var edges = new List<GlobalEdge>(96);

            for (int i = 0; i < roads.Length; i++)
            {
                List<int> sampled = SampleRoadNodes(roads[i], globalNodes, nodeIdsByPoint);
                for (int edgeIndex = 0; edgeIndex < sampled.Count - 1; edgeIndex++)
                {
                    int from = sampled[edgeIndex];
                    int to = sampled[edgeIndex + 1];
                    ulong edgeKey = PackUndirectedEdge(from, to);
                    if (!uniqueEdges.Add(edgeKey))
                    {
                        continue;
                    }

                    edges.Add(new GlobalEdge(from, to, DistanceCm(globalNodes[from], globalNodes[to])));
                }
            }

            var nodeChunkKeys = new long[globalNodes.Count];
            var nodesByChunk = new Dictionary<long, List<int>>();
            for (int nodeId = 0; nodeId < globalNodes.Count; nodeId++)
            {
                long chunkKey = GraphChunkKey.FromWorld(new WorldCmInt2(globalNodes[nodeId].Xcm, globalNodes[nodeId].Ycm), chunkSizeCm);
                nodeChunkKeys[nodeId] = chunkKey;
                if (!nodesByChunk.TryGetValue(chunkKey, out List<int>? nodes))
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
            foreach (GlobalEdge edge in edges)
            {
                AddDirectedEdge(edge.FromNodeId, edge.ToNodeId, edge.Cost, nodeChunkKeys, localNodeIdsByChunk, localEdgesByChunk, crossEdgesByChunk);
                AddDirectedEdge(edge.ToNodeId, edge.FromNodeId, edge.Cost, nodeChunkKeys, localNodeIdsByChunk, localEdgesByChunk, crossEdgesByChunk);
            }

            var graphChunks = new Dictionary<long, GraphChunkData>(nodesByChunk.Count);
            foreach ((long chunkKey, List<int> chunkNodes) in nodesByChunk)
            {
                List<LocalEdge> localEdges = localEdgesByChunk.TryGetValue(chunkKey, out List<LocalEdge>? storedLocalEdges)
                    ? storedLocalEdges
                    : s_emptyLocalEdges;
                List<GraphCrossEdge> crossEdges = crossEdgesByChunk.TryGetValue(chunkKey, out List<GraphCrossEdge>? storedCrossEdges)
                    ? storedCrossEdges
                    : s_emptyCrossEdges;

                var builder = new NodeGraphBuilder(chunkNodes.Count, localEdges.Count);
                for (int i = 0; i < chunkNodes.Count; i++)
                {
                    WorldPoint point = globalNodes[chunkNodes[i]];
                    builder.AddNode(point.Xcm, point.Ycm);
                }

                for (int i = 0; i < localEdges.Count; i++)
                {
                    LocalEdge edge = localEdges[i];
                    builder.AddEdge(edge.FromLocalNodeId, edge.ToLocalNodeId, edge.Cost);
                }

                graphChunks.Add(chunkKey, new GraphChunkData(builder.Build(), crossEdges.ToArray()));
            }

            var roadSplineChunks = new Dictionary<long, List<RoadSplineSpec>>();
            int nextStableId = 1;
            for (int roadIndex = 0; roadIndex < roads.Length; roadIndex++)
            {
                WorldPoint[] polyline = roads[roadIndex];
                for (int segmentIndex = 0; segmentIndex < polyline.Length - 1; segmentIndex++)
                {
                    WorldPoint previous = segmentIndex > 0 ? polyline[segmentIndex - 1] : polyline[segmentIndex];
                    WorldPoint start = polyline[segmentIndex];
                    WorldPoint end = polyline[segmentIndex + 1];
                    WorldPoint next = segmentIndex + 2 < polyline.Length ? polyline[segmentIndex + 2] : polyline[segmentIndex + 1];
                    RoadCurveGeometry curve = BuildRoadCurve(previous, start, end, next);
                    RoadSplineSpec spec = BuildRoadSpline(nextStableId++, in curve);
                    WorldPoint midpoint = EvaluateRoadPoint(in curve, 0.5f);
                    long ownerChunkKey = GraphChunkKey.FromWorld(new WorldCmInt2(midpoint.Xcm, midpoint.Ycm), chunkSizeCm);
                    if (!roadSplineChunks.TryGetValue(ownerChunkKey, out List<RoadSplineSpec>? chunkSplines))
                    {
                        chunkSplines = new List<RoadSplineSpec>();
                        roadSplineChunks.Add(ownerChunkKey, chunkSplines);
                    }

                    chunkSplines.Add(spec);
                }
            }

            var frozenRoadSplineChunks = new Dictionary<long, RoadSplineSpec[]>(roadSplineChunks.Count);
            foreach ((long chunkKey, List<RoadSplineSpec> chunkSplines) in roadSplineChunks)
            {
                frozenRoadSplineChunks.Add(chunkKey, chunkSplines.ToArray());
            }

            var landmarks = new Dictionary<byte, WorldPoint>
            {
                [(byte)RoadLandmarkId.BlueCapital] = blueCapital,
                [(byte)RoadLandmarkId.WestGate] = westGate,
                [(byte)RoadLandmarkId.CentralCrossing] = centralCrossing,
                [(byte)RoadLandmarkId.EastGate] = eastGate,
                [(byte)RoadLandmarkId.RedCapital] = redCapital,
                [(byte)RoadLandmarkId.NorthPass] = northPass,
                [(byte)RoadLandmarkId.SouthFord] = southFord,
                [(byte)RoadLandmarkId.NorthWatch] = northWatch,
                [(byte)RoadLandmarkId.SouthWatch] = southWatch,
            };

            return new RoadNetworkScenarioDefinition(
                chunkSizeCm,
                streamingRadiusCm: Math.Max(12000, chunkSizeCm * 2),
                graphChunks,
                frozenRoadSplineChunks,
                landmarks);
        }

        private static void AddDirectedEdge(
            int fromNodeId,
            int toNodeId,
            float cost,
            IReadOnlyList<long> nodeChunkKeys,
            IReadOnlyDictionary<long, Dictionary<int, int>> localNodeIdsByChunk,
            IDictionary<long, List<LocalEdge>> localEdgesByChunk,
            IDictionary<long, List<GraphCrossEdge>> crossEdgesByChunk)
        {
            long fromChunkKey = nodeChunkKeys[fromNodeId];
            long toChunkKey = nodeChunkKeys[toNodeId];
            int fromLocalNodeId = localNodeIdsByChunk[fromChunkKey][fromNodeId];
            int toLocalNodeId = localNodeIdsByChunk[toChunkKey][toNodeId];

            if (fromChunkKey == toChunkKey)
            {
                if (!localEdgesByChunk.TryGetValue(fromChunkKey, out List<LocalEdge>? localEdges))
                {
                    localEdges = new List<LocalEdge>();
                    localEdgesByChunk.Add(fromChunkKey, localEdges);
                }

                localEdges.Add(new LocalEdge(fromLocalNodeId, toLocalNodeId, cost));
                return;
            }

            if (!crossEdgesByChunk.TryGetValue(fromChunkKey, out List<GraphCrossEdge>? crossEdges))
            {
                crossEdges = new List<GraphCrossEdge>();
                crossEdgesByChunk.Add(fromChunkKey, crossEdges);
            }

            crossEdges.Add(new GraphCrossEdge(
                (ushort)fromLocalNodeId,
                toChunkKey,
                (ushort)toLocalNodeId,
                cost,
                tagSetId: 0));
        }

        private static List<int> SampleRoadNodes(
            IReadOnlyList<WorldPoint> road,
            List<WorldPoint> globalNodes,
            Dictionary<long, int> nodeIdsByPoint)
        {
            var result = new List<int>(road.Count * 2);
            for (int segmentIndex = 0; segmentIndex < road.Count - 1; segmentIndex++)
            {
                WorldPoint previous = segmentIndex > 0 ? road[segmentIndex - 1] : road[segmentIndex];
                WorldPoint start = road[segmentIndex];
                WorldPoint end = road[segmentIndex + 1];
                WorldPoint next = segmentIndex + 2 < road.Count ? road[segmentIndex + 2] : road[segmentIndex + 1];
                RoadCurveGeometry curve = BuildRoadCurve(previous, start, end, next);
                float curveLengthCm = EstimateCurveLengthCm(in curve);
                int steps = Math.Max(1, (int)MathF.Ceiling(curveLengthCm / SampleStepCm));
                int firstStep = segmentIndex == 0 ? 0 : 1;

                for (int step = firstStep; step <= steps; step++)
                {
                    float t = step / (float)steps;
                    WorldPoint sampled = EvaluateRoadPoint(in curve, t);
                    int nodeId = GetOrAddNode(sampled, globalNodes, nodeIdsByPoint);
                    if (result.Count == 0 || result[result.Count - 1] != nodeId)
                    {
                        result.Add(nodeId);
                    }
                }
            }

            return result;
        }

        private static int GetOrAddNode(WorldPoint point, IList<WorldPoint> globalNodes, IDictionary<long, int> nodeIdsByPoint)
        {
            long key = PackPoint(point.Xcm, point.Ycm);
            if (nodeIdsByPoint.TryGetValue(key, out int existing))
            {
                return existing;
            }

            int nodeId = globalNodes.Count;
            globalNodes.Add(point);
            nodeIdsByPoint.Add(key, nodeId);
            return nodeId;
        }

        private static RoadCurveGeometry BuildRoadCurve(WorldPoint previous, WorldPoint start, WorldPoint end, WorldPoint next)
        {
            Vector2 prev = previous.ToVector2();
            Vector2 p0 = start.ToVector2();
            Vector2 p3 = end.ToVector2();
            Vector2 nextPoint = next.ToVector2();
            Vector2 control0 = p0 + ((p3 - prev) / 6f);
            Vector2 control1 = p3 - ((nextPoint - p0) / 6f);
            return new RoadCurveGeometry(
                new WorldPoint((int)MathF.Round(p0.X, MidpointRounding.AwayFromZero), (int)MathF.Round(p0.Y, MidpointRounding.AwayFromZero)),
                new WorldPoint((int)MathF.Round(control0.X, MidpointRounding.AwayFromZero), (int)MathF.Round(control0.Y, MidpointRounding.AwayFromZero)),
                new WorldPoint((int)MathF.Round(control1.X, MidpointRounding.AwayFromZero), (int)MathF.Round(control1.Y, MidpointRounding.AwayFromZero)),
                new WorldPoint((int)MathF.Round(p3.X, MidpointRounding.AwayFromZero), (int)MathF.Round(p3.Y, MidpointRounding.AwayFromZero)));
        }

        private static RoadSplineSpec BuildRoadSpline(int stableId, in RoadCurveGeometry curve)
        {
            return new RoadSplineSpec(
                stableId,
                ToSplinePoint(curve.P0),
                ToSplinePoint(curve.P1),
                ToSplinePoint(curve.P2),
                ToSplinePoint(curve.P3),
                width: 1.65f,
                fill: RoadFill,
                border: RoadBorder,
                borderWidth: 0.08f,
                style: 0);
        }

        private static WorldPoint EvaluateRoadPoint(in RoadCurveGeometry curve, float t)
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
            return new WorldPoint(
                (int)MathF.Round(x, MidpointRounding.AwayFromZero),
                (int)MathF.Round(y, MidpointRounding.AwayFromZero));
        }

        private static float EstimateCurveLengthCm(in RoadCurveGeometry curve)
        {
            float length = 0f;
            WorldPoint previous = curve.P0;
            const int samples = 12;
            for (int i = 1; i <= samples; i++)
            {
                WorldPoint current = EvaluateRoadPoint(in curve, i / (float)samples);
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

        private static ulong PackUndirectedEdge(int a, int b)
        {
            uint min = (uint)Math.Min(a, b);
            uint max = (uint)Math.Max(a, b);
            return min | ((ulong)max << 32);
        }

        private static long PackPoint(int xcm, int ycm)
        {
            return ((long)xcm << 32) ^ (uint)ycm;
        }

        private static readonly List<LocalEdge> s_emptyLocalEdges = new();
        private static readonly List<GraphCrossEdge> s_emptyCrossEdges = new();

        public enum RoadLandmarkId : byte
        {
            None = 0,
            BlueCapital = 1,
            WestGate = 2,
            CentralCrossing = 3,
            EastGate = 4,
            RedCapital = 5,
            NorthPass = 6,
            SouthFord = 7,
            NorthWatch = 8,
            SouthWatch = 9,
        }

        public readonly struct RoadSplineSpec
        {
            public readonly int StableId;
            public readonly Vector3 P0;
            public readonly Vector3 P1;
            public readonly Vector3 P2;
            public readonly Vector3 P3;
            public readonly float Width;
            public readonly Vector4 Fill;
            public readonly Vector4 Border;
            public readonly float BorderWidth;
            public readonly byte Style;

            public RoadSplineSpec(
                int stableId,
                in Vector3 p0,
                in Vector3 p1,
                in Vector3 p2,
                in Vector3 p3,
                float width,
                in Vector4 fill,
                in Vector4 border,
                float borderWidth,
                byte style)
            {
                StableId = stableId;
                P0 = p0;
                P1 = p1;
                P2 = p2;
                P3 = p3;
                Width = width;
                Fill = fill;
                Border = border;
                BorderWidth = borderWidth;
                Style = style;
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

            public static WorldPoint Lerp(WorldPoint start, WorldPoint end, float t)
            {
                int x = (int)MathF.Round(start.Xcm + ((end.Xcm - start.Xcm) * t), MidpointRounding.AwayFromZero);
                int y = (int)MathF.Round(start.Ycm + ((end.Ycm - start.Ycm) * t), MidpointRounding.AwayFromZero);
                return new WorldPoint(x, y);
            }
        }

        private readonly struct GlobalEdge
        {
            public readonly int FromNodeId;
            public readonly int ToNodeId;
            public readonly float Cost;

            public GlobalEdge(int fromNodeId, int toNodeId, float cost)
            {
                FromNodeId = fromNodeId;
                ToNodeId = toNodeId;
                Cost = cost;
            }
        }

        private readonly struct RoadCurveGeometry
        {
            public readonly WorldPoint P0;
            public readonly WorldPoint P1;
            public readonly WorldPoint P2;
            public readonly WorldPoint P3;

            public RoadCurveGeometry(WorldPoint p0, WorldPoint p1, WorldPoint p2, WorldPoint p3)
            {
                P0 = p0;
                P1 = p1;
                P2 = p2;
                P3 = p3;
            }
        }

        private readonly struct LocalEdge
        {
            public readonly int FromLocalNodeId;
            public readonly int ToLocalNodeId;
            public readonly float Cost;

            public LocalEdge(int fromLocalNodeId, int toLocalNodeId, float cost)
            {
                FromLocalNodeId = fromLocalNodeId;
                ToLocalNodeId = toLocalNodeId;
                Cost = cost;
            }
        }
    }
}
