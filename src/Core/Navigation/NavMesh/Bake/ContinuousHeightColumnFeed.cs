using System;
using System.Collections.Generic;
using DotRecast.Core.Numerics;
using DotRecast.Recast;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    public sealed class ContinuousHeightBakeRequest
    {
        public IContinuousHeightmap Heightmap { get; init; }

        public WorldAabbCm Bounds { get; init; }

        public int? BlockedAtOrBelowHeightCm { get; init; }
    }

    /// <summary>
    /// Writes one Recast span per voxel column from a continuous heightmap sample.
    /// Columns outside the authored bounds repeat the edge sample: Recast's border
    /// ring hangs past the board, and a missing border sample is not a hole in the map.
    /// A sample that fails inside the authored bounds is an authoring error.
    /// </summary>
    internal static class ContinuousHeightColumnFeed
    {
        internal readonly struct Result
        {
            public Result(RcHeightfield heightfield, NavBorderPortal[] portals)
            {
                Heightfield = heightfield;
                Portals = portals ?? Array.Empty<NavBorderPortal>();
            }

            public RcHeightfield Heightfield { get; }

            public NavBorderPortal[] Portals { get; }
        }

        public static Result Build(
            IContinuousHeightmap heightmap,
            WorldAabbCm bounds,
            int? blockedAtOrBelowHeightCm,
            float maxSlopeDeg,
            float tileMinX,
            float tileMinZ,
            float tileMaxX,
            float tileMaxZ,
            int originXcm,
            int originZcm,
            int tileWidthCells,
            int tileHeightCells,
            RcConfig rcCfg,
            NavObstacleSet obstacles,
            string layerId,
            int agentRadiusCm)
        {
            if (heightmap == null) throw new ArgumentNullException(nameof(heightmap));
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                throw new InvalidOperationException("Continuous height bake requires positive heightmap bounds.");
            }

            if (float.IsNaN(maxSlopeDeg) || maxSlopeDeg < 0f || maxSlopeDeg >= 90f)
            {
                throw new InvalidOperationException($"Continuous height bake requires maxSlopeDeg in [0, 90). Actual={maxSlopeDeg}.");
            }

            float cs = rcCfg.Cs;
            float ch = rcCfg.Ch;
            float borderMeters = rcCfg.BorderSize * cs;
            float slopeLimit = MathF.Tan(maxSlopeDeg * MathF.PI / 180f);

            float sampleMinX = tileMinX - borderMeters;
            float sampleMinZ = tileMinZ - borderMeters;
            float sampleMaxX = tileMaxX + borderMeters;
            float sampleMaxZ = tileMaxZ + borderMeters;

            float minHeight = float.PositiveInfinity;
            float maxHeight = float.NegativeInfinity;
            int probeColumnsX = Math.Max(2, (int)MathF.Ceiling((sampleMaxX - sampleMinX) / cs));
            int probeColumnsZ = Math.Max(2, (int)MathF.Ceiling((sampleMaxZ - sampleMinZ) / cs));
            for (int z = 0; z < probeColumnsZ; z++)
            {
                float wz = sampleMinZ + (z + 0.5f) * cs;
                for (int x = 0; x < probeColumnsX; x++)
                {
                    float wx = sampleMinX + (x + 0.5f) * cs;
                    float y = SampleMeters(heightmap, bounds, wx, wz);
                    minHeight = MathF.Min(minHeight, y);
                    maxHeight = MathF.Max(maxHeight, y);
                }
            }

            if (float.IsInfinity(minHeight) || float.IsInfinity(maxHeight))
            {
                throw new InvalidOperationException("Continuous height bake sampled no columns.");
            }

            float margin = rcCfg.WalkableHeight + rcCfg.WalkableClimb;
            float bminY = minHeight - margin - ch;
            float bmaxY = maxHeight + rcCfg.WalkableHeight * 2f + margin;
            var bmin = new RcVec3f(sampleMinX, bminY, sampleMinZ);
            var bmax = new RcVec3f(sampleMaxX, bmaxY, sampleMaxZ);
            var builderCfg = new RcBuilderConfig(rcCfg, bmin, bmax, tileX: 0, tileZ: 0);
            var solid = new RcHeightfield(builderCfg.width, builderCfg.height, bmin, bmax, cs, ch, rcCfg.BorderSize);

            int westX = ColumnIndex(tileMinX + cs * 0.5f, bmin.X, cs, solid.width);
            int eastX = ColumnIndex(tileMaxX - cs * 0.5f, bmin.X, cs, solid.width);
            int northZ = ColumnIndex(tileMinZ + cs * 0.5f, bmin.Z, cs, solid.height);
            int southZ = ColumnIndex(tileMaxZ - cs * 0.5f, bmin.Z, cs, solid.height);
            var west = new bool[solid.height];
            var east = new bool[solid.height];
            var north = new bool[solid.width];
            var south = new bool[solid.width];
            bool anyWalkable = false;
            bool hasObstacles = obstacles?.Obstacles is { Count: > 0 };
            int mergeThreshold = rcCfg.WalkableClimb;

            for (int z = 0; z < solid.height; z++)
            {
                float wz = bmin.Z + (z + 0.5f) * cs;
                for (int x = 0; x < solid.width; x++)
                {
                    float wx = bmin.X + (x + 0.5f) * cs;
                    if (!TryEvaluateWalkable(heightmap, bounds, blockedAtOrBelowHeightCm, wx, wz, cs, slopeLimit, out float yMeters))
                    {
                        continue;
                    }

                    if (hasObstacles &&
                        NavObstacleGeometry.IsPointBlockedByObstacles(
                            (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(wx)),
                            (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(wz)),
                            obstacles,
                            layerId))
                    {
                        continue;
                    }

                    EmitSpan(solid, x, z, yMeters, bminY, ch, mergeThreshold);
                    anyWalkable = true;
                    if (x == westX) west[z] = true;
                    if (x == eastX) east[z] = true;
                    if (z == northZ) north[x] = true;
                    if (z == southZ) south[x] = true;
                }
            }

            if (!anyWalkable)
            {
                return new Result(null, Array.Empty<NavBorderPortal>());
            }

            var portals = new List<NavBorderPortal>(8);
            AddEdgePortals(portals, west, NavPortalSide.West, horizontal: false, tileMinX, tileMinZ, tileMaxX, tileMaxZ, bmin.X, bmin.Z, cs, originXcm, originZcm, tileWidthCells, tileHeightCells, agentRadiusCm);
            AddEdgePortals(portals, east, NavPortalSide.East, horizontal: false, tileMinX, tileMinZ, tileMaxX, tileMaxZ, bmin.X, bmin.Z, cs, originXcm, originZcm, tileWidthCells, tileHeightCells, agentRadiusCm);
            AddEdgePortals(portals, north, NavPortalSide.North, horizontal: true, tileMinX, tileMinZ, tileMaxX, tileMaxZ, bmin.X, bmin.Z, cs, originXcm, originZcm, tileWidthCells, tileHeightCells, agentRadiusCm);
            AddEdgePortals(portals, south, NavPortalSide.South, horizontal: true, tileMinX, tileMinZ, tileMaxX, tileMaxZ, bmin.X, bmin.Z, cs, originXcm, originZcm, tileWidthCells, tileHeightCells, agentRadiusCm);
            return new Result(solid, portals.ToArray());
        }

        private static bool TryEvaluateWalkable(
            IContinuousHeightmap heightmap,
            WorldAabbCm bounds,
            int? blockedAtOrBelowHeightCm,
            float wx,
            float wz,
            float cs,
            float slopeLimit,
            out float yMeters)
        {
            yMeters = SampleMeters(heightmap, bounds, wx, wz);
            float heightCm = SpatialScaleDefaults.MetersToCentimeters(yMeters);
            if (blockedAtOrBelowHeightCm.HasValue && heightCm <= blockedAtOrBelowHeightCm.Value)
            {
                return false;
            }

            float riseX = MathF.Abs(SampleMeters(heightmap, bounds, wx + cs, wz) - yMeters);
            float riseZ = MathF.Abs(SampleMeters(heightmap, bounds, wx, wz + cs) - yMeters);
            float slope = MathF.Max(riseX, riseZ) / MathF.Max(cs, 1e-4f);
            return slope <= slopeLimit + 1e-4f;
        }

        private static float SampleMeters(IContinuousHeightmap heightmap, WorldAabbCm bounds, float xMeters, float zMeters)
        {
            float xCm = SpatialScaleDefaults.MetersToCentimeters(xMeters);
            float zCm = SpatialScaleDefaults.MetersToCentimeters(zMeters);
            bool inside = xCm >= bounds.Left && xCm <= bounds.Right && zCm >= bounds.Top && zCm <= bounds.Bottom;
            float clampedX = Math.Clamp(xCm, bounds.Left, bounds.Right);
            float clampedZ = Math.Clamp(zCm, bounds.Top, bounds.Bottom);
            if (!heightmap.TrySampleHeightCm(clampedX, clampedZ, out float heightCm))
            {
                throw new InvalidOperationException(
                    inside
                        ? $"Continuous height sample failed inside authored bounds at ({clampedX},{clampedZ})cm."
                        : $"Continuous height edge sample failed at ({clampedX},{clampedZ})cm.");
            }

            return heightCm / SpatialScaleDefaults.CellCm;
        }

        private static void EmitSpan(RcHeightfield solid, int x, int z, float yMeters, float bminY, float ch, int mergeThreshold)
        {
            int smin = ClampSpanY((int)MathF.Round((yMeters - bminY) / ch));
            int smax = ClampSpanY(smin + 1);
            if (smax <= smin)
            {
                return;
            }

            RcRasterizations.AddSpan(solid, x, z, smin, smax, RcRecast.RC_WALKABLE_AREA, mergeThreshold);
        }

        private static int ClampSpanY(int value)
            => Math.Clamp(value, 1, RcRecast.RC_SPAN_MAX_HEIGHT - 1);

        private static int ColumnIndex(float world, float origin, float cs, int limit)
        {
            int index = (int)MathF.Floor((world - origin) / cs);
            return Math.Clamp(index, 0, Math.Max(0, limit - 1));
        }

        private static void AddEdgePortals(
            List<NavBorderPortal> portals,
            bool[] walkable,
            NavPortalSide side,
            bool horizontal,
            float tileMinX,
            float tileMinZ,
            float tileMaxX,
            float tileMaxZ,
            float fieldMinX,
            float fieldMinZ,
            float cs,
            int originXcm,
            int originZcm,
            int tileWidthCells,
            int tileHeightCells,
            int agentRadiusCm)
        {
            if (tileWidthCells <= 0 || tileHeightCells <= 0)
            {
                return;
            }

            int runStart = -1;
            for (int i = 0; i <= walkable.Length; i++)
            {
                bool open = i < walkable.Length && walkable[i];
                if (open)
                {
                    if (runStart < 0) runStart = i;
                    continue;
                }

                if (runStart < 0)
                {
                    continue;
                }

                AppendRun(portals, side, horizontal, runStart, i, tileMinX, tileMinZ, tileMaxX, tileMaxZ, fieldMinX, fieldMinZ, cs, originXcm, originZcm, tileWidthCells, tileHeightCells, agentRadiusCm);
                runStart = -1;
            }
        }

        private static void AppendRun(
            List<NavBorderPortal> portals,
            NavPortalSide side,
            bool horizontal,
            int runStart,
            int runEnd,
            float tileMinX,
            float tileMinZ,
            float tileMaxX,
            float tileMaxZ,
            float fieldMinX,
            float fieldMinZ,
            float cs,
            int originXcm,
            int originZcm,
            int tileWidthCells,
            int tileHeightCells,
            int agentRadiusCm)
        {
            float start = (horizontal ? fieldMinX : fieldMinZ) + runStart * cs;
            float end = (horizontal ? fieldMinX : fieldMinZ) + runEnd * cs;
            float edge = horizontal
                ? (side == NavPortalSide.North ? tileMinZ : tileMaxZ)
                : (side == NavPortalSide.West ? tileMinX : tileMaxX);
            float span = horizontal ? tileMaxX - tileMinX : tileMaxZ - tileMinZ;
            int cells = horizontal ? tileWidthCells : tileHeightCells;
            if (span <= 0f)
            {
                return;
            }

            int a = Math.Clamp((int)MathF.Round((start - (horizontal ? tileMinX : tileMinZ)) / span * cells), 0, cells);
            int b = Math.Clamp((int)MathF.Round((end - (horizontal ? tileMinX : tileMinZ)) / span * cells), 0, cells);
            if (b < a)
            {
                (a, b) = (b, a);
            }

            if (a == b)
            {
                b = Math.Min(cells, a + 1);
            }

            int fixedCell = horizontal
                ? (side == NavPortalSide.North ? 0 : tileHeightCells)
                : (side == NavPortalSide.West ? 0 : tileWidthCells);
            short u0 = horizontal ? (short)a : (short)fixedCell;
            short v0 = horizontal ? (short)fixedCell : (short)a;
            short u1 = horizontal ? (short)b : (short)fixedCell;
            short v1 = horizontal ? (short)fixedCell : (short)b;

            int x0cm;
            int z0cm;
            int x1cm;
            int z1cm;
            if (horizontal)
            {
                x0cm = ToLocalCm(start, originXcm);
                x1cm = ToLocalCm(end, originXcm);
                z0cm = ToLocalCm(edge, originZcm);
                z1cm = z0cm;
            }
            else
            {
                x0cm = ToLocalCm(edge, originXcm);
                x1cm = x0cm;
                z0cm = ToLocalCm(start, originZcm);
                z1cm = ToLocalCm(end, originZcm);
            }

            int length = Math.Max(1, (int)MathF.Round(MathF.Abs(horizontal ? x1cm - x0cm : z1cm - z0cm)));
            int clearance = Math.Max(0, Math.Min(length / 2, Math.Max(0, agentRadiusCm)));
            portals.Add(new NavBorderPortal(side, u0, v0, u1, v1, x0cm, z0cm, x1cm, z1cm, clearance));
        }

        private static int ToLocalCm(float worldMeters, int originCm)
            => (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(worldMeters)) - originCm;
    }
}
