using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.LogicHeightmap;
using Ludots.NavBake.Recast;
using NUnit.Framework;

namespace Ludots.Tests.NavBake;

public sealed class RecastNavTileBakerTests
{
    private const int ObstacleTestCellSizeCm = 1000;

    [Test]
    public void RecastNavTileBaker_FlatLogicHeightmap_ProducesReadableNavTile()
    {
        var logic = LogicHeightmapQuadGridAdapter.FromSamples(
            sampleColumns: LogicHeightmapChunk.ChunkSize,
            sampleRows: LogicHeightmapChunk.ChunkSize,
            heightCm: Enumerable.Repeat(600, LogicHeightmapChunk.TotalCells).ToArray());
        var cfg = new NavBuildConfig(heightScaleMeters: 2.0f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);
        var profile = new NavAgentProfileConfig
        {
            Id = "GroundLight",
            RadiusCm = 30,
            HeightCm = 180,
            MaxClimbCm = 45,
            MaxSlopeDeg = 45f
        };

        bool ok = RecastNavTileBaker.TryBake(
            logic,
            chunkX: 0,
            chunkY: 0,
            tileVersion: 7,
            cfg,
            profile,
            layer: 3,
            new NavObstacleSet(),
            out var tile,
            out var artifact);

        Assert.That(ok, Is.True, artifact.Message);
        Assert.That(tile.TileId.Layer, Is.EqualTo(3));
        Assert.That(tile.TileVersion, Is.EqualTo(7));
        Assert.That(tile.VertexCount, Is.GreaterThan(0));
        Assert.That(tile.TriangleCount, Is.GreaterThan(0));
        Assert.That(artifact.ErrorCode, Is.EqualTo(NavBakeErrorCode.None));
        Assert.That(artifact.TriangleCount, Is.EqualTo(tile.TriangleCount));

        using var ms = new MemoryStream();
        NavTileBinary.Write(ms, tile);
        ms.Position = 0;
        var readBack = NavTileBinary.Read(ms);
        Assert.That(readBack.TileId.Layer, Is.EqualTo(3));
        Assert.That(readBack.TriangleCount, Is.EqualTo(tile.TriangleCount));
    }

    [Test]
    public void RecastNavTileBaker_LogicHeightmap_UsesUnifiedBakeSource()
    {
        var logic = LogicHeightmapQuadGridAdapter.FromSamples(
            sampleColumns: LogicHeightmapChunk.ChunkSize,
            sampleRows: LogicHeightmapChunk.ChunkSize,
            heightCm: Enumerable.Repeat(600, LogicHeightmapChunk.TotalCells).ToArray());
        var cfg = new NavBuildConfig(heightScaleMeters: 2.0f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);
        var profile = new NavAgentProfileConfig
        {
            Id = "GroundLight",
            RadiusCm = 30,
            HeightCm = 180,
            MaxClimbCm = 45,
            MaxSlopeDeg = 45f
        };

        bool ok = RecastNavTileBaker.TryBake(
            logic,
            chunkX: 0,
            chunkY: 0,
            tileVersion: 8,
            cfg,
            profile,
            layer: 0,
            new NavObstacleSet(),
            out var tile,
            out var artifact);

        Assert.That(ok, Is.True, artifact.Message);
        Assert.That(tile.TileVersion, Is.EqualTo(8));
        Assert.That(tile.VertexCount, Is.GreaterThan(0));
        Assert.That(tile.TriangleCount, Is.GreaterThan(0));
        Assert.That(artifact.ErrorCode, Is.EqualTo(NavBakeErrorCode.None));
    }

