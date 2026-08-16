using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class LoadedGraphRuntimeTests
    {
        [Test]
        public void LoadedGraphRuntime_RebuildsLoadedView_AndProjectsNearestNode()
        {
            var loadedChunks = new WorldGridLoadedChunks(chunkSizeCm: 1000, loadedChunkCapacity: 2);
            var store = new ChunkedNodeGraphStore();
            store.SubscribeToLoadedChunks(loadedChunks);

            long aKey = GraphChunkKey.Pack(0, 0);
            long bKey = GraphChunkKey.Pack(1, 0);
            store.AddOrReplace(aKey, BuildChunk(
                new[] { (0, 0), (500, 0) },
                new[] { (0, 1, 1f) },
                new[] { new GraphCrossEdge(fromLocalNodeId: 1, toChunkKey: bKey, toLocalNodeId: 0, baseCost: 1f, tagSetId: 0) }));
            store.AddOrReplace(bKey, BuildChunk(
                new[] { (1500, 0) },
                Array.Empty<(int from, int to, float cost)>(),
                Array.Empty<GraphCrossEdge>()));

            using var runtime = new LoadedGraphRuntime(store, loadedChunks, preferredProjectionCellSizeCm: 500);

            Assert.That(runtime.CurrentGraph.NodeCount, Is.EqualTo(0));

            loadedChunks.SetLoaded(aKey, loaded: true);
            Assert.That(runtime.CurrentGraph.NodeCount, Is.EqualTo(2));
            Assert.That(runtime.TryFindNearestNode(new Ludots.Platform.Abstractions.WorldCmInt2(30, 20), 400, out int nodeIdA, out _), Is.True);
            Assert.That(nodeIdA, Is.EqualTo(0));

            loadedChunks.SetLoaded(bKey, loaded: true);
            Assert.That(runtime.CurrentGraph.NodeCount, Is.EqualTo(3));

            loadedChunks.SetLoaded(aKey, loaded: false);
            Assert.That(store.TryGetChunk(aKey, out _), Is.False, "Chunk store should follow unload lifecycle.");
            Assert.That(runtime.CurrentGraph.NodeCount, Is.EqualTo(1));
            Assert.That(runtime.TryFindNearestNode(new Ludots.Platform.Abstractions.WorldCmInt2(1490, 0), 200, out int nodeIdB, out _), Is.True);
            Assert.That(nodeIdB, Is.EqualTo(0));
        }

        [Test]
        public void AutoPathService_RuntimeBackedGraph_RefreshesProjectionAndPathingFromLoadedChunks()
        {
            var loadedChunks = new WorldGridLoadedChunks(chunkSizeCm: 1000, loadedChunkCapacity: 2);
            var store = new ChunkedNodeGraphStore();
            store.SubscribeToLoadedChunks(loadedChunks);

            long aKey = GraphChunkKey.Pack(0, 0);
            long bKey = GraphChunkKey.Pack(1, 0);
            store.AddOrReplace(aKey, BuildChunk(
                new[] { (0, 0), (500, 0) },
                new[] { (0, 1, 1f) },
                new[] { new GraphCrossEdge(fromLocalNodeId: 1, toChunkKey: bKey, toLocalNodeId: 0, baseCost: 1f, tagSetId: 0) }));
            store.AddOrReplace(bKey, BuildChunk(
                new[] { (1500, 0) },
                Array.Empty<(int from, int to, float cost)>(),
                Array.Empty<GraphCrossEdge>()));

            loadedChunks.SetLoaded(aKey, loaded: true);
            loadedChunks.SetLoaded(bKey, loaded: true);

            using var runtime = new LoadedGraphRuntime(store, loadedChunks, preferredProjectionCellSizeCm: 500);
            var agentProfiles = CreateAgentProfiles("graph_only");
            var navProfiles = new NavMeshProfileRegistry(new NavMeshBakeConfig
            {
                Profiles = new List<NavMeshAgentProfileConfig>
                {
                    new NavMeshAgentProfileConfig { Id = "graph_only", MaxClimbCm = 40, MaxSlopeDeg = 45 }
                }
            }, agentProfiles);
            var navRegistry = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>());
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: 16);
            var pathingConfig = new PathingConfig
            {
                AgentTypes = new List<PathingAgentTypeConfig>
                {
                    new PathingAgentTypeConfig
                    {
                        Id = "graph_only",
                        ProfileId = "graph_only",
                        Selection = new PathingSelectionConfig { Mode = PathSelectionMode.PreferGraph },
                        NodeGraph = new PathingNodeGraphConfig
                        {
                            ProjectionMaxRadiusCm = 300
                        }
                    }
                }
            };

            var service = new AutoPathService(runtime, navRegistry, navProfiles, agentProfiles, pathStore, pathingConfig);
            var request = new PathRequest(
                requestId: 1,
                actor: default,
                domain: PathDomain.Auto,
                start: PathEndpoint.FromWorldCm(5, 0),
                goal: PathEndpoint.FromWorldCm(1495, 0),
                budget: new PathBudget(maxExpanded: 0, maxPoints: 8));

            Assert.That(service.TrySolve(in request, out var solved), Is.True);
            Assert.That(solved.Status, Is.EqualTo(PathStatus.Found));

            var xs = new int[8];
            var ys = new int[8];
            Assert.That(service.TryCopyPath(in solved.Handle, xs, ys, out int count), Is.True);
            Assert.That(count, Is.EqualTo(3));
            Assert.That(xs[0], Is.EqualTo(0));
            Assert.That(xs[1], Is.EqualTo(500));
            Assert.That(xs[2], Is.EqualTo(1500));

            loadedChunks.SetLoaded(bKey, loaded: false);
            Assert.That(service.TrySolve(in request, out var afterUnload), Is.True);
            Assert.That(afterUnload.Status, Is.EqualTo(PathStatus.NotReady));
        }

        private static AgentProfileRegistry CreateAgentProfiles(string id)
        {
            return new AgentProfileRegistry(new[]
            {
                new AgentProfileConfig
                {
                    Id = id,
                    RadiusCm = 30,
                    HeightCm = 180,
                    ClearanceCm = 40,
                    Mass = 1,
                    Layer = 0
                }
            });
        }

        private static GraphChunkData BuildChunk(
            IReadOnlyList<(int x, int y)> nodes,
            IReadOnlyList<(int from, int to, float cost)> edges,
            GraphCrossEdge[] crossEdges)
        {
            var builder = new NodeGraphBuilder(nodes.Count, edges.Count);
            for (int i = 0; i < nodes.Count; i++)
            {
                builder.AddNode(nodes[i].x, nodes[i].y);
            }

            for (int i = 0; i < edges.Count; i++)
            {
                builder.AddEdge(edges[i].from, edges[i].to, edges[i].cost);
            }

            return new GraphChunkData(builder.Build(), crossEdges);
        }
    }
}
