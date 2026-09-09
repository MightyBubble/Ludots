using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// 权威发布器（.ntil + Manifest）合同测试。核心不变量：
    /// 批次级预校验先于任何磁盘写；checksum 从实际磁盘文件读取；备份阶段 manifest 最先
    /// 拿走、发布阶段 manifest 最后放回、回滚时瓦片恢复后 manifest 才恢复 —— 任一时刻读方
    /// 看到的是完整旧组 / 完整新组 / 明确不可加载（manifest 缺席），绝不出现
    /// "旧 manifest + 部分新瓦片"；回滚只撤销自己完成动作，未备份原文件逐字节不变。
    /// </summary>
    [TestFixture]
    public sealed class NavArtifactPublisherContractTests
    {
        private string _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ludots-nav-pub-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        [Test]
        public void FullPublish_WritesTilesAndManifest_WithChecksumsFromDisk()
        {
            NavTileManifest m = Publish(new[]
            {
                Entry(Flat(0, 0), 0, 0),
                Entry(Flat(1, 0), 1, 0)
            }, replaceWholeManifest: true);

            Assert.That(m.Tiles!.Length, Is.EqualTo(2));
            foreach (NavTileManifestEntry e in m.Tiles!)
            {
                string rel = NavAssetPaths.GetNavTileRelativePath("m", null, e.Layer, e.ProfileId, e.ChunkX, e.ChunkY);
                string diskPath = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
                Assert.That(File.Exists(diskPath), Is.True, $"tile {e.ChunkX},{e.ChunkY} {e.ProfileId} must exist at its official path");

                ulong diskChecksum;
                using (var fs = File.OpenRead(diskPath))
                {
                    diskChecksum = NavTileBinary.Read(fs).Checksum;
                }

                Assert.That(e.TileChecksum, Is.EqualTo("fnv1a64:" + diskChecksum.ToString("x16")),
                    "manifest checksum must equal the actual on-disk file checksum");
            }
        }

        [Test]
        public void Publish_ThenColdStoreLoad_PassesScopeValidation()
        {
            Publish(new[]
            {
                Entry(Flat(0, 0), 0, 0),
                Entry(Flat(1, 0), 1, 0)
            }, replaceWholeManifest: true);

            // Cold start: read the manifest back from disk, exactly as a new process would.
            NavTileManifest m = NavTileManifestSerializer.Read(ManifestPath());

            var store = new NavTileStore(
                id => File.OpenRead(TilePath("m", null, 0, "Small", id.ChunkX, id.ChunkY)),
                m,
                new NavTileStoreScope("m", string.Empty, 0, "Small"));

            Assert.That(store.GetOrLoad(new NavTileId(0, 0, 0)).TileVersion, Is.EqualTo(1u));
            Assert.That(store.GetOrLoad(new NavTileId(1, 0, 0)).TileId, Is.EqualTo(new NavTileId(1, 0, 0)));
            Assert.That(store.SnapshotLoadedTiles().Length, Is.EqualTo(2));
        }

        [Test]
        public void Publish_ThreeProfilesSameCoordinate_AreDistinctManifestIdentities()
        {
            NavTileManifest m = Publish(new[]
            {
                Entry(Flat(0, 0), 0, 0, "Small"),
                Entry(Flat(0, 0), 0, 0, "Medium"),
                Entry(Flat(0, 0), 0, 0, "Large")
            }, replaceWholeManifest: true);

            Assert.That(m.Tiles!.Length, Is.EqualTo(3));
            Assert.That(m.Tiles!.Select(t => t.ProfileId), Is.EquivalentTo(new[] { "Small", "Medium", "Large" }));
        }

        [Test]
        public void DuplicateIdentityInOneBatch_FailsBeforeTouchingDisk()
        {
            PublishBaseline();
            byte[] tileBefore = TileBytes("m", null, 0, "Small", 0, 0);
            byte[] manifestBefore = ManifestBytes();

            Assert.That(
                () => Publish(new[]
                {
                    Entry(Flat(0, 0), 0, 0),
                    Entry(Flat(0, 0), 0, 0)
                }, replaceWholeManifest: true),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("duplicate"));

            AssertBatchUntouched(tileBefore, manifestBefore);
        }

        [Test]
        public void InvalidSecondEntry_IdentityMismatch_FailsBeforeTouchingDisk()
        {
            PublishBaseline();
            byte[] tileBefore = TileBytes("m", null, 0, "Small", 0, 0);
            byte[] manifestBefore = ManifestBytes();

            NavTile payloadSaysLayer0 = Flat(0, 0);
            Assert.That(
                () => Publish(new[]
                {
                    Entry(Flat(0, 0), 0, 0),
                    new NavArtifactPublishEntry { Tile = payloadSaysLayer0, Layer = 1, ProfileId = "Small", ChunkX = 0, ChunkY = 0 }
                }, replaceWholeManifest: true),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("identity must come from one authority"));

            AssertBatchUntouched(tileBefore, manifestBefore);
        }

        [Test]
        public void DirtyPublish_WithoutPriorManifest_Fails()
        {
            Assert.That(
                () => Publish(new[] { Entry(Flat(0, 0), 0, 0) }, replaceWholeManifest: false),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("no prior manifest"));
        }

        [Test]
        public void DirtyPublish_WithForeignBoardPrior_Fails()
        {
            var foreign = new NavTileManifest
            {
                MapId = "m",
                BoardId = "harbor",
                SourceRevision = "sha1:source-a",
                Algorithm = "Recast",
                Mode = "Offline",
                TileVersion = 1,
                Tiles = new[] { ManifestEntryForTile(Flat(0, 0)) }
            };
            NavTileManifestSerializer.Write(ManifestPath(), foreign);

            Assert.That(
                () => Publish(new[] { Entry(Flat(0, 0), 0, 0) }, replaceWholeManifest: false),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("scope mismatch"));
        }

        [Test]
        public void DirtyPublish_WithChangedSourceOrConfig_Fails()
        {
            PublishBaseline();

            NavArtifactPublisher.Context changedSource = Ctx();
            changedSource.SourceRevision = "sha1:other";
            Assert.That(
                () => PublishCtx(changedSource, new[] { Entry(Flat(0, 0), 0, 0) }, replaceWholeManifest: false),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("different source revision"));

            NavArtifactPublisher.Context changedConfig = Ctx();
            changedConfig.Algorithm = "Cdt";
            Assert.That(
                () => PublishCtx(changedConfig, new[] { Entry(Flat(0, 0), 0, 0) }, replaceWholeManifest: false),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("different bake configuration"));
        }

        [Test]
        public void DirtyPublish_PreservesUntouchedTile_AndUpdatesChangedOne()
        {
            PublishBaseline();
            byte[] tileBBefore = TileBytes("m", null, 0, "Small", 1, 0);

            NavTileManifest merged = Publish(new[] { Entry(Flat(0, 0, version: 2), 0, 0) }, replaceWholeManifest: false);

            Assert.That(merged.Tiles!.Length, Is.EqualTo(2), "merge must keep the untouched tile (1,0)");
            NavTileManifestEntry changed = merged.Tiles!.Single(t => t.ChunkX == 0);
            Assert.That(changed.TileVersion, Is.EqualTo(2u));

            Assert.That(TileBytes("m", null, 0, "Small", 1, 0), Is.EqualTo(tileBBefore));
            string changedPath = TilePath("m", null, 0, "Small", 0, 0);
            ulong diskChecksum;
            using (var fs = File.OpenRead(changedPath))
            {
                diskChecksum = NavTileBinary.Read(fs).Checksum;
            }

            Assert.That(changed.TileChecksum, Is.EqualTo("fnv1a64:" + diskChecksum.ToString("x16")));
        }

        [Test]
        public void BackupFailureOnSecondTile_RestoresOnlyOwnActions_AndKeepsOriginalsByteIdentical()
        {
            PublishBaseline();
            byte[] tileABefore = TileBytes("m", null, 0, "Small", 0, 0);
            byte[] tileBBefore = TileBytes("m", null, 0, "Small", 1, 0);
            byte[] manifestBefore = ManifestBytes();

            // Lock tile (1,0) so the backup phase fails when moving tile B aside. Order: the
            // manifest is backed up first, then tile A, then tile B -- B fails.
            string lockedPath = TilePath("m", null, 0, "Small", 1, 0);
            using (var lockHandle = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.That(
                    () => Publish(new[]
                    {
                        Entry(Flat(0, 0, version: 2), 0, 0),
                        Entry(Flat(1, 0, version: 2), 1, 0)
                    }, replaceWholeManifest: true),
                    Throws.TypeOf<IOException>().With.Message.Contains("restored the previous batch"));
            }

            // Tile A was moved to backup then restored: byte-identical.
            Assert.That(TileBytes("m", null, 0, "Small", 0, 0), Is.EqualTo(tileABefore));
            // Tile B was never successfully backed up: original untouched.
            Assert.That(TileBytes("m", null, 0, "Small", 1, 0), Is.EqualTo(tileBBefore));
            // Manifest backed up first and restored last: byte-identical.
            Assert.That(ManifestBytes(), Is.EqualTo(manifestBefore));

            AssertNoResidue();
        }

        [Test]
        public void ManifestPublishFailure_FullyUndoesNewBatch_LeavingGroupNonLoadable()
        {
            // Fresh set, no prior batch. Occupy the manifest final path with a directory so the
            // last rename (manifest publish) fails after the new tiles are in place.
            Directory.CreateDirectory(ManifestPath());

            try
            {
                Assert.That(
                    () => Publish(new[]
                    {
                        Entry(Flat(0, 0, version: 2), 0, 0),
                        Entry(Flat(1, 0, version: 2), 1, 0)
                    }, replaceWholeManifest: true),
                    Throws.TypeOf<IOException>().With.Message.Contains("aborted"));
            }
            finally
            {
                if (Directory.Exists(ManifestPath())) Directory.Delete(ManifestPath(), recursive: true);
            }

            // Newly published tiles were removed; no partial batch is served.
            Assert.That(File.Exists(TilePath("m", null, 0, "Small", 0, 0)), Is.False);
            Assert.That(File.Exists(TilePath("m", null, 0, "Small", 1, 0)), Is.False);
            AssertNoResidue();
        }

        [Test]
        public void SourceRevision_FromBakeSnapshot_ChangesWhenTerrainInputChanges()
        {
            NavBakeContext terrainA = TerrainCtx();
            NavBakeContext terrainB = TerrainCtx(blockedCell: true);
            string revA = NavBakeSnapshotFingerprint.Compute(terrainA);
            string revB = NavBakeSnapshotFingerprint.Compute(terrainB);
            Assert.That(revB, Is.Not.EqualTo(revA), "changed terrain must change the snapshot fingerprint");

            NavArtifactPublisher.Context ctxA = Ctx();
            ctxA.SourceRevision = revA;
            NavArtifactPublisher.Context ctxB = Ctx();
            ctxB.SourceRevision = revB;

            NavTileManifest manifestA = NavArtifactPublisher.Publish(ctxA, new[]
            {
                Entry(Flat(0, 0), 0, 0),
                Entry(Flat(1, 0), 1, 0)
            }, replaceWholeManifest: true);
            Assert.That(manifestA.SourceRevision, Is.EqualTo(revA));

            NavTileManifest manifestB = NavArtifactPublisher.Publish(ctxB, new[]
            {
                Entry(Flat(0, 0), 0, 0),
                Entry(Flat(1, 0), 1, 0)
            }, replaceWholeManifest: true);
            Assert.That(manifestB.SourceRevision, Is.EqualTo(revB));
            Assert.That(manifestB.BuildHash, Is.Not.EqualTo(manifestA.BuildHash));

            // Dirty bake under the previous source once the board moved to a new source: refuse.
            Assert.That(
                () => NavArtifactPublisher.Publish(ctxA, new[] { Entry(Flat(0, 0), 0, 0) }, replaceWholeManifest: false),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("different source revision"));
        }

        [Test]
        public void SnapshotFingerprint_IgnoresLegacyCellCost_ButTracksAreaId()
        {
            // Same geometry/area; only the legacy cell.Cost differs -> same revision (#372: cost
            // is PathingConfig-orthogonal, never a bake input).
            NavBakeContext cost1 = TerrainCtx(cellCost: 1f, areaId: 0);
            NavBakeContext cost5 = TerrainCtx(cellCost: 5f, areaId: 0);
            Assert.That(NavBakeSnapshotFingerprint.Compute(cost5),
                Is.EqualTo(NavBakeSnapshotFingerprint.Compute(cost1)),
                "legacy cell.Cost must not enter the geometry source revision");

            // Authored areaId IS consumed by the bake (heightfield spans + TriAreaIds), so an
            // area reclassification must change the source revision.
            NavBakeContext area0 = TerrainCtx(areaId: 0);
            NavBakeContext area2 = TerrainCtx(areaId: 2);
            Assert.That(NavBakeSnapshotFingerprint.Compute(area2),
                Is.Not.EqualTo(NavBakeSnapshotFingerprint.Compute(area0)),
                "areaId is a bake input and must change the source revision");
        }

        [Test]
        public void SnapshotFingerprint_ChangesWhenTerrainExtentChanges()
        {
            Assert.That(
                NavBakeSnapshotFingerprint.Compute(TerrainCtx(width: 3, height: 2)),
                Is.Not.EqualTo(NavBakeSnapshotFingerprint.Compute(TerrainCtx(width: 2, height: 2))),
                "terrain width/height must be part of the snapshot");
        }

        [Test]
        public void SnapshotFingerprint_ChangesWhenTerrainOriginChanges()
        {
            Assert.That(
                NavBakeSnapshotFingerprint.Compute(TerrainCtx(originXcm: 5000)),
                Is.Not.EqualTo(NavBakeSnapshotFingerprint.Compute(TerrainCtx(originXcm: 0))),
                "terrain origin must be part of the snapshot");
        }

        [Test]
        public void SnapshotFingerprint_ChangesWhenProfileRadiusChanges()
        {
            Assert.That(
                NavBakeSnapshotFingerprint.Compute(TerrainCtx(radiusCm: 80f)),
                Is.Not.EqualTo(NavBakeSnapshotFingerprint.Compute(TerrainCtx(radiusCm: 35f))),
                "agent profile geometry (radius) must be part of the snapshot");
        }

        [Test]
        public void SnapshotFingerprint_DistinguishesUnicodeIdsSharingALowByte()
        {
            // '\u6c34' (water) and '\u3434' share the same UTF-16 low byte 0x34.
            Assert.That(
                NavBakeSnapshotFingerprint.Compute(TerrainCtx(layerId: "\u6c34")),
                Is.Not.EqualTo(NavBakeSnapshotFingerprint.Compute(TerrainCtx(layerId: "\u3434"))),
                "UTF-8 text mixing must not truncate to UTF-16 low bytes");
        }

        [Test]
        public void SnapshotFingerprint_RequiresAgentProfiles_FailsFast()
        {
            Assert.That(
                () => NavBakeSnapshotFingerprint.Compute(TerrainCtx(withAgents: false)),
                Throws.InvalidOperationException.With.Message.Contains("AgentProfileRegistry"));
        }

        private static NavBakeContext TerrainCtx(
            int width = 2,
            int height = 2,
            int originXcm = 0,
            float radiusCm = 35f,
            string layerId = "Ground",
            float cellCost = 1f,
            byte areaId = 0,
            bool blockedCell = false,
            bool withAgents = true)
        {
            var terrain = new MutableGridLogicTerrainField(
                width, height, cellSizeCm: 250, chunkSizeCells: 64, originXcm: originXcm);
            if (blockedCell)
            {
                terrain.SetCell(0, 0, new LogicTerrainCell(1, 0, LogicTerrainSurfaceFlags.Blocked, areaId: areaId, cost: cellCost));
            }
            else
            {
                terrain.SetCell(0, 0, new LogicTerrainCell(1, 0, LogicTerrainSurfaceFlags.None, areaId: areaId, cost: cellCost));
            }

            var agents = new AgentProfileRegistry(new[]
            {
                new AgentProfileConfig { Id = "Small", RadiusCm = radiusCm, HeightCm = 180f, ClearanceCm = 10f, Mass = 70f }
            });
            var config = new NavMeshBakeConfig
            {
                Profiles = new List<NavMeshAgentProfileConfig>
                {
                    new NavMeshAgentProfileConfig { Id = "Small", MaxClimbCm = 40, MaxSlopeDeg = 45f }
                },
                Layers = new List<NavLayerConfig> { new NavLayerConfig { Id = layerId, Layer = 0 } }
            };

            return new NavBakeContext
            {
                MapId = "m",
                SourceUri = "test:terrain",
                Terrain = terrain,
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = withAgents ? agents : null,
                Algorithm = NavBakeAlgorithmKind.Recast,
                Mode = NavBakeMode.Offline,
                BuildConfig = new NavBuildConfig(2.0f, 0.6f, 1)
            };
        }

        // ---- helpers ----

        private NavTileManifest Publish(NavArtifactPublishEntry[] entries, bool replaceWholeManifest)
            => NavArtifactPublisher.Publish(Ctx(), entries, replaceWholeManifest);

        private NavTileManifest PublishCtx(NavArtifactPublisher.Context ctx, NavArtifactPublishEntry[] entries, bool replaceWholeManifest)
            => NavArtifactPublisher.Publish(ctx, entries, replaceWholeManifest);

        private NavArtifactPublisher.Context Ctx()
            => new()
            {
                RootDir = _root,
                MapId = "m",
                BoardId = string.Empty,
                SourceRevision = "sha1:source-a",
                Algorithm = "Recast",
                Mode = "Offline",
                TileVersion = 1,
                WrittenUtc = "2026-01-01T00:00:00.0000000Z"
            };

        private static NavArtifactPublishEntry Entry(NavTile tile, int x, int y, string profileId = "Small")
            => new()
            {
                Tile = tile,
                Layer = tile.TileId.Layer,
                ProfileId = profileId,
                ChunkX = x,
                ChunkY = y
            };

        private static NavTileManifestEntry ManifestEntryForTile(NavTile tile)
            => new()
            {
                Layer = tile.TileId.Layer,
                ProfileId = "Small",
                ChunkX = tile.TileId.ChunkX,
                ChunkY = tile.TileId.ChunkY,
                TileVersion = tile.TileVersion,
                TileChecksum = "fnv1a64:" + tile.Checksum.ToString("x16")
            };

        private static NavTile Flat(int x, int y, int layer = 0, string profileId = "", uint version = 1)
            => DefaultGridNavTileFactory.CreateFlatTile(
                x, y, layer: layer, tileVersion: version, chunkSizeCells: 64, cellSizeCm: 250);

        private NavTileManifest PublishBaseline()
            => Publish(new[]
            {
                Entry(Flat(0, 0), 0, 0),
                Entry(Flat(1, 0), 1, 0)
            }, replaceWholeManifest: true);

        private string TilePath(string mapId, string? boardId, int layer, string profileId, int x, int y)
        {
            string rel = NavAssetPaths.GetNavTileRelativePath(mapId, boardId, layer, profileId, x, y);
            return Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
        }

        private byte[] TileBytes(string mapId, string? boardId, int layer, string profileId, int x, int y)
            => File.ReadAllBytes(TilePath(mapId, boardId, layer, profileId, x, y));

        private string ManifestPath()
        {
            string rel = NavAssetPaths.GetNavTileManifestRelativePath("m", null);
            return Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
        }

        private byte[] ManifestBytes() => File.ReadAllBytes(ManifestPath());

        private void AssertBatchUntouched(byte[] tileBytes, byte[] manifestBytes)
        {
            Assert.That(TileBytes("m", null, 0, "Small", 0, 0), Is.EqualTo(tileBytes));
            Assert.That(ManifestBytes(), Is.EqualTo(manifestBytes));
            AssertNoResidue();
        }

        private void AssertNoResidue()
        {
            foreach (string file in Directory.GetFiles(_root, "*.bak*", SearchOption.AllDirectories))
            {
                Assert.Fail($"leftover backup file: {file}");
            }

            foreach (string file in Directory.GetFiles(_root, "*.staging*", SearchOption.AllDirectories))
            {
                Assert.Fail($"leftover staging file: {file}");
            }
        }
    }
}
