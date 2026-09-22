using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Surfaces;
using Ludots.Core.TransportNetwork;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class TransportNetworkBoardAssemblyTests
    {
        [Test]
        public void ValidateSpatialDeclaration_RejectsTransportNetworkOnNonNodeGraphBoard()
        {
            var mapConfig = new MapConfig
            {
                Id = "assembly.reject.grid",
                Boards =
                {
                    new BoardConfig
                    {
                        Name = "default",
                        SpatialType = "Grid",
                        TransportNetwork = new TransportNetworkBoardDeclaration()
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                MapManager.ValidateSpatialDeclaration(mapConfig, new MapId(mapConfig.Id)));
            Assert.That(ex!.Message, Does.Contain("require a NodeGraph board"));
        }

        [Test]
        public void ValidateSpatialDeclaration_RejectsRootedAssetPath()
        {
            var mapConfig = new MapConfig
            {
                Id = "assembly.reject.rooted",
                Boards =
                {
                    new BoardConfig
                    {
                        Name = "default",
                        SpatialType = "NodeGraph",
                        TransportNetwork = new TransportNetworkBoardDeclaration { AssetPath = "/TransportNetwork/absolute.json" }
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                MapManager.ValidateSpatialDeclaration(mapConfig, new MapId(mapConfig.Id)));
            Assert.That(ex!.Message, Does.Contain("config-catalog relative"));
        }

        [Test]
        public void ValidateSpatialDeclaration_AcceptsNodeGraphDeclaration()
        {
            var mapConfig = new MapConfig
            {
                Id = "assembly.accept",
                Boards =
                {
                    new BoardConfig
                    {
                        Name = "default",
                        SpatialType = "NodeGraph",
                        TransportNetwork = new TransportNetworkBoardDeclaration()
                    }
                }
            };

            Assert.DoesNotThrow(() => MapManager.ValidateSpatialDeclaration(mapConfig, new MapId(mapConfig.Id)));
        }

        [Test]
        public void BoardConfigClone_PreservesTransportNetworkDeclaration()
        {
            var board = new BoardConfig
            {
                Name = "default",
                SpatialType = "NodeGraph",
                TransportNetwork = new TransportNetworkBoardDeclaration { AssetPath = "TransportNetwork/alt.json" }
            };

            var clone = board.Clone();

            Assert.That(clone.TransportNetwork, Is.SameAs(board.TransportNetwork));
        }

        [Test]
        public void Install_RollsBackAlreadyInstalledBoards_WhenALaterBoardFails()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "ludots-transport-rollback-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "TransportNetwork"));
            File.WriteAllText(Path.Combine(tempRoot, "config_catalog.json"),
                """
                [
                  { "Path": "TransportNetwork/transport_network.json", "Policy": "Replace" }
                ]
                """);
            File.WriteAllText(Path.Combine(tempRoot, "TransportNetwork", "transport_network.json"),
                """
                {
                  "id": "rollback.network",
                  "sampleStepCm": 500,
                  "defaultVisualWidthMeters": 2.0,
                  "nodes": [
                    { "id": "west", "xcm": -2400, "ycm": 0, "kind": "Normal", "tags": [] },
                    { "id": "east", "xcm": 2400, "ycm": 0, "kind": "Normal", "tags": [] }
                  ],
                  "segments": [
                    {
                      "id": "link",
                      "points": [ { "nodeId": "west" }, { "nodeId": "east" } ],
                      "sampleStepCm": 0,
                      "direction": "Bidirectional",
                      "flowDirection": "None",
                      "areaId": "Transport.Area.Land",
                      "tags": [],
                      "depthCm": 0,
                      "widthCm": 0,
                      "laneCount": 0,
                      "visualWidthMeters": 1.5
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
                var payloads = new SurfaceSourcePayloadRegistry();

                var goodBoard = new BoardConfig
                {
                    Name = "good",
                    SpatialType = "NodeGraph",
                    WidthCells = 512,
                    HeightCells = 512,
                    GridCellSizeCm = 100,
                    LoadedChunkCapacity = 64,
                    TransportNetwork = new TransportNetworkBoardDeclaration()
                };
                var missingBoard = new BoardConfig
                {
                    Name = "missing",
                    SpatialType = "NodeGraph",
                    TransportNetwork = new TransportNetworkBoardDeclaration()
                };
                var mapConfig = new MapConfig { Id = "assembly.rollback", Boards = { goodBoard, missingBoard } };

                var session = new MapSession(new MapId(mapConfig.Id), mapConfig);
                INodeGraphBoard board = (INodeGraphBoard)BoardFactory.Create(goodBoard, new BoardIdRegistry());
                session.AddBoard(board);
                var loadedChunks = (WorldGridLoadedChunks)board.LoadedChunks;

                TransportNetworkAsset expectedAsset = new TransportNetworkAssetLoader(pipeline).Load(catalog);
                TransportNetworkBakedAsset expectedBake = new TransportNetworkBaker().Bake(expectedAsset, loadedChunks.ChunkSizeCm);

                var runtime = new TransportNetworkMapRuntime(payloads);
                var ex = Assert.Throws<InvalidOperationException>(() =>
                    runtime.Install(session, mapConfig, pipeline, catalog, new ConfigConflictReport()));
                Assert.That(ex!.Message, Does.Contain("board 'missing'"));

                foreach (long chunkKey in expectedBake.RibbonChunks.Keys)
                {
                    Assert.That(payloads.TryGet(ComposeExpectedScopeId(chunkKey), out _), Is.False,
                        $"rollback must clear the ribbon payload installed for board 'good' (chunk {chunkKey})");
                }
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Test]
        public void MapJson_BindsTransportNetworkDeclaration_WithMapManagerOptions()
        {
            // Same options MapManager.LoadMapInternal uses for map fragments.
            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            string camelCaseLiteral = """
                {
                  "Id": "assembly.json.camel",
                  "Boards": [
                    { "name": "default", "spatialType": "NodeGraph", "transportNetwork": { "assetPath": "TransportNetwork/alt.json" } }
                  ]
                }
                """;

            var literalConfig = System.Text.Json.JsonSerializer.Deserialize<MapConfig>(camelCaseLiteral, options);

            Assert.That(literalConfig!.Boards[0].TransportNetwork, Is.Not.Null);
            Assert.That(literalConfig.Boards[0].TransportNetwork!.AssetPath, Is.EqualTo("TransportNetwork/alt.json"));

            string repoRoot = FindRepoRoot();
            string showcaseMapPath = Path.Combine(
                repoRoot,
                "mods", "showcases", "capability_standard", "CapabilityStandardTransportNetworkMod",
                "assets", "Maps", "capability_standard_transport_network.json");

            var showcaseConfig = System.Text.Json.JsonSerializer.Deserialize<MapConfig>(File.ReadAllText(showcaseMapPath), options);

            Assert.That(showcaseConfig!.Boards[0].TransportNetwork, Is.Not.Null,
                "the shipped asset-only showcase map must carry the board declaration");
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "showcase.registry.json")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not locate repository root.");
        }

        [Test]
        public void TransportNetworkMapRuntime_InstallsGraphChunksAndRibbonPayloads_AndTeardownClearsThem()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "ludots-transport-assembly-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "TransportNetwork"));
            File.WriteAllText(Path.Combine(tempRoot, "config_catalog.json"),
                """
                [
                  { "Path": "TransportNetwork/transport_network.json", "Policy": "Replace" }
                ]
                """);
            File.WriteAllText(Path.Combine(tempRoot, "TransportNetwork", "transport_network.json"),
                """
                {
                  "id": "assembly.network",
                  "sampleStepCm": 500,
                  "defaultVisualWidthMeters": 2.0,
                  "nodes": [
                    { "id": "west", "xcm": -2400, "ycm": 0, "kind": "Normal", "tags": [] },
                    { "id": "east", "xcm": 2400, "ycm": 0, "kind": "Normal", "tags": [] }
                  ],
                  "segments": [
                    {
                      "id": "link",
                      "points": [ { "nodeId": "west" }, { "nodeId": "east" } ],
                      "sampleStepCm": 0,
                      "direction": "Bidirectional",
                      "flowDirection": "None",
                      "areaId": "Transport.Area.Land",
                      "tags": [],
                      "depthCm": 0,
                      "widthCm": 0,
                      "laneCount": 0,
                      "visualWidthMeters": 1.5
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
                var payloads = new SurfaceSourcePayloadRegistry();

                var boardConfig = new BoardConfig
                {
                    Name = "default",
                    SpatialType = "NodeGraph",
                    WidthCells = 512,
                    HeightCells = 512,
                    GridCellSizeCm = 100,
                    LoadedChunkCapacity = 64,
                    TransportNetwork = new TransportNetworkBoardDeclaration()
                };
                var mapConfig = new MapConfig { Id = "assembly.install", Boards = { boardConfig } };

                var session = new MapSession(new MapId(mapConfig.Id), mapConfig);
                INodeGraphBoard board = (INodeGraphBoard)BoardFactory.Create(boardConfig, new BoardIdRegistry());
                session.AddBoard(board);
                var loadedChunks = (WorldGridLoadedChunks)board.LoadedChunks;

                // Independent bake to derive the expected chunk sets (graph chunks and
                // ribbon chunks are keyed independently: an edge crossing a chunk boundary
                // can yield a graph chunk without a ribbon chunk and vice versa).
                TransportNetworkAsset expectedAsset = new TransportNetworkAssetLoader(pipeline).Load(catalog);
                TransportNetworkBakedAsset expectedBake = new TransportNetworkBaker().Bake(expectedAsset, loadedChunks.ChunkSizeCm);
                Assert.That(expectedBake.GraphChunks.Count, Is.GreaterThan(0));
                Assert.That(expectedBake.RibbonChunks.Count, Is.GreaterThan(0));

                var runtime = new TransportNetworkMapRuntime(payloads);
                runtime.Install(session, mapConfig, pipeline, catalog, new ConfigConflictReport());

                foreach (long chunkKey in expectedBake.GraphChunks.Keys)
                {
                    Assert.That(board.GraphStore.TryGetChunk(chunkKey, out _), Is.True,
                        $"graph chunk {chunkKey} must be present in the board graph store");
                    Assert.That(loadedChunks.ActiveChunkKeys, Does.Contain(chunkKey),
                        $"graph chunk {chunkKey} must be marked loaded");
                }

                foreach (long chunkKey in expectedBake.RibbonChunks.Keys)
                {
                    int scopeId = ComposeExpectedScopeId(chunkKey);
                    Assert.That(payloads.TryGet(scopeId, out SurfacePayloadSnapshot snapshot), Is.True,
                        $"ribbon payload must be registered for chunk {chunkKey} under scope {scopeId}");
                    Assert.That(snapshot.Kind, Is.EqualTo(PresenterSurfaceKind.SplineRibbon));
                }

                runtime.Dispose();

                foreach (long chunkKey in expectedBake.RibbonChunks.Keys)
                {
                    Assert.That(payloads.TryGet(ComposeExpectedScopeId(chunkKey), out _), Is.False,
                        $"ribbon payload for chunk {chunkKey} must be removed on teardown");
                }
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        // Mirrors the Core scope-id contract (700M band) so presenter ScopeTags stay matchable.
        private static int ComposeExpectedScopeId(long chunkKey)
        {
            unchecked
            {
                int mixed = (int)(chunkKey ^ (chunkKey >> 32));
                return 700000000 + Math.Abs(mixed % 100000000);
            }
        }
    }
}
