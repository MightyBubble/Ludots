using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// 多板导航启动验收（issue #1346）：一张地图声明两块可导航板时，运行时必须为每块板
    /// 建立独立的 store 与瓦片几何，两块板共用局部瓦片坐标不互相覆盖，缺声明的板明确失败。
    /// 这里走真实 GameEngine 导航引导路径，不只测 registry 单元。
    /// </summary>
    [TestFixture]
    public sealed class NavMultiBoardBootstrapContractTests
    {
        private const int CellSizeCm = 250;
        private const int ChunkSizeCells = 64;

        [Test]
        public void LoadNavForMap_TwoNavigableBoards_RegistersIndependentStores()
        {
            string repoRoot = FindRepoRoot();
            string tempRoot = CreateTempAssetsRoot(repoRoot);
            try
            {
                using GameEngine engine = CreateEngine(repoRoot, tempRoot);
                var terrain = new FlatGridLogicTerrainField(
                    ChunkSizeCells * 2,
                    ChunkSizeCells * 2,
                    chunkSizeCells: ChunkSizeCells);

                const string mapId = "multiboard_contract";
                var mapConfig = new MapConfig
                {
                    Id = mapId,
                    Tags = new List<string> { MapTags.FeatureNavMeshOn.Name },
                    Boards = new List<BoardConfig>
                    {
                        CreateBoard("mainland", originXcm: 0, originZcm: 0),
                        CreateBoard("harbor", originXcm: -3_200_000, originZcm: -1_800_000)
                    }
                };

                WriteBoardTileFiles(tempRoot, mapId, "mainland", 0, 0);
                WriteBoardTileFiles(tempRoot, mapId, "harbor", -3_200_000, -1_800_000);

                SetLogicTerrain(engine, terrain);
                engine.LoadNavForMapForTests(mapId, mapConfig);

                Assert.That(engine.TryGetService(CoreServiceKeys.NavQueryServices, out NavQueryServiceRegistry registry), Is.True);

                Assert.That(registry.TryGetStore("mainland", 0, 0, out NavTileStore mainlandStore), Is.True,
                    "mainland must have its own store");
                Assert.That(registry.TryGetStore("harbor", 0, 0, out NavTileStore harborStore), Is.True,
                    "harbor must have its own store");
                Assert.That(mainlandStore, Is.Not.SameAs(harborStore));
                Assert.That(registry.TryGetBoardGeometry("mainland", out NavBoardTileGeometry mainlandGeometry), Is.True);
                Assert.That(registry.TryGetBoardGeometry("harbor", out NavBoardTileGeometry harborGeometry), Is.True);

                Assert.That(mainlandGeometry.OriginXcm, Is.EqualTo(0));
                Assert.That(harborGeometry.OriginXcm, Is.EqualTo(-3_200_000));
                Assert.That(mainlandGeometry, Is.Not.EqualTo(harborGeometry),
                    "boards must not share one aggregate tile geometry");
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Test]
        public void LoadNavForMap_TwoBoards_ResolveSameLocalTileIndependently()
        {
            string repoRoot = FindRepoRoot();
            string tempRoot = CreateTempAssetsRoot(repoRoot);
            try
            {
                using GameEngine engine = CreateEngine(repoRoot, tempRoot);
                var terrain = new FlatGridLogicTerrainField(
                    ChunkSizeCells * 2,
                    ChunkSizeCells * 2,
                    chunkSizeCells: ChunkSizeCells);

                const string mapId = "multiboard_query_contract";
                const int harborOriginXcm = -3_200_000;
                const int harborOriginZcm = -1_800_000;

                var mapConfig = new MapConfig
                {
                    Id = mapId,
                    Tags = new List<string> { MapTags.FeatureNavMeshOn.Name },
                    Boards = new List<BoardConfig>
                    {
                        CreateBoard("mainland", 0, 0),
                        CreateBoard("harbor", harborOriginXcm, harborOriginZcm)
                    }
                };

                WriteBoardTileFiles(tempRoot, mapId, "mainland", 0, 0);
                WriteBoardTileFiles(tempRoot, mapId, "harbor", harborOriginXcm, harborOriginZcm);

                SetLogicTerrain(engine, terrain);
                engine.LoadNavForMapForTests(mapId, mapConfig);

                Assert.That(engine.TryGetService(CoreServiceKeys.NavQueryServices, out NavQueryServiceRegistry registry), Is.True);

                Assert.That(registry.TryCreateQuery("mainland", 0, 0, null!, out NavQueryService mainland), Is.True);
                Assert.That(registry.TryCreateQuery("harbor", 0, 0, null!, out NavQueryService harbor), Is.True);

                Assert.That(mainland.TryProject(1000, 1000, out NavLocation mainlandLoc), Is.True);
                Assert.That(mainlandLoc.TileId, Is.EqualTo(new NavTileId(0, 0, 0)));

                int harborWorldXcm = harborOriginXcm + 1000;
                int harborWorldZcm = harborOriginZcm + 1000;
                Assert.That(harbor.TryProject(harborWorldXcm, harborWorldZcm, out NavLocation harborLoc), Is.True);
                Assert.That(harborLoc.TileId, Is.EqualTo(new NavTileId(0, 0, 0)),
                    "harbor resolves the same local tile coordinate at a different world position");

                Assert.That(mainland.TryProject(harborWorldXcm, harborWorldZcm, out _), Is.False,
                    "mainland must not answer a query that belongs to the harbor board address space");
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Test]
        public void LoadNavForMap_NavigableBoardWithoutNavTileGrid_FailsClosed()
        {
            string repoRoot = FindRepoRoot();
            string tempRoot = CreateTempAssetsRoot(repoRoot);
            try
            {
                using GameEngine engine = CreateEngine(repoRoot, tempRoot);
                var terrain = new FlatGridLogicTerrainField(
                    ChunkSizeCells,
                    ChunkSizeCells,
                    chunkSizeCells: ChunkSizeCells);

                var mapConfig = new MapConfig
                {
                    Id = "missing_grid_contract",
                    Tags = new List<string> { MapTags.FeatureNavMeshOn.Name },
                    Boards = new List<BoardConfig>
                    {
                        new BoardConfig { Name = "default", NavigationEnabled = true }
                    }
                };

                SetLogicTerrain(engine, terrain);
                Assert.That(
                    () => engine.LoadNavForMapForTests("missing_grid_contract", mapConfig),
                    Throws.InvalidOperationException.With.Message.Contains("NavTileGrid"));
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        private static BoardConfig CreateBoard(string name, int originXcm, int originZcm)
            => new BoardConfig
            {
                Name = name,
                NavigationEnabled = true,
                NavTileGrid = new NavTileGridConfig
                {
                    WidthChunks = 2,
                    HeightChunks = 2,
                    ChunkSizeCells = ChunkSizeCells,
                    CellSizeCm = CellSizeCm,
                    OriginXcm = originXcm,
                    OriginZcm = originZcm
                }
            };

        private static readonly string[] Profiles = { "Small", "Medium", "Large" };

        private static void WriteBoardTileFiles(
            string assetsRoot,
            string mapId,
            string? boardId,
            int originXcm,
            int originZcm)
        {
            var entries = new List<NavTileManifestEntry>();
            foreach (string profileId in Profiles)
            {
                for (int chunkY = 0; chunkY < 2; chunkY++)
                {
                    for (int chunkX = 0; chunkX < 2; chunkX++)
                    {
                        string relative = NavAssetPaths.GetNavTileRelativePath(mapId, boardId, 0, profileId, chunkX, chunkY);
                        string path = Path.Combine(assetsRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                        NavTile flat = DefaultGridNavTileFactory.CreateFlatTile(
                            chunkX,
                            chunkY,
                            layer: 0,
                            tileVersion: 1,
                            chunkSizeCells: ChunkSizeCells,
                            cellSizeCm: CellSizeCm);

                        using var ms = new MemoryStream();
                        NavTileBinary.Write(ms, flat);
                        File.WriteAllBytes(path, ms.ToArray());

                        entries.Add(new NavTileManifestEntry
                        {
                            Layer = 0,
                            ProfileId = profileId,
                            ChunkX = chunkX,
                            ChunkY = chunkY,
                            TileVersion = flat.TileVersion,
                            TileChecksum = "fnv1a64:" + NavTileBinary.ComputePersistedChecksum(flat).ToString("x16")
                        });
                    }
                }
            }

            if (!string.IsNullOrEmpty(boardId))
            {
                // A board-scoped map must carry its own manifest next to its tiles; the engine
                // resolves the manifest per board and refuses to serve one board without it.
                var manifest = new NavTileManifest
                {
                    MapId = mapId,
                    BoardId = boardId,
                    SourceRevision = "test-fixture:" + mapId + ":" + boardId,
                    Algorithm = "Recast",
                    Mode = "Offline",
                    TileVersion = 1,
                    Tiles = entries.ToArray()
                };

                string manifestRel = NavAssetPaths.GetNavTileManifestRelativePath(mapId, boardId);
                NavTileManifestSerializer.Write(
                    Path.Combine(assetsRoot, manifestRel.Replace('/', Path.DirectorySeparatorChar)),
                    manifest);
            }
        }

        private static GameEngine CreateEngine(string repoRoot, string tempAssetsRoot)
        {
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") },
                tempAssetsRoot);

            var vfs = (VirtualFileSystem)engine.VFS;
            vfs.Unmount("Core");
            vfs.Mount("Core", tempAssetsRoot);
            return engine;
        }

        private static string CreateTempAssetsRoot(string repoRoot)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "ludots-nav-multiboard-" + Guid.NewGuid().ToString("N"));
            CopyDirectory(Path.Combine(repoRoot, "assets"), tempRoot);
            return tempRoot;
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dir.Replace(source, target));
            }

            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                File.Copy(file, file.Replace(source, target), overwrite: true);
            }
        }

        [Test]
        public void LoadNavForMap_ValidManifest_IsPublishedForColdStart()
        {
            string repoRoot = FindRepoRoot();
            string tempRoot = CreateTempAssetsRoot(repoRoot);
            try
            {
                using GameEngine engine = CreateEngine(repoRoot, tempRoot);
                var terrain = new FlatGridLogicTerrainField(ChunkSizeCells * 2, ChunkSizeCells * 2, chunkSizeCells: ChunkSizeCells);
                const string mapId = "manifest_cold_start";

                var mapConfig = new MapConfig
                {
                    Id = mapId,
                    Tags = new List<string> { MapTags.FeatureNavMeshOn.Name },
                    Boards = new List<BoardConfig> { CreateBoard("default", 0, 0) }
                };

                WriteBoardTileFiles(tempRoot, mapId, null, 0, 0);
                WriteManifest(tempRoot, mapId, boardId: string.Empty);

                SetLogicTerrain(engine, terrain);
                engine.LoadNavForMapForTests(mapId, mapConfig);

                Assert.That(engine.TryGetService(CoreServiceKeys.NavTileManifest, out NavTileManifest manifest), Is.True);
                Assert.That(manifest!.MapId, Is.EqualTo(mapId));
                Assert.That(manifest.BuildHash, Does.StartWith("fnv1a64:"));
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Test]
        public void LoadNavForMap_ManifestForAnotherMap_FailsClosed()
        {
            string repoRoot = FindRepoRoot();
            string tempRoot = CreateTempAssetsRoot(repoRoot);
            try
            {
                using GameEngine engine = CreateEngine(repoRoot, tempRoot);
                var terrain = new FlatGridLogicTerrainField(ChunkSizeCells * 2, ChunkSizeCells * 2, chunkSizeCells: ChunkSizeCells);
                const string mapId = "manifest_foreign";

                var mapConfig = new MapConfig
                {
                    Id = mapId,
                    Tags = new List<string> { MapTags.FeatureNavMeshOn.Name },
                    Boards = new List<BoardConfig> { CreateBoard("default", 0, 0) }
                };

                WriteBoardTileFiles(tempRoot, mapId, null, 0, 0);
                // Same path, but the manifest claims a different map: geometry from one map
                // must never be served for another.
                WriteManifest(tempRoot, mapId, boardId: string.Empty, declaredMapId: "some_other_map");

                SetLogicTerrain(engine, terrain);
                Assert.That(
                    () => engine.LoadNavForMapForTests(mapId, mapConfig),
                    Throws.InvalidOperationException.With.Message.Contains("some_other_map"));
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Test]
        public void LoadNavForMap_CorruptManifest_FailsClosedWithRebakeAction()
        {
            string repoRoot = FindRepoRoot();
            string tempRoot = CreateTempAssetsRoot(repoRoot);
            try
            {
                using GameEngine engine = CreateEngine(repoRoot, tempRoot);
                var terrain = new FlatGridLogicTerrainField(ChunkSizeCells * 2, ChunkSizeCells * 2, chunkSizeCells: ChunkSizeCells);
                const string mapId = "manifest_corrupt";

                var mapConfig = new MapConfig
                {
                    Id = mapId,
                    Tags = new List<string> { MapTags.FeatureNavMeshOn.Name },
                    Boards = new List<BoardConfig> { CreateBoard("default", 0, 0) }
                };

                WriteBoardTileFiles(tempRoot, mapId, null, 0, 0);
                string rel = NavAssetPaths.GetNavTileManifestRelativePath(mapId);
                string path = Path.Combine(tempRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "{ not json");

                SetLogicTerrain(engine, terrain);
                Assert.That(
                    () => engine.LoadNavForMapForTests(mapId, mapConfig),
                    Throws.InvalidOperationException.With.Message.Contains("Re-bake"));
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Test]
        public void LoadNavForMap_WithoutManifest_FailsClosed()
        {
            string repoRoot = FindRepoRoot();
            string tempRoot = CreateTempAssetsRoot(repoRoot);
            try
            {
                using GameEngine engine = CreateEngine(repoRoot, tempRoot);
                var terrain = new FlatGridLogicTerrainField(ChunkSizeCells * 2, ChunkSizeCells * 2, chunkSizeCells: ChunkSizeCells);
                const string mapId = "missing_manifest";

                var mapConfig = new MapConfig
                {
                    Id = mapId,
                    Tags = new List<string> { MapTags.FeatureNavMeshOn.Name },
                    Boards = new List<BoardConfig> { CreateBoard("default", 0, 0) }
                };

                // Tiles exist but no manifest was ever written: a nav-enabled map must refuse
                // to serve an un-validated artifact set instead of silently querying it.
                WriteBoardTileFiles(tempRoot, mapId, null, 0, 0);

                SetLogicTerrain(engine, terrain);
                Assert.That(
                    () => engine.LoadNavForMapForTests(mapId, mapConfig),
                    Throws.InvalidOperationException.With.Message.Contains("no nav tile manifest"));
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        private static void WriteManifest(
            string assetsRoot,
            string mapId,
            string boardId,
            string? declaredMapId = null)
        {
            var entries = new List<NavTileManifestEntry>();
            foreach (string profileId in Profiles)
            {
                for (int chunkY = 0; chunkY < 2; chunkY++)
                {
                    for (int chunkX = 0; chunkX < 2; chunkX++)
                    {
                        entries.Add(new NavTileManifestEntry
                        {
                            Layer = 0,
                            ProfileId = profileId,
                            ChunkX = chunkX,
                            ChunkY = chunkY,
                            TileVersion = 1,
                            TileChecksum = "fnv1a64:0000000000000000"
                        });
                    }
                }
            }

            var manifest = new NavTileManifest
            {
                MapId = declaredMapId ?? mapId,
                BoardId = boardId,
                SourceRevision = "file:fnv1a64:deadbeef:0",
                Algorithm = "Recast",
                Mode = "Offline",
                TileVersion = 1,
                Tiles = entries.ToArray()
            };

            string rel = NavAssetPaths.GetNavTileManifestRelativePath(mapId);
            NavTileManifestSerializer.Write(Path.Combine(assetsRoot, rel.Replace('/', Path.DirectorySeparatorChar)), manifest);
        }

        private static void SetLogicTerrain(GameEngine engine, LogicTerrainField terrain)
        {
            typeof(GameEngine)
                .GetProperty(nameof(GameEngine.LogicTerrain), BindingFlags.Instance | BindingFlags.Public)!
                .SetValue(engine, terrain);
        }

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir, "showcase.registry.json"))) return dir;
                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("Repository root (showcase.registry.json) not found.");
        }
    }
}
