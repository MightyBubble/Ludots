using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Map.Hex;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Spatial;

namespace Ludots.Core.Navigation.NavMesh.Surface
{
    /// <summary>
    /// Cold-path compiler from <see cref="LogicTerrainField"/> to immutable
    /// <see cref="NavTriangleSurfaceSnapshot"/> + tile CSR. Geometry rules match <see cref="NavTileBuilder"/>.
    /// </summary>
    public static class LogicTerrainTriangleSurfaceCompiler
    {
        private const int FacesPerCell = 2;
        private const int MaxSplitPartsPerFace = 4;

        private readonly struct Vtx
        {
            public readonly int C;
            public readonly int R;
            public readonly Vector3 Pos;
            public readonly float WaterY;
            public readonly byte H;
            public readonly byte W;
            public readonly bool IsRamp;
            public readonly bool IsBlocked;
            public readonly byte AreaId;

            public Vtx(int c, int r, Vector3 pos, float waterY, byte h, byte w, bool isRamp, bool isBlocked, byte areaId)
            {
                C = c;
                R = r;
                Pos = pos;
                WaterY = waterY;
                H = h;
                W = w;
                IsRamp = isRamp;
                IsBlocked = isBlocked;
                AreaId = areaId;
            }
        }

        private readonly struct SplitPoints
        {
            public readonly Vector3 HighExt;
            public readonly float HighWaterY;
            public readonly Vector3 LowExt;
            public readonly float LowWaterY;

            public SplitPoints(Vector3 highExt, float highWaterY, Vector3 lowExt, float lowWaterY)
            {
                HighExt = highExt;
                HighWaterY = highWaterY;
                LowExt = lowExt;
                LowWaterY = lowWaterY;
            }
        }

        private readonly struct VertexKey : IEquatable<VertexKey>
        {
            public readonly int Xcm;
            public readonly int Ycm;
            public readonly int Zcm;

            public VertexKey(int xcm, int ycm, int zcm)
            {
                Xcm = xcm;
                Ycm = ycm;
                Zcm = zcm;
            }

            public bool Equals(VertexKey other) => Xcm == other.Xcm && Ycm == other.Ycm && Zcm == other.Zcm;
            public override bool Equals(object obj) => obj is VertexKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Xcm, Ycm, Zcm);
        }

        private readonly struct PendingTriangle
        {
            public readonly int StableId;
            public readonly int A;
            public readonly int B;
            public readonly int C;
            public readonly byte AreaId;

            public PendingTriangle(int stableId, int a, int b, int c, byte areaId)
            {
                StableId = stableId;
                A = a;
                B = b;
                C = c;
                AreaId = areaId;
            }
        }

        public static NavTriangleSurfaceTileIndex Compile(
            LogicTerrainField terrain,
            NavMeshBakeConfig bakeConfig,
            in NavBuildConfig buildConfig)
        {
            if (bakeConfig == null) throw new ArgumentNullException(nameof(bakeConfig));
            if (bakeConfig.TriangleSurface == null)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.triangleSurface is required.");
            }