    [Test]
    public void RecastNavTileBaker_AdjacentLogicChunksKeepSharedBorderReachable()
    {
        int cellSizeCm = 100;
        int tileExtentCm = LogicHeightmapChunk.ChunkSize * cellSizeCm;
        LogicHeightmap logic = LogicHeightmapQuadGridAdapter.FromSamples(
            sampleColumns: LogicHeightmapChunk.ChunkSize * 2,
            sampleRows: LogicHeightmapChunk.ChunkSize,
            heightCm: Enumerable.Repeat(600, LogicHeightmapChunk.TotalCells * 2).ToArray(),
            cellSizeXCm: cellSizeCm,
            cellSizeZCm: cellSizeCm);
        NavBuildConfig cfg = CreateDefaultBuildConfig();
        NavAgentProfileConfig profile = CreateGroundLightProfile();

        Assert.That(RecastNavTileBaker.TryBake(
            logic,
            chunkX: 0,
            chunkY: 0,
            tileVersion: 31,
            cfg,
            profile,
            layer: 0,
            new NavObstacleSet(),
            out NavTile westTile,
            out NavBakeArtifact westArtifact), Is.True, westArtifact.Message);

        Assert.That(RecastNavTileBaker.TryBake(
            logic,
            chunkX: 1,
            chunkY: 0,
            tileVersion: 31,
            cfg,
            profile,
            layer: 0,
            new NavObstacleSet(),
            out NavTile eastTile,
            out NavBakeArtifact eastArtifact), Is.True, eastArtifact.Message);

        Assert.That(ContainsLocalPoint(westTile, tileExtentCm, tileExtentCm / 2), Is.True,
            "The west tile Recast mesh must be clipped back to the shared border instead of eroding away from it.");
        Assert.That(ContainsLocalPoint(eastTile, 0, tileExtentCm / 2), Is.True,
            "The east tile Recast mesh must be clipped back to the shared border instead of eroding away from it.");

        var store = new NavTileStore(
            _ => throw new FileNotFoundException("Test tiles should already be resident."),
            tileWidthCm: tileExtentCm,
            tileHeightCm: tileExtentCm,
            bakedTileWidthCm: tileExtentCm,
            bakedTileHeightCm: tileExtentCm);
        store.Replace(westTile);
        store.Replace(eastTile);

        var query = new NavQueryService(store);
        NavPathResult path = query.TryFindPath(
            startXcm: tileExtentCm / 2,
            startZcm: tileExtentCm / 2,
            goalXcm: tileExtentCm + (tileExtentCm / 2),
            goalZcm: tileExtentCm / 2,
            maxPortals: 256);

        Assert.That(path.Status, Is.EqualTo(NavPathStatus.Ok));
        Assert.That(path.PathXcm.Length, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void RecastNavTileBaker_QuadLogicHeightmap_UsesCellSizeAndCentimeterHeight()
    {
        int[] heights = Enumerable.Repeat(3200, LogicHeightmapChunk.TotalCells).ToArray();
        LogicHeightmap logic = LogicHeightmapQuadGridAdapter.FromSamples(
            sampleColumns: LogicHeightmapChunk.ChunkSize,
            sampleRows: LogicHeightmapChunk.ChunkSize,
            heights,
            cellSizeXCm: 250,
            cellSizeZCm: 300);
        var cfg = new NavBuildConfig(heightScaleMeters: 2.0f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);
        var profile = new NavAgentProfileConfig
        {
            Id = "GroundLight",
            RadiusCm = 30,
            HeightCm = 180,
            MaxClimbCm = 45,
            MaxSlopeDeg = 45f
        };

        bool ok = RecastNavTileBaker.TryBake(
            logic,
            chunkX: 0,
            chunkY: 0,
            tileVersion: 9,
            cfg,
            profile,
            layer: 0,
            new NavObstacleSet(),
            out var tile,
            out var artifact);

        Assert.That(ok, Is.True, artifact.Message);
        Assert.That(tile.VertexCount, Is.GreaterThan(0));
        Assert.That(tile.TriangleCount, Is.GreaterThan(0));
        Assert.That(tile.VertexYcm.Max(), Is.GreaterThan(1500), "LogicHeightmap centimeters must not be clamped through VertexMap's 4-bit height.");
        Assert.That(tile.VertexXcm.Max(), Is.GreaterThan(12000), "QuadGrid geometry must use LogicHeightmap CellSizeXCm instead of Hex width.");
        Assert.That(tile.VertexZcm.Max(), Is.GreaterThan(15000), "QuadGrid geometry must use LogicHeightmap CellSizeZCm instead of Hex row spacing.");
    }

    [Test]
    public void RecastNavTileBaker_WaterLayerWithoutWater_EmitsValidEmptyTile()
    {
        var logic = LogicHeightmapQuadGridAdapter.FromSamples(
            sampleColumns: LogicHeightmapChunk.ChunkSize,
            sampleRows: LogicHeightmapChunk.ChunkSize,
            heightCm: Enumerable.Repeat(600, LogicHeightmapChunk.TotalCells).ToArray());
        var cfg = new NavBuildConfig(heightScaleMeters: 2.0f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);
        var profile = new NavAgentProfileConfig
        {
            Id = "Naval",
            RadiusCm = 140,
            HeightCm = 260,
            MaxClimbCm = 20,
            MaxSlopeDeg = 8f
        };

        bool ok = RecastNavTileBaker.TryBake(
            logic,
            chunkX: 0,
            chunkY: 0,
            tileVersion: 10,
            cfg,
            profile,
            layer: 1,
            new NavObstacleSet(),
            out var tile,
            out var artifact);

        Assert.That(ok, Is.True, artifact.Message);
        Assert.That(tile.TileId.Layer, Is.EqualTo(1));
        Assert.That(tile.TriangleCount, Is.EqualTo(0));
        Assert.That(tile.Portals.Length, Is.EqualTo(0));
        Assert.That(artifact.ErrorCode, Is.EqualTo(NavBakeErrorCode.None));

        using var ms = new MemoryStream();
        NavTileBinary.Write(ms, tile);
        ms.Position = 0;
        var readBack = NavTileBinary.Read(ms);
        Assert.That(readBack.TileId.Layer, Is.EqualTo(1));
        Assert.That(readBack.TriangleCount, Is.EqualTo(0));
    }

    [Test]
    public void RecastNavTileBaker_RuntimePolygonObstacleCutsNavMeshAndQueryAvoidsHole()
    {
        LogicHeightmap logic = CreateFlatQuadLogicHeightmap(ObstacleTestCellSizeCm);
        NavBuildConfig cfg = CreateDefaultBuildConfig();
        NavAgentProfileConfig profile = CreateGroundLightProfile();

        Assert.That(RecastNavTileBaker.TryBake(
            logic,
            chunkX: 0,
            chunkY: 0,
            tileVersion: 21,
            cfg,
            profile,
            layer: 0,
            new NavObstacleSet(),
            out NavTile baseTile,
            out NavBakeArtifact baseArtifact), Is.True, baseArtifact.Message);

        var obstacle = new NavObstacle
        {
            Id = "runtime_rect",
            Enabled = true,
            Kind = NavObstacleKind.Polygon,
            LayerId = "Ground",
            Points =
            {
                new NavPointCm(26_000, 12_000),
                new NavPointCm(36_000, 12_000),
                new NavPointCm(36_000, 50_000),
                new NavPointCm(26_000, 50_000)
            }
        };
        var obstacles = new NavObstacleSet();
        obstacles.Obstacles.Add(obstacle);

        Assert.That(RecastNavTileBaker.TryBake(
            logic,
            chunkX: 0,
            chunkY: 0,
            tileVersion: 22,
            cfg,
            profile,
            layer: 0,
            obstacles,
            out NavTile obstacleTile,
            out NavBakeArtifact obstacleArtifact), Is.True, obstacleArtifact.Message);

        Assert.That(baseTile.TriangleCount, Is.GreaterThan(0));
        Assert.That(obstacleTile.TriangleCount, Is.GreaterThan(0));
        Assert.That(obstacleTile.Checksum, Is.Not.EqualTo(baseTile.Checksum),
            "Runtime authored polygon obstacles must change baked navmesh geometry, not just presentation state.");

        int tileExtentCm = LogicHeightmapChunk.ChunkSize * ObstacleTestCellSizeCm;
        var store = new NavTileStore(
            _ => throw new FileNotFoundException("Test tile should already be resident."),
            tileWidthCm: tileExtentCm,
            tileHeightCm: tileExtentCm,
            bakedTileWidthCm: tileExtentCm,
            bakedTileHeightCm: tileExtentCm);
        store.Replace(obstacleTile);
        var query = new NavQueryService(store);

        Assert.That(query.TryProject(31_000, 31_000, out _), Is.False,
            "The obstacle center must not project onto a walkable triangle after runtime bake.");

        NavPathResult path = query.TryFindPath(
            startXcm: 10_000,
            startZcm: 31_000,
            goalXcm: 52_000,
            goalZcm: 31_000,
            maxPortals: 16_384);

        Assert.That(path.Status, Is.EqualTo(NavPathStatus.Ok));
        Assert.That(path.PathXcm.Length, Is.GreaterThan(2),
            "A same-tile path across the authored obstacle must keep a detour waypoint instead of returning a direct line.");
        Assert.That(path.PathXcm.Length, Is.EqualTo(path.PathZcm.Length));
        AssertPathDoesNotEnterPolygonInterior(path, obstacle.Points);
    }

    [Test]
    public void RecastNavTileBaker_RuntimeBorderObstacleRegeneratesPortalsForCrossChunkQuery()
    {
        const int cellSizeCm = 250;
        int tileExtentCm = LogicHeightmapChunk.ChunkSize * cellSizeCm;
        LogicHeightmap logic = LogicHeightmapQuadGridAdapter.FromSamples(
            sampleColumns: LogicHeightmapChunk.ChunkSize * 2,
            sampleRows: LogicHeightmapChunk.ChunkSize,
            heightCm: Enumerable.Repeat(600, LogicHeightmapChunk.TotalCells * 2).ToArray(),
            cellSizeXCm: cellSizeCm,
            cellSizeZCm: cellSizeCm);
        NavBuildConfig cfg = CreateDefaultBuildConfig();
        NavAgentProfileConfig profile = CreateGroundLightProfile();

        var obstacles = new NavObstacleSet();
        obstacles.Obstacles.Add(CreateRectObstacle(
            "shared_border_lower_block",
            tileExtentCm - 1_000,
            0,
            tileExtentCm + 1_000,
            12_000));

        Assert.That(RecastNavTileBaker.TryBake(
            logic,
            chunkX: 0,
            chunkY: 0,
            tileVersion: 41,
            cfg,
            profile,
            layer: 0,
            obstacles,
            out NavTile westTile,
            out NavBakeArtifact westArtifact), Is.True, westArtifact.Message);

        Assert.That(RecastNavTileBaker.TryBake(
            logic,
            chunkX: 1,
            chunkY: 0,
            tileVersion: 41,
            cfg,
            profile,
            layer: 0,
            obstacles,
            out NavTile eastTile,
            out NavBakeArtifact eastArtifact), Is.True, eastArtifact.Message);

        Assert.That(HasPortalCovering(westTile, NavPortalSide.East, 48, 63), Is.True,
            "Runtime Recast output must split the east border portal around the actual authored opening.\n" + DumpTile(westTile));
        Assert.That(HasPortalCovering(eastTile, NavPortalSide.West, 48, 63), Is.True,
            "Neighbor runtime Recast output must expose the same shared opening.\n" + DumpTile(eastTile));
        Assert.That(HasPortalCovering(westTile, NavPortalSide.East, 30, 34), Is.False,
            "Blocked border spans must not keep the old full-edge base portal after runtime Recast.\n" + DumpTile(westTile));

        var store = new NavTileStore(
            _ => throw new FileNotFoundException("Test tiles should already be resident."),
            tileWidthCm: tileExtentCm,
            tileHeightCm: tileExtentCm,
            bakedTileWidthCm: tileExtentCm,
            bakedTileHeightCm: tileExtentCm);
        store.Replace(westTile);
        store.Replace(eastTile);

        var query = new NavQueryService(store);
        NavPathResult path = query.TryFindPath(
            startXcm: tileExtentCm - 4_000,
            startZcm: 14_000,
            goalXcm: tileExtentCm + 4_000,
            goalZcm: 14_000,
            maxPortals: 512);

        Assert.That(path.Status, Is.EqualTo(NavPathStatus.Ok));
        Assert.That(path.PathXcm.Length, Is.GreaterThanOrEqualTo(2));
    }

    private static LogicHeightmap CreateFlatQuadLogicHeightmap(int cellSizeCm)
    {
        return LogicHeightmapQuadGridAdapter.FromSamples(
            sampleColumns: LogicHeightmapChunk.ChunkSize,
            sampleRows: LogicHeightmapChunk.ChunkSize,
            heightCm: Enumerable.Repeat(600, LogicHeightmapChunk.TotalCells).ToArray(),
            cellSizeXCm: cellSizeCm,
            cellSizeZCm: cellSizeCm);
    }

    private static NavObstacle CreateRectObstacle(string id, int minXcm, int minZcm, int maxXcm, int maxZcm)
    {
        var obstacle = new NavObstacle
        {
            Id = id,
            Enabled = true,
            Kind = NavObstacleKind.Polygon,
            LayerId = "Ground",
        };
        obstacle.Points.Add(new NavPointCm(minXcm, minZcm));
        obstacle.Points.Add(new NavPointCm(maxXcm, minZcm));
        obstacle.Points.Add(new NavPointCm(maxXcm, maxZcm));
        obstacle.Points.Add(new NavPointCm(minXcm, maxZcm));
        return obstacle;
    }

    private static bool HasPortalCovering(NavTile tile, NavPortalSide side, int expectedStart, int expectedEnd)
    {
        for (int i = 0; i < tile.Portals.Length; i++)
        {
            NavBorderPortal portal = tile.Portals[i];
            if (portal.Side != side)
            {
                continue;
            }

            int start;
            int end;
            if (side == NavPortalSide.West || side == NavPortalSide.East)
            {
                start = Math.Min(portal.V0, portal.V1);
                end = Math.Max(portal.V0, portal.V1);
            }
            else
            {
                start = Math.Min(portal.U0, portal.U1);
                end = Math.Max(portal.U0, portal.U1);
            }

            if (start <= expectedStart && end >= expectedEnd)
            {
                return true;
            }
        }

        return false;
    }

    private static string DumpPortals(NavTile tile, NavPortalSide side)
    {
        var parts = new List<string>();
        for (int i = 0; i < tile.Portals.Length; i++)
        {
            NavBorderPortal portal = tile.Portals[i];
            if (portal.Side != side)
            {
                continue;
            }

            int start;
            int end;
            if (side == NavPortalSide.West || side == NavPortalSide.East)
            {
                start = Math.Min(portal.V0, portal.V1);
                end = Math.Max(portal.V0, portal.V1);
            }
            else
            {
                start = Math.Min(portal.U0, portal.U1);
                end = Math.Max(portal.U0, portal.U1);
            }

            parts.Add($"{start}->{end} local=({portal.LeftXcm},{portal.LeftZcm})->({portal.RightXcm},{portal.RightZcm})");
        }

        return parts.Count == 0 ? "portals: <none>" : "portals: " + string.Join("; ", parts);
    }

    private static string DumpTile(NavTile tile)
    {
        string bounds = tile.VertexCount == 0
            ? "bounds=<empty>"
            : $"bounds={tile.VertexXcm.Min()},{tile.VertexZcm.Min()}->{tile.VertexXcm.Max()},{tile.VertexZcm.Max()}";
        return $"triangles={tile.TriangleCount}; portals={tile.Portals.Length}; {bounds}; " +
            $"E {DumpPortals(tile, NavPortalSide.East)}; W {DumpPortals(tile, NavPortalSide.West)}; " +
            $"N {DumpPortals(tile, NavPortalSide.North)}; S {DumpPortals(tile, NavPortalSide.South)}";
    }

    private static NavBuildConfig CreateDefaultBuildConfig()
    {
        return new NavBuildConfig(heightScaleMeters: 2.0f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);
    }

    private static NavAgentProfileConfig CreateGroundLightProfile()
    {
        return new NavAgentProfileConfig
        {
            Id = "GroundLight",
            RadiusCm = 30,
            HeightCm = 180,
            MaxClimbCm = 45,
            MaxSlopeDeg = 45f
        };
    }

    private static void AssertPathDoesNotEnterPolygonInterior(NavPathResult path, IReadOnlyList<NavPointCm> polygon)
    {
        for (int i = 1; i < path.PathXcm.Length; i++)
        {
            int ax = path.PathXcm[i - 1];
            int az = path.PathZcm[i - 1];
            int bx = path.PathXcm[i];
            int bz = path.PathZcm[i];
            for (int s = 1; s < 64; s++)
            {
                int x = ax + ((bx - ax) * s / 64);
                int z = az + ((bz - az) * s / 64);
                Assert.That(PointInPolygonInterior(x, z, polygon), Is.False,
                    $"Path segment {i - 1}->{i} enters authored obstacle polygon at {x},{z}.");
            }
        }
    }

    private static bool ContainsLocalPoint(NavTile tile, int localXcm, int localZcm)
    {
        for (int i = 0; i < tile.TriangleCount; i++)
        {
            int a = tile.TriA[i];
            int b = tile.TriB[i];
            int c = tile.TriC[i];
            if (PointInTriangleOrBoundary(
                    localXcm,
                    localZcm,
                    tile.VertexXcm[a],
                    tile.VertexZcm[a],
                    tile.VertexXcm[b],
                    tile.VertexZcm[b],
                    tile.VertexXcm[c],
                    tile.VertexZcm[c]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PointInTriangleOrBoundary(
        int px,
        int pz,
        int ax,
        int az,
        int bx,
        int bz,
        int cx,
        int cz)
    {
        long area = Orient2D(ax, az, bx, bz, cx, cz);
        if (area == 0)
        {
            return false;
        }

        long ab = Orient2D(ax, az, bx, bz, px, pz);
        long bc = Orient2D(bx, bz, cx, cz, px, pz);
        long ca = Orient2D(cx, cz, ax, az, px, pz);
        return area > 0
            ? ab >= 0 && bc >= 0 && ca >= 0
            : ab <= 0 && bc <= 0 && ca <= 0;
    }

    private static long Orient2D(int ax, int az, int bx, int bz, int cx, int cz)
    {
        return ((long)bx - ax) * ((long)cz - az) - (((long)bz - az) * ((long)cx - ax));
    }

    private static bool PointInPolygonInterior(int xcm, int zcm, IReadOnlyList<NavPointCm> polygon)
    {
        bool inside = false;
        int j = polygon.Count - 1;
        for (int i = 0; i < polygon.Count; j = i++)
        {
            int xi = polygon[i].Xcm;
            int zi = polygon[i].Zcm;
            int xj = polygon[j].Xcm;
            int zj = polygon[j].Zcm;
            if ((zi > zcm) == (zj > zcm))
            {
                continue;
            }

            double xInt = (double)(xj - xi) * (zcm - zi) / (zj - zi) + xi;
            if (xcm < xInt)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
