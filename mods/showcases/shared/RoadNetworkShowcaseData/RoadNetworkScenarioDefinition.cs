using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Presentation.Surfaces;
using Ludots.Core.TransportNetwork;

namespace RoadNetworkShowcaseMod.Runtime
{
    public sealed class RoadNetworkScenarioDefinition
    {
        private const int SampleStepCm = 600;
        private static readonly Vector4 RoadFill = new(0.78f, 0.58f, 0.28f, 0.88f);
        private static readonly Vector4 RoadBorder = new(0.96f, 0.82f, 0.52f, 0.94f);

        private readonly Dictionary<long, GraphChunkData> _graphChunks;
        private readonly Dictionary<long, RoadRibbonSpec[]> _splineRibbonChunks;
        private readonly Dictionary<byte, WorldPoint> _landmarks;

        public int ChunkSizeCm { get; }
        public int StreamingRadiusCm { get; }

        private RoadNetworkScenarioDefinition(
            int chunkSizeCm,
            int streamingRadiusCm,
            Dictionary<long, GraphChunkData> graphChunks,
            Dictionary<long, RoadRibbonSpec[]> splineRibbonChunks,
            Dictionary<byte, WorldPoint> landmarks)
        {
            ChunkSizeCm = chunkSizeCm;
            StreamingRadiusCm = streamingRadiusCm;
            _graphChunks = graphChunks;
            _splineRibbonChunks = splineRibbonChunks;
            _landmarks = landmarks;
        }

        public bool TryGetGraphChunk(long chunkKey, out GraphChunkData chunk)
        {
            return _graphChunks.TryGetValue(chunkKey, out chunk!);
        }

        public bool TryGetRoadRibbonChunk(long chunkKey, out RoadRibbonSpec[] segments)
        {
            return _splineRibbonChunks.TryGetValue(chunkKey, out segments!);
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

            TransportNetworkAsset asset = BuildAsset(
                blueCapital,
                westGate,
                centralCrossing,
                eastGate,
                redCapital,
                northPass,
                southFord,
                northWatch,
                southWatch);
            TransportNetworkBakedAsset baked = new TransportNetworkBaker().Bake(asset, chunkSizeCm);

            var graphChunks = new Dictionary<long, GraphChunkData>(baked.GraphChunks);
            var ribbonChunks = new Dictionary<long, RoadRibbonSpec[]>(baked.RibbonChunks.Count);
            int nextStableId = 1;
            foreach ((long chunkKey, SurfaceSplineSegment[] segments) in baked.RibbonChunks)
            {
                var specs = new RoadRibbonSpec[segments.Length];
                for (int i = 0; i < segments.Length; i++)
                {
                    SurfaceSplineSegment segment = segments[i];
                    specs[i] = new RoadRibbonSpec(
                        nextStableId++,
                        segment.P0,
                        segment.P1,
                        segment.P2,
                        segment.P3,
                        segment.WidthMeters,
                        RoadFill,
                        RoadBorder,
                        borderWidth: 0.08f);
                }

                ribbonChunks.Add(chunkKey, specs);
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
                ribbonChunks,
                landmarks);
        }

        private static TransportNetworkAsset BuildAsset(
            WorldPoint blueCapital,
            WorldPoint westGate,
            WorldPoint centralCrossing,
            WorldPoint eastGate,
            WorldPoint redCapital,
            WorldPoint northPass,
            WorldPoint southFord,
            WorldPoint northWatch,
            WorldPoint southWatch)
        {
            var asset = new TransportNetworkAsset
            {
                Id = "showcase.transport_network",
                SampleStepCm = SampleStepCm,
                DefaultVisualWidthMeters = 1.65f,
                Nodes = new List<TransportNetworkNode>
                {
                    Node("blue_capital", blueCapital),
                    Node("west_gate", westGate),
                    Node("central_crossing", centralCrossing),
                    Node("east_gate", eastGate),
                    Node("red_capital", redCapital),
                    Node("north_pass", northPass),
                    Node("south_ford", southFord, TransportNetworkNodeKind.Ford),
                    Node("north_watch", northWatch),
                    Node("south_watch", southWatch),
                },
                Segments = new List<TransportNetworkSegment>
                {
                    Segment("main", "blue_capital", "west_gate", "central_crossing", "east_gate", "red_capital"),
                    Segment("north_arc", "central_crossing", "north_pass", "north_watch", "east_gate"),
                    Segment("south_arc", "central_crossing", "south_ford", "south_watch", "east_gate"),
                    Segment("west_north", "west_gate", "north_pass"),
                    Segment("west_south", "west_gate", "south_ford"),
                }
            };
            return asset;
        }

        private static TransportNetworkNode Node(string id, WorldPoint point, TransportNetworkNodeKind kind = TransportNetworkNodeKind.Normal)
        {
            return new TransportNetworkNode
            {
                Id = id,
                Xcm = point.Xcm,
                Ycm = point.Ycm,
                Kind = kind,
                Tags = new List<string>()
            };
        }

        private static TransportNetworkSegment Segment(string id, params string[] nodeIds)
        {
            var points = new List<TransportNetworkPoint>(nodeIds.Length);
            for (int i = 0; i < nodeIds.Length; i++)
            {
                points.Add(TransportNetworkPoint.FromNode(nodeIds[i]));
            }

            return new TransportNetworkSegment
            {
                Id = id,
                Points = points,
                Direction = TransportNetworkDirection.Bidirectional,
                Tags = new List<string>(),
                VisualWidthMeters = 1.65f
            };
        }

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

        public readonly struct RoadRibbonSpec
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

            public RoadRibbonSpec(
                int stableId,
                in Vector3 p0,
                in Vector3 p1,
                in Vector3 p2,
                in Vector3 p3,
                float width,
                in Vector4 fill,
                in Vector4 border,
                float borderWidth)
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
        }
    }
}
