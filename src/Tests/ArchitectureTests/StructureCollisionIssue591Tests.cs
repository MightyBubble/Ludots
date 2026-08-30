using System;
using System.IO;
using System.Numerics;
using System.Text;
using Ludots.Core.Config;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.StructureCollision;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class StructureCollisionIssue591Tests
    {
        private const string FixtureRelativePath = "assets/StructureCollision/issue591_structure_collision.scoll.json";
        private const int GroundLayer = 0;
        private const int BridgeLayer = 1;
        private const uint InfantryMask = 1;
        private const uint MountedMask = 2;
        private const uint AllAgentsMask = 3;

        [Test]
        public void StructureCollisionAsset_LoadsCookedChunkedSoaContract()
        {
            StructureCollisionAsset asset = LoadFixtureAsset();

            Assert.That(asset.Header.Version, Is.EqualTo(1));
            Assert.That(asset.Header.Revision, Is.EqualTo(591));
            Assert.That(asset.SurfaceCount, Is.EqualTo(4));
            Assert.That(asset.ShapeCount, Is.EqualTo(4));
            Assert.That(asset.ChunkCount, Is.EqualTo(16));
            Assert.That(asset.ChunkSurfaceIndices.Length, Is.GreaterThanOrEqualTo(asset.SurfaceCount));
            Assert.That(asset.ChunkBlockerIndices.Length, Is.EqualTo(1));
            Assert.That(asset.ChunkPortalIndices.Length, Is.EqualTo(0));
            Assert.That(asset.TryGetSurfaceIndexById(101, out int bridgeSurface), Is.True);
            Assert.That(asset.GetPrimaryChunkForSurface(bridgeSurface), Is.EqualTo(asset.GetChunkIndex(1, 1)));
        }

        [Test]
        public void MapStructureCollisionLoader_FailsFastOnlyWhenStructureAwareMapDeclaresTheNeed()
        {
            var terrainOnly = new MapConfig { Id = "terrain_only" };
            Assert.That(MapStructureCollisionLoader.ResolveDeclaredAssetPath(terrainOnly), Is.Null);

            var missing = new MapConfig
            {
                Id = "structure_aware_missing",
                StructureAwareGrounding = true
            };
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => MapStructureCollisionLoader.ResolveDeclaredAssetPath(missing))!;
            Assert.That(ex.Message, Does.Contain("structure-aware grounding or navigation"));

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", FindRepoRoot());
            var declared = new MapConfig
            {
                Id = "structure_aware_ok",
                StructureAwareGrounding = true,
                StructureCollisionAsset = FixtureRelativePath
            };
            StructureCollisionAsset loaded = MapStructureCollisionLoader.Load(vfs, Array.Empty<string>(), declared)!;
            Assert.That(loaded.SurfaceCount, Is.EqualTo(4));
        }

        [Test]
        public void StructureCollisionAssetLoader_RejectsUnknownShapeLayerAndAgentMask()
        {
            AssertLoadFailsAfterReplacing("\"shapeId\": \"bridge_deck_shape\"", "\"shapeId\": \"missing_shape\"", "unknown shape");
            AssertLoadFailsAfterReplacing("\"kind\": \"Deck\"", "\"kind\": \"MissingSurfaceKind\"", "unknown kind");
            AssertLoadFailsAfterReplacing("\"layerId\": \"bridge\"", "\"layerId\": \"missing_layer\"", "unknown layer id");
            AssertLoadFailsAfterReplacing("\"agentMaskId\": \"all\"", "\"agentMaskId\": \"missing_agent\"", "unknown agent mask");
        }

        [Test]
        public void StructureCollisionAssetLoader_RejectsNumericEnumAndFlagStrings()
        {
            AssertLoadFailsAfterReplacing("\"kind\": \"WalkablePolygon\"", "\"kind\": \"99\"", "unknown kind");
            AssertLoadFailsAfterReplacing("\"kind\": \"Deck\"", "\"kind\": \"99\"", "unknown kind");
            AssertLoadFailsAfterReplacing("\"PickingGround\"", "\"128\"", "unknown flag");
        }

        [Test]
        public void StructureCollisionAssetLoader_RejectsMissingOrInvalidHeaderFieldsInsteadOfDefaulting()
        {
            AssertLoadFailsAfterRemovingLine("  \"version\": 1,", "requires positive version");
            AssertLoadFailsAfterRemovingLine("  \"coordinateScale\": 1,", "requires coordinateScale");
            AssertLoadFailsAfterReplacing("\"coordinateScale\": 1", "\"coordinateScale\": 0", "requires positive coordinateScale");
        }

        [Test]
        public void StructureCollisionAsset_RejectsOutOfRangeChunkSpan()
        {
            StructureCollisionAsset asset = LoadFixtureAsset();
            StructureChunkIndexEntry[] chunks = (StructureChunkIndexEntry[])asset.Chunks.Clone();
            int bridgeChunk = asset.GetChunkIndex(1, 1);
            StructureChunkIndexEntry original = chunks[bridgeChunk];
            chunks[bridgeChunk] = new StructureChunkIndexEntry(
                asset.ChunkSurfaceIndices.Length,
                surfaceCount: 2,
                original.BlockerStart,
                original.BlockerCount,
                original.PortalStart,
                original.PortalCount);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                new StructureCollisionAsset(
                    asset.Header,
                    asset.Layers,
                    asset.AgentMasks,
                    asset.Surfaces,
                    asset.Shapes,
                    chunks,
                    asset.ChunkSurfaceIndices,
                    asset.ChunkBlockerIndices,
                    asset.ChunkPortalIndices,
                    asset.SurfaceChunkStart,
                    asset.SurfaceChunkCount,
                    asset.SurfaceChunkIndices,
                    asset.ChunkColumns,
                    asset.ChunkRows))!;
            Assert.That(ex.Message, Does.Contain("out of range"));
        }

        [Test]
        public void StructureCollisionAsset_UsesHalfOpenShapeBoundsAtChunkBoundary()
        {
            StructureCollisionAsset asset = CreateBoundaryAsset();
            Assert.That(asset.TryGetSurfaceIndexById(201, out int surfaceIndex), Is.True);
            Assert.That(asset.TryGetChunkIndex(999.5f, 500f, out int leftChunk), Is.True);
            Assert.That(leftChunk, Is.EqualTo(asset.GetChunkIndex(0, 0)));
            Assert.That(asset.TryGetChunkIndex(1000f, 500f, out int rightChunk), Is.True);
            Assert.That(rightChunk, Is.EqualTo(asset.GetChunkIndex(1, 0)));
            Assert.That(asset.SurfaceChunkCount[surfaceIndex], Is.EqualTo(1));
            Assert.That(asset.SurfaceChunkIndices[asset.SurfaceChunkStart[surfaceIndex]], Is.EqualTo(leftChunk));

            Assert.That(asset.TryEvaluateSurfaceHeight(surfaceIndex, 999.5f, 500f, out float insideHeight), Is.True);
            Assert.That(insideHeight, Is.EqualTo(250f).Within(0.001f));
            Assert.That(asset.TryEvaluateSurfaceHeight(surfaceIndex, 1000f, 500f, out _), Is.False);
            Assert.That(asset.TryEvaluateSurfaceHeight(surfaceIndex, 750f, 750f, out _), Is.False);
        }

        [Test]
        public void ResolveGroundBatch_SelectsBridgeDeckWithoutMutatingTerrainHeightTruth()
        {
            StructureCollisionAsset asset = LoadFixtureAsset();
            var terrain = new FlatHeightmap(heightCm: 0f);
            var sampler = new GroundSurfaceSampler(terrain, asset, new StructureCollisionRuntimeState(asset));
            GroundSurfaceQueryPolicy bridgePolicy = new GroundSurfaceQueryPolicy(
                layerId: BridgeLayer,
                agentMask: AllAgentsMask,
                minHeightCm: 450f,
                maxHeightCm: 550f);

            SampleResult bridge = ResolveOne(sampler, 1200f, 1200f, in bridgePolicy);

            Assert.That(bridge.SurfaceId, Is.EqualTo(101));
            Assert.That(bridge.HeightCm, Is.EqualTo(500f).Within(0.001f));
            Assert.That(terrain.HeightCm, Is.EqualTo(0f));
            Assert.That(terrain.TrySampleHeightCm(1200f, 1200f, out float terrainHeight), Is.True);
            Assert.That(terrainHeight, Is.EqualTo(0f));
        }

        [Test]
        public void ResolveGroundBatch_GroundLevelPolicyRejectsBridgeDeckAndKeepsLowerSurface()
        {
            StructureCollisionAsset asset = LoadFixtureAsset();
            var sampler = new GroundSurfaceSampler(new FlatHeightmap(heightCm: 0f), asset, new StructureCollisionRuntimeState(asset));
            GroundSurfaceQueryPolicy groundLevelPolicy = new GroundSurfaceQueryPolicy(
                layerId: BridgeLayer,
                agentMask: AllAgentsMask,
                minHeightCm: -50f,
                maxHeightCm: 150f);

            SampleResult result = ResolveOne(sampler, 1200f, 1200f, in groundLevelPolicy);

            Assert.That(result.SurfaceId, Is.EqualTo(GroundSurfaceIds.TerrainSurface));
            Assert.That(result.HeightCm, Is.EqualTo(0f));
            Assert.That(((GroundSurfaceHitMask)result.HitMask & GroundSurfaceHitMask.Terrain) != 0, Is.True);
        }

        [Test]
        public void ResolveGroundBatch_RampReturnsStableHeightNormalAndSurfaceIds()
        {
            StructureCollisionAsset asset = LoadFixtureAsset();
            var sampler = new GroundSurfaceSampler(new FlatHeightmap(heightCm: 0f), asset, new StructureCollisionRuntimeState(asset));
            GroundSurfaceQueryPolicy rampPolicy = new GroundSurfaceQueryPolicy(
                layerId: BridgeLayer,
                agentMask: AllAgentsMask,
                minHeightCm: 0f,
                maxHeightCm: 220f,
                maxSlopeDegrees: 45f);

            SampleResult low = ResolveOne(sampler, 1200f, 1600f, in rampPolicy);
            SampleResult mid = ResolveOne(sampler, 1200f, 1800f, in rampPolicy);
            SampleResult repeat = ResolveOne(sampler, 1200f, 1800f, in rampPolicy);

            Assert.That(low.SurfaceId, Is.EqualTo(102));
            Assert.That(mid.SurfaceId, Is.EqualTo(102));
            Assert.That(repeat.SurfaceId, Is.EqualTo(102));
            Assert.That(low.HeightCm, Is.EqualTo(0f).Within(0.001f));
            Assert.That(mid.HeightCm, Is.EqualTo(100f).Within(0.001f));
            Assert.That(float.IsFinite(mid.NormalY), Is.True);
            Assert.That(mid.NormalY, Is.GreaterThan(0.8f));
        }

        [Test]
        public void StructureFlagsDriveSeparateMovementProjectilePhysicsAndDebugViews()
        {
            StructureCollisionAsset asset = LoadFixtureAsset();
            var runtime = new StructureCollisionRuntimeState(asset);
            Span<StructureCollisionBlockerView> navBlockers = stackalloc StructureCollisionBlockerView[8];
            Span<StructureCollisionBlockerView> projectileBlockers = stackalloc StructureCollisionBlockerView[8];
            Span<StructureCollisionBlockerView> physicsShapes = stackalloc StructureCollisionBlockerView[8];
            Span<StructureCollisionDebugRecord> debugRecords = stackalloc StructureCollisionDebugRecord[8];

            int navCount = StructureCollisionNavigationAdapter.CollectBlockers(asset, runtime, StructureCollisionBlockerKind.Movement, navBlockers);
            int projectileCount = StructureCollisionNavigationAdapter.CollectBlockers(asset, runtime, StructureCollisionBlockerKind.Projectile, projectileBlockers);
            int physicsCount = StructureCollisionPhysicsAdapter.CollectCollisionShapes(asset, runtime, physicsShapes);
            int debugCount = StructureCollisionDebugAdapter.CollectSurfaceDebugRecords(asset, runtime, debugRecords);

            Assert.That(navCount, Is.EqualTo(1));
            Assert.That(navBlockers[0].SurfaceId, Is.EqualTo(104));
            Assert.That(projectileCount, Is.EqualTo(0));
            Assert.That(physicsCount, Is.EqualTo(1));
            Assert.That(physicsShapes[0].SurfaceId, Is.EqualTo(104));
            Assert.That(ContainsDebugSurface(debugRecords.Slice(0, debugCount), 104), Is.True);
        }

        [Test]
        public void StructureAdapters_ReportRequiredCountsAndRejectTooSmallOutputSpans()
        {
            StructureCollisionAsset asset = LoadFixtureAsset();
            var runtime = new StructureCollisionRuntimeState(asset);

            int movementRequired = StructureCollisionNavigationAdapter.CountBlockers(asset, runtime, StructureCollisionBlockerKind.Movement);
            int physicsRequired = StructureCollisionPhysicsAdapter.CountCollisionShapes(asset, runtime);

            Assert.That(movementRequired, Is.EqualTo(1));
            Assert.That(physicsRequired, Is.EqualTo(1));
            InvalidOperationException movementEx = Assert.Throws<InvalidOperationException>(
                () => StructureCollisionNavigationAdapter.CollectBlockers(
                    asset,
                    runtime,
                    StructureCollisionBlockerKind.Movement,
                    Array.Empty<StructureCollisionBlockerView>()))!;
            InvalidOperationException physicsEx = Assert.Throws<InvalidOperationException>(
                () => StructureCollisionPhysicsAdapter.CollectCollisionShapes(
                    asset,
                    runtime,
                    Array.Empty<StructureCollisionBlockerView>()))!;
            Assert.That(movementEx.Message, Does.Contain("output span too small"));
            Assert.That(physicsEx.Message, Does.Contain("output span too small"));

            Assert.That(asset.TryGetSurfaceIndexById(104, out int gateIndex), Is.True);
            Assert.That(runtime.SetSurfaceEnabled(asset, gateIndex, enabled: false), Is.True);
            Assert.That(StructureCollisionNavigationAdapter.CountDirtyChunkInvalidations(runtime), Is.EqualTo(1));
            Assert.That(StructureCollisionPhysicsAdapter.CountDirtyChunkInvalidations(runtime), Is.EqualTo(1));

            InvalidOperationException navDirtyEx = Assert.Throws<InvalidOperationException>(
                () => StructureCollisionNavigationAdapter.CollectDirtyChunkInvalidations(runtime, Array.Empty<StructureChunkRevision>()))!;
            InvalidOperationException physicsDirtyEx = Assert.Throws<InvalidOperationException>(
                () => StructureCollisionPhysicsAdapter.CollectDirtyChunkInvalidations(runtime, Array.Empty<StructureChunkRevision>()))!;
            Assert.That(navDirtyEx.Message, Does.Contain("output span too small"));
            Assert.That(physicsDirtyEx.Message, Does.Contain("output span too small"));
        }

        [Test]
        public void AgentMaskSelectsTraversalResultForInfantryAndMountedUnits()
        {
            StructureCollisionAsset asset = LoadFixtureAsset();
            var sampler = new GroundSurfaceSampler(null, asset, new StructureCollisionRuntimeState(asset));
            GroundSurfaceQueryPolicy infantry = new GroundSurfaceQueryPolicy(
                layerId: BridgeLayer,
                agentMask: InfantryMask,
                minHeightCm: 250f,
                maxHeightCm: 350f);
            GroundSurfaceQueryPolicy mounted = new GroundSurfaceQueryPolicy(
                layerId: BridgeLayer,
                agentMask: MountedMask,
                minHeightCm: 250f,
                maxHeightCm: 350f);

            SampleResult infantryHit = SampleStructureOne(sampler, 2100f, 1150f, in infantry);
            SampleResult mountedHit = SampleStructureOne(sampler, 2100f, 1150f, in mounted);

            Assert.That(infantryHit.SurfaceId, Is.EqualTo(103));
            Assert.That(mountedHit.SurfaceId, Is.EqualTo(GroundSurfaceIds.NoSurface));
            Assert.That(mountedHit.HitMask, Is.EqualTo((byte)GroundSurfaceHitMask.None));
        }

        [Test]
        public void GateMutationUpdatesOnlyAffectedChunkAndInvalidatesDerivedConsumers()
        {
            StructureCollisionAsset asset = LoadFixtureAsset();
            var runtime = new StructureCollisionRuntimeState(asset);
            Assert.That(asset.TryGetSurfaceIndexById(104, out int gateIndex), Is.True);
            int gateChunk = asset.GetPrimaryChunkForSurface(gateIndex);

            Assert.That(runtime.SetSurfaceEnabled(asset, gateIndex, enabled: false), Is.True);

            Span<StructureChunkRevision> navInvalidations = stackalloc StructureChunkRevision[8];
            Span<StructureChunkRevision> physicsInvalidations = stackalloc StructureChunkRevision[8];
            int navDirtyCount = StructureCollisionNavigationAdapter.CollectDirtyChunkInvalidations(runtime, navInvalidations);
            int physicsDirtyCount = StructureCollisionPhysicsAdapter.CollectDirtyChunkInvalidations(runtime, physicsInvalidations);
            Span<StructureCollisionBlockerView> blockers = stackalloc StructureCollisionBlockerView[8];
            int blockerCount = StructureCollisionNavigationAdapter.CollectBlockers(asset, runtime, StructureCollisionBlockerKind.Movement, blockers);

            Assert.That(navDirtyCount, Is.EqualTo(1));
            Assert.That(navInvalidations[0].ChunkIndex, Is.EqualTo(gateChunk));
            Assert.That(navInvalidations[0].Revision, Is.EqualTo(1));
            Assert.That(physicsDirtyCount, Is.EqualTo(1));
            Assert.That(physicsInvalidations[0].ChunkIndex, Is.EqualTo(gateChunk));
            Assert.That(blockerCount, Is.EqualTo(0));
        }

        [Test]
        public void PickingAndCameraResolveSameStructureSurfacePolicy()
        {
            StructureCollisionAsset asset = LoadFixtureAsset();
            var sampler = new GroundSurfaceSampler(new FlatHeightmap(0f), asset, new StructureCollisionRuntimeState(asset));
            GroundSurfaceQueryPolicy policy = new GroundSurfaceQueryPolicy(
                layerId: BridgeLayer,
                agentMask: AllAgentsMask,
                minHeightCm: 450f,
                maxHeightCm: 550f);

            bool picking = StructureCollisionSelectionAdapter.TryResolveGround(sampler, 1200f, 1200f, in policy, out int pickingSurfaceId, out float pickingHeight);
            bool camera = StructureCollisionCameraGroundAdapter.TryResolveTargetHeight(sampler, 1200f, 1200f, in policy, out int cameraSurfaceId, out float cameraHeight);

            Assert.That(picking, Is.True);
            Assert.That(camera, Is.True);
            Assert.That(pickingSurfaceId, Is.EqualTo(101));
            Assert.That(cameraSurfaceId, Is.EqualTo(pickingSurfaceId));
            Assert.That(cameraHeight, Is.EqualTo(pickingHeight));
        }

        [Test]
        public void StructureGroundingStressBenchmark_ReportsBoundedZeroAllocationHotPath()
        {
            StructureGroundingBenchmarkResult result = StructureGroundingBenchmark.RunGridBenchmark(
                surfaceColumns: 300,
                surfaceRows: 100,
                samplesPerFrame: 50_000,
                frames: 100);

            TestContext.WriteLine(
                $"surfaces={result.TotalSurfaces}; chunks={result.LoadedChunks}; samples={result.SampledPoints}; visitedChunks={result.VisitedChunks}; candidates={result.TestedCandidateSurfaces}; maxCandidatesPerSample={result.MaxCandidateSurfacesPerSample}; elapsedMs={result.ElapsedMilliseconds:F3}; p95Ms={result.P95FrameMilliseconds:F3}; allocations={result.ManagedAllocationsBytes}");

            Assert.That(result.TotalSurfaces, Is.GreaterThanOrEqualTo(30_000));
            Assert.That(result.SampledPoints, Is.EqualTo(5_000_000));
            Assert.That(result.MaxCandidateSurfacesPerSample, Is.LessThan(result.TotalSurfaces));
            Assert.That(result.MaxCandidateSurfacesPerSample, Is.LessThanOrEqualTo(1));
            Assert.That(result.ManagedAllocationsBytes, Is.EqualTo(0));
        }

        [Test]
        public void StructureGroundingNonIdealStressBenchmark_ExercisesOverlapTerrainAndLongChunkSpans()
        {
            StructureGroundingBenchmarkResult result = StructureGroundingBenchmark.RunNonIdealBenchmark(
                samplesPerFrame: 20_000,
                frames: 50);

            TestContext.WriteLine(
                $"nonIdeal surfaces={result.TotalSurfaces}; chunks={result.LoadedChunks}; samples={result.SampledPoints}; visitedChunks={result.VisitedChunks}; candidates={result.TestedCandidateSurfaces}; maxCandidatesPerSample={result.MaxCandidateSurfacesPerSample}; elapsedMs={result.ElapsedMilliseconds:F3}; p95Ms={result.P95FrameMilliseconds:F3}; allocations={result.ManagedAllocationsBytes}");

            Assert.That(result.TotalSurfaces, Is.GreaterThanOrEqualTo(4));
            Assert.That(result.LoadedChunks, Is.GreaterThan(1));
            Assert.That(result.SampledPoints, Is.EqualTo(1_000_000));
            Assert.That(result.MaxCandidateSurfacesPerSample, Is.GreaterThan(1));
            Assert.That(result.TestedCandidateSurfaces, Is.GreaterThan(result.SampledPoints));
            Assert.That(result.ManagedAllocationsBytes, Is.EqualTo(0));
        }

        private static StructureCollisionAsset LoadFixtureAsset()
        {
            using Stream stream = File.OpenRead(Path.Combine(FindRepoRoot(), FixtureRelativePath));
            return StructureCollisionAssetJson.Read(stream);
        }

        private static StructureCollisionAsset CreateBoundaryAsset()
        {
            var header = new StructureCollisionHeader(
                version: 1,
                new WorldAabbCm(0, 0, 2000, 1000),
                chunkSizeCm: 1000,
                revision: 591,
                coordinateScale: 1f);
            var layers = new[] { new StructureLayerDefinition("bridge", BridgeLayer) };
            var masks = new[] { new StructureAgentMaskDefinition("all", AllAgentsMask) };
            var shapes = new[]
            {
                new StructureShapeDefinition
                {
                    Id = "right_boundary_deck_shape",
                    Kind = StructureShapeKind.WalkablePolygon,
                    Vertices = new[]
                    {
                        new StructurePointCm(500f, 250f),
                        new StructurePointCm(1000f, 250f),
                        new StructurePointCm(1000f, 750f),
                        new StructurePointCm(500f, 750f)
                    },
                    PlaneOriginXCm = 500f,
                    PlaneOriginZCm = 250f,
                    PlaneHeightCm = 250f,
                    MinHeightCm = 250f,
                    MaxHeightCm = 250f
                }
            };
            var surfaces = new[]
            {
                new StructureSurfaceDefinition
                {
                    SurfaceId = 201,
                    Kind = StructureSurfaceKind.Deck,
                    Flags = StructureSurfaceFlags.Walkable,
                    LayerId = BridgeLayer,
                    AgentMask = AllAgentsMask,
                    ShapeId = "right_boundary_deck_shape"
                }
            };

            return StructureCollisionAssetBuilder.Build(header, layers, masks, shapes, surfaces);
        }

        private static void AssertLoadFailsAfterReplacing(string oldValue, string newValue, string expectedMessage)
        {
            string json = File.ReadAllText(Path.Combine(FindRepoRoot(), FixtureRelativePath), Encoding.UTF8)
                .Replace(oldValue, newValue, StringComparison.Ordinal);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => StructureCollisionAssetJson.Read(stream))!;
            Assert.That(ex.Message, Does.Contain(expectedMessage));
        }

        private static void AssertLoadFailsAfterRemovingLine(string line, string expectedMessage)
        {
            string json = File.ReadAllText(Path.Combine(FindRepoRoot(), FixtureRelativePath), Encoding.UTF8);
            string removed = json
                .Replace(line + "\r\n", string.Empty, StringComparison.Ordinal)
                .Replace(line + "\n", string.Empty, StringComparison.Ordinal);
            Assert.That(removed, Is.Not.EqualTo(json), $"Fixture did not contain line '{line}'.");
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(removed));
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => StructureCollisionAssetJson.Read(stream))!;
            Assert.That(ex.Message, Does.Contain(expectedMessage));
        }

        private static SampleResult ResolveOne(
            IGroundSurfaceSampler sampler,
            float xCm,
            float zCm,
            in GroundSurfaceQueryPolicy policy)
        {
            Span<float> x = stackalloc float[1];
            Span<float> z = stackalloc float[1];
            Span<float> h = stackalloc float[1];
            Span<float> nx = stackalloc float[1];
            Span<float> ny = stackalloc float[1];
            Span<float> nz = stackalloc float[1];
            Span<int> surfaceIds = stackalloc int[1];
            Span<int> layerIds = stackalloc int[1];
            Span<byte> hitMask = stackalloc byte[1];
            x[0] = xCm;
            z[0] = zCm;
            sampler.ResolveGroundBatch(x, z, h, nx, ny, nz, surfaceIds, layerIds, hitMask, in policy);
            return new SampleResult(h[0], nx[0], ny[0], nz[0], surfaceIds[0], layerIds[0], hitMask[0]);
        }

        private static SampleResult SampleStructureOne(
            IGroundSurfaceSampler sampler,
            float xCm,
            float zCm,
            in GroundSurfaceQueryPolicy policy)
        {
            Span<float> x = stackalloc float[1];
            Span<float> z = stackalloc float[1];
            Span<float> h = stackalloc float[1];
            Span<float> nx = stackalloc float[1];
            Span<float> ny = stackalloc float[1];
            Span<float> nz = stackalloc float[1];
            Span<int> surfaceIds = stackalloc int[1];
            Span<int> layerIds = stackalloc int[1];
            Span<byte> hitMask = stackalloc byte[1];
            x[0] = xCm;
            z[0] = zCm;
            sampler.SampleStructureSurfaceBatch(x, z, h, nx, ny, nz, surfaceIds, layerIds, hitMask, in policy);
            return new SampleResult(h[0], nx[0], ny[0], nz[0], surfaceIds[0], layerIds[0], hitMask[0]);
        }

        private static bool ContainsDebugSurface(ReadOnlySpan<StructureCollisionDebugRecord> records, int surfaceId)
        {
            for (int i = 0; i < records.Length; i++)
            {
                if (records[i].SurfaceId == surfaceId)
                {
                    return true;
                }
            }

            return false;
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

        private readonly struct SampleResult
        {
            public SampleResult(float heightCm, float normalX, float normalY, float normalZ, int surfaceId, int layerId, byte hitMask)
            {
                HeightCm = heightCm;
                NormalX = normalX;
                NormalY = normalY;
                NormalZ = normalZ;
                SurfaceId = surfaceId;
                LayerId = layerId;
                HitMask = hitMask;
            }

            public float HeightCm { get; }

            public float NormalX { get; }

            public float NormalY { get; }

            public float NormalZ { get; }

            public int SurfaceId { get; }

            public int LayerId { get; }

            public byte HitMask { get; }
        }

        private sealed class FlatHeightmap : IContinuousHeightmap
        {
            public FlatHeightmap(float heightCm)
            {
                HeightCm = heightCm;
            }

            public float HeightCm { get; }

            public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = -1)
            {
                heightCm = HeightCm;
                return true;
            }

            public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = -1)
            {
                for (int i = 0; i < worldXCm.Length; i++)
                {
                    outHeightCm[i] = HeightCm;
                }

                return true;
            }

            public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = -1)
            {
                float directionY = ray.Direction.Y;
                if (MathF.Abs(directionY) < 0.0001f)
                {
                    hit = default;
                    return false;
                }

                float t = ((HeightCm * 0.01f) - ray.Origin.Y) / directionY;
                if (t < 0f)
                {
                    hit = default;
                    return false;
                }

                Vector3 point = ray.Origin + (ray.Direction * t);
                hit = new VisualGroundHit(point.X * 100f, point.Z * 100f, HeightCm, layerIndex, t, Vector3.UnitY);
                return true;
            }

            public bool RaycastGroundBatch(
                ReadOnlySpan<float> originXMeters,
                ReadOnlySpan<float> originYMeters,
                ReadOnlySpan<float> originZMeters,
                ReadOnlySpan<float> directionX,
                ReadOnlySpan<float> directionY,
                ReadOnlySpan<float> directionZ,
                Span<float> outWorldXCm,
                Span<float> outWorldYCm,
                Span<float> outHeightCm,
                Span<float> outDistanceMeters,
                Span<float> outNormalX,
                Span<float> outNormalY,
                Span<float> outNormalZ,
                Span<int> outLayerIndex,
                Span<byte> outHitMask,
                int layerIndex = -1)
            {
                for (int i = 0; i < originXMeters.Length; i++)
                {
                    outWorldXCm[i] = originXMeters[i] * 100f;
                    outWorldYCm[i] = originZMeters[i] * 100f;
                    outHeightCm[i] = HeightCm;
                    outDistanceMeters[i] = 0f;
                    outNormalX[i] = 0f;
                    outNormalY[i] = 1f;
                    outNormalZ[i] = 0f;
                    outLayerIndex[i] = layerIndex;
                    outHitMask[i] = 1;
                }

                return true;
            }
        }
    }
}