            bakeConfig.TriangleSurface.Validate(layeredSpan: bakeConfig.LayeredSpan);
            return Compile(terrain, buildConfig, bakeConfig.TriangleSurface);
        }

        /// <summary>
        /// Authoritative tile grid for a LogicTerrain field (origin + tile size + counts + halo).
        /// Shared by cold compile and offline query tile-space composition — no Hex query defaults.
        /// </summary>
        public static NavTriangleSurfaceTileGrid DeriveTileGrid(
            LogicTerrainField terrain,
            NavTriangleSurfaceConfig triangleSurface)
        {
            if (triangleSurface == null) throw new ArgumentNullException(nameof(triangleSurface));
            triangleSurface.Validate();
            return DeriveTileGrid(
                terrain,
                triangleSurface.HaloPaddingCm,
                triangleSurface.TileSubdivisionsX,
                triangleSurface.TileSubdivisionsZ,
                "NavMeshBakeConfig.triangleSurface");
        }

        public static NavTriangleSurfaceTileGrid DeriveTileGrid(LogicTerrainField terrain, int haloPaddingCm)
            => DeriveTileGrid(terrain, haloPaddingCm, tileSubdivisionsX: 1, tileSubdivisionsZ: 1, "NavTriangleSurfaceConfig");

        private static NavTriangleSurfaceTileGrid DeriveTileGrid(
            LogicTerrainField terrain,
            int haloPaddingCm,
            int tileSubdivisionsX,
            int tileSubdivisionsZ,
            string path)
        {
            if (terrain == null) throw new ArgumentNullException(nameof(terrain));
            if (haloPaddingCm < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(haloPaddingCm), haloPaddingCm, "Halo/padding must be nonnegative.");
            }

            if (tileSubdivisionsX <= 0)
            {
                throw new InvalidOperationException($"{path}.tileSubdivisionsX must be > 0.");
            }

            if (tileSubdivisionsZ <= 0)
            {
                throw new InvalidOperationException($"{path}.tileSubdivisionsZ must be > 0.");
            }

            terrain.GetWorldPositionMeters(0, 0, out float originXm, out float originZm);
            int originXcm = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(originXm));
            int originZcm = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(originZm));
            // Hex world XZ spacing is HexWidth/RowSpacing (meters), not EdgeLengthCm.
            int terrainChunkWidthCm;
            int terrainChunkHeightCm;
            if (terrain.Topology == LogicTerrainTopology.Hex)
            {
                var metrics = new HexMetrics(terrain.HorizontalStepCm);
                terrainChunkWidthCm = checked((int)MathF.Round(
                    metrics.HexWidthCm * terrain.ChunkSizeCells));
                terrainChunkHeightCm = checked((int)MathF.Round(
                    metrics.RowSpacingCm * terrain.ChunkSizeCells));
            }
            else
            {
                terrainChunkWidthCm = checked(terrain.ChunkSizeCells * terrain.HorizontalStepCm);
                terrainChunkHeightCm = checked(terrain.ChunkSizeCells * terrain.VerticalStepCm);
            }

            if (terrainChunkWidthCm % tileSubdivisionsX != 0)
            {
                throw new InvalidOperationException(
                    $"{path}.tileSubdivisionsX ({tileSubdivisionsX}) must divide the derived " +
                    $"terrain chunk width {terrainChunkWidthCm}cm for topology {terrain.Topology}.");
            }

            if (terrainChunkHeightCm % tileSubdivisionsZ != 0)
            {
                throw new InvalidOperationException(
                    $"{path}.tileSubdivisionsZ ({tileSubdivisionsZ}) must divide the derived " +
                    $"terrain chunk height {terrainChunkHeightCm}cm for topology {terrain.Topology}.");
            }

            int tileWidthCm = terrainChunkWidthCm / tileSubdivisionsX;
            int tileHeightCm = terrainChunkHeightCm / tileSubdivisionsZ;
            if (tileWidthCm <= 0 || tileHeightCm <= 0)
            {
                throw new InvalidOperationException(
                    $"LogicTerrainTriangleSurfaceCompiler derived non-positive tile size ({tileWidthCm}x{tileHeightCm}) for topology {terrain.Topology}.");
            }

            return new NavTriangleSurfaceTileGrid(
                originXcm,
                originZcm,
                tileWidthCm,
                tileHeightCm,
                checked(terrain.WidthChunks * tileSubdivisionsX),
                checked(terrain.HeightChunks * tileSubdivisionsZ),
                haloPaddingCm);
        }

        public static NavTriangleSurfaceTileIndex Compile(
            LogicTerrainField terrain,
            in NavBuildConfig buildConfig,
            NavTriangleSurfaceConfig triangleSurface)
        {
            if (triangleSurface == null) throw new ArgumentNullException(nameof(triangleSurface));
            triangleSurface.Validate();
            return Compile(
                terrain,
                buildConfig,
                triangleSurface.HaloPaddingCm,
                triangleSurface.TileSubdivisionsX,
                triangleSurface.TileSubdivisionsZ);
        }

        public static NavTriangleSurfaceTileIndex Compile(
            LogicTerrainField terrain,
            in NavBuildConfig buildConfig,
            int haloPaddingCm)
            => Compile(terrain, buildConfig, haloPaddingCm, tileSubdivisionsX: 1, tileSubdivisionsZ: 1);

        private static NavTriangleSurfaceTileIndex Compile(
            LogicTerrainField terrain,
            in NavBuildConfig buildConfig,
            int haloPaddingCm,
            int tileSubdivisionsX,
            int tileSubdivisionsZ)
        {
            if (terrain == null) throw new ArgumentNullException(nameof(terrain));
            if (haloPaddingCm < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(haloPaddingCm), haloPaddingCm, "Halo/padding must be nonnegative.");
            }

            // Uniform FlatGrid is O(chunks): two triangles per terrain chunk, never per-cell enumeration.
            if (terrain.IsUniformFlatGridSurface && terrain.Topology == LogicTerrainTopology.Grid)
            {
                return CompileUniformFlatGrid(terrain, buildConfig, haloPaddingCm, tileSubdivisionsX, tileSubdivisionsZ);
            }

            int mapWidth = terrain.WidthCells;
            int mapHeight = terrain.HeightCells;

            var vertexIndex = new Dictionary<VertexKey, int>();
            var vx = new List<int>();
            var vy = new List<int>();
            var vz = new List<int>();
            var pending = new List<PendingTriangle>();

            for (int r = 0; r < mapHeight; r++)
            {
                for (int c = 0; c < mapWidth; c++)
                {
                    if (r >= mapHeight - 1 || c >= mapWidth - 1)
                    {
                        continue;
                    }

                    bool isOdd = terrain.Topology == LogicTerrainTopology.Hex && (r & 1) == 1;
                    var v1 = GetVertex(terrain, mapWidth, mapHeight, c, r, buildConfig.HeightScaleMeters);

                    Vtx t1p1, t1p2, t1p3;
                    Vtx t2p1, t2p2, t2p3;

                    if (!isOdd)
                    {
                        t1p1 = v1;
                        t1p2 = GetVertex(terrain, mapWidth, mapHeight, c + 1, r, buildConfig.HeightScaleMeters);
                        t1p3 = GetVertex(terrain, mapWidth, mapHeight, c, r + 1, buildConfig.HeightScaleMeters);

                        t2p1 = t1p2;
                        t2p2 = GetVertex(terrain, mapWidth, mapHeight, c + 1, r + 1, buildConfig.HeightScaleMeters);
                        t2p3 = t1p3;
                    }
                    else
                    {
                        t1p1 = v1;
                        t1p2 = GetVertex(terrain, mapWidth, mapHeight, c + 1, r, buildConfig.HeightScaleMeters);
                        t1p3 = GetVertex(terrain, mapWidth, mapHeight, c + 1, r + 1, buildConfig.HeightScaleMeters);

                        t2p1 = v1;
                        t2p2 = t1p3;
                        t2p3 = GetVertex(terrain, mapWidth, mapHeight, c, r + 1, buildConfig.HeightScaleMeters);
                    }

                    AddFace(
                        terrain,
                        mapWidth,
                        mapHeight,
                        buildConfig,
                        r,
                        c,
                        faceIndex: 0,
                        t1p1,
                        t1p2,
                        t1p3,
                        vertexIndex,
                        vx,
                        vy,
                        vz,
                        pending);
                    AddFace(
                        terrain,
                        mapWidth,
                        mapHeight,
                        buildConfig,
                        r,
                        c,
                        faceIndex: 1,
                        t2p1,
                        t2p2,
                        t2p3,
                        vertexIndex,
                        vx,
                        vy,
                        vz,
                        pending);
                }
            }

            return FinishCompile(terrain, haloPaddingCm, tileSubdivisionsX, tileSubdivisionsZ, vertexIndex, vx, vy, vz, pending);
        }

        /// <summary>
        /// Sparse FlatGrid cold path: sample the uniform cell once, then emit exactly two
        /// upward-wound walk-candidate solid triangles per terrain chunk (including partial
        /// final chunks). Blocked or submerged uniform fields emit a valid empty surface.
        /// </summary>
        private static NavTriangleSurfaceTileIndex CompileUniformFlatGrid(
            LogicTerrainField terrain,
            in NavBuildConfig buildConfig,
            int haloPaddingCm,
            int tileSubdivisionsX,
            int tileSubdivisionsZ)
        {
            int mapWidth = terrain.WidthCells;
            int mapHeight = terrain.HeightCells;
            if (mapWidth <= 0 || mapHeight <= 0)
            {
                throw new InvalidOperationException(
                    $"FlatGrid LogicTerrainField extents must be positive; got {mapWidth}x{mapHeight}.");
            }

            // One cell sample owns uniform height/water/blocked/area for the whole field.
            LogicTerrainCell cell = terrain.GetCell(0, 0);
            float heightY = cell.HeightLevel * buildConfig.HeightScaleMeters;
            float waterY = cell.WaterHeightLevel * buildConfig.HeightScaleMeters;
            bool submerged = waterY > heightY;
            bool emitWalkable = !cell.IsBlocked && !submerged;

            var vertexIndex = new Dictionary<VertexKey, int>();
            var vx = new List<int>();
            var vy = new List<int>();
            var vz = new List<int>();
            var pending = new List<PendingTriangle>();

            int widthChunks = terrain.WidthChunks;
            int heightChunks = terrain.HeightChunks;
            int chunkSizeCells = terrain.ChunkSizeCells;
            int stepXcm = terrain.HorizontalStepCm;
            int stepZcm = terrain.VerticalStepCm;

            terrain.GetWorldPositionMeters(0, 0, out float originXm, out float originZm);
            int originXcm = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(originXm));
            int originZcm = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(originZm));
            int heightYcm = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(heightY));

            if (emitWalkable)
            {
                for (int chunkY = 0; chunkY < heightChunks; chunkY++)
                {
                    int cellY0 = checked(chunkY * chunkSizeCells);
                    int cellY1 = Math.Min(mapHeight, checked(cellY0 + chunkSizeCells));
                    if (cellY1 <= cellY0)
                    {
                        continue;
                    }

                    int z0cm = checked(originZcm + cellY0 * stepZcm);
                    int z1cm = checked(originZcm + cellY1 * stepZcm);

                    for (int chunkX = 0; chunkX < widthChunks; chunkX++)
                    {
                        int cellX0 = checked(chunkX * chunkSizeCells);
                        int cellX1 = Math.Min(mapWidth, checked(cellX0 + chunkSizeCells));
                        if (cellX1 <= cellX0)
                        {
                            continue;
                        }

                        int x0cm = checked(originXcm + cellX0 * stepXcm);
                        int x1cm = checked(originXcm + cellX1 * stepXcm);

                        // Deterministic upward-wound order matching DefaultGridNavTileFactory:
                        // SW(0), SE(1), NE(2), NW(3); tri0=0,1,3 ; tri1=1,2,3.
                        int iSw = GetOrAddVertexCm(x0cm, heightYcm, z0cm, vertexIndex, vx, vy, vz);
                        int iSe = GetOrAddVertexCm(x1cm, heightYcm, z0cm, vertexIndex, vx, vy, vz);
                        int iNe = GetOrAddVertexCm(x1cm, heightYcm, z1cm, vertexIndex, vx, vy, vz);
                        int iNw = GetOrAddVertexCm(x0cm, heightYcm, z1cm, vertexIndex, vx, vy, vz);

                        // Stable ids use the chunk origin cell so they stay deterministic across sparse compiles.
                        int stable0 = EncodeStableId(mapWidth, cellY0, cellX0, faceIndex: 0, splitPart: 0);
                        int stable1 = EncodeStableId(mapWidth, cellY0, cellX0, faceIndex: 1, splitPart: 0);
                        pending.Add(new PendingTriangle(stable0, iSw, iSe, iNw, cell.AreaId));
                        pending.Add(new PendingTriangle(stable1, iSe, iNe, iNw, cell.AreaId));
                    }
                }
            }

            return FinishCompile(terrain, haloPaddingCm, tileSubdivisionsX, tileSubdivisionsZ, vertexIndex, vx, vy, vz, pending);
        }

        private static NavTriangleSurfaceTileIndex FinishCompile(
            LogicTerrainField terrain,
            int haloPaddingCm,
            int tileSubdivisionsX,
            int tileSubdivisionsZ,
            Dictionary<VertexKey, int> vertexIndex,
            List<int> vx,
            List<int> vy,
            List<int> vz,
            List<PendingTriangle> pending)
        {
            _ = vertexIndex;
            pending.Sort(static (a, b) => a.StableId.CompareTo(b.StableId));

            int triCount = pending.Count;
            var triA = new int[triCount];
            var triB = new int[triCount];
            var triC = new int[triCount];
            var triAreaIds = new byte[triCount];
            var triStableIds = new int[triCount];
            var triFlags = new NavTriangleSurfaceFlags[triCount];
            var walkFlags = NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;

            for (int i = 0; i < triCount; i++)
            {
                PendingTriangle tri = pending[i];
                triA[i] = tri.A;
                triB[i] = tri.B;
                triC[i] = tri.C;
                triAreaIds[i] = tri.AreaId;
                triStableIds[i] = tri.StableId;
                triFlags[i] = walkFlags;
            }

            var snapshot = new NavTriangleSurfaceSnapshot(
                vx.ToArray(),
                vy.ToArray(),
                vz.ToArray(),
                triA,
                triB,
                triC,
                triAreaIds,
                triStableIds,
                triFlags);

            NavTriangleSurfaceTileGrid grid = DeriveTileGrid(
                terrain,
                haloPaddingCm,
                tileSubdivisionsX,
                tileSubdivisionsZ,
                "NavMeshBakeConfig.triangleSurface");
            return NavTriangleSurfaceTileIndex.Build(snapshot, grid);
        }

        private static int GetOrAddVertexCm(
            int xcm,
            int ycm,
            int zcm,
            Dictionary<VertexKey, int> vertexIndex,
            List<int> vx,
            List<int> vy,
            List<int> vz)
        {
            var key = new VertexKey(xcm, ycm, zcm);
            if (vertexIndex.TryGetValue(key, out int idx))
            {
                return idx;
            }

            idx = vx.Count;
            vertexIndex.Add(key, idx);
            vx.Add(xcm);
            vy.Add(ycm);
            vz.Add(zcm);
            return idx;
        }

        private static int EncodeStableId(int mapWidth, int row, int col, int faceIndex, int splitPart)
            => checked((((row * mapWidth + col) * FacesPerCell + faceIndex) * MaxSplitPartsPerFace + splitPart));

        private static Vtx GetVertex(
            LogicTerrainField terrain,
            int mapWidth,
            int mapHeight,
            int c,
            int r,
            float heightScale)
        {
            byte h = 0;
            byte w = 0;
            bool ramp = false;
            bool blocked = false;
            byte areaId = 0;

            if ((uint)c < (uint)mapWidth && (uint)r < (uint)mapHeight)
            {
                LogicTerrainCell cell = terrain.GetCell(c, r);
                h = cell.HeightLevel;
                w = cell.WaterHeightLevel;
                ramp = cell.IsRamp;
                blocked = cell.IsBlocked;
                areaId = cell.AreaId;
            }

            terrain.GetWorldPositionMeters(c, r, out float worldX, out float worldZ);
            float y = h * heightScale;
            float waterY = w * heightScale;
            return new Vtx(c, r, new Vector3(worldX, y, worldZ), waterY, h, w, ramp, blocked, areaId);
        }

        private static void AddFace(
            LogicTerrainField terrain,
            int mapWidth,
            int mapHeight,
            in NavBuildConfig config,
            int row,
            int col,
            int faceIndex,
            in Vtx p1,
            in Vtx p2,
            in Vtx p3,
            Dictionary<VertexKey, int> vertexIndex,
            List<int> vx,
            List<int> vy,
            List<int> vz,
            List<PendingTriangle> pending)
        {
            byte minH = Math.Min(p1.H, Math.Min(p2.H, p3.H));
            byte maxH = Math.Max(p1.H, Math.Max(p2.H, p3.H));
            byte areaId = ResolveAreaId(p1.AreaId, p2.AreaId, p3.AreaId);

            if (p1.IsBlocked || p2.IsBlocked || p3.IsBlocked)
            {
                return;
            }

            if (minH == maxH)
            {
                TryAppendWalkableTri(
                    config,
                    mapWidth,
                    row,
                    col,
                    faceIndex,
                    splitPart: 0,
                    p1.Pos,
                    p1.WaterY,
                    p2.Pos,
                    p2.WaterY,
                    p3.Pos,
                    p3.WaterY,
                    areaId,
                    vertexIndex,
                    vx,
                    vy,
                    vz,
                    pending);
                return;
            }

            bool isRamp = p1.IsRamp || p2.IsRamp || p3.IsRamp;
            if (isRamp)
            {
                TryAppendWalkableTri(
                    config,
                    mapWidth,
                    row,
                    col,
                    faceIndex,
                    splitPart: 0,
                    p1.Pos,
                    p1.WaterY,
                    p2.Pos,
                    p2.WaterY,
                    p3.Pos,
                    p3.WaterY,
                    areaId,
                    vertexIndex,
                    vx,
                    vy,
                    vz,
                    pending);
                return;
            }

            if (p1.H != p2.H && p1.H != p3.H && p2.H != p3.H)
            {
                return;
            }

            Vtx a = p1;
            Vtx b = p2;
            Vtx c = p3;
            if (a.H < b.H) (a, b) = (b, a);
            if (a.H < c.H) (a, c) = (c, a);
            if (b.H < c.H) (b, c) = (c, b);

            if (a.H == b.H)
            {
                Vtx h1 = a;
                Vtx h2 = b;
                Vtx l = c;

                if (TryGetSplit(terrain, mapWidth, mapHeight, config.HeightScaleMeters, h1, l, out var m1) &&
                    TryGetSplit(terrain, mapWidth, mapHeight, config.HeightScaleMeters, h2, l, out var m2))
                {
                    TryAppendWalkableTri(
                        config,
                        mapWidth,
                        row,
                        col,
                        faceIndex,
                        splitPart: 0,
                        h1.Pos,
                        h1.WaterY,
                        h2.Pos,
                        h2.WaterY,
                        m1.HighExt,
                        m1.HighWaterY,
                        ResolveAreaId(h1.AreaId, h2.AreaId, h1.AreaId),
                        vertexIndex,
                        vx,
                        vy,
                        vz,
                        pending);
                    TryAppendWalkableTri(
                        config,
                        mapWidth,
                        row,
                        col,
                        faceIndex,
                        splitPart: 1,
                        h2.Pos,
                        h2.WaterY,
                        m2.HighExt,
                        m2.HighWaterY,
                        m1.HighExt,
                        m1.HighWaterY,
                        ResolveAreaId(h2.AreaId, h1.AreaId, h2.AreaId),
                        vertexIndex,
                        vx,
                        vy,
                        vz,
                        pending);
                    TryAppendWalkableTri(
                        config,
                        mapWidth,
                        row,
                        col,
                        faceIndex,
                        splitPart: 2,
                        l.Pos,
                        l.WaterY,
                        m2.LowExt,
                        m2.LowWaterY,
                        m1.LowExt,
                        m1.LowWaterY,
                        l.AreaId,
                        vertexIndex,
                        vx,
                        vy,
                        vz,
                        pending);
                }

                return;
            }

            Vtx h = a;
            Vtx l1 = b;
            Vtx l2 = c;

            if (TryGetSplit(terrain, mapWidth, mapHeight, config.HeightScaleMeters, h, l1, out var s1) &&
                TryGetSplit(terrain, mapWidth, mapHeight, config.HeightScaleMeters, h, l2, out var s2))
            {
                TryAppendWalkableTri(
                    config,
                    mapWidth,
                    row,
                    col,
                    faceIndex,
                    splitPart: 0,
                    h.Pos,
                    h.WaterY,
                    s1.HighExt,
                    s1.HighWaterY,
                    s2.HighExt,
                    s2.HighWaterY,
                    h.AreaId,
                    vertexIndex,
                    vx,
                    vy,
                    vz,
                    pending);
                TryAppendWalkableTri(
                    config,
                    mapWidth,
                    row,
                    col,
                    faceIndex,
                    splitPart: 1,
                    l1.Pos,
                    l1.WaterY,
                    l2.Pos,
                    l2.WaterY,
                    s1.LowExt,
                    s1.LowWaterY,
                    ResolveAreaId(l1.AreaId, l2.AreaId, l1.AreaId),
                    vertexIndex,
                    vx,
                    vy,
                    vz,
                    pending);
                TryAppendWalkableTri(
                    config,
                    mapWidth,
                    row,
                    col,
                    faceIndex,
                    splitPart: 2,
                    l2.Pos,
                    l2.WaterY,
                    s2.LowExt,
                    s2.LowWaterY,
                    s1.LowExt,
                    s1.LowWaterY,
                    ResolveAreaId(l2.AreaId, l1.AreaId, l2.AreaId),
                    vertexIndex,
                    vx,
                    vy,
                    vz,
                    pending);
            }
        }

        private static void TryAppendWalkableTri(
            in NavBuildConfig config,
            int mapWidth,
            int row,
            int col,
            int faceIndex,
            int splitPart,
            Vector3 a,
            float wa,
            Vector3 b,
            float wb,
            Vector3 c,
            float wc,
            byte areaId,
            Dictionary<VertexKey, int> vertexIndex,
            List<int> vx,
            List<int> vy,
            List<int> vz,
            List<PendingTriangle> pending)
        {
            if (wa > a.Y || wb > b.Y || wc > c.Y)
            {
                return;
            }

            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 n = Vector3.Cross(ab, ac);
            float len = n.Length();
            if (len <= 1e-6f)
            {
                return;
            }

            n /= len;
            if (n.Y < 0f)
            {
                (b, c) = (c, b);
                (wb, wc) = (wc, wb);
                n = -n;
            }

            if (n.Y < config.MinWalkableUpDot)
            {
                return;
            }

            int ia = GetOrAddVertex(a, vertexIndex, vx, vy, vz);
            int ib = GetOrAddVertex(b, vertexIndex, vx, vy, vz);
            int ic = GetOrAddVertex(c, vertexIndex, vx, vy, vz);
            if (ia == ib || ib == ic || ia == ic)
            {
                return;
            }

            int stableId = EncodeStableId(mapWidth, row, col, faceIndex, splitPart);
            pending.Add(new PendingTriangle(stableId, ia, ib, ic, areaId));
        }

        private static byte ResolveAreaId(byte a, byte b, byte c)
        {
            if (a == b || a == c) return a;
            if (b == c) return b;
            return a;
        }

        private static int GetOrAddVertex(
            Vector3 p,
            Dictionary<VertexKey, int> vertexIndex,
            List<int> vx,
            List<int> vy,
            List<int> vz)
        {
            int xcm = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(p.X));
            int ycm = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(p.Y));
            int zcm = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(p.Z));
            var key = new VertexKey(xcm, ycm, zcm);
            if (vertexIndex.TryGetValue(key, out int idx))
            {
                return idx;
            }

            idx = vx.Count;
            vertexIndex.Add(key, idx);
            vx.Add(xcm);
            vy.Add(ycm);
            vz.Add(zcm);
            return idx;
        }

        private static bool TryGetSplit(
            LogicTerrainField terrain,
            int mapWidth,
            int mapHeight,
            float heightScale,
            in Vtx high,
            in Vtx low,
            out SplitPoints split)
        {
            split = default;
            if (high.H == low.H)
            {
                return false;
            }

            float midX = (high.Pos.X + low.Pos.X) * 0.5f;
            float midZ = (high.Pos.Z + low.Pos.Z) * 0.5f;
            float midWaterY = (high.WaterY + low.WaterY) * 0.5f;

            float highExtX = midX;
            float lowExtX = midX;

            bool shouldStraighten = terrain.Topology == LogicTerrainTopology.Hex &&
                GetCliffStraighten(terrain, mapWidth, mapHeight, high.C, high.R, low.C, low.R);

            if (shouldStraighten)
            {
                float dirX = MathF.Sign(low.Pos.X - high.Pos.X);
                float smoothedX = HexCoordinates.HexWidth * (high.C + 0.25f);
                float bias = HexCoordinates.HexWidth * 0.5f;
                if (dirX != 0f)
                {
                    smoothedX += dirX * bias;
                }

                highExtX = smoothedX;
                lowExtX = smoothedX;
            }

            Vector3 highExt = new Vector3(highExtX, high.Pos.Y, midZ);
            Vector3 lowExt = new Vector3(lowExtX, low.Pos.Y, midZ);
            split = new SplitPoints(highExt, midWaterY, lowExt, midWaterY);
            return true;
        }

        private static bool GetCliffStraighten(
            LogicTerrainField terrain,
            int mapWidth,
            int mapHeight,
            int cA,
            int rA,
            int cB,
            int rB)
        {
            int baseC;
            int baseR;
            int edgeIndex;

            if (rA == rB)
            {
                if (cA + 1 == cB)
                {
                    baseC = cA;
                    baseR = rA;
                    edgeIndex = 0;
                }
                else if (cB + 1 == cA)
                {
                    baseC = cB;
                    baseR = rB;
                    edgeIndex = 0;
                }
                else
                {
                    return false;
                }
            }
            else if (rA + 1 == rB || rB + 1 == rA)
            {
                bool aUpper = rA < rB;
                int upC = aUpper ? cA : cB;
                int upR = aUpper ? rA : rB;
                int downC = aUpper ? cB : cA;
                int downR = aUpper ? rB : rA;

                if (downR != upR + 1)
                {
                    return false;
                }

                bool isOdd = (upR & 1) == 1;
                int brC = isOdd ? upC + 1 : upC;
                int brR = upR + 1;
                int blC = isOdd ? upC : upC - 1;
                int blR = upR + 1;

                if (downC == brC && downR == brR)
                {
                    baseC = upC;
                    baseR = upR;
                    edgeIndex = 1;
                }
                else if (downC == blC && downR == blR)
                {
                    baseC = upC;
                    baseR = upR;
                    edgeIndex = 2;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            if ((uint)baseC >= (uint)mapWidth || (uint)baseR >= (uint)mapHeight)
            {
                return false;
            }

            return terrain.TryGetCliffStraightenEdge(baseC, baseR, edgeIndex, out bool value) && value;
        }
    }
}
