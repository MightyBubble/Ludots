using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Mathematics;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class LayeredSpanRuntimeZeroAllocContractTests
    {
        private static readonly NavTriangleSurfaceFlags FloorFlags =
            NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;

        [Test]
        public void ProcessBudgetInto_ReportsSeparateBakeAndCommitTicks_AndGenerationChecksum()
        {
            using var harness = CreateHarness(tileCountX: 2, tileCountZ: 2, dirtyTileCapacity: 16);
            var published = new RuntimeNavMeshRebuildPublishedTile[harness.Config.RuntimeIncremental.PublishedTileCapacity];
            var failures = new NavBakeResultEntry[harness.Config.RuntimeIncremental.StagedEntryCapacity];
            WarmupFullPath(harness, published, failures);

            harness.LiveObstacles.BeginCapture();
            harness.LiveObstacles.EndCaptureAndSort();
            harness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0));
            harness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(1, 0));

            RuntimeNavMeshRebuildBatchStats stats;
            long bakeSum = 0;
            long commitSum = 0;
            ulong checksum = 0;
            do
            {
                stats = harness.Queue.ProcessBudgetInto(
                    harness.Config.RuntimeIncremental.TileBudgetPerFixedTick,
                    published.AsSpan(),
                    failures.AsSpan());
                bakeSum += stats.BakeTicks;
                commitSum += stats.CommitTicks;
                if (stats.Committed)
                {
                    checksum = stats.GenerationChecksum;
                }
            }
            while (!stats.Committed && !stats.Aborted && harness.Queue.PendingTileCount + harness.Queue.SealedRemainingCount > 0);

            Assert.That(stats.Committed, Is.True);
            Assert.That(bakeSum, Is.GreaterThan(0L));
            Assert.That(commitSum, Is.GreaterThan(0L));
            Assert.That(checksum, Is.Not.EqualTo(0UL));
            Assert.That(stats.WorkerCount, Is.EqualTo(1));
            Assert.That(harness.Queue.DroppedDirtyCommandCount, Is.EqualTo(0));
            Assert.That(harness.Queue.CapacityGrowthCount, Is.EqualTo(0));
        }

        [Test]
        public void LayeredSpan_RuntimeTransaction_AfterWarmup_AllocatesExactlyZeroManagedBytes()
        {
            using var harness = CreateHarness(tileCountX: 2, tileCountZ: 2, dirtyTileCapacity: 16);
            var published = new RuntimeNavMeshRebuildPublishedTile[harness.Config.RuntimeIncremental.PublishedTileCapacity];
            var failures = new NavBakeResultEntry[harness.Config.RuntimeIncremental.StagedEntryCapacity];
            WarmupFullPath(harness, published, failures);

            // Restore an open mesh after warmup's obstacle pass so reachability is proven on a clear floor.
            harness.LiveObstacles.BeginCapture();
            harness.LiveObstacles.EndCaptureAndSort();
            for (int y = 0; y < harness.TileCountZ; y++)
            {
                for (int x = 0; x < harness.TileCountX; x++)
                {
                    harness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(x, y));
                }
            }

            Drain(harness, published, failures);

            var query = new NavQueryService(
                harness.Store,
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                new NavQueryTileSpace(0, 0, harness.TileWidthCm, harness.TileHeightCm));
            Assert.That(harness.Store.TryGet(new NavTileId(0, 0, 0), out NavTile openTile), Is.True);
            Assert.That(openTile.TriangleCount, Is.GreaterThan(0), "Open floor tile must have walkable triangles before the measured rebuild.");
            NavPathResult pathBefore = query.TryFindPath(50, 50, 350, 350);
            Assert.That(pathBefore.Status, Is.EqualTo(NavPathStatus.Ok), $"Open floor must remain reachable before the measured obstacle rebuild (status={pathBefore.Status}).");

            ulong generationBefore = harness.Store.Generation;
            ulong checksumBefore = RequireTileChecksum(harness.Store, 0, 0);

            // Large circle that genuinely severs the corridor across tile (0,0).
            harness.LiveObstacles.BeginCapture();
            int circle = harness.LiveObstacles.BeginPrimitive(1, 0, NavObstacleKind.Circle, 0, 200);
            harness.LiveObstacles.SetCircle(circle, centerXcm: 200, centerZcm: 200, radiusCm: 160);

            long before = GC.GetAllocatedBytesForCurrentThread();
            harness.LiveObstacles.EndCaptureAndSort();
            int dirty = harness.Queue.EnqueueDirtyAabb(
                new WorldAabbCm(40, 40, 320, 320),
                includeNeighbors: true);

            RuntimeNavMeshRebuildBatchStats stats;
            do
            {
                stats = harness.Queue.ProcessBudgetInto(
                    harness.Config.RuntimeIncremental.TileBudgetPerFixedTick,
                    published.AsSpan(),
                    failures.AsSpan());
            }
            while (!stats.Committed && !stats.Aborted && harness.Queue.PendingTileCount + harness.Queue.SealedRemainingCount > 0);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(dirty, Is.GreaterThan(0));
            Assert.That(stats.Aborted, Is.False);
            Assert.That(stats.Committed, Is.True);
            Assert.That(stats.Generation, Is.GreaterThan(generationBefore));
            Assert.That(harness.Store.Generation, Is.EqualTo(stats.Generation));
            ulong checksumAfter = RequireTileChecksum(harness.Store, 0, 0);
            Assert.That(checksumAfter, Is.Not.EqualTo(checksumBefore));
            Assert.That(allocated, Is.EqualTo(0L), $"Measured managed allocation was {allocated} bytes.");

            NavPathResult pathAfter = query.TryFindPath(50, 50, 350, 350);
            Assert.That(pathAfter.Status, Is.Not.EqualTo(NavPathStatus.Ok), "Blocking obstacle must change reachability, not only checksum.");
        }

        [Test]
        public void LayeredSpan_RuntimeCapacities_ExhaustionNamesOwnerAndRejectsPartialGeneration()
        {
            using (var dirtyHarness = CreateHarness(tileCountX: 2, tileCountZ: 2, dirtyTileCapacity: 1))
            {
                dirtyHarness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0));
                InvalidOperationException dirtyEx = Assert.Throws<InvalidOperationException>(
                    () => dirtyHarness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(1, 0)))!;
                Assert.That(dirtyEx.Message, Does.Contain("dirtyTileCapacity"));
                Assert.That(dirtyEx.Message, Does.Contain("required"));
            }

            using (var stagedHarness = CreateHarness(
                       tileCountX: 2,
                       tileCountZ: 2,
                       dirtyTileCapacity: 8,
                       mutate: cfg => cfg.StagedEntryCapacity = 1))
            {
                var published = AllocPublished(stagedHarness);
                var failures = AllocFailures(stagedHarness);
                ulong gen = stagedHarness.Store.Generation;
                ulong checksum = RequireOptionalChecksum(stagedHarness.Store, 0, 0);
                stagedHarness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0));
                stagedHarness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(1, 0));
                InvalidOperationException stagedEx = Assert.Throws<InvalidOperationException>(
                    () => stagedHarness.Queue.ProcessBudgetInto(8, published, failures))!;
                Assert.That(stagedEx.Message, Does.Contain("stagedEntryCapacity"));
                Assert.That(stagedEx.Message, Does.Contain("required"));
                Assert.That(stagedHarness.Store.Generation, Is.EqualTo(gen));
                Assert.That(RequireOptionalChecksum(stagedHarness.Store, 0, 0), Is.EqualTo(checksum));
            }

            using (var publishedHarness = CreateHarness(
                       tileCountX: 2,
                       tileCountZ: 2,
                       dirtyTileCapacity: 8,
                       mutate: cfg => cfg.PublishedTileCapacity = 1))
            {
                // Construction succeeds; commit must fail before store mutation when publish capacity is too small.
                var published = AllocPublished(publishedHarness);
                var failures = AllocFailures(publishedHarness);
                publishedHarness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0));
                publishedHarness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(1, 0));
                ulong gen = publishedHarness.Store.Generation;
                InvalidOperationException publishedEx = Assert.Throws<InvalidOperationException>(
                    () => publishedHarness.Queue.ProcessBudgetInto(8, published, failures))!;
                Assert.That(publishedEx.Message, Does.Contain("publishedTileCapacity"));
                Assert.That(publishedEx.Message, Does.Contain("required"));
                Assert.That(publishedHarness.Store.Generation, Is.EqualTo(gen));
                Assert.That(publishedHarness.Store.Revision, Is.EqualTo(0u));
            }

            using (var residentHarness = CreateHarness(
                       tileCountX: 2,
                       tileCountZ: 2,
                       dirtyTileCapacity: 8,
                       mutate: cfg => cfg.ResidentTileCapacity = 1))
            {
                var published = AllocPublished(residentHarness);
                var failures = AllocFailures(residentHarness);
                residentHarness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0));
                Drain(residentHarness, published, failures);
                ulong gen = residentHarness.Store.Generation;
                ulong checksum = RequireTileChecksum(residentHarness.Store, 0, 0);
                uint revision = residentHarness.Store.Revision;
                var oversizedWindow = new[]
                {
                    new NavBakeTileCoord(0, 0),
                    new NavBakeTileCoord(1, 0)
                };
                InvalidOperationException residentEx = Assert.Throws<InvalidOperationException>(
                    () => residentHarness.Queue.RequestResidentWindowTransition(oversizedWindow))!;
                Assert.That(residentEx.Message, Does.Contain("residentTileCapacity"));
                Assert.That(residentEx.Message, Does.Contain("2"));
                Assert.That(residentHarness.Store.Generation, Is.EqualTo(gen));
                Assert.That(residentHarness.Store.Revision, Is.EqualTo(revision));
                Assert.That(RequireTileChecksum(residentHarness.Store, 0, 0), Is.EqualTo(checksum));
            }

            using (var vertexHarness = CreateHarness(
                       tileCountX: 2,
                       tileCountZ: 2,
                       dirtyTileCapacity: 4,
                       mutate: cfg =>
                       {
                           cfg.OutputVertexCapacity = 1;
                           cfg.OutputTriangleCapacity = Math.Max(cfg.OutputTriangleCapacity, 64);
                           cfg.OutputPortalCapacity = Math.Max(cfg.OutputPortalCapacity, 16);
                       }))
            {
                var published = AllocPublished(vertexHarness);
                var failures = AllocFailures(vertexHarness);
                ulong gen = vertexHarness.Store.Generation;
                vertexHarness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0));
                InvalidOperationException vertexEx = Assert.Throws<InvalidOperationException>(
                    () => vertexHarness.Queue.ProcessBudgetInto(4, published, failures))!;
                Assert.That(vertexEx.Message, Does.Contain("outputVertexCapacity"));
                Assert.That(vertexEx.Message, Does.Contain("required"));
                Assert.That(vertexHarness.Store.Generation, Is.EqualTo(gen));
            }

            using (var triangleHarness = CreateHarness(
                       tileCountX: 2,
                       tileCountZ: 2,
                       dirtyTileCapacity: 4,
                       mutate: cfg =>
                       {
                           cfg.OutputVertexCapacity = Math.Max(cfg.OutputVertexCapacity, 64);
                           cfg.OutputTriangleCapacity = 1;
                           cfg.OutputPortalCapacity = Math.Max(cfg.OutputPortalCapacity, 16);
                       }))
            {
                var published = AllocPublished(triangleHarness);
                var failures = AllocFailures(triangleHarness);
                ulong gen = triangleHarness.Store.Generation;
                triangleHarness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0));
                InvalidOperationException triangleEx = Assert.Throws<InvalidOperationException>(
                    () => triangleHarness.Queue.ProcessBudgetInto(4, published, failures))!;
                Assert.That(triangleEx.Message, Does.Contain("outputTriangleCapacity"));
                Assert.That(triangleEx.Message, Does.Contain("required"));
                Assert.That(triangleHarness.Store.Generation, Is.EqualTo(gen));
            }

            using (var portalHarness = CreateHarness(
                       tileCountX: 2,
                       tileCountZ: 2,
                       dirtyTileCapacity: 4))
            {
                // Prove outputPortalCapacity is enforced before mutation (store preflight).
                var tinyPortalStore = new NavTileStore(
                    _ => throw new InvalidOperationException("unused"),
                    residentTileCapacity: 8,
                    outputVertexCapacity: portalHarness.Store.OutputVertexCapacity,
                    outputTriangleCapacity: portalHarness.Store.OutputTriangleCapacity,
                    outputPortalCapacity: 1);
                NavTile rich = NavTile.CreateBanked(
                    portalHarness.Store.OutputVertexCapacity,
                    portalHarness.Store.OutputTriangleCapacity,
                    portalCapacity: 4);
                rich.AssignHeader(new NavTileId(0, 0, 0), 1, 1UL, 0, 0);
                rich.SetCounts(0, 0, 2);
                rich.Portals[0] = new NavBorderPortal(NavPortalSide.East, 1, 0, 1, 1, 1, 0, 0, 1, 0, 1, 30);
                rich.Portals[1] = new NavBorderPortal(NavPortalSide.West, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 30);
                Span<byte> scratch = stackalloc byte[NavTileBinary.GetSerializedSize(rich)];
                NavTileBinary.AssignChecksum(rich, scratch);

                ulong gen = tinyPortalStore.Generation;
                uint rev = tinyPortalStore.Revision;
                InvalidOperationException portalEx = Assert.Throws<InvalidOperationException>(
                    () => tinyPortalStore.Replace(rich))!;
                Assert.That(portalEx.Message, Does.Contain("outputPortalCapacity"));
                Assert.That(portalEx.Message, Does.Contain("required"));
                Assert.That(tinyPortalStore.Generation, Is.EqualTo(gen));
                Assert.That(tinyPortalStore.Revision, Is.EqualTo(rev));
                Assert.That(tinyPortalStore.TryGet(rich.TileId, out _), Is.False);
            }

            using (var callerSpanHarness = CreateHarness(tileCountX: 2, tileCountZ: 2, dirtyTileCapacity: 4))
            {
                var tinyPublished = new RuntimeNavMeshRebuildPublishedTile[1];
                var failures = AllocFailures(callerSpanHarness);
                ulong gen = callerSpanHarness.Store.Generation;
                callerSpanHarness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0));
                InvalidOperationException publishedSpanEx = Assert.Throws<InvalidOperationException>(
                    () => callerSpanHarness.Queue.ProcessBudgetInto(4, tinyPublished, failures))!;
                Assert.That(publishedSpanEx.Message, Does.Contain("publishedOut"));
                Assert.That(publishedSpanEx.Message, Does.Contain("publishedTileCapacity"));
                Assert.That(callerSpanHarness.Store.Generation, Is.EqualTo(gen));

                var published = AllocPublished(callerSpanHarness);
                var tinyFailures = new NavBakeResultEntry[1];
                InvalidOperationException failureSpanEx = Assert.Throws<InvalidOperationException>(
                    () => callerSpanHarness.Queue.ProcessBudgetInto(4, published, tinyFailures))!;
                Assert.That(failureSpanEx.Message, Does.Contain("failuresOut"));
                Assert.That(failureSpanEx.Message, Does.Contain("stagedEntryCapacity"));
                Assert.That(callerSpanHarness.Store.Generation, Is.EqualTo(gen));
            }

            // storeGroupCapacity too small for two layer stores must fail at construction (before any mutation).
            InvalidOperationException storeGroupEx = Assert.Throws<InvalidOperationException>(
                () =>
                {
                    using var _ = CreateHarness(
                        tileCountX: 1,
                        tileCountZ: 1,
                        dirtyTileCapacity: 4,
                        mutate: cfg => cfg.StoreGroupCapacity = 1,
                        extraLayers: true);
                })!;
            Assert.That(storeGroupEx.Message, Does.Contain("storeGroupCapacity"));
            Assert.That(storeGroupEx.Message, Does.Contain("required"));
        }

        [Test]
        public void NavTileStore_AtomicMultiStore_SecondStoreCapacityFailureLeavesFirstUntouched()
        {
            NavLayeredSpanConfig layered = CreateLayeredConfig(scratchSlotCount: 2);
            NavMeshBakeConfig config = CreateConfig(layered);
            var pool = new LayeredSpanScratchPool(layered);
            var algorithm = new LayeredSpanNavBakeAlgorithm(pool);
            var service = new NavBakeService(algorithm);
            NavTriangleSurfaceTileIndex surface = CreateFlatSurface(2, 2, layered);
            var context = new NavBakeContext
            {
                MapId = "layered_span_two_store_capacity",
                SourceUri = "Core:Maps/layered_span_two_store_capacity.tris",
                TriangleSurface = surface,
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.LayeredSpan,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };
            NavBakeResult bake = service.Bake(context);
            Assert.That(bake.FailureCount, Is.EqualTo(0), bake.Entries[0].Artifact.Message);
            NavTile tile = bake.Entries[0].Tile;
            Assert.That(tile.VertexCount, Is.GreaterThan(1));
            Assert.That(tile.TriangleCount, Is.GreaterThan(0));

            var storeA = new NavTileStore(
                _ => throw new InvalidOperationException("unused"),
                residentTileCapacity: 8,
                outputVertexCapacity: Math.Max(256, tile.VertexCount),
                outputTriangleCapacity: Math.Max(512, tile.TriangleCount),
                outputPortalCapacity: Math.Max(64, tile.PortalCount));
            storeA.ReplaceGenerationBatch(1UL, new[] { tile });
            ulong genA = storeA.Generation;
            uint revA = storeA.Revision;
            ulong checksumA = RequireTileChecksum(storeA, 0, 0);

            var storeB = new NavTileStore(
                _ => throw new InvalidOperationException("unused"),
                residentTileCapacity: 8,
                outputVertexCapacity: 1,
                outputTriangleCapacity: Math.Max(512, tile.TriangleCount),
                outputPortalCapacity: Math.Max(64, tile.PortalCount));

            NavTile layer1 = NavTile.CreateBanked(
                Math.Max(256, tile.VertexCount),
                Math.Max(512, tile.TriangleCount),
                Math.Max(64, tile.PortalCount));
            layer1.CopyGeometryFrom(tile);
            layer1.AssignHeader(new NavTileId(0, 0, 1), tile.TileVersion, tile.BuildConfigHash, tile.OriginXcm, tile.OriginZcm);
            Span<byte> scratch = stackalloc byte[NavTileBinary.GetSerializedSize(layer1)];
            NavTileBinary.AssignChecksum(layer1, scratch);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => NavTileStore.ReplaceGenerationBatchesAtomically(
                    new[] { storeA, storeB },
                    new IReadOnlyList<NavTile>[]
                    {
                        new[] { tile },
                        new[] { layer1 }
                    },
                    out _))!;
            Assert.That(ex.Message, Does.Contain("outputVertexCapacity"));
            Assert.That(ex.Message, Does.Contain("required"));
            Assert.That(storeA.Generation, Is.EqualTo(genA));
            Assert.That(storeA.Revision, Is.EqualTo(revA));
            Assert.That(RequireTileChecksum(storeA, 0, 0), Is.EqualTo(checksumA));
            Assert.That(storeB.Generation, Is.EqualTo(0UL));
            Assert.That(storeB.Revision, Is.EqualTo(0u));
            Assert.That(storeB.TryGet(new NavTileId(0, 0, 1), out _), Is.False);
        }

        [Test]
        public void LayeredSpan_DirtyAabb_On64x64_VisitsOnlyLocalCandidates()
        {
            using var harness = CreateHarness(tileCountX: 64, tileCountZ: 64, dirtyTileCapacity: 64);
            int dirty = harness.Queue.EnqueueDirtyAabb(
                new WorldAabbCm(harness.TileWidthCm, harness.TileHeightCm, harness.TileWidthCm / 2, harness.TileHeightCm / 2),
                includeNeighbors: true);
            Assert.That(dirty, Is.GreaterThan(0));
            Assert.That(dirty, Is.LessThanOrEqualTo(9));
            Assert.That(harness.Queue.LastDirtyVisitedCandidateCount, Is.EqualTo(dirty));
            Assert.That(harness.Queue.LastDirtyVisitedCandidateCount, Is.LessThan(64));
            Assert.That(harness.Queue.LastDirtyVisitedCandidateCount, Is.LessThan(4096));
        }

        [Test]
        public void LayeredSpan_OneWorkerAndConfiguredWorkers_MatchTileChecksums()
        {
            ulong oneWorker = BakeChecksumWithWorkers(maxDegree: 1);
            ulong configured = BakeChecksumWithWorkers(maxDegree: 2);
            Assert.That(configured, Is.EqualTo(oneWorker));

            ulong runA = BakeRuntimeObstacleChecksum();
            ulong runB = BakeRuntimeObstacleChecksum();
            Assert.That(runB, Is.EqualTo(runA));
        }

        private static ulong BakeRuntimeObstacleChecksum()
        {
            using var harness = CreateHarness(tileCountX: 2, tileCountZ: 1, dirtyTileCapacity: 8);
            var published = new RuntimeNavMeshRebuildPublishedTile[harness.Config.RuntimeIncremental.PublishedTileCapacity];
            var failures = new NavBakeResultEntry[harness.Config.RuntimeIncremental.StagedEntryCapacity];
            WarmupFullPath(harness, published, failures);
            return RunObstacleChangeAndChecksum(harness, published, failures, entityId: 9);
        }

        private static ulong BakeChecksumWithWorkers(int maxDegree)
        {
            NavLayeredSpanConfig layered = CreateLayeredConfig(scratchSlotCount: Math.Max(2, maxDegree));
            NavMeshBakeConfig config = CreateConfig(layered);
            var pool = new LayeredSpanScratchPool(layered);
            var algorithm = new LayeredSpanNavBakeAlgorithm(pool);
            var service = new NavBakeService(algorithm);
            NavTriangleSurfaceTileIndex surface = CreateFlatSurface(2, 1, layered);
            var context = new NavBakeContext
            {
                MapId = "layered_span_worker_determinism",
                SourceUri = "Core:Maps/layered_span_worker_determinism.tris",
                TriangleSurface = surface,
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0), new NavBakeTileCoord(1, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.LayeredSpan,
                Execution = new NavBakeExecutionOptions
                {
                    Parallel = maxDegree > 1,
                    MaxDegreeOfParallelism = maxDegree
                }
            };

            NavBakeResult result = service.Bake(context);
            Assert.That(result.FailureCount, Is.EqualTo(0));
            ulong mix = 1469598103934665603UL;
            NavBakeResultEntry[] ordered = new NavBakeResultEntry[result.Entries.Count];
            for (int i = 0; i < result.Entries.Count; i++) ordered[i] = result.Entries[i];
            Array.Sort(ordered, static (a, b) =>
            {
                int y = a.Target.ChunkY.CompareTo(b.Target.ChunkY);
                if (y != 0) return y;
                return a.Target.ChunkX.CompareTo(b.Target.ChunkX);
            });
            for (int i = 0; i < ordered.Length; i++)
            {
                NavTile tile = ordered[i].Tile;
                mix ^= tile.Checksum;
                mix *= 1099511628211UL;
                mix ^= (ulong)(uint)tile.TileId.ChunkX;
                mix *= 1099511628211UL;
                mix ^= (ulong)(uint)tile.TileId.ChunkY;
                mix *= 1099511628211UL;
            }

            return mix;
        }

        private static void WarmupFullPath(
            Harness harness,
            RuntimeNavMeshRebuildPublishedTile[] published,
            NavBakeResultEntry[] failures)
        {
            for (int y = 0; y < harness.TileCountZ; y++)
            {
                for (int x = 0; x < harness.TileCountX; x++)
                {
                    harness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(x, y));
                }
            }

            Drain(harness, published, failures);
            // Warm the obstacle-overlay + blocked-geometry path before measuring.
            harness.LiveObstacles.BeginCapture();
            int warmCircle = harness.LiveObstacles.BeginPrimitive(99, 0, NavObstacleKind.Circle, 0, 200);
            harness.LiveObstacles.SetCircle(warmCircle, centerXcm: 200, centerZcm: 200, radiusCm: 80);
            harness.LiveObstacles.EndCaptureAndSort();
            harness.Queue.EnqueueDirtyAabb(new WorldAabbCm(40, 40, 320, 320), includeNeighbors: true);
            Drain(harness, published, failures);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.GetAllocatedBytesForCurrentThread();
        }

        private static ulong RunObstacleChangeAndChecksum(
            Harness harness,
            RuntimeNavMeshRebuildPublishedTile[] published,
            NavBakeResultEntry[] failures,
            int entityId)
        {
            harness.LiveObstacles.BeginCapture();
            int circle = harness.LiveObstacles.BeginPrimitive(entityId, 0, NavObstacleKind.Circle, 0, 200);
            harness.LiveObstacles.SetCircle(circle, 200, 200, 80);
            harness.LiveObstacles.EndCaptureAndSort();
            harness.Queue.EnqueueDirtyAabb(new WorldAabbCm(40, 40, 320, 320), includeNeighbors: false);
            Drain(harness, published, failures);
            return RequireTileChecksum(harness.Store, 0, 0);
        }

        private static void Drain(
            Harness harness,
            RuntimeNavMeshRebuildPublishedTile[] published,
            NavBakeResultEntry[] failures)
        {
            for (int guard = 0; guard < 64; guard++)
            {
                if (harness.Queue.PendingTileCount == 0 && harness.Queue.SealedRemainingCount == 0)
                {
                    return;
                }

                RuntimeNavMeshRebuildBatchStats stats = harness.Queue.ProcessBudgetInto(
                    Math.Max(1, harness.Config.RuntimeIncremental.TileBudgetPerFixedTick),
                    published.AsSpan(),
                    failures.AsSpan());
                if (stats.Aborted)
                {
                    throw new InvalidOperationException(failures[0].Artifact.Message);
                }

                if (stats.Committed && harness.Queue.PendingTileCount == 0 && harness.Queue.SealedRemainingCount == 0)
                {
                    return;
                }
            }

            throw new InvalidOperationException("Drain exceeded budget iterations.");
        }

        private static ulong RequireTileChecksum(NavTileStore store, int cx, int cy)
        {
            Assert.That(store.TryGet(new NavTileId(cx, cy, 0), out NavTile tile), Is.True);
            return tile.Checksum;
        }

        private static ulong RequireOptionalChecksum(NavTileStore store, int cx, int cy)
        {
            return store.TryGet(new NavTileId(cx, cy, 0), out NavTile tile) ? tile.Checksum : 0UL;
        }

        private static RuntimeNavMeshRebuildPublishedTile[] AllocPublished(Harness harness) =>
            new RuntimeNavMeshRebuildPublishedTile[harness.Config.RuntimeIncremental.PublishedTileCapacity];

        private static NavBakeResultEntry[] AllocFailures(Harness harness) =>
            new NavBakeResultEntry[harness.Config.RuntimeIncremental.StagedEntryCapacity];

        private static Harness CreateHarness(
            int tileCountX,
            int tileCountZ,
            int dirtyTileCapacity,
            Action<NavRuntimeIncrementalConfig>? mutate = null,
            bool extraLayers = false)
        {
            NavLayeredSpanConfig layered = CreateLayeredConfig(scratchSlotCount: 2);
            NavMeshBakeConfig config = CreateConfig(layered);
            if (extraLayers)
            {
                config.Layers = new List<NavLayerConfig>
                {
                    new NavLayerConfig { Id = "Ground", Layer = 0 },
                    new NavLayerConfig { Id = "Bridge", Layer = 1 }
                };
            }

            config.RuntimeIncremental.DirtyTileCapacity = dirtyTileCapacity;
            config.RuntimeIncremental.TileBudgetPerFixedTick = 4;
            mutate?.Invoke(config.RuntimeIncremental);

            var pool = new LayeredSpanScratchPool(layered);
            var algorithm = new LayeredSpanNavBakeAlgorithm(pool);
            var service = new NavBakeService(algorithm);
            NavTriangleSurfaceTileIndex surface = CreateFlatSurface(tileCountX, tileCountZ, layered);
            int tileWidthCm = surface.Grid.TileWidthCm;
            int tileHeightCm = surface.Grid.TileHeightCm;
            var live = new RuntimeNavObstacleSnapshot(
                config.RuntimeIncremental.ObstaclePrimitiveCapacity,
                config.RuntimeIncremental.PolygonVertexCapacity,
                "Ground");
            var probe = live.CreateCompatibleEmpty();
            var context = new NavBakeContext
            {
                MapId = "layered_span_runtime_zero_alloc",
                SourceUri = "Core:Maps/layered_span_runtime_zero_alloc.tris",
                TriangleSurface = surface,
                Obstacles = live,
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 1,
                Mode = NavBakeMode.RuntimeIncremental,
                Algorithm = NavBakeAlgorithmKind.LayeredSpan,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };

            var store = new NavTileStore(
                _ => throw new InvalidOperationException("LayeredSpan runtime zero-alloc test publishes before disk load."),
                config.RuntimeIncremental);
            var profiles = new NavMeshProfileRegistry(config, CreateAgentProfiles());
            var registryMap = new Dictionary<NavQueryServiceKey, NavTileStore>
            {
                [new NavQueryServiceKey(0, 0)] = store
            };
            NavTileStore? bridgeStore = null;
            if (extraLayers)
            {
                bridgeStore = new NavTileStore(
                    _ => throw new InvalidOperationException("LayeredSpan runtime zero-alloc test publishes before disk load."),
                    config.RuntimeIncremental);
                registryMap[new NavQueryServiceKey(1, 0)] = bridgeStore;
            }

            var registry = new NavQueryServiceRegistry(registryMap, NavQueryTileSpace.FromGrid(surface.Grid));
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(service, context, registry, profiles);
            return new Harness(
                config,
                live,
                probe,
                store,
                queue,
                pool,
                tileCountX,
                tileCountZ,
                tileWidthCm,
                tileHeightCm,
                bridgeStore);
        }

        private static NavTriangleSurfaceTileIndex CreateFlatSurface(
            int tileCountX,
            int tileCountZ,
            NavLayeredSpanConfig layered)
        {
            int cell = layered.RasterCellSizeCm;
            // Bake/query harnesses: one axis-aligned quad per tile (matches Adapter_FlatFloor).
            // Large dirty-AABB maps only need grid math, so keep a single spanning quad.
            bool perTileQuads = checked(tileCountX * tileCountZ) <= 16;
            int cellsPerTile = 4;
            int tileWidthCm = perTileQuads ? checked(cellsPerTile * cell) : cell;
            int tileHeightCm = tileWidthCm;

            NavTriangleSurfaceSnapshot surface;
            if (perTileQuads)
            {
                int tileCount = checked(tileCountX * tileCountZ);
                int vertCount = checked(tileCount * 4);
                int triCount = checked(tileCount * 2);
                var vx = new int[vertCount];
                var vy = new int[vertCount];
                var vz = new int[vertCount];
                var ta = new int[triCount];
                var tb = new int[triCount];
                var tc = new int[triCount];
                var areas = new byte[triCount];
                var stables = new int[triCount];
                var flags = new NavTriangleSurfaceFlags[triCount];

                int v = 0;
                int t = 0;
                int stable = 1;
                for (int tz = 0; tz < tileCountZ; tz++)
                {
                    for (int tx = 0; tx < tileCountX; tx++)
                    {
                        int minX = checked(tx * tileWidthCm);
                        int maxX = checked(minX + tileWidthCm);
                        int minZ = checked(tz * tileHeightCm);
                        int maxZ = checked(minZ + tileHeightCm);
                        int v0 = v++;
                        int v1 = v++;
                        int v2 = v++;
                        int v3 = v++;
                        vx[v0] = minX; vy[v0] = 0; vz[v0] = minZ;
                        vx[v1] = maxX; vy[v1] = 0; vz[v1] = minZ;
                        vx[v2] = minX; vy[v2] = 0; vz[v2] = maxZ;
                        vx[v3] = maxX; vy[v3] = 0; vz[v3] = maxZ;

                        ta[t] = v0; tb[t] = v1; tc[t] = v2;
                        areas[t] = 0; stables[t] = stable++; flags[t] = FloorFlags; t++;
                        ta[t] = v1; tb[t] = v3; tc[t] = v2;
                        areas[t] = 0; stables[t] = stable++; flags[t] = FloorFlags; t++;
                    }
                }

                surface = new NavTriangleSurfaceSnapshot(vx, vy, vz, ta, tb, tc, areas, stables, flags);
            }
            else
            {
                int widthCm = checked(tileCountX * tileWidthCm);
                int heightCm = checked(tileCountZ * tileHeightCm);
                surface = new NavTriangleSurfaceSnapshot(
                    vertexXcm: new[] { 0, widthCm, 0, widthCm },
                    vertexYcm: new[] { 0, 0, 0, 0 },
                    vertexZcm: new[] { 0, 0, heightCm, heightCm },
                    triA: new[] { 0, 1 },
                    triB: new[] { 1, 3 },
                    triC: new[] { 2, 2 },
                    triAreaIds: new byte[] { 0, 0 },
                    triStableIds: new[] { 1, 2 },
                    triFlags: new[] { FloorFlags, FloorFlags });
            }

            var grid = new NavTriangleSurfaceTileGrid(
                originXcm: 0,
                originZcm: 0,
                tileWidthCm,
                tileHeightCm,
                tileCountX,
                tileCountZ,
                haloPaddingCm: checked(layered.RasterHaloCells * cell));
            return NavTriangleSurfaceTileIndex.Build(surface, grid);
        }

        private static NavMeshBakeConfig CreateConfig(NavLayeredSpanConfig layered)
        {
            return new NavMeshBakeConfig
            {
                Mode = NavBakeNames.ModeRuntimeIncremental,
                Algorithm = NavBakeNames.AlgorithmLayeredSpan,
                Profiles = new List<NavMeshAgentProfileConfig>
                {
                    new NavMeshAgentProfileConfig { Id = "Small", MaxClimbCm = 40, MaxSlopeDeg = 45 }
                },
                Layers = new List<NavLayerConfig>
                {
                    new NavLayerConfig { Id = "Ground", Layer = 0 }
                },
                Areas = new List<NavAreaCostConfig>(),
                RuntimeIncremental = new NavRuntimeIncrementalConfig
                {
                    TileBudgetPerFixedTick = 1,
                    IncludeNeighborTiles = true,
                    HeightScaleMeters = 1f,
                    MinWalkableUpDot = 0.6f,
                    CliffHeightThreshold = 1,
                    TrackedStructuralEntityCapacity = 32,
                    ObstaclePrimitiveCapacity = 64,
                    PolygonVertexCapacity = 512,
                    DirtyTileCapacity = 64,
                    StagedEntryCapacity = 64,
                    PublishedTileCapacity = 64,
                    StoreGroupCapacity = 8,
                    ResidentTileCapacity = 128,
                    OutputVertexCapacity = Math.Max(256, layered.TriangulationVertexCapacity),
                    OutputTriangleCapacity = Math.Max(512, layered.TriangulationTriangleCapacity),
                    OutputPortalCapacity = Math.Max(64, layered.BorderPortalCapacity),
                    InitialResidentChunkX = 0,
                    InitialResidentChunkZ = 0,
                    InitialResidentWidthChunks = 1,
                    InitialResidentHeightChunks = 1
                },
                LayeredSpan = layered,
                TriangleSurface = new NavTriangleSurfaceConfig
                {
                    HaloPaddingCm = checked(layered.RasterHaloCells * layered.RasterCellSizeCm)
                },
                Recast = new NavRecastConfig { RasterCellSizeCm = 10, RasterCellHeightCm = 5 }
            };
        }

        private static NavLayeredSpanConfig CreateLayeredConfig(int scratchSlotCount)
        {
            return new NavLayeredSpanConfig
            {
                ScratchSlotCount = scratchSlotCount,
                RasterCellSizeCm = 100,
                RasterHaloCells = 0,
                SameSurfaceToleranceCm = 5,
                MaxSimplificationErrorCm = 0,
                HeightRounding = NavLayeredSpanConfig.HeightRoundingRoundHalfAwayFromZero,
                MaxLawsonFlipCount = 100_000,
                ColumnCapacity = 256,
                SpanCapacity = 512,
                ClassifiedSpanCapacity = 512,
                WalkableSpanCapacity = 512,
                LinkCapacity = 2048,
                SheetCapacity = 512,
                PortalIntervalCapacity = 2048,
                RegionCapacity = 256,
                ChartCapacity = 64,
                RingCapacity = 64,
                ContourVertexCapacity = 1024,
                ContourEdgeCapacity = 1024,
                SeamCapacity = 256,
                CanonicalLinkCapacity = 2048,
                SplitPointCapacity = 256,
                TriangulationVertexCapacity = 1024,
                TriangulationTriangleCapacity = 2048,
                ConstrainedEdgeCapacity = 2048,
                BorderPortalCapacity = 128,
                PolygonVertexCapacity = 1024,
                AdjacencyEdgeCapacity = 6144,
                BridgeCandidateCapacity = 1024,
                RingWorkCapacity = 128,
                TemporaryConstraintFlagCapacity = 2048
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

        private sealed class Harness : IDisposable
        {
            public Harness(
                NavMeshBakeConfig config,
                RuntimeNavObstacleSnapshot liveObstacles,
                RuntimeNavObstacleSnapshot pinnedObstaclesProbe,
                NavTileStore store,
                RuntimeIncrementalNavMeshRebuildQueue queue,
                LayeredSpanScratchPool pool,
                int tileCountX,
                int tileCountZ,
                int tileWidthCm,
                int tileHeightCm,
                NavTileStore? bridgeStore = null)
            {
                Config = config;
                LiveObstacles = liveObstacles;
                PinnedObstaclesProbe = pinnedObstaclesProbe;
                Store = store;
                Queue = queue;
                _pool = pool;
                TileCountX = tileCountX;
                TileCountZ = tileCountZ;
                TileWidthCm = tileWidthCm;
                TileHeightCm = tileHeightCm;
                BridgeStore = bridgeStore;
            }

            public NavMeshBakeConfig Config { get; }
            public RuntimeNavObstacleSnapshot LiveObstacles { get; }
            public RuntimeNavObstacleSnapshot PinnedObstaclesProbe { get; }
            public NavTileStore Store { get; }
            public NavTileStore? BridgeStore { get; }
            public RuntimeIncrementalNavMeshRebuildQueue Queue { get; }
            public int TileCountX { get; }
            public int TileCountZ { get; }
            public int TileWidthCm { get; }
            public int TileHeightCm { get; }
            private readonly LayeredSpanScratchPool _pool;

            public void Dispose()
            {
                // Scratch pool has no Dispose; keep reference to prevent premature GC during measurement.
                GC.KeepAlive(_pool);
                GC.KeepAlive(BridgeStore);
            }
        }
    }
}
