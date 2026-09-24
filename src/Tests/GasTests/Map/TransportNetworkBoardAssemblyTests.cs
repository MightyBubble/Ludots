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
        private const string CatalogAssetPath = "TransportNetwork/transport_network.json";

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
                        TransportNetwork = new TransportNetworkBoardDeclaration { AssetPath = CatalogAssetPath }
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                MapManager.ValidateSpatialDeclaration(mapConfig, new MapId(mapConfig.Id)));
            Assert.That(ex!.Message, Does.Contain("require a NodeGraph board"));
        }

        [Test]
        public void ValidateSpatialDeclaration_RejectsMissingAssetPath()
        {
            var mapConfig = new MapConfig
            {
                Id = "assembly.reject.empty",
                Boards =
                {
                    NodeGraphBoard("default", assetPath: string.Empty)
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                MapManager.ValidateSpatialDeclaration(mapConfig, new MapId(mapConfig.Id)));
            Assert.That(ex!.Message, Does.Contain("AssetPath is required"));
        }

        [Test]
        public void ValidateSpatialDeclaration_RejectsRootedAssetPath()
        {
            var mapConfig = new MapConfig
            {
                Id = "assembly.reject.rooted",
                Boards =
                {
                    NodeGraphBoard("default", "/TransportNetwork/absolute.json")
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                MapManager.ValidateSpatialDeclaration(mapConfig, new MapId(mapConfig.Id)));
            Assert.That(ex!.Message, Does.Contain("catalog-relative"));
        }

        [Test]
        public void ValidateSpatialDeclaration_AcceptsNodeGraphDeclaration()
        {
            var mapConfig = new MapConfig
            {
                Id = "assembly.accept",
                Boards = { NodeGraphBoard("default", CatalogAssetPath) }
            };

            Assert.DoesNotThrow(() => MapManager.ValidateSpatialDeclaration(mapConfig, new MapId(mapConfig.Id)));
        }

        [Test]
        public void BoardConfigClone_CopiesTransportNetworkDeclaration()
        {
            BoardConfig board = NodeGraphBoard("default", "TransportNetwork/alt.json");
            board.TerrainProjectAsRamp = true;

            BoardConfig clone = board.Clone();

            Assert.That(clone.TransportNetwork, Is.Not.SameAs(board.TransportNetwork));
            Assert.That(clone.TransportNetwork!.AssetPath, Is.EqualTo("TransportNetwork/alt.json"));
            Assert.That(clone.TerrainProjectAsRamp, Is.True);
        }

        [Test]
        public void Install_RollsBackAlreadyInstalledBoards_WhenALaterBoardFails()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "ludots-transport-rollback-" + Guid.NewGuid().ToString("N"));
            WriteCatalogAsset(tempRoot, "rollback.network");

            try
            {
                (ConfigPipeline pipeline, ConfigCatalog catalog) = LoadCatalog(tempRoot);
                var payloads = new SurfaceSourcePayloadRegistry();
                BoardConfig goodBoard = NodeGraphBoard("good", CatalogAssetPath);
                BoardConfig missingBoard = NodeGraphBoard("missing", CatalogAssetPath);
                var mapConfig = new MapConfig { Id = "assembly.rollback", Boards = { goodBoard, missingBoard } };
                var session = new MapSession(new MapId(mapConfig.Id), mapConfig);
                INodeGraphBoard board = (INodeGraphBoard)BoardFactory.Create(goodBoard, new BoardIdRegistry());
                session.AddBoard(board);
                var loadedChunks = (WorldGridLoadedChunks)board.LoadedChunks;
                TransportNetworkBakedAsset expectedBake = BakeCatalog(pipeline, catalog, loadedChunks.ChunkSizeCm);

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

            string showcaseMapPath = Path.Combine(
                FindRepoRoot(),
                "mods", "showcases", "capability_standard", "CapabilityStandardTransportNetworkMod",
                "assets", "Maps", "capability_standard_transport_network.json");
            var showcaseConfig = System.Text.Json.JsonSerializer.Deserialize<MapConfig>(File.ReadAllText(showcaseMapPath), options);

            Assert.That(showcaseConfig!.Boards[0].TransportNetwork, Is.Not.Null);
            Assert.That(showcaseConfig.Boards[0].TransportNetwork!.AssetPath, Is.EqualTo(CatalogAssetPath));
        }

        [Test]
        public void TransportNetworkMapRuntime_InstallsGraphChunksAndRibbonPayloads_AndTeardownClearsThem()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "ludots-transport-assembly-" + Guid.NewGuid().ToString("N"));
            WriteCatalogAsset(tempRoot, "assembly.network");

            try
            {
                (ConfigPipeline pipeline, ConfigCatalog catalog) = LoadCatalog(tempRoot);
                var payloads = new SurfaceSourcePayloadRegistry();
                BoardConfig boardConfig = NodeGraphBoard("default", CatalogAssetPath);
                var mapConfig = new MapConfig { Id = "assembly.install", Boards = { boardConfig } };
                var session = new MapSession(new MapId(mapConfig.Id), mapConfig);
                INodeGraphBoard board = (INodeGraphBoard)BoardFactory.Create(boardConfig, new BoardIdRegistry());
                session.AddBoard(board);
                var loadedChunks = (WorldGridLoadedChunks)board.LoadedChunks;
                TransportNetworkBakedAsset expectedBake = BakeCatalog(pipeline, catalog, loadedChunks.ChunkSizeCm);
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

        private static BoardConfig NodeGraphBoard(string name, string assetPath)
        {
            return new BoardConfig
            {
                Name = name,
                SpatialType = "NodeGraph",
                WidthCm = 51200,
                HeightCm = 51200,
                LoadedChunkCapacity = 64,
                TransportNetwork = new TransportNetworkBoardDeclaration { AssetPath = assetPath }
            };
        }

        private static void WriteCatalogAsset(string tempRoot, string assetId)
        {
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
                  "id": "ASSET_ID",
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
                """.Replace("ASSET_ID", assetId, StringComparison.Ordinal));
        }

        private static (ConfigPipeline Pipeline, ConfigCatalog Catalog) LoadCatalog(string tempRoot)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", tempRoot);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            return (pipeline, ConfigCatalogLoader.Load(pipeline));
        }

        private static TransportNetworkBakedAsset BakeCatalog(ConfigPipeline pipeline, ConfigCatalog catalog, int chunkSizeCm)
        {
            TransportNetworkAsset asset = new TransportNetworkAssetLoader(pipeline).Load(catalog);
            return new TransportNetworkBaker().Bake(asset, chunkSizeCm);
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
