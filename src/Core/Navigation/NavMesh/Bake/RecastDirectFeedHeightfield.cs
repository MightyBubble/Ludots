using System;
using DotRecast.Core.Numerics;
using DotRecast.Recast;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Spatial;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    /// <summary>
    /// Terrain→Recast solid heightfield column feed: walks LogicTerrainField cells over
    /// the tile footprint plus the Recast border margin and writes one floor span per
    /// voxel column directly. The per-cell triangle re-meshing stage is skipped entirely;
    /// walkability, water, cliff and area semantics mirror NavTileBuilder per cell, and
    /// obstacle footprints drop cells the same way the triangle path drops triangles.
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
            int cellsW = endC - startC;
            int cellsH = endR - startR;
            var walkable = new bool[cellsW * cellsH];
            var levels = new byte[cellsW * cellsH];
            var areaIds = new byte[cellsW * cellsH];

            float minHeight = float.PositiveInfinity;
            float maxHeight = float.NegativeInfinity;
            int walkableCount = 0;
            for (int r = startR; r < endR; r++)
            {
                for (int c = startC; c < endC; c++)
                {
                    int idx = (r - startR) * cellsW + (c - startC);
                    if (!NavTileBuilder.IsCellAnyTriangleWalkable(terrain, mapWidth, mapHeight, c, r, legacyConfig))
                    {
                        continue;
                    }

                    LogicTerrainCell cell = terrain.GetCell(c, r);
                    walkable[idx] = true;
                    levels[idx] = cell.HeightLevel;
                    areaIds[idx] = cell.AreaId;
                    walkableCount++;

                    float y = cell.HeightLevel * legacyConfig.HeightScaleMeters;
                    minHeight = MathF.Min(minHeight, y);
                    maxHeight = MathF.Max(maxHeight, y);
                }
            }

            if (walkableCount == 0)
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
                    int idx = (r - startR) * cellsW + (c - startC);
                    if (!walkable[idx])
                    {
                        continue;
                    }

                    terrain.GetWorldPositionMeters(c, r, out float ax, out float az);
                    terrain.GetWorldPositionMeters(c + 1, r + 1, out float bx, out float bz);
                    float centerX = (ax + bx) * 0.5f;
                    float centerZ = (az + bz) * 0.5f;

                    if (hasObstacles)
                    {
                        int cxcm = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(centerX));
                        int czcm = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(centerZ));
                        if (NavObstacleGeometry.IsTriangleBlockedByObstacles(cxcm, czcm, cxcm, czcm, cxcm, czcm, obstacles, layerId))
                        {
                            continue;
                        }
                    }

                    float floor = levels[idx] * legacyConfig.HeightScaleMeters;
                    int smin = ClampSpanY((int)MathF.Round((floor - bminY) / ch));
                    int smax = ClampSpanY(smin + 1);
                    if (smax <= smin)
                    {
                        smax = ClampSpanY(smin + 1);
                        if (smax <= smin) continue;
                    }

                    int area = areaIds[idx] > 0 ? areaIds[idx] : RcRecast.RC_WALKABLE_AREA;

                    int x0 = ClampVoxel((int)MathF.Floor((ax - bmin.X) / cs), solid.width);
                    int x1 = Math.Min(solid.width, Math.Max(x0 + 1, ClampVoxel((int)MathF.Ceiling((bx - bmin.X) / cs), solid.width) + 1));
                    int z0 = ClampVoxel((int)MathF.Floor((az - bmin.Z) / cs), solid.height);
                    int z1 = Math.Min(solid.height, Math.Max(z0 + 1, ClampVoxel((int)MathF.Ceiling((bz - bmin.Z) / cs), solid.height) + 1));

                    for (int z = z0; z < z1; z++)
                    {
                        for (int x = x0; x < x1; x++)
                        {
                            RcRasterizations.AddSpan(solid, x, z, smin, smax, area, mergeThreshold);
                        }
                    }
                }
            }

            return solid;
        }

        private static int ClampSpanY(int value)
            => Math.Clamp(value, 1, RcRecast.RC_SPAN_MAX_HEIGHT - 1);

        private static int ClampVoxel(int value, int limit)
            => Math.Clamp(value, 0, limit - 1);
    }
}
