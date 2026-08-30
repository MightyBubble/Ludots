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
    /// skipping the per-cell triangle re-meshing stage. There is no hand-mirrored rule set here:
    /// corner construction goes through NavTileBuilder.GetVertex, the walkable decision through
    /// NavTileBuilder.ClassifyTriangleEmission, and the two-level cliff boundary through
    /// NavTileBuilder.TryGetSplit (hex cliff-straighten inherited). Obstacles are tested against
    /// the real triangle corner coordinates. Area id 63 is the Recast walkable marker in this
    /// feed and is rejected as an authored id.
    /// </summary>
    internal static class RecastDirectFeedHeightfield
    {
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

            terrain.GetWorldPositionMeters(0, 0, out float originXm, out float originZm);

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

                    NavTileBuilder.Vtx v00 = NavTileBuilder.GetVertex(terrain, mapWidth, mapHeight, c, r, originXm, originZm, heightScale);
                    NavTileBuilder.Vtx v10 = NavTileBuilder.GetVertex(terrain, mapWidth, mapHeight, c + 1, r, originXm, originZm, heightScale);
                    NavTileBuilder.Vtx v01 = NavTileBuilder.GetVertex(terrain, mapWidth, mapHeight, c, r + 1, originXm, originZm, heightScale);
                    NavTileBuilder.Vtx v11 = NavTileBuilder.GetVertex(terrain, mapWidth, mapHeight, c + 1, r + 1, originXm, originZm, heightScale);

                    bool oddRow = isHex && (r & 1) == 1;
                    NavTileBuilder.Vtx ta = v00, tb = v10, tc = v01;
                    NavTileBuilder.Vtx ua = v10, ub = v11, uc = v01;
                    float tax = ax, taz = az, tbx = bx, tbz = bz, tcx = dx, tcz = dz;
                    float uax = bx, uaz = bz, ubx = ex, ubz = ez, ucx = dx, ucz = dz;
                    if (oddRow)
                    {
                        ta = v00; tb = v10; tc = v11;
                        ua = v00; ub = v11; uc = v01;
                        tcx = ex; tcz = ez;
                        uax = ax; uaz = az; ucx = dx; ucz = dz;
                    }

                    NavTileBuilder.NavWalkableEmission firstEmission = NavTileBuilder.ClassifyTriangleEmission(ta, tb, tc, legacyConfig);
                    NavTileBuilder.NavWalkableEmission secondEmission = NavTileBuilder.ClassifyTriangleEmission(ua, ub, uc, legacyConfig);
                    TwoLevelBoundary? firstBoundary = BuildSplitBoundary(terrain, mapWidth, mapHeight, originXm, originZm, heightScale, ta, tb, tc, firstEmission);
                    TwoLevelBoundary? secondBoundary = BuildSplitBoundary(terrain, mapWidth, mapHeight, originXm, originZm, heightScale, ua, ub, uc, secondEmission);

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
                                EmitTriangleColumn(solid, x, z, wx, wz, firstEmission, firstBoundary, tax, taz, heightScale, bminY, ch, mergeThreshold);
                            }
                            else
                            {
                                EmitTriangleColumn(solid, x, z, wx, wz, secondEmission, secondBoundary, uax, uaz, heightScale, bminY, ch, mergeThreshold);
                            }
                        }
                    }
                }
            }

            return solid;
        }

        /// <summary>Two-level cliff boundary: the segment joining the two lone-to-pair split
        /// midpoints (HighExt ends from NavTileBuilder.TryGetSplit, hex cliff-straighten
        /// inherited); the lone corner's side of it takes LoneLevel, the other PairLevel.</summary>
        private readonly struct TwoLevelBoundary
        {
            public float MidAx { get; init; }
            public float MidAz { get; init; }
            public float MidBx { get; init; }
            public float MidBz { get; init; }
            public float LoneX { get; init; }
            public float LoneZ { get; init; }
        }

        private static TwoLevelBoundary? BuildSplitBoundary(
            LogicTerrainField terrain,
            int mapWidth,
            int mapHeight,
            float originXm,
            float originZm,
            float heightScale,
            in NavTileBuilder.Vtx a,
            in NavTileBuilder.Vtx b,
            in NavTileBuilder.Vtx c,
            NavTileBuilder.NavWalkableEmission emission)
        {
            if (emission.Kind != NavTileBuilder.NavWalkableEmissionKind.TwoLevelSplit)
            {
                return null;
            }

            NavTileBuilder.Vtx lone = emission.LoneIndex == 0 ? a : emission.LoneIndex == 1 ? b : c;
            NavTileBuilder.Vtx p1 = emission.LoneIndex == 0 ? b : a;
            NavTileBuilder.Vtx p2 = emission.LoneIndex == 2 ? b : c;
            bool loneIsHigh = lone.H > p1.H;
            bool okA = loneIsHigh
                ? NavTileBuilder.TryGetSplit(terrain, mapWidth, mapHeight, originXm, originZm, heightScale, lone, p1, out var s1)
                : NavTileBuilder.TryGetSplit(terrain, mapWidth, mapHeight, originXm, originZm, heightScale, p1, lone, out s1);
            bool okB = loneIsHigh
                ? NavTileBuilder.TryGetSplit(terrain, mapWidth, mapHeight, originXm, originZm, heightScale, lone, p2, out var s2)
                : NavTileBuilder.TryGetSplit(terrain, mapWidth, mapHeight, originXm, originZm, heightScale, p2, lone, out s2);
            if (!okA || !okB)
            {
                return null;
            }

            return new TwoLevelBoundary
            {
                MidAx = s1.HighExt.X,
                MidAz = s1.HighExt.Z,
                MidBx = s2.HighExt.X,
                MidBz = s2.HighExt.Z,
                LoneX = lone.Pos.X,
                LoneZ = lone.Pos.Z
            };
        }

        /// <summary>Per-column emission for a pre-classified base triangle. Flat floors emit one
        /// level, ramps their full range, two-level triangles test the shared cliff boundary;
        /// dropped triangles emit nothing.</summary>
        private static void EmitTriangleColumn(
            RcHeightfield solid,
            int x,
            int z,
            float wx,
            float wz,
            in NavTileBuilder.NavWalkableEmission emission,
            in TwoLevelBoundary? boundary,
            float loneWorldX,
            float loneWorldZ,
            float heightScale,
            float bminY,
            float ch,
            int mergeThreshold)
        {
            switch (emission.Kind)
            {
                case NavTileBuilder.NavWalkableEmissionKind.FlatFloor:
                    EmitLevelRange(solid, x, z, emission.LowLevel * heightScale, emission.LowLevel * heightScale, bminY, ch, mergeThreshold, emission.AreaId);
                    break;
                case NavTileBuilder.NavWalkableEmissionKind.RampRange:
                    EmitLevelRange(solid, x, z, emission.LowLevel * heightScale, emission.HighLevel * heightScale, bminY, ch, mergeThreshold, emission.AreaId);
                    break;
                case NavTileBuilder.NavWalkableEmissionKind.TwoLevelSplit:
                    if (boundary is not TwoLevelBoundary b)
                    {
                        return;
                    }

                    float nx = b.MidBx - b.MidAx;
                    float nz = b.MidBz - b.MidAz;
                    float sideRef = (nx * (b.LoneZ - b.MidAz)) - (nz * (b.LoneX - b.MidAx));
                    float side = (nx * (wz - b.MidAz)) - (nz * (wx - b.MidAx));
                    bool loneSide = (sideRef >= 0f) == (side >= 0f);
                    float level = loneSide ? emission.LoneLevel : emission.PairLevel;
                    EmitLevelRange(solid, x, z, level * heightScale, level * heightScale, bminY, ch, mergeThreshold, emission.AreaId);
                    break;
            }
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

        private static int ClampSpanY(int value)
            => Math.Clamp(value, 1, RcRecast.RC_SPAN_MAX_HEIGHT - 1);

        private static int ClampVoxel(int value, int limit)
            => Math.Clamp(value, 0, limit - 1);
    }
}
