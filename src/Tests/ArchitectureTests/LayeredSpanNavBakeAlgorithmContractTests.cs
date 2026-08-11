using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class LayeredSpanNavBakeAlgorithmContractTests
    {
        private static readonly NavTriangleSurfaceFlags FloorFlags =
            NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;

        [Test]
        public void SlopeQ1M_ColdIntegerConversion_IsDeterministicAndTableBacked()
        {
            Assert.That(LayeredSpanSlopeQ1M.CosDegrees(0), Is.EqualTo(1_000_000));
            Assert.That(LayeredSpanSlopeQ1M.CosDegrees(45), Is.EqualTo(707_107));
            Assert.That(LayeredSpanSlopeQ1M.CosDegrees(60), Is.EqualTo(500_000));
            Assert.That(
                LayeredSpanSlopeQ1M.CompileMinWalkableUpDotQ1M(45f, "test"),
                Is.EqualTo(707_107));
            Assert.That(
                LayeredSpanSlopeQ1M.CompileMinWalkableUpDotQ1M(40f, "test"),
                Is.EqualTo(LayeredSpanSlopeQ1M.CosDegrees(40)));

            InvalidOperationException nonInt = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanSlopeQ1M.CompileMinWalkableUpDotQ1M(45.5f, "owner.maxSlopeDeg"))!;
            Assert.That(nonInt.Message, Does.Contain("exact integer"));
            Assert.That(nonInt.Message, Does.Contain("owner.maxSlopeDeg"));

            Assert.Throws<ArgumentOutOfRangeException>(() => LayeredSpanSlopeQ1M.CosDegrees(90));
            Assert.Throws<InvalidOperationException>(
                () => LayeredSpanSlopeQ1M.CompileMinWalkableUpDotQ1M(90f, "owner"));
        }

        [Test]
        public void LayeredSpanConfig_RejectsMissingUnknownAndNonPositiveValues()
        {
            var profiles = CreateAgentProfiles();
            string missing = CreateTempNavConfig(
                """
                {
                  "mode": "offline",
                  "algorithm": "layered-span",
                  "profiles": [ { "id": "Small", "maxClimbCm": 40, "maxSlopeDeg": 45 } ],
                  "layers": [ { "id": "Ground", "layer": 0 } ],
                  "areas": [],
                  "runtimeIncremental": {
                    "tileBudgetPerFixedTick": 1,
                    "includeNeighborTiles": true,
                    "heightScaleMeters": 1,
                    "minWalkableUpDot": 0.6,
                    "cliffHeightThreshold": 1,
                    "trackedStructuralEntityCapacity": 256,
                    "obstaclePrimitiveCapacity": 512,
                    "polygonVertexCapacity": 4096,
                    "dirtyTileCapacity": 64,
                    "stagedEntryCapacity": 64,
                    "publishedTileCapacity": 64,
                    "storeGroupCapacity": 8,
                    "residentTileCapacity": 128,
                    "outputVertexCapacity": 256,
                    "outputTriangleCapacity": 512,
                    "outputPortalCapacity": 64,
                    "initialResidentChunkX": 0,
                    "initialResidentChunkZ": 0,
                    "initialResidentWidthChunks": 1,
                    "initialResidentHeightChunks": 1
                  }
                }
                """);
            try
            {
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                    () => LoadTempConfig(missing, profiles))!;
                Assert.That(ex.Message, Does.Contain("layeredSpan"));
            }
            finally
            {
                Directory.Delete(missing, recursive: true);
            }

            string unknown = CreateTempNavConfig(ValidNavmeshJsonWithLayeredSpanPatch(
                """
                "scratchSlotCount": 2,
                "extraField": 1,
                """));
            try
            {
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                    () => LoadTempConfig(unknown, profiles))!;
                Assert.That(ex.Message, Does.Contain("extraField"));
            }
            finally
            {
                Directory.Delete(unknown, recursive: true);
            }

            string zeroSlots = CreateTempNavConfig(ValidNavmeshJson(scratchSlotCount: 0));
            try
            {
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                    () => LoadTempConfig(zeroSlots, profiles))!;
                Assert.That(ex.Message, Does.Contain("scratchSlotCount"));
            }
            finally
            {
                Directory.Delete(zeroSlots, recursive: true);
            }
        }

        [Test]
        public void ScratchPool_AcquireRelease_IsDeterministicThreadSafeAndFailsOnExhaustion()
        {
            NavLayeredSpanConfig config = CreateConfig(scratchSlotCount: 2);
            var pool = new LayeredSpanScratchPool(config);
            Assert.That(pool.AvailableCount, Is.EqualTo(2));

            LayeredSpanScratchSlot a = pool.Acquire();
            LayeredSpanScratchSlot b = pool.Acquire();
            Assert.That(a.SlotIndex, Is.EqualTo(0));
            Assert.That(b.SlotIndex, Is.EqualTo(1));
            Assert.That(ReferenceEquals(a.Raw, b.Raw), Is.False);

            InvalidOperationException exhausted = Assert.Throws<InvalidOperationException>(() => pool.Acquire())!;
            Assert.That(exhausted.Message, Does.Contain("scratchSlotCount"));
            Assert.That(exhausted.Message, Does.Contain("2"));

            pool.Release(b);
            pool.Release(a);
            LayeredSpanScratchSlot again = pool.Acquire();
            Assert.That(again.SlotIndex, Is.EqualTo(0));
            Assert.That(ReferenceEquals(again, a), Is.True);
            pool.Release(again);

            Assert.Throws<InvalidOperationException>(() => pool.Release(a));
        }

        [Test]
        public void Adapter_EmptyTile_IsLegalNavValidEmptyWithChecksum()
        {
            // Fully blocked solid wall (no WalkCandidate) over the tile produces zero polygons.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 400, 0, 400 },
                vertexYcm: new[] { 0, 0, 200, 200 },
                vertexZcm: new[] { 0, 0, 400, 400 },
                triA: new[] { 0, 1 },
                triB: new[] { 1, 3 },
                triC: new[] { 2, 2 },
                triAreaIds: new byte[] { 1, 1 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { NavTriangleSurfaceFlags.Solid, NavTriangleSurfaceFlags.Solid });
            NavBakeResult result = BakeOnce(surface, tileWidthCm: 400, tileHeightCm: 400, haloPaddingCm: 100);
            Assert.That(result.FailureCount, Is.EqualTo(0), result.Entries[0].Artifact.Message);
            NavTile tile = result.Entries[0].Tile;
            Assert.That(tile.TriangleCount, Is.EqualTo(0));
            Assert.That(tile.VertexCount, Is.EqualTo(0));
            Assert.That(tile.PortalCount, Is.EqualTo(0));
            Assert.That(tile.ActivePortals.Length, Is.EqualTo(0));
            Assert.That(tile.Checksum, Is.Not.EqualTo(0UL));
            Assert.That(result.Entries[0].Artifact.ErrorCode, Is.EqualTo(NavBakeErrorCode.None));
        }

        [Test]
        public void BankedUnusedCapacity_PoisonDoesNotAffectChecksumQueryOrValidEmpty()
        {
            NavTriangleSurfaceSnapshot surface = QuadFloor(0, 0, 400, 400, y: 0, area: 1, stable: 1);
            NavBakeResult result = BakeOnce(surface, tileWidthCm: 400, tileHeightCm: 400, haloPaddingCm: 100);
            Assert.That(result.FailureCount, Is.EqualTo(0), result.Entries[0].Artifact.Message);
            NavTile tile = result.Entries[0].Tile;
            Assert.That(tile.PortalCapacity, Is.GreaterThan(tile.PortalCount));
            Assert.That(tile.VertexCapacity, Is.GreaterThan(tile.VertexCount));
            Assert.That(tile.TriangleCapacity, Is.GreaterThan(tile.TriangleCount));

            ulong checksumBefore = tile.Checksum;
            byte[] bytesBefore;
            using (var ms = new MemoryStream())
            {
                NavTileBinary.Write(ms, tile);
                bytesBefore = ms.ToArray();
            }

            var poisonPortal = new NavBorderPortal(
                NavPortalSide.West,
                1, 2, 3, 4,
                leftXcm: 999, leftYcm: 888, leftZcm: 777,
                rightXcm: 666, rightYcm: 555, rightZcm: 444,
                clearanceCm: 1);
            for (int i = tile.PortalCount; i < tile.PortalCapacity; i++)
            {
                tile.Portals[i] = poisonPortal;
            }

            for (int i = tile.VertexCount; i < tile.VertexCapacity; i++)
            {
                tile.VertexXcm[i] = 123456;
                tile.VertexYcm[i] = -98765;
                tile.VertexZcm[i] = 444444;
            }

            for (int i = tile.TriangleCount; i < tile.TriangleCapacity; i++)
            {
                tile.TriA[i] = 7;
                tile.TriB[i] = 8;
                tile.TriC[i] = 9;
                tile.N0[i] = 10;
                tile.N1[i] = 11;
                tile.N2[i] = 12;
                tile.TriAreaIds[i] = 255;
            }

            Assert.That(tile.ActivePortals.Length, Is.EqualTo(tile.PortalCount));
            Assert.That(tile.ActiveVertexXcm.Length, Is.EqualTo(tile.VertexCount));
            Assert.That(tile.ActiveTriA.Length, Is.EqualTo(tile.TriangleCount));

            Span<byte> scratch = stackalloc byte[NavTileBinary.GetSerializedSize(tile)];
            NavTileBinary.AssignChecksum(tile, scratch);
            Assert.That(tile.Checksum, Is.EqualTo(checksumBefore));

            using (var ms = new MemoryStream())
            {
                NavTileBinary.Write(ms, tile);
                Assert.That(ms.ToArray(), Is.EqualTo(bytesBefore));
            }

            byte[] detour = DetourNavQueryEngine.BuildDetourTileBytes(tile, 400, 400);
            NavPathResult path = DetourNavQueryEngine.FindPathFromDetourTileBytes(
                new[] { detour },
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                startXcm: 50,
                startZcm: 50,
                goalXcm: 350,
                goalZcm: 350,
                maxPortals: 64);
            Assert.That(path.Status, Is.EqualTo(NavPathStatus.Ok));

            // Valid-empty banked tile: unused capacity poison must not change empty semantics.
            NavTile empty = NavTile.CreateBanked(32, 32, 32);
            NavValidEmptyTile.Fill(
                empty,
                new NavTileId(0, 0, 0),
                tileVersion: 1,
                buildConfigHash: 1UL,
                originXcm: 0,
                originZcm: 0,
                scratch);
            for (int i = 0; i < empty.PortalCapacity; i++)
            {
                empty.Portals[i] = poisonPortal;
            }

            Assert.That(empty.PortalCount, Is.EqualTo(0));
            Assert.That(empty.ActivePortals.Length, Is.EqualTo(0));
            Assert.That(empty.Checksum, Is.Not.EqualTo(0UL));
            using var emptyMs = new MemoryStream();
            NavTileBinary.Write(emptyMs, empty);
            emptyMs.Position = 0;
            NavTile emptyRoundTrip = NavTileBinary.Read(emptyMs);
            Assert.That(emptyRoundTrip.PortalCount, Is.EqualTo(0));
            Assert.That(emptyRoundTrip.Checksum, Is.EqualTo(empty.Checksum));
        }

        [Test]
        public void FlatGridBaseline_BankedUnusedAreaPoison_DoesNotAffectDetourArea()
        {
            NavTile tile = DefaultGridNavTileFactory.CreateFlatTile(
                chunkX: 0,
                chunkY: 0,
                layer: 0,
                tileVersion: 1,
                chunkSizeCells: 4,
                cellSizeCm: SpatialScaleDefaults.CellCm,
                areaId: 3);
            Assert.That(tile.TriangleCount, Is.EqualTo(2));
            Assert.That(tile.TriangleCapacity, Is.EqualTo(2));

            // Grow into a banked buffer so unused TriAreaIds slots can be poisoned without changing TriangleCount.
            NavTile banked = NavTile.CreateBanked(
                Math.Max(32, tile.VertexCount),
                Math.Max(32, tile.TriangleCount),
                Math.Max(32, tile.PortalCount));
            banked.CopyGeometryFrom(tile);
            Assert.That(banked.TriangleCount, Is.EqualTo(2));
            Assert.That(banked.TriangleCapacity, Is.GreaterThan(banked.TriangleCount));
            Assert.That(banked.ActiveTriAreaIds[0], Is.EqualTo((byte)3));
            Assert.That(banked.ActiveTriAreaIds[1], Is.EqualTo((byte)3));

            for (int i = banked.TriangleCount; i < banked.TriangleCapacity; i++)
            {
                banked.TriAreaIds[i] = 255;
            }

            // Poisoning capacity must not change Length-based reads that ignore the active span.
            Assert.That(banked.TriAreaIds.Length, Is.GreaterThan(banked.TriangleCount));
            Assert.That(banked.TriAreaIds[0], Is.EqualTo((byte)3));

            byte[] clean = DetourNavQueryEngine.BuildFlatGridBaselineDetourTileBytes(tile, 400, 400);
            byte[] poisoned = DetourNavQueryEngine.BuildFlatGridBaselineDetourTileBytes(banked, 400, 400);
            Assert.That(poisoned, Is.EqualTo(clean));

            NavPathResult path = DetourNavQueryEngine.FindPathFromDetourTileBytes(
                new[] { poisoned },
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                startXcm: 50,
                startZcm: 50,
                goalXcm: 350,
                goalZcm: 350,
                maxPortals: 64);
            Assert.That(path.Status, Is.EqualTo(NavPathStatus.Ok));
        }

        [Test]
        public void Adapter_FlatFloor_ProducesNonEmptyDeterministicTileAndLocalCoords()
        {
            NavTriangleSurfaceSnapshot surface = QuadFloor(0, 0, 400, 400, y: 25, area: 3, stable: 11);
            NavBakeResult first = BakeOnce(surface, tileWidthCm: 400, tileHeightCm: 400, haloPaddingCm: 100);
            NavBakeResult second = BakeOnce(surface, tileWidthCm: 400, tileHeightCm: 400, haloPaddingCm: 100);
            Assert.That(first.FailureCount, Is.EqualTo(0), first.Entries[0].Artifact.Message);
            Assert.That(first.Entries[0].Tile.TriangleCount, Is.GreaterThan(0));
            Assert.That(first.Entries[0].ToTileBytes(), Is.EqualTo(second.Entries[0].ToTileBytes()));

            NavTile tile = first.Entries[0].Tile;
            Assert.That(tile.OriginXcm, Is.EqualTo(0));
            Assert.That(tile.OriginZcm, Is.EqualTo(0));
            // Obstacle-free flat floor emits Editor Bridge flat-grid-baseline-v2 footprint.
            Assert.That(tile.VertexCount, Is.EqualTo(4));
            Assert.That(tile.TriangleCount, Is.EqualTo(2));
            Assert.That(tile.PortalCount, Is.EqualTo(4));
            Assert.That(first.Entries[0].Artifact.Message, Is.EqualTo(DefaultGridNavTileFactory.SourceId));
            Assert.That(DefaultGridNavTileFactory.MatchesFlatBaselineFootprint(tile, 400, 400), Is.True);
            for (int i = 0; i < tile.VertexCount; i++)
            {
                Assert.That(tile.VertexXcm[i], Is.GreaterThanOrEqualTo(0));
                Assert.That(tile.VertexXcm[i], Is.LessThanOrEqualTo(400));
                Assert.That(tile.VertexZcm[i], Is.GreaterThanOrEqualTo(0));
                Assert.That(tile.VertexZcm[i], Is.LessThanOrEqualTo(400));
                Assert.That(tile.VertexYcm[i], Is.EqualTo(25));
            }
        }

        [Test]
        public void Adapter_DifferentSlotAcquisitionOrder_DoesNotChangeTileBytes()
        {
            NavLayeredSpanConfig config = CreateConfig(scratchSlotCount: 2, columnCapacity: 256, spanCapacity: 512);
            var pool = new LayeredSpanScratchPool(config);
            var algorithm = new LayeredSpanNavBakeAlgorithm(pool);
            NavTriangleSurfaceSnapshot surface = QuadFloor(0, 0, 400, 400, y: 0, area: 1, stable: 1);
            NavBakeContext context = CreateContext(surface, config, tileWidthCm: 400, tileHeightCm: 400, haloPaddingCm: 100);

            LayeredSpanScratchSlot hold = pool.Acquire();
            NavBakeResult withSlot1 = new NavBakeService(algorithm).Bake(context);
            pool.Release(hold);
            NavBakeResult withSlot0 = new NavBakeService(algorithm).Bake(context);

            Assert.That(withSlot1.FailureCount, Is.EqualTo(0), withSlot1.Entries[0].Artifact.Message);
            Assert.That(withSlot0.FailureCount, Is.EqualTo(0), withSlot0.Entries[0].Artifact.Message);
            Assert.That(withSlot1.Entries[0].ToTileBytes(), Is.EqualTo(withSlot0.Entries[0].ToTileBytes()));
        }

        [Test]
        public void Adapter_StackedBorderPortals_PreserveDistinctYAndDeterministicOrder()
        {
            // Halo depth 2 so the immediate cross-border neighbor is not an outer clearance seed
            // (outer seeds have clearance 0 and would fail region eligibility for agent radius 30).
            var surface = BuildStackedFullRasterFloors(
                originXcm: -200,
                originZcm: -200,
                cellSizeCm: 100,
                cellsX: 5,
                cellsZ: 5,
                lowY: 0,
                highY: 500);

            NavBakeResult result = BakeOnce(
                surface,
                tileWidthCm: 100,
                tileHeightCm: 100,
                haloPaddingCm: 200,
                tileCountX: 1,
                tileCountZ: 1,
                originXcm: 0,
                originZcm: 0);

            Assert.That(result.FailureCount, Is.EqualTo(0), result.Entries[0].Artifact.Message);
            ReadOnlySpan<NavBorderPortal> portals = result.Entries[0].Tile.ActivePortals;
            var eastYs = new List<int>();
            for (int i = 0; i < portals.Length; i++)
            {
                if (portals[i].Side != NavPortalSide.East)
                {
                    continue;
                }

                Assert.That(portals[i].LeftXcm, Is.EqualTo(100));
                Assert.That(portals[i].RightXcm, Is.EqualTo(100));
                Assert.That(portals[i].ClearanceCm, Is.GreaterThanOrEqualTo(30));
                eastYs.Add(portals[i].LeftYcm);
            }

            Assert.That(eastYs.Count, Is.GreaterThanOrEqualTo(2), $"portalCount={portals.Length}");
            Assert.That(eastYs, Does.Contain(0));
            Assert.That(eastYs, Does.Contain(500));
            for (int i = 0; i < portals.Length; i++)
            {
                if (portals[i].Side != NavPortalSide.East)
                {
                    continue;
                }

                // U/V contract: tile-local centimetres (not cell indices).
                Assert.That(portals[i].U0, Is.EqualTo((short)100));
                Assert.That(portals[i].U1, Is.EqualTo((short)100));
                Assert.That(portals[i].V0, Is.EqualTo((short)portals[i].LeftZcm));
                Assert.That(portals[i].V1, Is.EqualTo((short)portals[i].RightZcm));
            }
            var distinctEastY = new HashSet<int>(eastYs);
            Assert.That(distinctEastY.Count, Is.EqualTo(2));
            for (int i = 1; i < portals.Length; i++)
            {
                Assert.That(ComparePortalOrder(portals[i - 1], portals[i]), Is.LessThanOrEqualTo(0));
            }
        }

        private static NavTriangleSurfaceSnapshot BuildStackedFullRasterFloors(
            int originXcm,
            int originZcm,
            int cellSizeCm,
            int cellsX,
            int cellsZ,
            int lowY,
            int highY)
        {
            int cellCount = checked(cellsX * cellsZ);
            int triCount = checked(cellCount * 4); // 2 tris * 2 heights
            int vertCount = checked(cellCount * 8); // 4 verts * 2 heights
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
            for (int layer = 0; layer < 2; layer++)
            {
                int y = layer == 0 ? lowY : highY;
                byte area = (byte)(layer + 1);
                for (int cz = 0; cz < cellsZ; cz++)
                {
                    for (int cx = 0; cx < cellsX; cx++)
                    {
                        int minX = checked(originXcm + cx * cellSizeCm);
                        int maxX = checked(minX + cellSizeCm);
                        int minZ = checked(originZcm + cz * cellSizeCm);
                        int maxZ = checked(minZ + cellSizeCm);
                        int v0 = v++;
                        int v1 = v++;
                        int v2 = v++;
                        int v3 = v++;
                        vx[v0] = minX; vy[v0] = y; vz[v0] = minZ;
                        vx[v1] = maxX; vy[v1] = y; vz[v1] = minZ;
                        vx[v2] = minX; vy[v2] = y; vz[v2] = maxZ;
                        vx[v3] = maxX; vy[v3] = y; vz[v3] = maxZ;

                        ta[t] = v0; tb[t] = v1; tc[t] = v2;
                        areas[t] = area; stables[t] = stable++; flags[t] = FloorFlags; t++;
                        ta[t] = v1; tb[t] = v3; tc[t] = v2;
                        areas[t] = area; stables[t] = stable++; flags[t] = FloorFlags; t++;
                    }
                }
            }

            return new NavTriangleSurfaceSnapshot(vx, vy, vz, ta, tb, tc, areas, stables, flags);
        }

        [Test]
        public void Adapter_WrongInputCapability_FailsLoudlyWithoutFallback()
        {
            NavLayeredSpanConfig config = CreateConfig();
            var pool = new LayeredSpanScratchPool(config);
            var algorithm = new LayeredSpanNavBakeAlgorithm(pool);
            var service = new NavBakeService(algorithm);
            var context = new NavBakeContext
            {
                MapId = "layered_wrong_input",
                SourceUri = "Core:Maps/layered_wrong_input.vtxm",
                Terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4),
                Obstacles = new NavObstacleSet(),
                Config = CreateBakeConfig(config),
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.LayeredSpan,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => service.Bake(context))!;
            Assert.That(ex.Message, Does.Contain("triangle-surface").Or.Contain("OfflineTriangleSurface").Or.Contain("does not support"));
        }

        [Test]
        public void Adapter_CapacityFailure_NamesOwnerAndRequiredAmount()
        {
            NavLayeredSpanConfig config = CreateConfig(columnCapacity: 1, spanCapacity: 1);
            var pool = new LayeredSpanScratchPool(config);
            var algorithm = new LayeredSpanNavBakeAlgorithm(pool);
            NavTriangleSurfaceSnapshot surface = QuadFloor(0, 0, 400, 400, y: 0, area: 1, stable: 1);
            NavBakeContext context = CreateContext(surface, config, tileWidthCm: 400, tileHeightCm: 400, haloPaddingCm: 100);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => new NavBakeService(algorithm).Bake(context))!;
            Assert.That(ex.Message, Does.Contain("required"));
            Assert.That(ex.Message.Contains("capacity", StringComparison.OrdinalIgnoreCase), Is.True);
        }

        [Test]
        public void NavTileBinary_V3_RoundTripsPortalY_AndRejectsV2()
        {
            Assert.That(NavTileBinary.FormatVersion, Is.EqualTo((ushort)3));
            var tile = new NavTile(
                new NavTileId(1, 2, 0),
                tileVersion: 9,
                buildConfigHash: 123UL,
                checksum: 0UL,
                originXcm: 100,
                originZcm: 200,
                vertexXcm: new[] { 0, 10, 0 },
                vertexYcm: new[] { 5, 5, 5 },
                vertexZcm: new[] { 0, 0, 10 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                n0: new[] { -1 },
                n1: new[] { -1 },
                n2: new[] { -1 },
                triAreaIds: new byte[] { 7 },
                portals: new[]
                {
                    new NavBorderPortal(
                        NavPortalSide.West,
                        0, 0, 0, 1,
                        leftXcm: 0, leftYcm: 40, leftZcm: 0,
                        rightXcm: 0, rightYcm: 55, rightZcm: 10,
                        clearanceCm: 30)
                });

            using var ms = new MemoryStream();
            NavTileBinary.Write(ms, tile);
            byte[] bytes = ms.ToArray();
            ms.Position = 0;
            NavTile roundTrip = NavTileBinary.Read(ms);
            Assert.That(roundTrip.Portals[0].LeftYcm, Is.EqualTo(40));
            Assert.That(roundTrip.Portals[0].RightYcm, Is.EqualTo(55));
            Assert.That(roundTrip.Checksum, Is.Not.EqualTo(0UL));

            // Corrupt version to 2 and ensure hard reject (no compatibility reader).
            bytes[4] = 2;
            bytes[5] = 0;
            using var bad = new MemoryStream(bytes);
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => NavTileBinary.Read(bad))!;
            Assert.That(ex.Message, Does.Contain("version"));
        }

        [Test]
        public void HaloGrid_CheckedAlignment_IsExact()
        {
            var grid = new NavTriangleSurfaceTileGrid(0, 0, 400, 400, 2, 2, haloPaddingCm: 100);
            LayeredSpanNavBakeAlgorithm.DeriveTargetRaster(
                grid,
                new NavBakeTileCoord(1, 0),
                cellSizeCm: 100,
                haloCells: 1,
                out int originX,
                out int originZ,
                out int tMinX,
                out int tMinZ,
                out int tMaxX,
                out int tMaxZ,
                out int colsX,
                out int colsZ);
            Assert.That(tMinX, Is.EqualTo(400));
            Assert.That(tMaxX, Is.EqualTo(800));
            Assert.That(tMinZ, Is.EqualTo(0));
            Assert.That(tMaxZ, Is.EqualTo(400));
            Assert.That(originX, Is.EqualTo(300));
            Assert.That(originZ, Is.EqualTo(-100));
            Assert.That(colsX, Is.EqualTo(6));
            Assert.That(colsZ, Is.EqualTo(6));
        }

        private static NavBakeResult BakeOnce(
            NavTriangleSurfaceSnapshot surface,
            int tileWidthCm,
            int tileHeightCm,
            int haloPaddingCm,
            int tileCountX = 1,
            int tileCountZ = 1,
            int originXcm = 0,
            int originZcm = 0,
            NavBakeTileCoord? target = null)
        {
            NavLayeredSpanConfig config = CreateConfig(
                scratchSlotCount: 2,
                columnCapacity: 512,
                spanCapacity: 1024,
                rasterHaloCells: haloPaddingCm / 100);
            var pool = new LayeredSpanScratchPool(config);
            var algorithm = new LayeredSpanNavBakeAlgorithm(pool);
            NavBakeContext context = CreateContext(
                surface,
                config,
                tileWidthCm,
                tileHeightCm,
                haloPaddingCm,
                tileCountX,
                tileCountZ,
                originXcm,
                originZcm,
                target ?? new NavBakeTileCoord(0, 0));
            return new NavBakeService(algorithm).Bake(context);
        }

        private static NavBakeContext CreateContext(
            NavTriangleSurfaceSnapshot surface,
            NavLayeredSpanConfig layered,
            int tileWidthCm,
            int tileHeightCm,
            int haloPaddingCm,
            int tileCountX = 1,
            int tileCountZ = 1,
            int originXcm = 0,
            int originZcm = 0,
            NavBakeTileCoord? target = null)
        {
            var grid = new NavTriangleSurfaceTileGrid(
                originXcm,
                originZcm,
                tileWidthCm,
                tileHeightCm,
                tileCountX,
                tileCountZ,
                haloPaddingCm);
            var index = NavTriangleSurfaceTileIndex.Build(surface, grid);
            return new NavBakeContext
            {
                MapId = "layered_span_contract",
                SourceUri = "Core:Maps/layered_span_contract.tris",
                TriangleSurface = index,
                Obstacles = new NavObstacleSet(),
                Config = CreateBakeConfig(layered),
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { target ?? new NavBakeTileCoord(0, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 3,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.LayeredSpan,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };
        }

        private static NavMeshBakeConfig CreateBakeConfig(NavLayeredSpanConfig layered)
        {
            return new NavMeshBakeConfig
            {
                Mode = NavBakeNames.ModeOffline,
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
                    OutputVertexCapacity = 256,
                    OutputTriangleCapacity = 512,
                    OutputPortalCapacity = 64,
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

        private static NavLayeredSpanConfig CreateConfig(
            int scratchSlotCount = 2,
            int columnCapacity = 64,
            int spanCapacity = 128,
            int rasterHaloCells = 1)
        {
            return new NavLayeredSpanConfig
            {
                ScratchSlotCount = scratchSlotCount,
                RasterCellSizeCm = 100,
                RasterHaloCells = rasterHaloCells,
                SameSurfaceToleranceCm = 5,
                MaxSimplificationErrorCm = 0,
                HeightRounding = NavLayeredSpanConfig.HeightRoundingRoundHalfAwayFromZero,
                MaxLawsonFlipCount = 100_000,
                ColumnCapacity = columnCapacity,
                SpanCapacity = spanCapacity,
                ClassifiedSpanCapacity = spanCapacity,
                WalkableSpanCapacity = spanCapacity,
                LinkCapacity = spanCapacity * 4,
                SheetCapacity = spanCapacity,
                PortalIntervalCapacity = spanCapacity * 4,
                RegionCapacity = Math.Max(8, spanCapacity / 2),
                ChartCapacity = Math.Max(8, spanCapacity / 4),
                RingCapacity = Math.Max(8, spanCapacity / 4),
                ContourVertexCapacity = spanCapacity * 4,
                ContourEdgeCapacity = spanCapacity * 4,
                SeamCapacity = spanCapacity,
                CanonicalLinkCapacity = spanCapacity * 4,
                SplitPointCapacity = spanCapacity,
                TriangulationVertexCapacity = spanCapacity * 4,
                TriangulationTriangleCapacity = spanCapacity * 8,
                ConstrainedEdgeCapacity = spanCapacity * 8,
                BorderPortalCapacity = Math.Max(16, spanCapacity / 2),
                PolygonVertexCapacity = spanCapacity * 4,
                AdjacencyEdgeCapacity = spanCapacity * 24,
                BridgeCandidateCapacity = spanCapacity * 4,
                RingWorkCapacity = Math.Max(16, spanCapacity / 2),
                TemporaryConstraintFlagCapacity = spanCapacity * 8
            };
        }

        private static NavTriangleSurfaceSnapshot QuadFloor(
            int minX,
            int minZ,
            int maxX,
            int maxZ,
            int y,
            byte area,
            int stable)
        {
            return new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { minX, maxX, minX, maxX },
                vertexYcm: new[] { y, y, y, y },
                vertexZcm: new[] { minZ, minZ, maxZ, maxZ },
                triA: new[] { 0, 1 },
                triB: new[] { 1, 3 },
                triC: new[] { 2, 2 },
                triAreaIds: new[] { area, area },
                triStableIds: new[] { stable, stable + 1 },
                triFlags: new[] { FloorFlags, FloorFlags });
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

        private static int ComparePortalOrder(in NavBorderPortal a, in NavBorderPortal b)
        {
            int c = ((byte)a.Side).CompareTo((byte)b.Side);
            if (c != 0) return c;
            c = a.LeftXcm.CompareTo(b.LeftXcm);
            if (c != 0) return c;
            c = a.LeftYcm.CompareTo(b.LeftYcm);
            if (c != 0) return c;
            c = a.LeftZcm.CompareTo(b.LeftZcm);
            if (c != 0) return c;
            c = a.RightXcm.CompareTo(b.RightXcm);
            if (c != 0) return c;
            c = a.RightYcm.CompareTo(b.RightYcm);
            if (c != 0) return c;
            c = a.RightZcm.CompareTo(b.RightZcm);
            if (c != 0) return c;
            return a.ClearanceCm.CompareTo(b.ClearanceCm);
        }

        private static string ValidNavmeshJson(int scratchSlotCount = 2)
        {
            return $$"""
                {
                  "mode": "offline",
                  "algorithm": "layered-span",
                  "profiles": [ { "id": "Small", "maxClimbCm": 40, "maxSlopeDeg": 45 } ],
                  "layers": [ { "id": "Ground", "layer": 0 } ],
                  "areas": [],
                  "runtimeIncremental": {
                    "tileBudgetPerFixedTick": 1,
                    "includeNeighborTiles": true,
                    "heightScaleMeters": 1,
                    "minWalkableUpDot": 0.6,
                    "cliffHeightThreshold": 1,
                    "trackedStructuralEntityCapacity": 256,
                    "obstaclePrimitiveCapacity": 512,
                    "polygonVertexCapacity": 4096,
                    "dirtyTileCapacity": 64,
                    "stagedEntryCapacity": 64,
                    "publishedTileCapacity": 64,
                    "storeGroupCapacity": 8,
                    "residentTileCapacity": 128,
                    "outputVertexCapacity": 256,
                    "outputTriangleCapacity": 512,
                    "outputPortalCapacity": 64,
                    "initialResidentChunkX": 0,
                    "initialResidentChunkZ": 0,
                    "initialResidentWidthChunks": 1,
                    "initialResidentHeightChunks": 1
                  },
                  "layeredSpan": {
                    "scratchSlotCount": {{scratchSlotCount}},
                    "rasterCellSizeCm": 100,
                    "rasterHaloCells": 1,
                    "sameSurfaceToleranceCm": 5,
                    "maxSimplificationErrorCm": 0,
                    "heightRounding": "roundHalfAwayFromZero",
                    "maxLawsonFlipCount": 100000,
                    "columnCapacity": 64,
                    "spanCapacity": 128,
                    "classifiedSpanCapacity": 128,
                    "walkableSpanCapacity": 128,
                    "linkCapacity": 256,
                    "sheetCapacity": 128,
                    "portalIntervalCapacity": 256,
                    "regionCapacity": 64,
                    "chartCapacity": 32,
                    "ringCapacity": 32,
                    "contourVertexCapacity": 256,
                    "contourEdgeCapacity": 256,
                    "seamCapacity": 64,
                    "canonicalLinkCapacity": 256,
                    "splitPointCapacity": 64,
                    "triangulationVertexCapacity": 256,
                    "triangulationTriangleCapacity": 512,
                    "constrainedEdgeCapacity": 512,
                    "borderPortalCapacity": 64,
                    "polygonVertexCapacity": 256,
                    "adjacencyEdgeCapacity": 1536,
                    "bridgeCandidateCapacity": 256,
                    "ringWorkCapacity": 64,
                    "temporaryConstraintFlagCapacity": 512
                },
                "triangleSurface": {
                  "haloPaddingCm": 100
                },
                "recast": {
                  "rasterCellSizeCm": 10,
                  "rasterCellHeightCm": 5
                }
                }
                """;
        }

        private static string ValidNavmeshJsonWithLayeredSpanPatch(string layeredBodyPrefix)
        {
            return $$"""
                {
                  "mode": "offline",
                  "algorithm": "layered-span",
                  "profiles": [ { "id": "Small", "maxClimbCm": 40, "maxSlopeDeg": 45 } ],
                  "layers": [ { "id": "Ground", "layer": 0 } ],
                  "areas": [],
                  "runtimeIncremental": {
                    "tileBudgetPerFixedTick": 1,
                    "includeNeighborTiles": true,
                    "heightScaleMeters": 1,
                    "minWalkableUpDot": 0.6,
                    "cliffHeightThreshold": 1,
                    "trackedStructuralEntityCapacity": 256,
                    "obstaclePrimitiveCapacity": 512,
                    "polygonVertexCapacity": 4096,
                    "dirtyTileCapacity": 64,
                    "stagedEntryCapacity": 64,
                    "publishedTileCapacity": 64,
                    "storeGroupCapacity": 8,
                    "residentTileCapacity": 128,
                    "outputVertexCapacity": 256,
                    "outputTriangleCapacity": 512,
                    "outputPortalCapacity": 64,
                    "initialResidentChunkX": 0,
                    "initialResidentChunkZ": 0,
                    "initialResidentWidthChunks": 1,
                    "initialResidentHeightChunks": 1
                  },
                  "layeredSpan": {
                    {{layeredBodyPrefix}}
                    "rasterCellSizeCm": 100,
                    "rasterHaloCells": 1,
                    "sameSurfaceToleranceCm": 5,
                    "maxSimplificationErrorCm": 0,
                    "heightRounding": "roundHalfAwayFromZero",
                    "maxLawsonFlipCount": 100000,
                    "columnCapacity": 64,
                    "spanCapacity": 128,
                    "classifiedSpanCapacity": 128,
                    "walkableSpanCapacity": 128,
                    "linkCapacity": 256,
                    "sheetCapacity": 128,
                    "portalIntervalCapacity": 256,
                    "regionCapacity": 64,
                    "chartCapacity": 32,
                    "ringCapacity": 32,
                    "contourVertexCapacity": 256,
                    "contourEdgeCapacity": 256,
                    "seamCapacity": 64,
                    "canonicalLinkCapacity": 256,
                    "splitPointCapacity": 64,
                    "triangulationVertexCapacity": 256,
                    "triangulationTriangleCapacity": 512,
                    "constrainedEdgeCapacity": 512,
                    "borderPortalCapacity": 64,
                    "polygonVertexCapacity": 256,
                    "adjacencyEdgeCapacity": 1536,
                    "bridgeCandidateCapacity": 256,
                    "ringWorkCapacity": 64,
                    "temporaryConstraintFlagCapacity": 512
                },
                "triangleSurface": {
                  "haloPaddingCm": 100
                },
                "recast": {
                  "rasterCellSizeCm": 10,
                  "rasterCellHeightCm": 5
                }
                }
                """;
        }

        private static string CreateTempNavConfig(string navmeshJson)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "ludots-layered-span-nav-" + Guid.NewGuid().ToString("N"));
            string configs = Path.Combine(tempRoot, "Configs");
            Directory.CreateDirectory(Path.Combine(configs, "Navigation"));
            File.WriteAllText(Path.Combine(configs, "config_catalog.json"),
                """
                [
                  { "Path": "Navigation/navmesh.json", "Policy": "DeepObject" }
                ]
                """);
            File.WriteAllText(Path.Combine(configs, "Navigation", "navmesh.json"), navmeshJson);
            return tempRoot;
        }

        private static NavMeshBakeConfig LoadTempConfig(string root, AgentProfileRegistry profiles)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", root);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            return new NavMeshBakeConfigLoader(pipeline, profiles).Load(catalog);
        }
    }
}
