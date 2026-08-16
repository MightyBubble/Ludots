using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.GraphQuery;
using Ludots.Core.Navigation.GraphSemantics.GAS;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.TransportNetwork;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class TransportNetworkCoreTests
    {
        [Test]
        public void TransportNetworkAssetLoader_LoadsStrictJsonThroughConfigPipeline()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "ludots-transport-network-loader-" + Guid.NewGuid().ToString("N"));
            string configsRoot = Path.Combine(tempRoot, "Configs");
            Directory.CreateDirectory(Path.Combine(configsRoot, "TransportNetwork"));
            File.WriteAllText(Path.Combine(configsRoot, "config_catalog.json"),
                """
                [
                  { "Path": "TransportNetwork/transport_network.json", "Policy": "Replace" }
                ]
                """);
            File.WriteAllText(Path.Combine(configsRoot, "TransportNetwork", "transport_network.json"),
                """
                {
                  "id": "transport.loader.contract",
                  "sampleStepCm": 500,
                  "defaultVisualWidthMeters": 2.5,
                  "nodes": [
                    { "id": "harbor_a", "xcm": 0, "ycm": 0, "kind": "Port", "tags": ["Transport.Hub"] },
                    { "id": "harbor_b", "xcm": 1000, "ycm": 0, "kind": "Embark", "tags": [] }
                  ],
                  "segments": [
                    {
                      "id": "channel",
                      "points": [ { "nodeId": "harbor_a" }, { "nodeId": "harbor_b" } ],
                      "sampleStepCm": 0,
                      "direction": "Bidirectional",
                      "flowDirection": "Forward",
                      "areaId": "Transport.Area.Canal",
                      "tags": ["Transport.Area.Water"],
                      "depthCm": 400,
                      "widthCm": 1200,
                      "laneCount": 0,
                      "visualWidthMeters": 0
                    }
                  ]
                }
                """);

            try
            {
                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", tempRoot);
                var pipeline = new ConfigPipeline(vfs, modLoader: null!);
                ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);

                TransportNetworkAsset asset = new TransportNetworkAssetLoader(pipeline).Load(catalog);

                Assert.That(asset.Id, Is.EqualTo("transport.loader.contract"));
                Assert.That(asset.Nodes[0].Kind, Is.EqualTo(TransportNetworkNodeKind.Port));
                Assert.That(asset.Segments[0].FlowDirection, Is.EqualTo(TransportNetworkFlowDirection.Forward));
                Assert.That(asset.Segments[0].DepthCm, Is.EqualTo(400));
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Test]
        public void TransportNetworkBaker_ProducesDeterministicGraphAndRibbon_WithWaterReadyFields()
        {
            TransportNetworkAsset asset = CreateWaterReadyAsset(sampleStepCm: 1000);

            TransportNetworkBakedAsset first = new TransportNetworkBaker().Bake(asset, chunkSizeCm: 1000);
            TransportNetworkBakedAsset second = new TransportNetworkBaker().Bake(asset, chunkSizeCm: 1000);

            Assert.That(BuildBakeSignature(first), Is.EqualTo(BuildBakeSignature(second)));
            Assert.That(first.SampledNodeCount, Is.GreaterThanOrEqualTo(4));
            Assert.That(first.DirectedEdgeCount, Is.GreaterThanOrEqualTo(6));
            Assert.That(first.RibbonChunks.Values.Sum(segments => segments.Length), Is.EqualTo(asset.Segments.Sum(segment => segment.Points.Count - 1)));

            NodeGraph graph = BuildLoadedGraph(first);
            Assert.That(AnyEdgeHasTag(graph, "Transport.Area.Shallow"), Is.True);
            Assert.That(AnyEdgeHasTag(graph, "Transport.Area.DeepWater"), Is.True);
            Assert.That(AnyEdgeHasTag(graph, "Transport.Flow.Downstream"), Is.True);
            Assert.That(AnyEdgeHasTag(graph, "Transport.Flow.Upstream"), Is.True);
            Assert.That(AnyNodeHasTag(graph, "Transport.NodeKind.Port"), Is.True);
            Assert.That(AnyEdgeCapacity(graph, depthCm: 100, widthCm: 300), Is.True);
            Assert.That(AnyEdgeCapacity(graph, depthCm: 500, widthCm: 1200), Is.True);
        }

        [Test]
        public void ChunkedStore_FlattensTagSetsAndCapacity_ForLocalAndCrossChunkEdges()
        {
            TagBits256 localTag = BitsForTag("Transport.Test.Local");
            TagBits256 crossTag = BitsForTag("Transport.Test.Cross");
            long aKey = GraphChunkKey.Pack(0, 0);
            long bKey = GraphChunkKey.Pack(1, 0);

            var aBuilder = new NodeGraphBuilder(2, 1);
            ushort localTagSetId = aBuilder.AddTagSet(in localTag);
            aBuilder.AddNode(0, 0);
            aBuilder.AddNode(900, 0);
            aBuilder.AddEdge(0, 1, 10f, localTagSetId, depthCm: 200, widthCm: 700);

            var bBuilder = new NodeGraphBuilder(1, 0);
            bBuilder.AddNode(1200, 0);

            var store = new ChunkedNodeGraphStore();
            store.AddOrReplace(aKey, new GraphChunkData(aBuilder.Build(), new[]
            {
                new GraphCrossEdge(1, bKey, 0, 20f, tagSetId: 0, depthCm: 300, widthCm: 800, tagBits: crossTag)
            }));
            store.AddOrReplace(bKey, new GraphChunkData(bBuilder.Build(), Array.Empty<GraphCrossEdge>()));

            NodeGraph graph = store.BuildLoadedView().Graph;

            Assert.That(graph.EdgeCount, Is.EqualTo(2));
            Assert.That(AnyEdgeHasTag(graph, "Transport.Test.Local"), Is.True);
            Assert.That(AnyEdgeHasTag(graph, "Transport.Test.Cross"), Is.True);
            Assert.That(AnyEdgeCapacity(graph, 200, 700), Is.True);
            Assert.That(AnyEdgeCapacity(graph, 300, 800), Is.True);
        }

        [Test]
        public void NodeGraphBuilder_ResetClearsTagPool()
        {
            TagBits256 firstTag = BitsForTag("Transport.Test.Reset.First");
            TagBits256 secondTag = BitsForTag("Transport.Test.Reset.Second");

            var builder = new NodeGraphBuilder(1, 0);
            ushort firstTagSetId = builder.AddTagSet(in firstTag);
            Assert.That(firstTagSetId, Is.EqualTo(1));
            builder.AddNode(0, 0, firstTagSetId);
            NodeGraph firstGraph = builder.Build();
            Assert.That(firstGraph.TagSets.Length, Is.EqualTo(2));

            builder.Reset();
            ushort secondTagSetId = builder.AddTagSet(in secondTag);
            Assert.That(secondTagSetId, Is.EqualTo(1));
            builder.AddNode(100, 0, secondTagSetId);
            NodeGraph secondGraph = builder.Build();

            Assert.That(secondGraph.TagSets.Length, Is.EqualTo(2));
            Assert.That(secondGraph.TagSets[1].Intersects(in secondTag), Is.True);
            Assert.That(AnyTagSetHasTag(secondGraph, firstTag), Is.False);
        }

        [Test]
        public void GraphQueryServices_ProjectSnapAndStitchWithoutDuplicateLegBoundary()
        {
            var builder = new NodeGraphBuilder(3, 2);
            builder.AddNode(0, 0);
            builder.AddNode(100, 0);
            builder.AddNode(200, 0);
            builder.AddEdge(0, 1, 100f);
            builder.AddEdge(1, 2, 100f);
            NodeGraph graph = builder.Build();
            INodeGraphSpatialIndex index = LoadedGraphRuntime.CreateSpatialIndex(graph, preferredCellSizeCm: 100);

            Span<int> candidates = stackalloc int[8];
            Assert.That(GraphEdgeProjectionQuery.TryProjectNearestEdge(
                graph,
                index,
                new WorldCmInt2(50, 25),
                radiusCm: 100,
                candidates,
                out GraphEdgeProjection projection), Is.True);
            Assert.That(projection.FromNodeId, Is.EqualTo(0));
            Assert.That(projection.ToNodeId, Is.EqualTo(1));
            Assert.That(projection.ProjectedXcm, Is.EqualTo(50));
            Assert.That(projection.ProjectedYcm, Is.EqualTo(0));
            Assert.That(projection.DistanceSqCm, Is.EqualTo(625f));

            Span<int> polyX = stackalloc[] { 0, 100, 200, 0 };
            Span<int> polyY = stackalloc[] { 0, 0, 0, 0 };
            int snapped = PolylineGoalSnapQuery.SnapGoalOntoPolyline(125f, 40f, polyX, polyY, count: 3);
            Assert.That(snapped, Is.EqualTo(3));
            Assert.That(polyX[2], Is.EqualTo(125));
            Assert.That(polyY[2], Is.EqualTo(0));

            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: 16);
            var routeBuilder = new GraphHybridRouteBuilder(
                new AutoPathService(graph, CreateAgentProfiles(("graph", 0, 0)), pathStore, CreatePathingConfig(("graph", "graph", false))),
                pathStore);
            Span<PathEndpoint> waypoints = stackalloc[]
            {
                PathEndpoint.FromNodeId(0),
                PathEndpoint.FromNodeId(1),
                PathEndpoint.FromNodeId(2)
            };
            Span<int> outX = stackalloc int[8];
            Span<int> outY = stackalloc int[8];

            Assert.That(routeBuilder.TryStitch(default, "graph", waypoints, outX, outY, out int count, out string failure), Is.True, failure);
            Assert.That(count, Is.EqualTo(3));
            Assert.That(outX.Slice(0, count).ToArray(), Is.EqualTo(new[] { 0, 100, 200 }));
        }

        [Test]
        public void AutoPathService_DynamicOverlaySwitchesRouteOnlyWhenEnabled()
        {
            NodeGraph graph = CreateOverlayChoiceGraph();
            var overlay = new GraphEdgeCostOverlay();
            overlay.EnsureCapacity(graph.EdgeCount);
            int blockedEdge = FindEdge(graph, fromNodeId: 0, toNodeId: 1);
            overlay.Blocked[blockedEdge] = 1;

            int[] staticY = SolveYPath(graph, "agent", useDynamicOverlay: false, overlay);
            int[] dynamicY = SolveYPath(graph, "agent", useDynamicOverlay: true, overlay);

            Assert.That(staticY, Is.EqualTo(new[] { 0, 0, 100 }));
            Assert.That(dynamicY, Is.EqualTo(new[] { 0, 100, 100 }));
        }

        [Test]
        public void AutoPathService_DynamicOverlayRequiresRegisteredOverlay()
        {
            NodeGraph graph = CreateOverlayChoiceGraph();
            var service = new AutoPathService(
                graph,
                CreateAgentProfiles(("agent", 0, 0)),
                new PathStore(maxPaths: 8, maxPointsPerPath: 16),
                CreatePathingConfig(("agent", "agent", true)));
            var request = new PathRequest(
                1,
                default,
                PathDomain.Auto,
                "agent",
                PathEndpoint.FromNodeId(0),
                PathEndpoint.FromNodeId(3),
                new PathBudget(0, 16));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => service.TrySolve(in request, out _))!;
            Assert.That(ex.Message, Does.Contain("useDynamicOverlay"));
        }

        [Test]
        public void WaterCapacity_BlocksShallowNarrowEdge_PerAgentDraftAndBeam()
        {
            NodeGraph graph = BuildLoadedGraph(new TransportNetworkBaker().Bake(CreateWaterReadyAsset(sampleStepCm: 1000), chunkSizeCm: 1000));
            var overlay = new GraphEdgeCostOverlay();

            int[] shallow = SolveYPath(
                graph,
                "skiff",
                useDynamicOverlay: false,
                overlay,
                CreateAgentProfiles(("skiff", 50, 50), ("barge", 300, 50)),
                CreatePathingConfig(("skiff", "skiff", false), ("barge", "barge", false)),
                PathEndpoint.FromWorldCm(0, 0),
                PathEndpoint.FromWorldCm(300, 0));
            int[] deep = SolveYPath(
                graph,
                "barge",
                useDynamicOverlay: false,
                overlay,
                CreateAgentProfiles(("skiff", 50, 50), ("barge", 300, 50)),
                CreatePathingConfig(("skiff", "skiff", false), ("barge", "barge", false)),
                PathEndpoint.FromWorldCm(0, 0),
                PathEndpoint.FromWorldCm(300, 0));

            Assert.That(shallow, Is.EqualTo(new[] { 0, 0 }));
            Assert.That(deep, Does.Contain(200));
            Assert.That(deep.Length, Is.GreaterThan(shallow.Length));
        }

        [Test]
        public void WaterFlowDirection_ProducesDirectionalCostAsymmetryThroughTags()
        {
            NodeGraph graph = BuildLoadedGraph(new TransportNetworkBaker().Bake(CreateWaterReadyAsset(sampleStepCm: 1000), chunkSizeCm: 1000));
            int harborA = FindNode(graph, 0, 0);
            int harborB = FindNode(graph, 300, 0);
            var policy = new TagRuleTraversalPolicy(graph)
            {
                EdgeRules = new[]
                {
                    new TagRuleTraversalPolicy.TagRule(BitsForTag("Transport.Flow.Downstream"), 1f, 0f, block: false),
                    new TagRuleTraversalPolicy.TagRule(BitsForTag("Transport.Flow.Upstream"), 4f, 0f, block: false)
                }
            };
            var scratch = new NodeGraphPathScratch();
            Span<int> path = stackalloc int[16];

            GraphPathResult downstream = NodeGraphPathService.FindPathAStar(graph, harborA, harborB, path, ref scratch, ref policy);
            GraphPathResult upstream = NodeGraphPathService.FindPathAStar(graph, harborB, harborA, path, ref scratch, ref policy);

            Assert.That(downstream.Status, Is.EqualTo(GraphPathStatus.Success));
            Assert.That(upstream.Status, Is.EqualTo(GraphPathStatus.Success));
            Assert.That(upstream.TravelCost, Is.GreaterThan(downstream.TravelCost));
        }

        [Test]
        public void TopologyControlsBarrierAndBridgeConnectivity()
        {
            NodeGraph withoutSharedNode = BuildLoadedGraph(new TransportNetworkBaker().Bake(CreateBarrierAsset(withSharedBridgeNode: false), chunkSizeCm: 1000));
            NodeGraph withSharedNode = BuildLoadedGraph(new TransportNetworkBaker().Bake(CreateBarrierAsset(withSharedBridgeNode: true), chunkSizeCm: 1000));
            var scratch = new NodeGraphPathScratch();
            var policy = new DefaultTraversalPolicy();
            Span<int> path = stackalloc int[16];

            GraphPathResult blocked = NodeGraphPathService.FindPathAStar(
                withoutSharedNode,
                FindNode(withoutSharedNode, -300, 0),
                FindNode(withoutSharedNode, 300, 0),
                path,
                ref scratch,
                ref policy);
            GraphPathResult bridged = NodeGraphPathService.FindPathAStar(
                withSharedNode,
                FindNode(withSharedNode, -300, 0),
                FindNode(withSharedNode, 300, 0),
                path,
                ref scratch,
                ref policy);

            Assert.That(blocked.Status, Is.EqualTo(GraphPathStatus.NotFound));
            Assert.That(bridged.Status, Is.EqualTo(GraphPathStatus.Success));
            Assert.That(AnyNodeHasTag(withSharedNode, "Transport.NodeKind.Bridge"), Is.True);
        }

        [Test]
        public void CoreTransportNetworkAndGraphQuery_NamesStayDomainNeutral()
        {
            string repoRoot = FindRepoRoot();
            string[] roots =
            {
                Path.Combine(repoRoot, "src", "Core", "TransportNetwork"),
                Path.Combine(repoRoot, "src", "Core", "Navigation", "GraphQuery")
            };
            string[] forbidden = { "road", "corridor", "fort", "landmark" };
            var hits = roots
                .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                .SelectMany(file => File.ReadLines(file)
                    .Select((line, index) => new { file, line, index })
                    .Where(item => forbidden.Any(token => item.line.Contains(token, StringComparison.OrdinalIgnoreCase)))
                    .Select(item => $"{Path.GetRelativePath(repoRoot, item.file)}:{item.index + 1}: {item.line.Trim()}"))
                .ToList();

            Assert.That(hits, Is.Empty, string.Join(Environment.NewLine, hits));
        }

        private static TransportNetworkAsset CreateWaterReadyAsset(int sampleStepCm)
        {
            return new TransportNetworkAsset
            {
                Id = "transport.water.contract",
                SampleStepCm = sampleStepCm,
                DefaultVisualWidthMeters = 2f,
                Nodes = new List<TransportNetworkNode>
                {
                    new() { Id = "harbor_a", Xcm = 0, Ycm = 0, Kind = TransportNetworkNodeKind.Port, Tags = new List<string>() },
                    new() { Id = "harbor_b", Xcm = 300, Ycm = 0, Kind = TransportNetworkNodeKind.Port, Tags = new List<string>() },
                    new() { Id = "deep_mid_a", Xcm = 0, Ycm = 200, Kind = TransportNetworkNodeKind.Normal, Tags = new List<string>() },
                    new() { Id = "deep_mid_b", Xcm = 300, Ycm = 200, Kind = TransportNetworkNodeKind.Normal, Tags = new List<string>() }
                },
                Segments = new List<TransportNetworkSegment>
                {
                    Segment("shallow", "Transport.Area.Shallow", depthCm: 100, widthCm: 300, TransportNetworkFlowDirection.Forward, "harbor_a", "harbor_b"),
                    Segment("deep", "Transport.Area.DeepWater", depthCm: 500, widthCm: 1200, TransportNetworkFlowDirection.None, "harbor_a", "deep_mid_a", "deep_mid_b", "harbor_b")
                }
            };
        }

        private static TransportNetworkSegment Segment(
            string id,
            string areaId,
            int depthCm,
            int widthCm,
            TransportNetworkFlowDirection flowDirection,
            params string[] nodeIds)
        {
            return new TransportNetworkSegment
            {
                Id = id,
                AreaId = areaId,
                Direction = TransportNetworkDirection.Bidirectional,
                FlowDirection = flowDirection,
                Tags = new List<string> { "Transport.Area.Water" },
                DepthCm = depthCm,
                WidthCm = widthCm,
                LaneCount = 0,
                VisualWidthMeters = 0,
                Points = nodeIds.Select(TransportNetworkPoint.FromNode).ToList()
            };
        }

        private static TransportNetworkAsset CreateBarrierAsset(bool withSharedBridgeNode)
        {
            var asset = new TransportNetworkAsset
            {
                Id = "transport.topology.contract",
                SampleStepCm = 1000,
                DefaultVisualWidthMeters = 1.5f,
                Nodes = new List<TransportNetworkNode>
                {
                    new() { Id = "left", Xcm = -300, Ycm = 0, Tags = new List<string>() },
                    new() { Id = "right", Xcm = 300, Ycm = 0, Tags = new List<string>() },
                    new() { Id = "water_a", Xcm = 0, Ycm = -300, Tags = new List<string>() },
                    new() { Id = "water_b", Xcm = 0, Ycm = 300, Tags = new List<string>() }
                },
                Segments = new List<TransportNetworkSegment>
                {
                    Segment("water", "Transport.Area.River", 200, 500, TransportNetworkFlowDirection.None, "water_a", "water_b")
                }
            };

            if (withSharedBridgeNode)
            {
                asset.Nodes.Add(new TransportNetworkNode
                {
                    Id = "bridge",
                    Xcm = 0,
                    Ycm = 0,
                    Kind = TransportNetworkNodeKind.Bridge,
                    Tags = new List<string>()
                });
                asset.Segments.Add(Segment("land_left", "Transport.Area.Land", 0, 0, TransportNetworkFlowDirection.None, "left", "bridge"));
                asset.Segments.Add(Segment("land_right", "Transport.Area.Land", 0, 0, TransportNetworkFlowDirection.None, "bridge", "right"));
            }
            else
            {
                asset.Nodes.Add(new TransportNetworkNode { Id = "left_bank", Xcm = -10, Ycm = 0, Tags = new List<string>() });
                asset.Nodes.Add(new TransportNetworkNode { Id = "right_bank", Xcm = 10, Ycm = 0, Tags = new List<string>() });
                asset.Segments.Add(Segment("land_left", "Transport.Area.Land", 0, 0, TransportNetworkFlowDirection.None, "left", "left_bank"));
                asset.Segments.Add(Segment("land_right", "Transport.Area.Land", 0, 0, TransportNetworkFlowDirection.None, "right_bank", "right"));
            }

            return asset;
        }

        private static NodeGraph CreateOverlayChoiceGraph()
        {
            var builder = new NodeGraphBuilder(4, 4);
            builder.AddNode(0, 0);
            builder.AddNode(100, 0);
            builder.AddNode(0, 100);
            builder.AddNode(100, 100);
            builder.AddEdge(0, 1, 1f);
            builder.AddEdge(1, 3, 1f);
            builder.AddEdge(0, 2, 2f);
            builder.AddEdge(2, 3, 2f);
            return builder.Build();
        }

        private static int[] SolveYPath(
            NodeGraph graph,
            string agentTypeId,
            bool useDynamicOverlay,
            GraphEdgeCostOverlay overlay)
        {
            return SolveYPath(
                graph,
                agentTypeId,
                useDynamicOverlay,
                overlay,
                CreateAgentProfiles((agentTypeId, 0, 0)),
                CreatePathingConfig((agentTypeId, agentTypeId, useDynamicOverlay)),
                PathEndpoint.FromNodeId(0),
                PathEndpoint.FromNodeId(3));
        }

        private static int[] SolveYPath(
            NodeGraph graph,
            string agentTypeId,
            bool useDynamicOverlay,
            GraphEdgeCostOverlay overlay,
            AgentProfileRegistry agentProfiles,
            PathingConfig config,
            PathEndpoint start,
            PathEndpoint goal)
        {
            config.AgentTypes.First(agent => string.Equals(agent.Id, agentTypeId, StringComparison.Ordinal)).NodeGraph.UseDynamicOverlay = useDynamicOverlay;
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: 32);
            var service = new AutoPathService(graph, agentProfiles, pathStore, config, overlay);
            var request = new PathRequest(1, default, PathDomain.Auto, agentTypeId, start, goal, new PathBudget(0, 32));
            Assert.That(service.TrySolve(in request, out PathResult result), Is.True);
            Assert.That(result.Status, Is.EqualTo(PathStatus.Found));
            Span<int> x = stackalloc int[32];
            Span<int> y = stackalloc int[32];
            Assert.That(service.TryCopyPath(in result.Handle, x, y, out int count), Is.True);
            int[] pathY = y.Slice(0, count).ToArray();
            pathStore.Release(result.Handle);
            return pathY;
        }

        private static AgentProfileRegistry CreateAgentProfiles(params (string id, float draftCm, float beamCm)[] profiles)
        {
            return new AgentProfileRegistry(profiles.Select(profile => new AgentProfileConfig
            {
                Id = profile.id,
                RadiusCm = 30,
                HeightCm = 180,
                ClearanceCm = 40,
                DraftCm = profile.draftCm,
                BeamCm = profile.beamCm,
                Mass = 1,
                Layer = 0
            }).ToArray());
        }

        private static PathingConfig CreatePathingConfig(params (string id, string profileId, bool useDynamicOverlay)[] agents)
        {
            return new PathingConfig
            {
                AgentTypes = agents.Select(agent => new PathingAgentTypeConfig
                {
                    Id = agent.id,
                    ProfileId = agent.profileId,
                    Selection = new PathingSelectionConfig { Mode = PathSelectionMode.PreferGraph },
                    NodeGraph = new PathingNodeGraphConfig
                    {
                        ProjectionMaxRadiusCm = 500,
                        UseDynamicOverlay = agent.useDynamicOverlay
                    }
                }).ToList()
            };
        }

        private static NodeGraph BuildLoadedGraph(TransportNetworkBakedAsset baked)
        {
            var store = new ChunkedNodeGraphStore();
            foreach ((long chunkKey, GraphChunkData chunk) in baked.GraphChunks)
            {
                store.AddOrReplace(chunkKey, chunk);
            }

            return store.BuildLoadedView().Graph;
        }

        private static string BuildBakeSignature(TransportNetworkBakedAsset baked)
        {
            var sb = new StringBuilder();
            foreach ((long chunkKey, GraphChunkData chunk) in baked.GraphChunks.OrderBy(pair => pair.Key))
            {
                NodeGraph graph = chunk.Graph;
                sb.Append("chunk=").Append(chunkKey).Append(';');
                AppendSpan(sb, graph.PosXcm);
                AppendSpan(sb, graph.PosYcm);
                AppendSpan(sb, graph.EdgeStart);
                AppendSpan(sb, graph.EdgeTo);
                AppendSpan(sb, graph.EdgeBaseCost);
                AppendSpan(sb, graph.EdgeDepthCm);
                AppendSpan(sb, graph.EdgeWidthCm);
                sb.Append("cross=").Append(chunk.CrossEdges.Length).Append(';');
            }

            foreach ((long chunkKey, var ribbons) in baked.RibbonChunks.OrderBy(pair => pair.Key))
            {
                sb.Append("ribbon=").Append(chunkKey).Append(':').Append(ribbons.Length).Append(';');
            }

            return sb.ToString();
        }

        private static void AppendSpan<T>(StringBuilder sb, ReadOnlySpan<T> span)
        {
            sb.Append('[');
            for (int i = 0; i < span.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(span[i]);
            }

            sb.Append(']');
        }

        private static bool AnyNodeHasTag(NodeGraph graph, string tag)
        {
            TagBits256 bits = BitsForTag(tag);
            for (int i = 0; i < graph.NodeCount; i++)
            {
                if (graph.TagSets[graph.NodeTagSetId[i]].Intersects(in bits))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AnyEdgeHasTag(NodeGraph graph, string tag)
        {
            TagBits256 bits = BitsForTag(tag);
            for (int i = 0; i < graph.EdgeCount; i++)
            {
                if (graph.TagSets[graph.EdgeTagSetId[i]].Intersects(in bits))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AnyTagSetHasTag(NodeGraph graph, in TagBits256 bits)
        {
            for (int i = 0; i < graph.TagSets.Length; i++)
            {
                if (graph.TagSets[i].Intersects(in bits))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AnyEdgeCapacity(NodeGraph graph, int depthCm, int widthCm)
        {
            for (int i = 0; i < graph.EdgeCount; i++)
            {
                if (graph.EdgeDepthCm[i] == depthCm && graph.EdgeWidthCm[i] == widthCm)
                {
                    return true;
                }
            }

            return false;
        }

        private static TagBits256 BitsForTag(string tag)
        {
            int tagId = TagRegistry.GetId(tag);
            if (tagId == TagRegistry.InvalidId)
            {
                tagId = TagRegistry.Register(tag);
            }

            return GraphTagSetRegistry.TagBitsFromIds(stackalloc[] { tagId });
        }

        private static int FindNode(NodeGraph graph, int xcm, int ycm)
        {
            for (int i = 0; i < graph.NodeCount; i++)
            {
                if (graph.PosXcm[i] == xcm && graph.PosYcm[i] == ycm)
                {
                    return i;
                }
            }

            throw new InvalidOperationException($"Node ({xcm},{ycm}) was not found.");
        }

        private static int FindEdge(NodeGraph graph, int fromNodeId, int toNodeId)
        {
            NodeGraph.EdgeRange range = graph.GetOutgoingEdges(fromNodeId);
            for (int e = range.Start; e < range.EndExclusive; e++)
            {
                if (graph.EdgeTo[e] == toNodeId)
                {
                    return e;
                }
            }

            throw new InvalidOperationException($"Edge {fromNodeId}->{toNodeId} was not found.");
        }

        private static string FindRepoRoot()
        {
            string? current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(Path.Combine(current, "src", "Core", "Ludots.Core.csproj")))
                {
                    return current;
                }

                current = Directory.GetParent(current)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate repo root.");
        }
    }
}
