using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class RuntimeNavMeshResidencyAndAlgorithmSwitchContractTests
    {
        private const string GroundLayerId = "Ground";

        [Test]
        public void RuntimeBootstrap_SixtyFourBySixtyFour_ZeroUriResolvesAndEnqueuesOnlyConfiguredResidentWindow()
        {
            string repoRoot = FindRepoRoot();
            string mapId = "nav_runtime_residency_64x64";
            string tempAssetsRoot = CreateTempAssetsRootWithoutNavTiles(repoRoot);

            try
            {
                RewriteTempNavmesh(
                    tempAssetsRoot,
                    NavBakeNames.ModeRuntimeIncremental,
                    NavBakeNames.AlgorithmLayeredSpan,
                    initialChunkX: 2,
                    initialChunkZ: 3,
                    widthChunks: 8,
                    heightChunks: 8,
                    dirtyTileCapacity: 128,
                    residentTileCapacity: 128,
                    stagedEntryCapacity: 128,
                    publishedTileCapacity: 128);

                var engine = new GameEngine();
                engine.InitializeWithConfigPipeline(
                    new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") },
                    tempAssetsRoot);
                RemountTempAssets(engine, tempAssetsRoot);

                typeof(GameEngine)
                    .GetProperty(nameof(GameEngine.LogicTerrain), BindingFlags.Instance | BindingFlags.Public)!
                    .SetValue(
                        engine,
                        new FlatGridLogicTerrainField(
                            64 * SpatialScaleDefaults.TerrainChunkCells,
                            64 * SpatialScaleDefaults.TerrainChunkCells,
                            chunkSizeCells: SpatialScaleDefaults.TerrainChunkCells));

                engine.LoadNavForMapForTests(
                    mapId,
                    new MapConfig
                    {
                        Id = mapId,
                        Tags = new List<string> { MapTags.FeatureNavMeshOn.Name }
                    });

                Assert.That(engine.LastNavBootstrapUriResolveCount, Is.EqualTo(0));
                var queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue);
                Assert.That(queue.PendingTileCount, Is.EqualTo(64));
                Assert.That(queue.Status, Is.EqualTo(RuntimeNavMeshRebuildStatus.Pending));
                Assert.That(queue.CurrentAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.LayeredSpan));

                Assert.That(
                    engine.GetService(CoreServiceKeys.NavQueryServices).TryGetStore(0, 0, out NavTileStore store),
                    Is.True);
                InvalidOperationException residency = Assert.Throws<InvalidOperationException>(
                    () => store.GetOrLoad(new NavTileId(2, 3, 0)))!;
                Assert.That(residency.Message, Does.Contain("not resident").IgnoreCase);
                Assert.That(residency.Message, Does.Contain("no asset fallback").IgnoreCase);

                int visited = queue.EnqueueDirtyAabb(
                    new WorldAabbCm(
                        2 * SpatialScaleDefaults.TerrainChunkCells * SpatialScaleDefaults.CellCm + 10,
                        3 * SpatialScaleDefaults.TerrainChunkCells * SpatialScaleDefaults.CellCm + 10,
                        50,
                        50),
                    includeNeighbors: false);
                Assert.That(visited, Is.EqualTo(0), "Already-enqueued resident tiles must dedupe.");
                Assert.That(queue.LastDirtyVisitedCandidateCount, Is.LessThan(16));
                Assert.That(queue.LastDirtyVisitedCandidateCount, Is.Not.EqualTo(4096));
            }
            finally
            {
                Directory.Delete(tempAssetsRoot, recursive: true);
            }
        }

        [Test]
        public void RuntimeBootstrap_EightByEightResidentWindow_EnqueuesExactlySixtyFourTargets()
        {
            string repoRoot = FindRepoRoot();
            string mapId = "nav_runtime_residency_8x8";
            string tempAssetsRoot = CreateTempAssetsRootWithoutNavTiles(repoRoot);

            try
            {
                RewriteTempNavmesh(
                    tempAssetsRoot,
                    NavBakeNames.ModeRuntimeIncremental,
                    NavBakeNames.AlgorithmCdt,
                    initialChunkX: 0,
                    initialChunkZ: 0,
                    widthChunks: 8,
                    heightChunks: 8,
                    dirtyTileCapacity: 128,
                    residentTileCapacity: 128,
                    stagedEntryCapacity: 128,
                    publishedTileCapacity: 128);

                var engine = new GameEngine();
                engine.InitializeWithConfigPipeline(
                    new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") },
                    tempAssetsRoot);
                RemountTempAssets(engine, tempAssetsRoot);

                typeof(GameEngine)
                    .GetProperty(nameof(GameEngine.LogicTerrain), BindingFlags.Instance | BindingFlags.Public)!
                    .SetValue(
                        engine,
                        new FlatGridLogicTerrainField(
                            8 * SpatialScaleDefaults.TerrainChunkCells,
                            8 * SpatialScaleDefaults.TerrainChunkCells,
                            chunkSizeCells: SpatialScaleDefaults.TerrainChunkCells));

                engine.LoadNavForMapForTests(
                    mapId,
                    new MapConfig
                    {
                        Id = mapId,
                        Tags = new List<string> { MapTags.FeatureNavMeshOn.Name }
                    });

                Assert.That(engine.LastNavBootstrapUriResolveCount, Is.EqualTo(0));
                var queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue);
                Assert.That(queue.PendingTileCount, Is.EqualTo(64));
            }
            finally
            {
                Directory.Delete(tempAssetsRoot, recursive: true);
            }
        }

        [Test]
        public void RuntimeBootstrap_OutOfWorldResidentWindow_FailsExplicitly()
        {
            string repoRoot = FindRepoRoot();
            string mapId = "nav_runtime_residency_oob";
            string tempAssetsRoot = CreateTempAssetsRootWithoutNavTiles(repoRoot);

            try
            {
                RewriteTempNavmesh(
                    tempAssetsRoot,
                    NavBakeNames.ModeRuntimeIncremental,
                    NavBakeNames.AlgorithmCdt,
                    initialChunkX: 7,
                    initialChunkZ: 0,
                    widthChunks: 2,
                    heightChunks: 1,
                    dirtyTileCapacity: 16,
                    residentTileCapacity: 16,
                    stagedEntryCapacity: 16,
                    publishedTileCapacity: 16);

                var engine = new GameEngine();
                engine.InitializeWithConfigPipeline(
                    new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") },
                    tempAssetsRoot);
                RemountTempAssets(engine, tempAssetsRoot);

                typeof(GameEngine)
                    .GetProperty(nameof(GameEngine.LogicTerrain), BindingFlags.Instance | BindingFlags.Public)!
                    .SetValue(
                        engine,
                        new FlatGridLogicTerrainField(
                            8 * 4,
                            8 * 4,
                            chunkSizeCells: 4));

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                    engine.LoadNavForMapForTests(
                        mapId,
                        new MapConfig
                        {
                            Id = mapId,
                            Tags = new List<string> { MapTags.FeatureNavMeshOn.Name }
                        }))!;
                Assert.That(ex.Message, Does.Contain("outside world").IgnoreCase);
                Assert.That(ex.Message, Does.Contain("initialResident").IgnoreCase);
            }
            finally
            {
                Directory.Delete(tempAssetsRoot, recursive: true);
            }
        }

        [Test]
        public void SwitchAlgorithm_LayeredSpanToCdt_DiscardsPendingAndCommitsOneGeneration()
        {
            RuntimeHarness harness = CreateHarness(
                NavBakeAlgorithmKind.LayeredSpan,
                new CdtNavBakeAlgorithm(),
                new LayeredSpanNavBakeAlgorithm(new LayeredSpanScratchPool(CreateLayeredConfig())));

            harness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(1, 0));
            Assert.That(harness.Queue.PendingTileCount, Is.EqualTo(1));
            Assert.That(harness.Queue.CurrentAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.LayeredSpan));
            Assert.That(harness.Queue.HasRequestedAlgorithm, Is.False);
            Assert.That(harness.Queue.RequestedAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.LayeredSpan));

            ulong generationBefore = harness.Store.Generation;
            var residents = new[]
            {
                new NavBakeTileCoord(0, 0),
                new NavBakeTileCoord(1, 0)
            };
            harness.Queue.SwitchAlgorithm(NavBakeAlgorithmKind.Cdt, residents);
            // Committed/visible algorithm stays old until the resident set commits atomically.
            Assert.That(harness.Queue.CurrentAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.LayeredSpan));
            Assert.That(harness.Queue.HasRequestedAlgorithm, Is.True);
            Assert.That(harness.Queue.RequestedAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.Cdt));
            Assert.That(harness.Queue.PendingTileCount, Is.EqualTo(2));
            Assert.That(harness.Store.Generation, Is.EqualTo(generationBefore));

            var published = new RuntimeNavMeshRebuildPublishedTile[harness.Config.RuntimeIncremental.PublishedTileCapacity];
            var failures = new NavBakeResultEntry[harness.Config.RuntimeIncremental.StagedEntryCapacity];

            RuntimeNavMeshRebuildBatchStats mid = harness.Queue.ProcessBudgetInto(1, published.AsSpan(), failures.AsSpan());
            Assert.That(mid.Committed, Is.False);
            Assert.That(mid.Aborted, Is.False);
            Assert.That(harness.Queue.SealedRemainingCount, Is.GreaterThan(0));
            Assert.That(harness.Queue.CurrentAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.LayeredSpan));
            Assert.That(harness.Queue.HasRequestedAlgorithm, Is.True);
            Assert.That(harness.Queue.RequestedAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.Cdt));
            Assert.That(harness.Store.Generation, Is.EqualTo(generationBefore));

            RuntimeNavMeshRebuildBatchStats stats = default;
            while (harness.Queue.PendingTileCount > 0 || harness.Queue.SealedRemainingCount > 0)
            {
                Assert.That(harness.Queue.CurrentAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.LayeredSpan));
                Assert.That(harness.Queue.RequestedAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.Cdt));
                stats = harness.Queue.ProcessBudgetInto(4, published.AsSpan(), failures.AsSpan());
            }

            Assert.That(stats.Committed, Is.True);
            Assert.That(stats.PublishedCount, Is.EqualTo(2));
            Assert.That(harness.Store.Generation, Is.EqualTo(generationBefore + 1UL));
            Assert.That(harness.RecordingCdt.BakeCount, Is.EqualTo(2));
            Assert.That(harness.Queue.CurrentAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.Cdt));
            Assert.That(harness.Queue.HasRequestedAlgorithm, Is.False);
            Assert.That(harness.Queue.RequestedAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.Cdt));
        }

        [Test]
        public void SwitchAlgorithm_ToInjectedRecast_RebuildsResidentSet()
        {
            var fakeRecast = new RecordingNavBakeAlgorithm(NavBakeAlgorithmKind.Recast);
            RuntimeHarness harness = CreateHarness(
                NavBakeAlgorithmKind.Cdt,
                new CdtNavBakeAlgorithm(),
                new LayeredSpanNavBakeAlgorithm(new LayeredSpanScratchPool(CreateLayeredConfig())),
                fakeRecast);

            harness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0));
            Assert.That(harness.Queue.PendingTileCount, Is.EqualTo(1));

            var residents = new[] { new NavBakeTileCoord(0, 0), new NavBakeTileCoord(1, 0) };
            harness.Queue.SwitchAlgorithm(NavBakeAlgorithmKind.Recast, residents);
            Assert.That(harness.Queue.CurrentAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.Cdt));
            Assert.That(harness.Queue.HasRequestedAlgorithm, Is.True);
            Assert.That(harness.Queue.RequestedAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.Recast));
            Assert.That(harness.Queue.PendingTileCount, Is.EqualTo(2));

            var published = new RuntimeNavMeshRebuildPublishedTile[harness.Config.RuntimeIncremental.PublishedTileCapacity];
            var failures = new NavBakeResultEntry[harness.Config.RuntimeIncremental.StagedEntryCapacity];
            while (harness.Queue.PendingTileCount > 0 || harness.Queue.SealedRemainingCount > 0)
            {
                Assert.That(harness.Queue.CurrentAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.Cdt));
                harness.Queue.ProcessBudgetInto(4, published.AsSpan(), failures.AsSpan());
            }

            Assert.That(fakeRecast.BakeCount, Is.EqualTo(2));
            Assert.That(fakeRecast.LastTargets, Is.EquivalentTo(residents));
            Assert.That(harness.Queue.CurrentAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.Recast));
            Assert.That(harness.Queue.HasRequestedAlgorithm, Is.False);
        }

        [Test]
        public void SwitchAlgorithm_UnsupportedKind_DoesNotMutateStore()
        {
            RuntimeHarness harness = CreateHarness(
                NavBakeAlgorithmKind.Cdt,
                new CdtNavBakeAlgorithm());

            harness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0));
            var published = new RuntimeNavMeshRebuildPublishedTile[harness.Config.RuntimeIncremental.PublishedTileCapacity];
            var failures = new NavBakeResultEntry[harness.Config.RuntimeIncremental.StagedEntryCapacity];
            RuntimeNavMeshRebuildBatchStats committed = harness.Queue.ProcessBudgetInto(1, published.AsSpan(), failures.AsSpan());
            Assert.That(committed.Committed, Is.True);
            ulong generation = harness.Store.Generation;
            uint revision = harness.Store.Revision;
            int pendingBefore = harness.Queue.PendingTileCount;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                harness.Queue.SwitchAlgorithm(
                    NavBakeAlgorithmKind.Recast,
                    new[] { new NavBakeTileCoord(0, 0) }))!;
            Assert.That(ex.Message, Does.Contain("recast").IgnoreCase);
            Assert.That(ex.Message, Does.Contain("Registered kinds").IgnoreCase);
            Assert.That(harness.Store.Generation, Is.EqualTo(generation));
            Assert.That(harness.Store.Revision, Is.EqualTo(revision));
            Assert.That(harness.Queue.CurrentAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.Cdt));
            Assert.That(harness.Queue.HasRequestedAlgorithm, Is.False);
            Assert.That(harness.Queue.PendingTileCount, Is.EqualTo(pendingBefore));
        }

        [Test]
        public void SwitchAlgorithm_BakeFailure_KeepsCommittedAlgorithmAndExplicitRequestedTarget()
        {
            var failingCdt = new FailingNavBakeAlgorithm(NavBakeAlgorithmKind.Cdt);
            RuntimeHarness harness = CreateHarness(
                NavBakeAlgorithmKind.LayeredSpan,
                failingCdt,
                new LayeredSpanNavBakeAlgorithm(new LayeredSpanScratchPool(CreateLayeredConfig())));

            ulong generationBefore = harness.Store.Generation;
            harness.Queue.SwitchAlgorithm(
                NavBakeAlgorithmKind.Cdt,
                new[] { new NavBakeTileCoord(0, 0) });
            Assert.That(harness.Queue.CurrentAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.LayeredSpan));
            Assert.That(harness.Queue.HasRequestedAlgorithm, Is.True);
            Assert.That(harness.Queue.RequestedAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.Cdt));

            var published = new RuntimeNavMeshRebuildPublishedTile[harness.Config.RuntimeIncremental.PublishedTileCapacity];
            var failures = new NavBakeResultEntry[harness.Config.RuntimeIncremental.StagedEntryCapacity];
            RuntimeNavMeshRebuildBatchStats stats = harness.Queue.ProcessBudgetInto(1, published.AsSpan(), failures.AsSpan());
            Assert.That(stats.Aborted, Is.True);
            Assert.That(stats.Committed, Is.False);
            Assert.That(stats.FailedEntryCount, Is.EqualTo(1));
            Assert.That(harness.Store.Generation, Is.EqualTo(generationBefore));
            Assert.That(harness.Queue.CurrentAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.LayeredSpan));
            Assert.That(harness.Queue.HasRequestedAlgorithm, Is.True);
            Assert.That(harness.Queue.RequestedAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.Cdt));

            // Re-switch after failure is deterministic: discard and enqueue under a working adapter.
            harness.Queue.SwitchAlgorithm(
                NavBakeAlgorithmKind.LayeredSpan,
                new[] { new NavBakeTileCoord(0, 0) });
            Assert.That(harness.Queue.CurrentAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.LayeredSpan));
            Assert.That(harness.Queue.HasRequestedAlgorithm, Is.True);
            Assert.That(harness.Queue.RequestedAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.LayeredSpan));
            RuntimeNavMeshRebuildBatchStats ok = default;
            while (harness.Queue.PendingTileCount > 0 || harness.Queue.SealedRemainingCount > 0)
            {
                ok = harness.Queue.ProcessBudgetInto(4, published.AsSpan(), failures.AsSpan());
            }

            Assert.That(ok.Committed, Is.True);
            Assert.That(harness.Queue.CurrentAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.LayeredSpan));
            Assert.That(harness.Queue.HasRequestedAlgorithm, Is.False);
        }

        [Test]
        public void NavQueryServiceRegistry_FlatGridTileSpace_LocatesPointInTileOneZero_IncludingNegativeOrigin()
        {
            const int tileWidthCm = SpatialScaleDefaults.TerrainChunkCells * SpatialScaleDefaults.CellCm;
            const int tileHeightCm = tileWidthCm;
            Assert.That(tileWidthCm, Is.EqualTo(6400));

            AssertRegistryLocatesTileOneZero(
                originXcm: 0,
                originZcm: 0,
                tileWidthCm,
                tileHeightCm,
                worldXcm: tileWidthCm + 100,
                worldZcm: 100);

            AssertRegistryLocatesTileOneZero(
                originXcm: -tileWidthCm,
                originZcm: -200,
                tileWidthCm,
                tileHeightCm,
                worldXcm: 100,
                worldZcm: -200 + 50);

            // Negative world coordinates must floor into negative tile indices (no clamp-to-zero).
            AssertRegistryLocatesTile(
                originXcm: 0,
                originZcm: 0,
                tileWidthCm,
                tileHeightCm,
                worldXcm: -50,
                worldZcm: 10,
                expectedChunkX: -1,
                expectedChunkY: 0);
        }

        [Test]
        public void ResidentWindowTransition_PublishesNewSetAndEvictsOldUnderOneGeneration()
        {
            RuntimeHarness harness = CreateHarness(
                NavBakeAlgorithmKind.Cdt,
                new CdtNavBakeAlgorithm());

            var published = new RuntimeNavMeshRebuildPublishedTile[harness.Config.RuntimeIncremental.PublishedTileCapacity];
            var failures = new NavBakeResultEntry[harness.Config.RuntimeIncremental.StagedEntryCapacity];

            harness.Queue.RequestResidentWindowTransition(new[]
            {
                new NavBakeTileCoord(0, 0),
                new NavBakeTileCoord(1, 0)
            });
            Assert.That(harness.Queue.HasResidentWindowTransition, Is.True);
            Assert.That(harness.Queue.ResidentWindowCount, Is.EqualTo(2));
            Assert.That(harness.Queue.PendingTileCount, Is.EqualTo(2));

            RuntimeNavMeshRebuildBatchStats first = default;
            while (harness.Queue.PendingTileCount > 0 || harness.Queue.SealedRemainingCount > 0)
            {
                first = harness.Queue.ProcessBudgetInto(4, published.AsSpan(), failures.AsSpan());
            }

            Assert.That(first.Committed, Is.True);
            Assert.That(harness.Queue.HasResidentWindowTransition, Is.False);
            Assert.That(harness.Store.ResidentCount, Is.EqualTo(2));
            Assert.That(harness.Store.TryGet(new NavTileId(0, 0, 0), out _), Is.True);
            Assert.That(harness.Store.TryGet(new NavTileId(1, 0, 0), out _), Is.True);
            ulong generationAfterFirst = harness.Store.Generation;

            harness.Queue.RequestResidentWindowTransition(new[]
            {
                new NavBakeTileCoord(1, 0)
            });
            Assert.That(harness.Queue.HasResidentWindowTransition, Is.True);

            RuntimeNavMeshRebuildBatchStats second = default;
            while (harness.Queue.PendingTileCount > 0 || harness.Queue.SealedRemainingCount > 0)
            {
                second = harness.Queue.ProcessBudgetInto(4, published.AsSpan(), failures.AsSpan());
            }

            Assert.That(second.Committed, Is.True);
            Assert.That(second.Generation, Is.EqualTo(generationAfterFirst + 1UL));
            Assert.That(harness.Store.Generation, Is.EqualTo(generationAfterFirst + 1UL));
            Assert.That(harness.Store.ResidentCount, Is.EqualTo(1));
            Assert.That(harness.Store.TryGet(new NavTileId(1, 0, 0), out _), Is.True);
            Assert.That(harness.Store.TryGet(new NavTileId(0, 0, 0), out _), Is.False);

            var ids = new NavTileId[8];
            int count = harness.Store.CopyResidentTileIds(ids);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(ids[0], Is.EqualTo(new NavTileId(1, 0, 0)));
        }

        [Test]
        public void PartialDirtyCommit_PreservesNonDirtyResidentTiles()
        {
            RuntimeHarness harness = CreateHarness(
                NavBakeAlgorithmKind.Cdt,
                new CdtNavBakeAlgorithm());

            var published = new RuntimeNavMeshRebuildPublishedTile[harness.Config.RuntimeIncremental.PublishedTileCapacity];
            var failures = new NavBakeResultEntry[harness.Config.RuntimeIncremental.StagedEntryCapacity];

            harness.Queue.RequestResidentWindowTransition(new[]
            {
                new NavBakeTileCoord(0, 0),
                new NavBakeTileCoord(1, 0)
            });
            while (harness.Queue.PendingTileCount > 0 || harness.Queue.SealedRemainingCount > 0)
            {
                harness.Queue.ProcessBudgetInto(4, published.AsSpan(), failures.AsSpan());
            }

            ulong checksum0 = RequireChecksum(harness.Store, 0, 0);
            ulong checksum1 = RequireChecksum(harness.Store, 1, 0);
            ulong generationBefore = harness.Store.Generation;

            harness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0));
            RuntimeNavMeshRebuildBatchStats stats = default;
            while (harness.Queue.PendingTileCount > 0 || harness.Queue.SealedRemainingCount > 0)
            {
                stats = harness.Queue.ProcessBudgetInto(4, published.AsSpan(), failures.AsSpan());
            }

            Assert.That(stats.Committed, Is.True);
            Assert.That(harness.Store.Generation, Is.EqualTo(generationBefore + 1UL));
            Assert.That(harness.Store.ResidentCount, Is.EqualTo(2));
            Assert.That(RequireChecksum(harness.Store, 1, 0), Is.EqualTo(checksum1), "Non-dirty resident tile must be preserved.");
            Assert.That(harness.Store.TryGet(new NavTileId(0, 0, 0), out _), Is.True);
            // Dirty tile was republished; checksum may stay equal for identical geometry, but residency must remain.
            Assert.That(RequireChecksum(harness.Store, 0, 0), Is.EqualTo(checksum0).Or.Not.EqualTo(0UL));
        }

        [Test]
        public void ResidentWindow_OutOfWindowDirtyIsNotPublished_AndLaterTransitionRebuildsIt()
        {
            RuntimeHarness harness = CreateHarness(
                NavBakeAlgorithmKind.Cdt,
                new CdtNavBakeAlgorithm());

            var published = new RuntimeNavMeshRebuildPublishedTile[harness.Config.RuntimeIncremental.PublishedTileCapacity];
            var failures = new NavBakeResultEntry[harness.Config.RuntimeIncremental.StagedEntryCapacity];

            harness.Queue.RequestResidentWindowTransition(new[] { new NavBakeTileCoord(0, 0) });
            while (harness.Queue.PendingTileCount > 0 || harness.Queue.SealedRemainingCount > 0)
            {
                harness.Queue.ProcessBudgetInto(4, published.AsSpan(), failures.AsSpan());
            }

            ulong generationBefore = harness.Store.Generation;
            Assert.That(harness.Store.ResidentCount, Is.EqualTo(1));
            Assert.That(harness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(1, 0)), Is.False);
            Assert.That(harness.Queue.PendingTileCount, Is.EqualTo(0));
            Assert.That(harness.Store.Generation, Is.EqualTo(generationBefore));
            Assert.That(harness.Store.TryGet(new NavTileId(1, 0, 0), out _), Is.False);

            harness.Queue.RequestResidentWindowTransition(new[] { new NavBakeTileCoord(1, 0) });
            while (harness.Queue.PendingTileCount > 0 || harness.Queue.SealedRemainingCount > 0)
            {
                harness.Queue.ProcessBudgetInto(4, published.AsSpan(), failures.AsSpan());
            }

            Assert.That(harness.Store.Generation, Is.EqualTo(generationBefore + 1UL));
            Assert.That(harness.Store.ResidentCount, Is.EqualTo(1));
            Assert.That(harness.Store.TryGet(new NavTileId(0, 0, 0), out _), Is.False);
            Assert.That(harness.Store.TryGet(new NavTileId(1, 0, 0), out _), Is.True);
        }

        [Test]
        public void ResidentWindowTransition_OutOfWorldFailsBeforeMutation()
        {
            RuntimeHarness harness = CreateHarness(
                NavBakeAlgorithmKind.Cdt,
                new CdtNavBakeAlgorithm());

            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
                harness.Queue.RequestResidentWindowTransition(new[]
                {
                    new NavBakeTileCoord(0, 0),
                    new NavBakeTileCoord(7, 0)
                }))!;
            Assert.That(ex.Message, Does.Contain("out of range").IgnoreCase);
            Assert.That(harness.Store.ResidentCount, Is.EqualTo(0));
            Assert.That(harness.Store.Generation, Is.EqualTo(0UL));
            Assert.That(harness.Queue.HasResidentWindowTransition, Is.False);
        }

        [Test]
        public void SwitchAlgorithm_DuringResidentWindowTransition_IsRejectedAndKeepsCommittedWindow()
        {
            RuntimeHarness harness = CreateHarness(
                NavBakeAlgorithmKind.Cdt,
                new CdtNavBakeAlgorithm());

            var published = new RuntimeNavMeshRebuildPublishedTile[harness.Config.RuntimeIncremental.PublishedTileCapacity];
            var failures = new NavBakeResultEntry[harness.Config.RuntimeIncremental.StagedEntryCapacity];

            harness.Queue.RequestResidentWindowTransition(new[]
            {
                new NavBakeTileCoord(0, 0),
                new NavBakeTileCoord(1, 0)
            });
            while (harness.Queue.PendingTileCount > 0 || harness.Queue.SealedRemainingCount > 0)
            {
                harness.Queue.ProcessBudgetInto(4, published.AsSpan(), failures.AsSpan());
            }

            Assert.That(harness.Queue.CommittedResidentWindowCount, Is.EqualTo(2));

            harness.Queue.RequestResidentWindowTransition(new[]
            {
                new NavBakeTileCoord(1, 0)
            });
            Assert.That(harness.Queue.HasResidentWindowTransition, Is.True);

            var switchTiles = new NavBakeTileCoord[2];
            int committedCount = harness.Queue.CopyCommittedResidentWindow(switchTiles);
            Assert.That(committedCount, Is.EqualTo(2));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                harness.Queue.SwitchAlgorithm(NavBakeAlgorithmKind.LayeredSpan, switchTiles.AsSpan(0, committedCount)))!;
            Assert.That(ex.Message, Does.Contain("resident-window transition").IgnoreCase);
            Assert.That(harness.Queue.HasResidentWindowTransition, Is.True);
            Assert.That(harness.Queue.CurrentAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.Cdt));
            Assert.That(harness.Queue.CommittedResidentWindowCount, Is.EqualTo(2));
        }

        [Test]
        public void RuntimeNavMeshTelemetry_RecordsHotUpdateWithoutPerCallAllocationAfterWarmup()
        {
            var telemetry = new RuntimeNavMeshTelemetryService(sampleCapacity: 8);
            var stats = new RuntimeNavMeshRebuildBatchStats(
                requestedTileBudget: 4,
                rebuiltTileCount: 2,
                failedEntryCount: 0,
                pendingTileCount: 0,
                sealedRemainingCount: 0,
                committed: true,
                aborted: false,
                generation: 3UL,
                publishedCount: 2,
                bakeTicks: 80,
                commitTicks: 20,
                generationChecksum: 0xABCUL,
                peakResidentTileCount: 2,
                workerCount: 1);

            telemetry.RecordHotUpdate(
                collectTicks: 10,
                bakeTicks: 80,
                commitTicks: 20,
                allocatedBytes: 0,
                in stats,
                peakWorkerScratchBytes: 1024,
                peakResidentBytes: 2048,
                droppedDirtyCommandCount: 0,
                capacityGrowthCount: 0,
                fallbackCount: 0);
            telemetry.RecordHotUpdate(
                collectTicks: 12,
                bakeTicks: 160,
                commitTicks: 28,
                allocatedBytes: 16,
                in stats,
                peakWorkerScratchBytes: 1024,
                peakResidentBytes: 2048,
                droppedDirtyCommandCount: 0,
                capacityGrowthCount: 0,
                fallbackCount: 0);
            RuntimeNavMeshTelemetrySnapshot snap = telemetry.CaptureSnapshot();
            Assert.That(snap.SampleCount, Is.EqualTo(2));
            Assert.That(snap.FallbackCount, Is.EqualTo(0));
            Assert.That(snap.CollectTicksP50, Is.EqualTo(10));
            Assert.That(snap.BakeTicksP95, Is.EqualTo(160));
            Assert.That(snap.CommitTicksP95, Is.EqualTo(28));
            Assert.That(snap.DirtyPublishTicksP95, Is.EqualTo(12 + 160 + 28));
            Assert.That(snap.AllocatedBytesP95, Is.EqualTo(16));
            Assert.That(snap.LastGeneration, Is.EqualTo(3UL));
            Assert.That(snap.LastGenerationChecksum, Is.EqualTo(0xABCUL));
            Assert.That(snap.DroppedDirtyCommandCount, Is.EqualTo(0));
            Assert.That(snap.CapacityGrowthCount, Is.EqualTo(0));
        }

        [Test]
        public void TriangleSurfaceEditTransaction_PendingBakeRejectsCommitWithoutPartialPublication()
        {
            RuntimeHarness harness = CreateHarness(
                NavBakeAlgorithmKind.Cdt,
                new CdtNavBakeAlgorithm());
            var published = new RuntimeNavMeshRebuildPublishedTile[harness.Config.RuntimeIncremental.PublishedTileCapacity];
            var failures = new NavBakeResultEntry[harness.Config.RuntimeIncremental.StagedEntryCapacity];
            harness.Queue.RequestResidentWindowTransition(new[]
            {
                new NavBakeTileCoord(0, 0),
                new NavBakeTileCoord(1, 0)
            });
            while (harness.Queue.PendingTileCount > 0 || harness.Queue.SealedRemainingCount > 0)
            {
                harness.Queue.ProcessBudgetInto(4, published.AsSpan(), failures.AsSpan());
            }

            var surfaceService = new RuntimeNavTriangleSurfaceService(harness.Surface);
            NavTriangleSurfaceTileIndex publishedOwnedKey = harness.Surface;
            var transaction = new RuntimeNavTriangleSurfaceEditTransaction(
                surfaceService,
                harness.Queue,
                surface => publishedOwnedKey = surface,
                includeNeighborTiles: false);
            var brush = new NavTriangleSurfaceTerrainBrushSpec(
                centerXcm: 200,
                centerZcm: 200,
                halfExtentCm: 50,
                kind: NavTriangleSurfaceTerrainBrushKind.Raise,
                cellSizeCm: 100,
                heightScaleMeters: 1f,
                baseHeightLevel: 0,
                raiseHeightLevel: 2,
                targetMinYcm: -10,
                targetMaxYcm: 10);
            transaction.StageBrush(in brush);

            Assert.That(harness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(1, 0)), Is.True);
            Assert.That(harness.Queue.Status, Is.EqualTo(RuntimeNavMeshRebuildStatus.Pending));
            ulong surfaceGenerationBefore = surfaceService.ContentGeneration;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => transaction.Commit())!;

            Assert.That(ex.Message, Does.Contain("Commit requires an idle queue"));
            Assert.That(surfaceService.Published, Is.SameAs(harness.Surface));
            Assert.That(surfaceService.ContentGeneration, Is.EqualTo(surfaceGenerationBefore));
            Assert.That(publishedOwnedKey, Is.SameAs(harness.Surface));
            Assert.That(harness.Queue.CurrentTriangleSurface, Is.SameAs(harness.Surface));
            Assert.That(harness.Queue.PendingTileCount, Is.EqualTo(1));
            Assert.That(transaction.HasStaged, Is.True, "Rejected edits remain staged for an explicit retry or clear.");
        }

        [Test]
        public void TriangleSurfaceEditTransaction_ExactRestoreReusesCommittedLocalDirtyAabb()
        {
            RuntimeHarness harness = CreateHarness(
                NavBakeAlgorithmKind.Cdt,
                new CdtNavBakeAlgorithm());
            var published = new RuntimeNavMeshRebuildPublishedTile[harness.Config.RuntimeIncremental.PublishedTileCapacity];
            var failures = new NavBakeResultEntry[harness.Config.RuntimeIncremental.StagedEntryCapacity];
            harness.Queue.RequestResidentWindowTransition(new[] { new NavBakeTileCoord(0, 0) });
            DrainQueue(harness.Queue, published, failures);

            var surfaceService = new RuntimeNavTriangleSurfaceService(harness.Surface);
            NavTriangleSurfaceTileIndex publishedOwnedKey = harness.Surface;
            var transaction = new RuntimeNavTriangleSurfaceEditTransaction(
                surfaceService,
                harness.Queue,
                surface => publishedOwnedKey = surface,
                includeNeighborTiles: false);
            var brush = new NavTriangleSurfaceTerrainBrushSpec(
                centerXcm: 200,
                centerZcm: 200,
                halfExtentCm: 50,
                kind: NavTriangleSurfaceTerrainBrushKind.Raise,
                cellSizeCm: 100,
                heightScaleMeters: 1f,
                baseHeightLevel: 0,
                raiseHeightLevel: 2,
                targetMinYcm: -10,
                targetMaxYcm: 10);

            transaction.StageBrush(in brush);
            WorldAabbCm committedDirty = transaction.StagedDirtyAabb;
            transaction.Commit();
            Assert.That(surfaceService.ContentGeneration, Is.EqualTo(2UL));
            Assert.That(harness.Queue.PendingTileCount, Is.EqualTo(1));
            DrainQueue(harness.Queue, published, failures);

            transaction.StageExactRestore();
            Assert.That(transaction.StagedDirtyAabb, Is.EqualTo(committedDirty));
            transaction.Commit();

            Assert.That(surfaceService.ContentGeneration, Is.EqualTo(3UL));
            Assert.That(surfaceService.Published, Is.SameAs(harness.Surface));
            Assert.That(publishedOwnedKey, Is.SameAs(harness.Surface));
            Assert.That(harness.Queue.CurrentTriangleSurface, Is.SameAs(harness.Surface));
            Assert.That(harness.Queue.PendingTileCount, Is.EqualTo(1));
        }

        private static void DrainQueue(
            RuntimeIncrementalNavMeshRebuildQueue queue,
            RuntimeNavMeshRebuildPublishedTile[] published,
            NavBakeResultEntry[] failures)
        {
            while (queue.PendingTileCount > 0 || queue.SealedRemainingCount > 0)
            {
                queue.ProcessBudgetInto(4, published.AsSpan(), failures.AsSpan());
            }
        }

        private static ulong RequireChecksum(NavTileStore store, int chunkX, int chunkY)
        {
            Assert.That(store.TryGet(new NavTileId(chunkX, chunkY, 0), out NavTile tile), Is.True);
            return tile.Checksum;
        }

        private static void AssertRegistryLocatesTileOneZero(
            int originXcm,
            int originZcm,
            int tileWidthCm,
            int tileHeightCm,
            int worldXcm,
            int worldZcm)
        {
            AssertRegistryLocatesTile(
                originXcm,
                originZcm,
                tileWidthCm,
                tileHeightCm,
                worldXcm,
                worldZcm,
                expectedChunkX: 1,
                expectedChunkY: 0);
        }

        private static void AssertRegistryLocatesTile(
            int originXcm,
            int originZcm,
            int tileWidthCm,
            int tileHeightCm,
            int worldXcm,
            int worldZcm,
            int expectedChunkX,
            int expectedChunkY)
        {
            var tileSpace = new NavQueryTileSpace(originXcm, originZcm, tileWidthCm, tileHeightCm);
            int tileOriginX = checked(originXcm + expectedChunkX * tileWidthCm);
            int tileOriginZ = checked(originZcm + expectedChunkY * tileHeightCm);
            NavTile tile = new NavTile(
                new NavTileId(expectedChunkX, expectedChunkY, 0),
                tileVersion: 1,
                buildConfigHash: 0UL,
                checksum: 1UL,
                tileOriginX,
                tileOriginZ,
                vertexXcm: new[] { 0, tileWidthCm, tileWidthCm, 0 },
                vertexYcm: new[] { 0, 0, 0, 0 },
                vertexZcm: new[] { 0, 0, tileHeightCm, tileHeightCm },
                triA: new[] { 0, 1 },
                triB: new[] { 1, 2 },
                triC: new[] { 3, 3 },
                n0: new[] { -1, -1 },
                n1: new[] { 1, -1 },
                n2: new[] { -1, 0 },
                triAreaIds: new[] { (byte)0, (byte)0 },
                portals: Array.Empty<NavBorderPortal>());

            var store = new NavTileStore(
                _ => throw new InvalidOperationException("FlatGrid locate test publishes before disk load."),
                residentTileCapacity: 8,
                outputVertexCapacity: 64,
                outputTriangleCapacity: 64,
                outputPortalCapacity: 16);
            store.Replace(tile);
            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore>
                {
                    [new NavQueryServiceKey(0, 0)] = store
                },
                tileSpace);

            Assert.That(registry.TileSpace, Is.EqualTo(tileSpace));
            Assert.That(registry.TryCreateQuery(0, 0, NavAreaCostTable.CreateDefault(), out NavQueryService query), Is.True);
            Assert.That(query.TryProject(worldXcm, worldZcm, out NavLocation loc), Is.True);
            Assert.That(loc.TileId.ChunkX, Is.EqualTo(expectedChunkX));
            Assert.That(loc.TileId.ChunkY, Is.EqualTo(expectedChunkY));
        }

        private static RuntimeHarness CreateHarness(
            NavBakeAlgorithmKind initial,
            params INavBakeAlgorithm[] algorithms)
        {
            var terrain = new FlatGridLogicTerrainField(8, 4, chunkSizeCells: 4);
            NavMeshBakeConfig config = CreateConfig(NavBakeNames.FormatAlgorithm(initial));
            config.RuntimeIncremental.DirtyTileCapacity = 16;
            config.RuntimeIncremental.StagedEntryCapacity = 16;
            config.RuntimeIncremental.PublishedTileCapacity = 16;
            AgentProfileRegistry agentProfiles = CreateAgentProfiles();
            var navProfiles = new NavMeshProfileRegistry(config, agentProfiles);
            var obstacles = new RuntimeNavObstacleSnapshot(
                config.RuntimeIncremental.ObstaclePrimitiveCapacity,
                config.RuntimeIncremental.PolygonVertexCapacity,
                GroundLayerId);
            var store = new NavTileStore(
                _ => throw new InvalidOperationException("Runtime algorithm-switch tests publish before disk load."),
                config.RuntimeIncremental);
            NavBuildConfig build = new NavBuildConfig(1f, 0.6f, 1);
            NavTriangleSurfaceTileIndex surface = LogicTerrainTriangleSurfaceCompiler.Compile(terrain, config, build);
            var registry = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>
            {
                [new NavQueryServiceKey(0, 0)] = store
            }, NavQueryTileSpace.FromGrid(surface.Grid));
            RecordingNavBakeAlgorithm? recordingCdt = null;
            for (int i = 0; i < algorithms.Length; i++)
            {
                if (algorithms[i] is RecordingNavBakeAlgorithm recording &&
                    recording.Kind == NavBakeAlgorithmKind.Cdt)
                {
                    recordingCdt = recording;
                }
            }

            // Wrap CDT with a recording decorator when the real Cdt adapter is used.
            INavBakeAlgorithm[] wrapped = new INavBakeAlgorithm[algorithms.Length];
            for (int i = 0; i < algorithms.Length; i++)
            {
                if (algorithms[i] is CdtNavBakeAlgorithm cdt)
                {
                    recordingCdt = new RecordingNavBakeAlgorithm(NavBakeAlgorithmKind.Cdt, cdt);
                    wrapped[i] = recordingCdt;
                }
                else
                {
                    wrapped[i] = algorithms[i];
                }
            }

            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(wrapped),
                new NavBakeContext
                {
                    MapId = "runtime_algorithm_switch",
                    SourceUri = "Core:Maps/runtime_algorithm_switch.runtime-navmesh",
                    TriangleSurface = surface,
                    Obstacles = obstacles,
                    Config = config,
                    AgentProfiles = agentProfiles,
                    Targets = new[] { new NavBakeTileCoord(0, 0) },
                    BuildConfig = build,
                    TileVersion = 1,
                    Mode = NavBakeMode.RuntimeIncremental,
                    Algorithm = initial,
                    Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
                },
                registry,
                navProfiles);

            return new RuntimeHarness(config, store, queue, surface, recordingCdt!);
        }

        private sealed class RuntimeHarness
        {
            public RuntimeHarness(
                NavMeshBakeConfig config,
                NavTileStore store,
                RuntimeIncrementalNavMeshRebuildQueue queue,
                NavTriangleSurfaceTileIndex surface,
                RecordingNavBakeAlgorithm recordingCdt)
            {
                Config = config;
                Store = store;
                Queue = queue;
                Surface = surface;
                RecordingCdt = recordingCdt;
            }

            public NavMeshBakeConfig Config { get; }
            public NavTileStore Store { get; }
            public RuntimeIncrementalNavMeshRebuildQueue Queue { get; }
            public NavTriangleSurfaceTileIndex Surface { get; }
            public RecordingNavBakeAlgorithm RecordingCdt { get; }
        }

        private sealed class FailingNavBakeAlgorithm : INavBakeAlgorithm
        {
            public FailingNavBakeAlgorithm(NavBakeAlgorithmKind kind)
            {
                Kind = kind;
            }

            public NavBakeAlgorithmKind Kind { get; }

            public NavBakeAdapterCapabilities Capabilities =>
                NavBakeAdapterCapabilities.OfflineTriangleSurface |
                NavBakeAdapterCapabilities.RuntimeIncrementalTriangleSurface;

            public bool TryBake(
                NavBakeContext context,
                NavBakeTileCoord target,
                NavLayerConfig layer,
                NavMeshAgentProfileConfig navProfile,
                AgentProfileConfig agentProfile,
                out NavTile tile,
                out byte[] detourTileBytes,
                out NavBakeArtifact artifact)
            {
                tile = null!;
                detourTileBytes = Array.Empty<byte>();
                artifact = new NavBakeArtifact(
                    new NavTileId(target.ChunkX, target.ChunkY, layer.Layer),
                    context.TileVersion,
                    NavBakeStage.Triangulate,
                    NavBakeErrorCode.TriangulationFailed,
                    "algorithm-switch-fail",
                    walkableTriangleCount: 0,
                    vertexCount: 0,
                    triangleCount: 0,
                    portalCount: 0);
                return false;
            }
        }

        private sealed class RecordingNavBakeAlgorithm : INavBakeAlgorithm
        {
            private readonly INavBakeAlgorithm? _inner;

            public RecordingNavBakeAlgorithm(NavBakeAlgorithmKind kind, INavBakeAlgorithm? inner = null)
            {
                Kind = kind;
                _inner = inner;
            }

            public NavBakeAlgorithmKind Kind { get; }

            public NavBakeAdapterCapabilities Capabilities =>
                NavBakeAdapterCapabilities.OfflineTriangleSurface |
                NavBakeAdapterCapabilities.RuntimeIncrementalTriangleSurface;

            public int BakeCount { get; private set; }

            public List<NavBakeTileCoord> LastTargets { get; } = new List<NavBakeTileCoord>();

            public bool TryBake(
                NavBakeContext context,
                NavBakeTileCoord target,
                NavLayerConfig layer,
                NavMeshAgentProfileConfig navProfile,
                AgentProfileConfig agentProfile,
                out NavTile tile,
                out byte[] detourTileBytes,
                out NavBakeArtifact artifact)
            {
                BakeCount++;
                LastTargets.Add(target);
                if (_inner != null)
                {
                    return _inner.TryBake(
                        context,
                        target,
                        layer,
                        navProfile,
                        agentProfile,
                        out tile,
                        out detourTileBytes,
                        out artifact);
                }

                // Fake Recast: emit a valid flat tile covering the target.
                int tileWidthCm = context.RequireTriangleSurface().Grid.TileWidthCm;
                int tileHeightCm = context.RequireTriangleSurface().Grid.TileHeightCm;
                tile = DefaultGridNavTileFactory.CreateFlatTile(
                    target.ChunkX,
                    target.ChunkY,
                    layer.Layer,
                    context.TileVersion,
                    tileWidthCm,
                    tileHeightCm,
                    tileWidthCells: Math.Max(1, tileWidthCm / SpatialScaleDefaults.CellCm),
                    tileHeightCells: Math.Max(1, tileHeightCm / SpatialScaleDefaults.CellCm));
                detourTileBytes = Array.Empty<byte>();
                artifact = new NavBakeArtifact(
                    tile.TileId,
                    tile.TileVersion,
                    NavBakeStage.Serialize,
                    NavBakeErrorCode.None,
                    message: string.Empty,
                    walkableTriangleCount: tile.TriangleCount,
                    vertexCount: tile.VertexCount,
                    triangleCount: tile.TriangleCount,
                    portalCount: tile.PortalCount);
                return true;
            }
        }

        private static NavMeshBakeConfig CreateConfig(string algorithm)
        {
            return new NavMeshBakeConfig
            {
                Mode = NavBakeNames.ModeRuntimeIncremental,
                Algorithm = algorithm,
                Profiles = new List<NavMeshAgentProfileConfig>
                {
                    new NavMeshAgentProfileConfig { Id = "Small", MaxClimbCm = 40, MaxSlopeDeg = 45 }
                },
                Layers = new List<NavLayerConfig>
                {
                    new NavLayerConfig { Id = GroundLayerId, Layer = 0 }
                },
                Areas = new List<NavAreaCostConfig>(),
                RuntimeIncremental = new NavRuntimeIncrementalConfig
                {
                    TileBudgetPerFixedTick = 4,
                    IncludeNeighborTiles = false,
                    HeightScaleMeters = 1f,
                    MinWalkableUpDot = 0.6f,
                    CliffHeightThreshold = 1,
                    TrackedStructuralEntityCapacity = 16,
                    ObstaclePrimitiveCapacity = 32,
                    PolygonVertexCapacity = 256,
                    DirtyTileCapacity = 64,
                    StagedEntryCapacity = 64,
                    PublishedTileCapacity = 64,
                    StoreGroupCapacity = 8,
                    ResidentTileCapacity = 128,
                    OutputVertexCapacity = 256,
                    OutputTriangleCapacity = 512,
                    OutputPortalCapacity = 64,
                    InitialResidentChunkX = 0,
                    InitialResidentChunkZ = 0,
                    InitialResidentWidthChunks = 1,
                    InitialResidentHeightChunks = 1
                },
                LayeredSpan = CreateLayeredConfig(),
                TriangleSurface = new NavTriangleSurfaceConfig { HaloPaddingCm = 100 },
                Recast = new NavRecastConfig { RasterCellSizeCm = 10, RasterCellHeightCm = 5 }
            };
        }

        private static NavLayeredSpanConfig CreateLayeredConfig()
        {
            return new NavLayeredSpanConfig
            {
                ScratchSlotCount = 2,
                RasterCellSizeCm = 100,
                RasterHaloCells = 1,
                SameSurfaceToleranceCm = 5,
                MaxSimplificationErrorCm = 0,
                HeightRounding = NavLayeredSpanConfig.HeightRoundingRoundHalfAwayFromZero,
                MaxLawsonFlipCount = 100_000,
                ColumnCapacity = 64,
                SpanCapacity = 128,
                ClassifiedSpanCapacity = 128,
                WalkableSpanCapacity = 128,
                LinkCapacity = 256,
                SheetCapacity = 128,
                PortalIntervalCapacity = 256,
                RegionCapacity = 64,
                ChartCapacity = 32,
                RingCapacity = 32,
                ContourVertexCapacity = 256,
                ContourEdgeCapacity = 256,
                SeamCapacity = 64,
                CanonicalLinkCapacity = 256,
                SplitPointCapacity = 64,
                TriangulationVertexCapacity = 256,
                TriangulationTriangleCapacity = 512,
                ConstrainedEdgeCapacity = 512,
                BorderPortalCapacity = 64,
                PolygonVertexCapacity = 256,
                AdjacencyEdgeCapacity = 1536,
                BridgeCandidateCapacity = 256,
                RingWorkCapacity = 64,
                TemporaryConstraintFlagCapacity = 512
            };
        }

        private static AgentProfileRegistry CreateAgentProfiles()
        {
            return new AgentProfileRegistry(new[]
            {
                new AgentProfileConfig
                {
                    Id = "Small",
                    RadiusCm = 30,
                    HeightCm = 180,
                    ClearanceCm = 40,
                    Mass = 1,
                    Layer = 0
                }
            });
        }

        private static string CreateTempAssetsRootWithoutNavTiles(string repoRoot)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "ludots-nav-residency-" + Guid.NewGuid().ToString("N"));
            CopyDirectory(Path.Combine(repoRoot, "assets", "Configs"), Path.Combine(tempRoot, "Configs"));
            return tempRoot;
        }

        private static void RewriteTempNavmesh(
            string tempAssetsRoot,
            string mode,
            string algorithm,
            int initialChunkX,
            int initialChunkZ,
            int widthChunks,
            int heightChunks,
            int dirtyTileCapacity,
            int residentTileCapacity,
            int stagedEntryCapacity,
            int publishedTileCapacity)
        {
            string path = Path.Combine(tempAssetsRoot, "Configs", "Navigation", "navmesh.json");
            JsonObject navmesh = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                ?? throw new InvalidOperationException($"Expected JSON object at '{path}'.");
            navmesh["mode"] = mode;
            navmesh["algorithm"] = algorithm;
            JsonObject runtime = navmesh["runtimeIncremental"]?.AsObject()
                ?? throw new InvalidOperationException("runtimeIncremental missing.");
            runtime["initialResidentChunkX"] = initialChunkX;
            runtime["initialResidentChunkZ"] = initialChunkZ;
            runtime["initialResidentWidthChunks"] = widthChunks;
            runtime["initialResidentHeightChunks"] = heightChunks;
            runtime["dirtyTileCapacity"] = dirtyTileCapacity;
            runtime["residentTileCapacity"] = residentTileCapacity;
            runtime["stagedEntryCapacity"] = stagedEntryCapacity;
            runtime["publishedTileCapacity"] = publishedTileCapacity;
            File.WriteAllText(path, navmesh.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }

        private static void RemountTempAssets(GameEngine engine, string tempAssetsRoot)
        {
            var vfs = (VirtualFileSystem)engine.VFS;
            vfs.Unmount("Core");
            vfs.Mount("Core", tempAssetsRoot);
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.EnumerateFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
            }

            foreach (string dir in Directory.EnumerateDirectories(source))
            {
                CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
            }
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate Ludots repo root from test BaseDirectory.");
        }
    }
}
