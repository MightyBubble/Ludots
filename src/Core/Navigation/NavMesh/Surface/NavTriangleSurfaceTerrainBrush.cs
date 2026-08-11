using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Spatial;

namespace Ludots.Core.Navigation.NavMesh.Surface
{
    public enum NavTriangleSurfaceTerrainBrushKind : byte
    {
        Block = 0,
        Raise = 1
    }

    /// <summary>
    /// Authoritative terrain brush applied against a published triangle surface.
    /// Rebuilds only tiles that intersect the brush; untouched tiles keep source triangles.
    /// </summary>
    public readonly struct NavTriangleSurfaceTerrainBrushSpec
    {
        public NavTriangleSurfaceTerrainBrushSpec(
            int centerXcm,
            int centerZcm,
            int halfExtentCm,
            NavTriangleSurfaceTerrainBrushKind kind,
            int cellSizeCm,
            float heightScaleMeters,
            byte baseHeightLevel,
            byte raiseHeightLevel,
            int targetMinYcm,
            int targetMaxYcm)
        {
            if (halfExtentCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(halfExtentCm), halfExtentCm, "Brush half extent must be > 0.");
            }

            if (cellSizeCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSizeCm), cellSizeCm, "Cell size must be > 0.");
            }

            if (!float.IsFinite(heightScaleMeters) || heightScaleMeters <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(heightScaleMeters), heightScaleMeters, "Height scale must be finite and > 0.");
            }

            if (targetMinYcm > targetMaxYcm)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetMinYcm),
                    targetMinYcm,
                    $"Terrain brush targetMinYcm must be <= targetMaxYcm ({targetMaxYcm}).");
            }

            CenterXcm = centerXcm;
            CenterZcm = centerZcm;
            HalfExtentCm = halfExtentCm;
            Kind = kind;
            CellSizeCm = cellSizeCm;
            HeightScaleMeters = heightScaleMeters;
            BaseHeightLevel = baseHeightLevel;
            RaiseHeightLevel = raiseHeightLevel;
            TargetMinYcm = targetMinYcm;
            TargetMaxYcm = targetMaxYcm;
        }

        public int CenterXcm { get; }
        public int CenterZcm { get; }
        public int HalfExtentCm { get; }
        public NavTriangleSurfaceTerrainBrushKind Kind { get; }
        public int CellSizeCm { get; }
        public float HeightScaleMeters { get; }
        public byte BaseHeightLevel { get; }
        public byte RaiseHeightLevel { get; }
        public int TargetMinYcm { get; }
        public int TargetMaxYcm { get; }

        public WorldAabbCm ResolveAabb()
            => new WorldAabbCm(
                checked(CenterXcm - HalfExtentCm),
                checked(CenterZcm - HalfExtentCm),
                checked(HalfExtentCm * 2),
                checked(HalfExtentCm * 2));
    }

    /// <summary>
    /// Deterministic cell-local overlay brush. Does not mutate the source index.
    /// <para>
    /// Only walk-candidate triangles fully inside the authored target Y band are edited. Source geometry
    /// outside the cell-aligned brush is clipped and preserved with interpolated 3D vertices, while
    /// triangles above or below the target band are copied exactly. This keeps bridges, caves, stacked
    /// floors, ramps, and other arbitrary scene triangles intact when editing the ground stratum.
    /// </para>
    /// </summary>
    public static class NavTriangleSurfaceTerrainBrush
    {
        private static readonly NavTriangleSurfaceFlags WalkFlags =
            NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;

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
            public override bool Equals(object? obj) => obj is VertexKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Xcm, Ycm, Zcm);
        }

        private readonly struct PendingTriangle
        {
            public readonly int StableId;
            public readonly int A;
            public readonly int B;
            public readonly int C;
            public readonly byte AreaId;
            public readonly NavTriangleSurfaceFlags Flags;

            public PendingTriangle(int stableId, int a, int b, int c, byte areaId, NavTriangleSurfaceFlags flags)
            {
                StableId = stableId;
                A = a;
                B = b;
                C = c;
                AreaId = areaId;
                Flags = flags;
            }
        }

        private readonly struct SurfaceVertex : IEquatable<SurfaceVertex>
        {
            public readonly int Xcm;
            public readonly int Ycm;
            public readonly int Zcm;

            public SurfaceVertex(int xcm, int ycm, int zcm)
            {
                Xcm = xcm;
                Ycm = ycm;
                Zcm = zcm;
            }

            public bool Equals(SurfaceVertex other) => Xcm == other.Xcm && Ycm == other.Ycm && Zcm == other.Zcm;
        }

        private enum ClipBoundary : byte
        {
            Left = 0,
            Right = 1,
            Top = 2,
            Bottom = 3
        }

        public static NavTriangleSurfaceTileIndex Apply(
            NavTriangleSurfaceTileIndex source,
            in NavTriangleSurfaceTerrainBrushSpec spec,
            out WorldAabbCm dirtyAabb)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            NavTriangleSurfaceTileGrid grid = source.Grid;
            WorldAabbCm brush = spec.ResolveAabb();
            if (brush.Width <= 0 || brush.Height <= 0)
            {
                throw new InvalidOperationException("Terrain brush AABB must have positive width and height.");
            }

            int originX = grid.OriginXcm;
            int originZ = grid.OriginZcm;
            int cell = spec.CellSizeCm;
            int minCellX = MathUtil.FloorDiv(checked(brush.Left - originX), cell);
            int minCellZ = MathUtil.FloorDiv(checked(brush.Top - originZ), cell);
            int maxCellX = MathUtil.FloorDiv(checked(brush.Right - 1 - originX), cell);
            int maxCellZ = MathUtil.FloorDiv(checked(brush.Bottom - 1 - originZ), cell);
            if (maxCellX < minCellX || maxCellZ < minCellZ)
            {
                throw new InvalidOperationException(
                    $"Terrain brush cell range inverted: X[{minCellX},{maxCellX}] Z[{minCellZ},{maxCellZ}].");
            }

            int cellsPerTileX = checked(grid.TileWidthCm / cell);
            int cellsPerTileZ = checked(grid.TileHeightCm / cell);
            if (cellsPerTileX <= 0 || cellsPerTileZ <= 0 ||
                checked(cellsPerTileX * cell) != grid.TileWidthCm ||
                checked(cellsPerTileZ * cell) != grid.TileHeightCm)
            {
                throw new InvalidOperationException(
                    $"Terrain brush requires tile size ({grid.TileWidthCm}x{grid.TileHeightCm}) to be an integer multiple of cellSizeCm {cell}.");
            }

            int cellCountX = checked(grid.TileCountX * cellsPerTileX);
            int cellCountZ = checked(grid.TileCountZ * cellsPerTileZ);
            if (maxCellX < 0 || maxCellZ < 0 || minCellX >= cellCountX || minCellZ >= cellCountZ)
            {
                throw new InvalidOperationException(
                    $"Terrain brush AABB {brush} does not intersect any triangle-surface cell.");
            }

            minCellX = MathUtil.Clamp(minCellX, 0, cellCountX - 1);
            minCellZ = MathUtil.Clamp(minCellZ, 0, cellCountZ - 1);
            maxCellX = MathUtil.Clamp(maxCellX, 0, cellCountX - 1);
            maxCellZ = MathUtil.Clamp(maxCellZ, 0, cellCountZ - 1);
            var editAabb = new WorldAabbCm(
                checked(originX + minCellX * cell),
                checked(originZ + minCellZ * cell),
                checked((maxCellX - minCellX + 1) * cell),
                checked((maxCellZ - minCellZ + 1) * cell));

            int minTileX = MathUtil.FloorDiv(checked(editAabb.Left - originX), grid.TileWidthCm);
            int minTileZ = MathUtil.FloorDiv(checked(editAabb.Top - originZ), grid.TileHeightCm);
            int maxTileX = MathUtil.FloorDiv(checked(editAabb.Right - 1 - originX), grid.TileWidthCm);
            int maxTileZ = MathUtil.FloorDiv(checked(editAabb.Bottom - 1 - originZ), grid.TileHeightCm);

            if (maxTileX < 0 ||
                maxTileZ < 0 ||
                minTileX >= grid.TileCountX ||
                minTileZ >= grid.TileCountZ)
            {
                throw new InvalidOperationException(
                    $"Terrain brush AABB {brush} does not intersect any triangle-surface tile " +
                    $"(origin=({originX},{originZ}), tileSize=({grid.TileWidthCm},{grid.TileHeightCm}), " +
                    $"tileCount=({grid.TileCountX},{grid.TileCountZ})).");
            }

            if (minTileX < 0) minTileX = 0;
            if (minTileZ < 0) minTileZ = 0;
            if (maxTileX >= grid.TileCountX) maxTileX = grid.TileCountX - 1;
            if (maxTileZ >= grid.TileCountZ) maxTileZ = grid.TileCountZ - 1;
            if (minTileX > maxTileX || minTileZ > maxTileZ)
            {
                throw new InvalidOperationException(
                    $"Terrain brush AABB {brush} clamped to an empty tile range.");
            }

            dirtyAabb = editAabb;

            int raiseYcm = HeightLevelToCm(spec.RaiseHeightLevel, spec.HeightScaleMeters);

            var vertexIndex = new Dictionary<VertexKey, int>();
            var vx = new List<int>();
            var vy = new List<int>();
            var vz = new List<int>();
            var pending = new List<PendingTriangle>();

            NavTriangleSurfaceSnapshot surface = source.Surface;
            ReadOnlySpan<int> srcVx = surface.VertexXcm;
            ReadOnlySpan<int> srcVy = surface.VertexYcm;
            ReadOnlySpan<int> srcVz = surface.VertexZcm;
            ReadOnlySpan<int> srcA = surface.TriA;
            ReadOnlySpan<int> srcB = surface.TriB;
            ReadOnlySpan<int> srcC = surface.TriC;
            ReadOnlySpan<byte> srcAreas = surface.TriAreaIds;
            ReadOnlySpan<int> srcStable = surface.TriStableIds;
            ReadOnlySpan<NavTriangleSurfaceFlags> srcFlags = surface.TriFlags;

            int maxStableId = -1;
            for (int tri = 0; tri < surface.TriangleCount; tri++)
            {
                if (srcStable[tri] > maxStableId)
                {
                    maxStableId = srcStable[tri];
                }
            }

            int nextStableId = checked(maxStableId + 1);
            for (int tri = 0; tri < surface.TriangleCount; tri++)
            {
                int a = srcA[tri];
                int b = srcB[tri];
                int c = srcC[tri];
                bool editable = (srcFlags[tri] & NavTriangleSurfaceFlags.WalkCandidate) != 0 &&
                    TriangleIsInsideTargetHeightBand(
                        srcVy[a],
                        srcVy[b],
                        srcVy[c],
                        spec.TargetMinYcm,
                        spec.TargetMaxYcm) &&
                    TriangleIntersectsAabb(
                        srcVx[a], srcVz[a],
                        srcVx[b], srcVz[b],
                        srcVx[c], srcVz[c],
                        in editAabb);
                if (editable)
                {
                    EmitTriangleOutsideBrush(
                        new SurfaceVertex(srcVx[a], srcVy[a], srcVz[a]),
                        new SurfaceVertex(srcVx[b], srcVy[b], srcVz[b]),
                        new SurfaceVertex(srcVx[c], srcVy[c], srcVz[c]),
                        in editAabb,
                        srcStable[tri],
                        srcAreas[tri],
                        srcFlags[tri],
                        ref nextStableId,
                        vertexIndex,
                        vx,
                        vy,
                        vz,
                        pending);
                    continue;
                }

                int ia = GetOrAdd(srcVx[a], srcVy[a], srcVz[a], vertexIndex, vx, vy, vz);
                int ib = GetOrAdd(srcVx[b], srcVy[b], srcVz[b], vertexIndex, vx, vy, vz);
                int ic = GetOrAdd(srcVx[c], srcVy[c], srcVz[c], vertexIndex, vx, vy, vz);
                int stableId = srcStable[tri];
                pending.Add(new PendingTriangle(stableId, ia, ib, ic, srcAreas[tri], srcFlags[tri]));
            }

            for (int cz = minCellZ; cz <= maxCellZ; cz++)
            {
                for (int cx = minCellX; cx <= maxCellX; cx++)
                {
                    if (spec.Kind == NavTriangleSurfaceTerrainBrushKind.Block)
                    {
                        continue;
                    }

                    int x0 = checked(originX + cx * cell);
                    int z0 = checked(originZ + cz * cell);
                    int x1 = checked(x0 + cell);
                    int z1 = checked(z0 + cell);
                    EmitCellQuad(
                        x0,
                        z0,
                        x1,
                        z1,
                        raiseYcm,
                        ref nextStableId,
                        vertexIndex,
                        vx,
                        vy,
                        vz,
                        pending);
                }
            }

            pending.Sort(static (a, b) => a.StableId.CompareTo(b.StableId));
            int triCount = pending.Count;
            var triA = new int[triCount];
            var triB = new int[triCount];
            var triC = new int[triCount];
            var triAreaIds = new byte[triCount];
            var triStableIds = new int[triCount];
            var triFlags = new NavTriangleSurfaceFlags[triCount];
            for (int i = 0; i < triCount; i++)
            {
                PendingTriangle t = pending[i];
                triA[i] = t.A;
                triB[i] = t.B;
                triC[i] = t.C;
                triAreaIds[i] = t.AreaId;
                triStableIds[i] = t.StableId;
                triFlags[i] = t.Flags;
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
            return NavTriangleSurfaceTileIndex.Build(snapshot, grid);
        }

        /// <summary>
        /// Exact restore helper: republish a previously captured immutable surface index.
        /// Dirty AABB is the union of tiles whose triangle signatures differ.
        /// </summary>
        public static WorldAabbCm ComputeChangedTileAabb(
            NavTriangleSurfaceTileIndex before,
            NavTriangleSurfaceTileIndex after)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (after == null) throw new ArgumentNullException(nameof(after));
            if (!GridsEqual(before.Grid, after.Grid))
            {
                throw new InvalidOperationException(
                    "ComputeChangedTileAabb requires identical triangle-surface tile grids.");
            }

            NavTriangleSurfaceTileGrid grid = before.Grid;
            int dirtyMinX = int.MaxValue;
            int dirtyMinZ = int.MaxValue;
            int dirtyMaxX = int.MinValue;
            int dirtyMaxZ = int.MinValue;
            bool any = false;

            for (int tz = 0; tz < grid.TileCountZ; tz++)
            {
                for (int tx = 0; tx < grid.TileCountX; tx++)
                {
                    if (TileTriangleSignature(before, tx, tz) == TileTriangleSignature(after, tx, tz))
                    {
                        continue;
                    }

                    any = true;
                    int minX = checked(grid.OriginXcm + tx * grid.TileWidthCm);
                    int minZ = checked(grid.OriginZcm + tz * grid.TileHeightCm);
                    int maxX = checked(minX + grid.TileWidthCm);
                    int maxZ = checked(minZ + grid.TileHeightCm);
                    dirtyMinX = Math.Min(dirtyMinX, minX);
                    dirtyMinZ = Math.Min(dirtyMinZ, minZ);
                    dirtyMaxX = Math.Max(dirtyMaxX, maxX);
                    dirtyMaxZ = Math.Max(dirtyMaxZ, maxZ);
                }
            }

            if (!any)
            {
                throw new InvalidOperationException(
                    "ComputeChangedTileAabb found no tile differences; refusing an empty dirty AABB.");
            }

            return new WorldAabbCm(
                dirtyMinX,
                dirtyMinZ,
                checked(dirtyMaxX - dirtyMinX),
                checked(dirtyMaxZ - dirtyMinZ));
        }

        private static bool GridsEqual(in NavTriangleSurfaceTileGrid left, in NavTriangleSurfaceTileGrid right)
            => left.OriginXcm == right.OriginXcm &&
               left.OriginZcm == right.OriginZcm &&
               left.TileWidthCm == right.TileWidthCm &&
               left.TileHeightCm == right.TileHeightCm &&
               left.TileCountX == right.TileCountX &&
               left.TileCountZ == right.TileCountZ &&
               left.HaloPaddingCm == right.HaloPaddingCm;

        private static ulong TileTriangleSignature(NavTriangleSurfaceTileIndex index, int tileX, int tileZ)
        {
            ReadOnlySpan<int> tris = index.GetTriangleIndices(tileX, tileZ);
            NavTriangleSurfaceSnapshot surface = index.Surface;
            ulong hash = 1469598103934665603UL;
            for (int i = 0; i < tris.Length; i++)
            {
                int tri = tris[i];
                hash = Fnv(hash, (ulong)(uint)surface.TriStableIds[tri]);
                hash = Fnv(hash, (ulong)(uint)surface.VertexXcm[surface.TriA[tri]]);
                hash = Fnv(hash, (ulong)(uint)surface.VertexYcm[surface.TriA[tri]]);
                hash = Fnv(hash, (ulong)(uint)surface.VertexZcm[surface.TriA[tri]]);
                hash = Fnv(hash, (ulong)(uint)surface.VertexXcm[surface.TriB[tri]]);
                hash = Fnv(hash, (ulong)(uint)surface.VertexYcm[surface.TriB[tri]]);
                hash = Fnv(hash, (ulong)(uint)surface.VertexZcm[surface.TriB[tri]]);
                hash = Fnv(hash, (ulong)(uint)surface.VertexXcm[surface.TriC[tri]]);
                hash = Fnv(hash, (ulong)(uint)surface.VertexYcm[surface.TriC[tri]]);
                hash = Fnv(hash, (ulong)(uint)surface.VertexZcm[surface.TriC[tri]]);
                hash = Fnv(hash, surface.TriAreaIds[tri]);
                hash = Fnv(hash, (byte)surface.TriFlags[tri]);
            }

            return hash;
        }

        private static ulong Fnv(ulong hash, ulong value)
        {
            unchecked
            {
                hash ^= value;
                return hash * 1099511628211UL;
            }
        }

        private static int HeightLevelToCm(byte heightLevel, float heightScaleMeters)
        {
            float meters = heightLevel * heightScaleMeters;
            return (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(meters));
        }

        private static bool TriangleIsInsideTargetHeightBand(
            int ay,
            int by,
            int cy,
            int minY,
            int maxY)
            => ay >= minY && ay <= maxY &&
               by >= minY && by <= maxY &&
               cy >= minY && cy <= maxY;

        private static bool TriangleIntersectsAabb(
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz,
            in WorldAabbCm aabb)
        {
            int minX = ax;
            if (bx < minX) minX = bx;
            if (cx < minX) minX = cx;
            int maxX = ax;
            if (bx > maxX) maxX = bx;
            if (cx > maxX) maxX = cx;
            int minZ = az;
            if (bz < minZ) minZ = bz;
            if (cz < minZ) minZ = cz;
            int maxZ = az;
            if (bz > maxZ) maxZ = bz;
            if (cz > maxZ) maxZ = cz;

            return minX < aabb.Right && maxX > aabb.Left && minZ < aabb.Bottom && maxZ > aabb.Top;
        }

        private static void EmitTriangleOutsideBrush(
            in SurfaceVertex a,
            in SurfaceVertex b,
            in SurfaceVertex c,
            in WorldAabbCm brush,
            int originalStableId,
            byte areaId,
            NavTriangleSurfaceFlags flags,
            ref int nextStableId,
            Dictionary<VertexKey, int> vertexIndex,
            List<int> vx,
            List<int> vy,
            List<int> vz,
            List<PendingTriangle> pending)
        {
            Span<SurfaceVertex> current = stackalloc SurfaceVertex[12];
            Span<SurfaceVertex> inside = stackalloc SurfaceVertex[12];
            Span<SurfaceVertex> outside = stackalloc SurfaceVertex[12];
            current[0] = a;
            current[1] = b;
            current[2] = c;
            int currentCount = 3;
            bool originalStableIdAssigned = false;

            for (ClipBoundary boundary = ClipBoundary.Left; boundary <= ClipBoundary.Bottom; boundary++)
            {
                SplitPolygon(
                    current.Slice(0, currentCount),
                    boundary,
                    in brush,
                    inside,
                    out int insideCount,
                    outside,
                    out int outsideCount);
                EmitPolygon(
                    outside.Slice(0, outsideCount),
                    originalStableId,
                    areaId,
                    flags,
                    ref originalStableIdAssigned,
                    ref nextStableId,
                    vertexIndex,
                    vx,
                    vy,
                    vz,
                    pending);

                currentCount = insideCount;
                for (int i = 0; i < insideCount; i++)
                {
                    current[i] = inside[i];
                }

                if (currentCount == 0)
                {
                    break;
                }
            }
        }

        private static void SplitPolygon(
            ReadOnlySpan<SurfaceVertex> source,
            ClipBoundary boundary,
            in WorldAabbCm brush,
            Span<SurfaceVertex> inside,
            out int insideCount,
            Span<SurfaceVertex> outside,
            out int outsideCount)
        {
            insideCount = 0;
            outsideCount = 0;
            if (source.IsEmpty)
            {
                return;
            }

            SurfaceVertex start = source[source.Length - 1];
            long startDistance = SignedInsideDistance(in start, boundary, in brush);
            bool startInside = startDistance >= 0;
            for (int i = 0; i < source.Length; i++)
            {
                SurfaceVertex end = source[i];
                long endDistance = SignedInsideDistance(in end, boundary, in brush);
                bool endInside = endDistance >= 0;
                if (startInside != endInside)
                {
                    SurfaceVertex intersection = Intersect(in start, in end, startDistance, endDistance, boundary, in brush);
                    AddUnique(inside, ref insideCount, in intersection);
                    AddUnique(outside, ref outsideCount, in intersection);
                }

                if (endInside)
                {
                    AddUnique(inside, ref insideCount, in end);
                }
                else
                {
                    AddUnique(outside, ref outsideCount, in end);
                }

                start = end;
                startDistance = endDistance;
                startInside = endInside;
            }

            RemoveClosingDuplicate(inside, ref insideCount);
            RemoveClosingDuplicate(outside, ref outsideCount);
        }

        private static long SignedInsideDistance(
            in SurfaceVertex vertex,
            ClipBoundary boundary,
            in WorldAabbCm brush)
            => boundary switch
            {
                ClipBoundary.Left => (long)vertex.Xcm - brush.Left,
                ClipBoundary.Right => (long)brush.Right - vertex.Xcm,
                ClipBoundary.Top => (long)vertex.Zcm - brush.Top,
                ClipBoundary.Bottom => (long)brush.Bottom - vertex.Zcm,
                _ => throw new InvalidOperationException($"Unknown clip boundary '{boundary}'.")
            };

        private static SurfaceVertex Intersect(
            in SurfaceVertex start,
            in SurfaceVertex end,
            long startDistance,
            long endDistance,
            ClipBoundary boundary,
            in WorldAabbCm brush)
        {
            long denominator = checked(startDistance - endDistance);
            if (denominator == 0)
            {
                throw new InvalidOperationException("Terrain brush clip edge has no boundary intersection.");
            }

            int x = Interpolate(start.Xcm, end.Xcm, startDistance, denominator);
            int y = Interpolate(start.Ycm, end.Ycm, startDistance, denominator);
            int z = Interpolate(start.Zcm, end.Zcm, startDistance, denominator);
            switch (boundary)
            {
                case ClipBoundary.Left:
                    x = brush.Left;
                    break;
                case ClipBoundary.Right:
                    x = brush.Right;
                    break;
                case ClipBoundary.Top:
                    z = brush.Top;
                    break;
                case ClipBoundary.Bottom:
                    z = brush.Bottom;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown clip boundary '{boundary}'.");
            }

            return new SurfaceVertex(x, y, z);
        }

        private static int Interpolate(int start, int end, long numerator, long denominator)
        {
            long delta = checked((long)end - start);
            long scaled = checked(delta * numerator);
            long quotient = DivideRoundHalfAwayFromZero(scaled, denominator);
            return checked(start + (int)quotient);
        }

        private static long DivideRoundHalfAwayFromZero(long numerator, long denominator)
        {
            if (denominator < 0)
            {
                numerator = checked(-numerator);
                denominator = checked(-denominator);
            }

            long quotient = numerator / denominator;
            long remainder = numerator % denominator;
            long twiceRemainder = checked(Math.Abs(remainder) * 2L);
            if (twiceRemainder >= denominator)
            {
                quotient = checked(quotient + Math.Sign(numerator));
            }

            return quotient;
        }

        private static void AddUnique(Span<SurfaceVertex> destination, ref int count, in SurfaceVertex vertex)
        {
            if (count > 0 && destination[count - 1].Equals(vertex))
            {
                return;
            }

            if (count >= destination.Length)
            {
                throw new InvalidOperationException(
                    $"Terrain brush polygon scratch capacity ({destination.Length}) exhausted; required {count + 1}.");
            }

            destination[count++] = vertex;
        }

        private static void RemoveClosingDuplicate(Span<SurfaceVertex> polygon, ref int count)
        {
            if (count > 1 && polygon[0].Equals(polygon[count - 1]))
            {
                count--;
            }
        }

        private static void EmitPolygon(
            ReadOnlySpan<SurfaceVertex> polygon,
            int originalStableId,
            byte areaId,
            NavTriangleSurfaceFlags flags,
            ref bool originalStableIdAssigned,
            ref int nextStableId,
            Dictionary<VertexKey, int> vertexIndex,
            List<int> vx,
            List<int> vy,
            List<int> vz,
            List<PendingTriangle> pending)
        {
            if (polygon.Length < 3)
            {
                return;
            }

            int root = GetOrAdd(polygon[0].Xcm, polygon[0].Ycm, polygon[0].Zcm, vertexIndex, vx, vy, vz);
            for (int i = 1; i + 1 < polygon.Length; i++)
            {
                if (TriangleArea2XZ(in polygon[0], in polygon[i], in polygon[i + 1]) == 0)
                {
                    continue;
                }

                int b = GetOrAdd(polygon[i].Xcm, polygon[i].Ycm, polygon[i].Zcm, vertexIndex, vx, vy, vz);
                int c = GetOrAdd(polygon[i + 1].Xcm, polygon[i + 1].Ycm, polygon[i + 1].Zcm, vertexIndex, vx, vy, vz);
                int stableId = originalStableIdAssigned ? nextStableId++ : originalStableId;
                originalStableIdAssigned = true;
                pending.Add(new PendingTriangle(stableId, root, b, c, areaId, flags));
            }
        }

        private static long TriangleArea2XZ(
            in SurfaceVertex a,
            in SurfaceVertex b,
            in SurfaceVertex c)
            => checked(
                ((long)b.Xcm - a.Xcm) * ((long)c.Zcm - a.Zcm) -
                ((long)b.Zcm - a.Zcm) * ((long)c.Xcm - a.Xcm));

        private static void EmitCellQuad(
            int x0cm,
            int z0cm,
            int x1cm,
            int z1cm,
            int ycm,
            ref int nextStableId,
            Dictionary<VertexKey, int> vertexIndex,
            List<int> vx,
            List<int> vy,
            List<int> vz,
            List<PendingTriangle> pending)
        {
            int iSw = GetOrAdd(x0cm, ycm, z0cm, vertexIndex, vx, vy, vz);
            int iSe = GetOrAdd(x1cm, ycm, z0cm, vertexIndex, vx, vy, vz);
            int iNe = GetOrAdd(x1cm, ycm, z1cm, vertexIndex, vx, vy, vz);
            int iNw = GetOrAdd(x0cm, ycm, z1cm, vertexIndex, vx, vy, vz);
            pending.Add(new PendingTriangle(nextStableId++, iSw, iSe, iNw, areaId: 0, WalkFlags));
            pending.Add(new PendingTriangle(nextStableId++, iSe, iNe, iNw, areaId: 0, WalkFlags));
        }

        private static int GetOrAdd(
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
    }
}
