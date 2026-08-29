using System;
using DotRecast.Core.Numerics;
using DotRecast.Recast;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Spatial;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    /// <summary>
    /// Terrain→Recast solid heightfield column feed: walks LogicTerrainField cell quads over
    /// the tile footprint plus the Recast border margin and writes floor spans per voxel column,
    /// skipping the per-cell triangle re-meshing stage. Emission mirrors NavTileBuilder's
    /// AddFace/AppendWalkableTri rules per containing base triangle: water above ground skips,
    /// ramp triangles emit their full height range, single-level triangles emit one floor,
    /// two-level triangles split at the lone-corner midpoint into high and low floors,
    /// three-level triangles are dropped, and blocked cells do not affect emission (they only
    /// feed the clearance/portal mask downstream). Obstacles are tested against the real
    /// triangle corner coordinates. Area id 63 is the Recast walkable marker in this feed and
    /// is rejected as an authored id.
    /// </summary>
    internal static class RecastDirectFeedHeightfield
    {
        private readonly struct Corner
        {
            public readonly byte Level;
            public readonly byte Water;
            public readonly bool Ramp;
            public readonly byte AreaId;

            public Corner(byte level, byte water, bool ramp, byte areaId)
            {
                Level = level;
                Water = water;
                Ramp = ramp;
                AreaId = areaId;
            }

            public float GroundMeters(float heightScale) => Level * heightScale;
            public float WaterMeters(float heightScale) => Water * heightScale;
        }

        public static RcHeightfield? BuildSolidHeightfield(
            LogicTerrainField terrain,
            int chunkX,
            int chunkY,
            in NavBuildConfig legacyConfig,
            RcConfig rcCfg,
            float tileMinX,
            float tileMinZ,
            float tileMaxX,
            float tileMaxZ,
            NavObstacleSet obstacles,
            string layerId)
        {
            float cellStepMeters = MathF.Max(0.01f, SpatialScaleDefaults.CentimetersToMeters(Math.Min(terrain.HorizontalStepCm, terrain.VerticalStepCm)));
            int expandedCells = Math.Max(1, (int)MathF.Ceiling(rcCfg.BorderSize * rcCfg.Cs / cellStepMeters)) + 1;
            int expandedChunkRadius = Math.Max(1, (expandedCells + terrain.ChunkSizeCells - 1) / terrain.ChunkSizeCells);
            int minChunkX = Math.Max(0, chunkX - expandedChunkRadius);
            int minChunkY = Math.Max(0, chunkY - expandedChunkRadius);
            int maxChunkX = Math.Min(terrain.WidthChunks - 1, chunkX + expandedChunkRadius);
            int maxChunkY = Math.Min(terrain.HeightChunks - 1, chunkY + expandedChunkRadius);

            int startC = minChunkX * terrain.ChunkSizeCells;
            int startR = minChunkY * terrain.ChunkSizeCells;
            int endC = Math.Min(terrain.WidthCells, (maxChunkX + 1) * terrain.ChunkSizeCells);
            int endR = Math.Min(terrain.HeightCells, (maxChunkY + 1) * terrain.ChunkSizeCells);

            int mapWidth = terrain.WidthCells;
            int mapHeight = terrain.HeightCells;
            bool isHex = terrain.Topology == LogicTerrainTopology.Hex;
            float heightScale = legacyConfig.HeightScaleMeters;

            float minHeight = float.PositiveInfinity;
            float maxHeight = float.NegativeInfinity;
            bool anyWalkableSurface = false;
            for (int r = Math.Max(0, startR - 1); r <= Math.Min(mapHeight - 1, endR); r++)
            {
                for (int c = Math.Max(0, startC - 1); c <= Math.Min(mapWidth - 1, endC); c++)
                {
                    LogicTerrainCell cell = terrain.GetCell(c, r);
                    if (cell.AreaId >= RecastNavTileBaker.ReservedWalkableAreaId)
                    {
                        throw new InvalidOperationException(
                            $"Terrain cell ({c},{r}) carries authored area id {cell.AreaId}; ids >= {RecastNavTileBaker.ReservedWalkableAreaId} are reserved for the Recast walkable marker and must not be authored.");
                    }

                    float y = cell.HeightLevel * heightScale;
                    minHeight = MathF.Min(minHeight, y);
                    maxHeight = MathF.Max(maxHeight, y);
                    if (cell.WaterHeightLevel * heightScale <= y)
                    {
                        anyWalkableSurface = true;
                    }
                }
            }

            if (!anyWalkableSurface)
            {
                return null;
            }

            float margin = rcCfg.WalkableHeight + rcCfg.WalkableClimb;
            float bminY = minHeight - margin - rcCfg.Ch;
            float bmaxY = maxHeight + rcCfg.WalkableHeight * 2f + margin;
            float borderMeters = rcCfg.BorderSize * rcCfg.Cs;

            var bmin = new RcVec3f(tileMinX - borderMeters, bminY, tileMinZ - borderMeters);
            var bmax = new RcVec3f(tileMaxX + borderMeters, bmaxY, tileMaxZ + borderMeters);
            var builderCfg = new RcBuilderConfig(rcCfg, bmin, bmax, tileX: 0, tileZ: 0);
            var solid = new RcHeightfield(builderCfg.width, builderCfg.height, bmin, bmax, rcCfg.Cs, rcCfg.Ch, rcCfg.BorderSize);

            float cs = rcCfg.Cs;
            float ch = rcCfg.Ch;
            int mergeThreshold = rcCfg.WalkableClimb;
            bool hasObstacles = obstacles?.Obstacles is { Count: > 0 };

            for (int r = startR; r < endR; r++)
            {
                for (int c = startC; c < endC; c++)
                {
                    terrain.GetWorldPositionMeters(c, r, out float ax, out float az);
                    terrain.GetWorldPositionMeters(c + 1, r, out float bx, out float bz);
                    terrain.GetWorldPositionMeters(c, r + 1, out float dx, out float dz);
                    terrain.GetWorldPositionMeters(c + 1, r + 1, out float ex, out float ez);

                    // Cells whose footprint misses the solid bounds entirely are not border
                    // input; folding them into the outermost columns would import distant
                    // height/area/obstacle state. Partial overlaps clamp.
                    if (ex <= bmin.X || ax >= bmax.X || ez <= bmin.Z || az >= bmax.Z)
                    {
                        continue;
                    }

                    Corner v00 = GetCorner(terrain, mapWidth, mapHeight, c, r);
                    Corner v10 = GetCorner(terrain, mapWidth, mapHeight, c + 1, r);
                    Corner v01 = GetCorner(terrain, mapWidth, mapHeight, c, r + 1);
                    Corner v11 = GetCorner(terrain, mapWidth, mapHeight, c + 1, r + 1);

                    bool oddRow = isHex && (r & 1) == 1;
                    Corner ta = v00, tb = v10, tc = v01;
                    Corner ua = v10, ub = v11, uc = v01;
                    float tax = ax, taz = az, tbx = bx, tbz = bz, tcx = dx, tcz = dz;
                    float uax = bx, uaz = bz, ubx = ex, ubz = ez, ucx = dx, ucz = dz;
                    if (oddRow)
                    {
                        ta = v00; tb = v10; tc = v11;
                        ua = v00; ub = v11; uc = v01;
                        tcx = ex; tcz = ez;
                        uax = ax; uaz = az; ucx = dx; ucz = dz;
                    }

                    bool firstBlockedByObstacle = false, secondBlockedByObstacle = false;
                    if (hasObstacles)
                    {
                        firstBlockedByObstacle = NavObstacleGeometry.IsTriangleBlockedByObstacles(
                            (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(tax)),
                            (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(taz)),
                            (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(tbx)),
                            (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(tbz)),
                            (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(tcx)),
                            (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(tcz)),
                            obstacles, layerId);
                        secondBlockedByObstacle = NavObstacleGeometry.IsTriangleBlockedByObstacles(
                            (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(uax)),
                            (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(uaz)),
                            (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(ubx)),
                            (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(ubz)),
                            (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(ucx)),
                            (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(ucz)),
                            obstacles, layerId);
                    }

                    int x0 = ClampVoxel((int)MathF.Floor((ax - bmin.X) / cs), solid.width);
                    int x1 = Math.Min(solid.width, Math.Max(x0 + 1, ClampVoxel((int)MathF.Ceiling((ex - bmin.X) / cs), solid.width) + 1));
                    int z0 = ClampVoxel((int)MathF.Floor((az - bmin.Z) / cs), solid.height);
                    int z1 = Math.Min(solid.height, Math.Max(z0 + 1, ClampVoxel((int)MathF.Ceiling((ez - bmin.Z) / cs), solid.height) + 1));

                    for (int z = z0; z < z1; z++)
                    {
                        float wz = bmin.Z + (z + 0.5f) * cs;
                        for (int x = x0; x < x1; x++)
                        {
                            float wx = bmin.X + (x + 0.5f) * cs;
                            // Grid quads split along the (c+1,r)-(c,r+1) diagonal on even hex
                            // rows and the (c,r)-(c+1,r+1) diagonal on odd rows, matching
                            // NavTileBuilder's triangle pairing.
                            float u = (wx - ax) / MathF.Max(1e-6f, ex - ax);
                            float v = (wz - az) / MathF.Max(1e-6f, ez - az);
                            bool first = oddRow ? u >= v : u + v < 1f;
                            if (first && firstBlockedByObstacle) continue;
                            if (!first && secondBlockedByObstacle) continue;

                            if (first)
                            {
                                EmitTriangleColumn(solid, x, z, wx, wz, ta, tb, tc, tax, taz, tbx, tbz, tcx, tcz, heightScale, bminY, ch, mergeThreshold);
                            }
                            else
                            {
                                EmitTriangleColumn(solid, x, z, wx, wz, ua, ub, uc, uax, uaz, ubx, ubz, ucx, ucz, heightScale, bminY, ch, mergeThreshold);
                            }
                        }
                    }
                }
            }

            return solid;
        }

        /// <summary>Mirrors AddFace/AppendWalkableTri per column: water skips, ramps emit the
        /// full height range, one level emits one floor, two levels split at the lone-corner
        /// midpoint into high/low floors, three distinct levels drop.</summary>
        private static void EmitTriangleColumn(
            RcHeightfield solid,
            int x,
            int z,
            float wx,
            float wz,
            in Corner a,
            in Corner b,
            in Corner c,
            float ax, float az,
            float bx, float bz,
            float cx, float cz,
            float heightScale,
            float bminY,
            float ch,
            int mergeThreshold)
        {
            if (a.WaterMeters(heightScale) > a.GroundMeters(heightScale) ||
                b.WaterMeters(heightScale) > b.GroundMeters(heightScale) ||
                c.WaterMeters(heightScale) > c.GroundMeters(heightScale))
            {
                return;
            }

            byte area = ResolveAreaId(a.AreaId, b.AreaId, c.AreaId);
            byte lo = Math.Min(a.Level, Math.Min(b.Level, c.Level));
            byte hi = Math.Max(a.Level, Math.Max(b.Level, c.Level));

            if (a.Ramp || b.Ramp || c.Ramp)
            {
                EmitLevelRange(solid, x, z, lo * heightScale, hi * heightScale, bminY, ch, mergeThreshold, area);
                return;
            }

            if (lo == hi)
            {
                EmitLevelRange(solid, x, z, lo * heightScale, lo * heightScale, bminY, ch, mergeThreshold, area);
                return;
            }

            int distinct = (a.Level != b.Level ? 1 : 0) + (b.Level != c.Level ? 1 : 0) + (a.Level != c.Level ? 1 : 0);
            if (distinct >= 2)
            {
                return;
            }

            // two levels: the lone corner sits on one side of the midpoint split; columns on
            // the lone corner's side take the lone level, the rest take the pair's level.
            bool loneIsA = a.Level != b.Level && a.Level != c.Level;
            bool loneIsB = b.Level != a.Level && b.Level != c.Level;
            float loneX = loneIsA ? ax : loneIsB ? bx : cx;
            float loneZ = loneIsA ? az : loneIsB ? bz : cz;
            float loneLevel = loneIsA ? a.Level : loneIsB ? b.Level : c.Level;
            float pairLevel = loneLevel == lo ? hi : lo;
            float pairX = loneIsA ? bx : ax;
            float pairZ = loneIsA ? bz : az;

            float midX = (loneX + pairX) * 0.5f;
            float midZ = (loneZ + pairZ) * 0.5f;
            float towardsLone = (wx - midX) * (loneX - midX) + (wz - midZ) * (loneZ - midZ);
            float level = towardsLone >= 0f ? loneLevel : pairLevel;
            EmitLevelRange(solid, x, z, level * heightScale, level * heightScale, bminY, ch, mergeThreshold, area);
        }

        private static void EmitLevelRange(
            RcHeightfield solid,
            int x,
            int z,
            float yMinMeters,
            float yMaxMeters,
            float bminY,
            float ch,
            int mergeThreshold,
            byte area)
        {
            int smin = ClampSpanY((int)MathF.Round((yMinMeters - bminY) / ch));
            int smax = ClampSpanY((int)MathF.Round((yMaxMeters - bminY) / ch));
            if (smax <= smin)
            {
                smax = ClampSpanY(smin + 1);
                if (smax <= smin) return;
            }

            RcRasterizations.AddSpan(solid, x, z, smin, smax, area > 0 ? area : RcRecast.RC_WALKABLE_AREA, mergeThreshold);
        }

        private static byte ResolveAreaId(byte a, byte b, byte c)
        {
            if (a == b || a == c) return a;
            if (b == c) return b;
            return a;
        }

        private static Corner GetCorner(LogicTerrainField terrain, int mapWidth, int mapHeight, int c, int r)
        {
            if ((uint)c < (uint)mapWidth && (uint)r < (uint)mapHeight)
            {
                LogicTerrainCell cell = terrain.GetCell(c, r);
                return new Corner(cell.HeightLevel, cell.WaterHeightLevel, cell.IsRamp, cell.AreaId);
            }

            return new Corner(0, 0, false, 0);
        }

        private static int ClampSpanY(int value)
            => Math.Clamp(value, 1, RcRecast.RC_SPAN_MAX_HEIGHT - 1);

        private static int ClampVoxel(int value, int limit)
            => Math.Clamp(value, 0, limit - 1);
    }
}
