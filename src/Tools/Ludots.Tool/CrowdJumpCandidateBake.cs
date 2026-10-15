using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Tool;

/// <summary>
/// 一次跳跃候选烘焙要用的高度、桥面掩码和地图参数。地图没写 jumpLinkBake 时不传。
/// </summary>
public sealed class CrowdJumpLinkBakeInput
{
    public required string MapId { get; init; }
    public required NavJumpLinkBakeConfig Settings { get; init; }
    public required NavHeightField Heights { get; init; }
    public required byte[] Bridge { get; init; }
}

/// <summary>
/// 地形级跳跃候选。方向表用 NavGridSteps。坡度上限的正切用机动档案 maxSlopeDeg 的同一式子：Fix64.FromDouble 再乘 Deg2Rad 后 Tan。
/// </summary>
public static class CrowdJumpCandidateBake
{
    public static NavSurfaceJumpCandidate[] Bake(
        string mapId,
        int navCells,
        byte[] terrain,
        IReadOnlyList<string> terrainTypeIds,
        NavJumpLinkBakeConfig settings,
        NavHeightField heights,
        byte[] bridge,
        int cellSizeCm)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(terrainTypeIds);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentNullException.ThrowIfNull(bridge);
        int n2 = checked(navCells * navCells);
        if (terrain.Length != n2 || heights.HeightsCm.Length != n2 || heights.Slope.Length != n2 || bridge.Length != n2)
        {
            throw new InvalidOperationException(
                $"地图 \"{mapId}\" 的跳跃候选输入长度不一致（导航格 {navCells}×{navCells}）。");
        }

        if (heights.NavCellsPerSide != navCells || heights.NavCellSizeCm != cellSizeCm)
        {
            throw new InvalidOperationException(
                $"地图 \"{mapId}\" 的高度场是 {heights.NavCellsPerSide} 格、边长 {heights.NavCellSizeCm} cm，导航格是 {navCells} 格、边长 {cellSizeCm} cm。");
        }

        int maxCells = CellsOf(mapId, "maxGapCm", settings.MaxGapCm, cellSizeCm);
        int spacing = CellsOf(mapId, "linkSpacingCm", settings.LinkSpacingCm, cellSizeCm);
        if (!(settings.EndpointMaxSlopeDeg > 0f) || float.IsInfinity(settings.EndpointMaxSlopeDeg))
        {
            throw new InvalidOperationException(
                $"地图 \"{mapId}\" 的 jumpLinkBake.endpointMaxSlopeDeg = {settings.EndpointMaxSlopeDeg}，需为 > 0。");
        }

        bool[] endpoint = TerrainMask(mapId, "endpointTerrain", settings.EndpointTerrain, terrainTypeIds);
        bool[] hardGap = TerrainMask(mapId, "gapTerrain", settings.GapTerrain, terrainTypeIds);
        Fix64 degrees = Fix64.FromDouble(settings.EndpointMaxSlopeDeg);
        Fix64 flat = Fix64Math.Tan(degrees * Fix64.Deg2Rad);
        Fix64[] height = heights.HeightsCm;
        Fix64[] slope = heights.Slope;
        int bw = (navCells + spacing - 1) / spacing;
        var order = new List<Slot>();
        var indexByBucket = new Dictionary<int, int>();
        for (int y = 0; y < navCells; y++)
        {
            for (int x = 0; x < navCells; x++)
            {
                int a = y * navCells + x;
                if (!Dry(a))
                {
                    continue;
                }

                for (int d = 0; d < 8; d++)
                {
                    int hard = 0;
                    bool diagonal = d >= 4;
                    for (int t = 1; t <= maxCells + 1; t++)
                    {
                        int cx = x + NavGridSteps.DX[d] * t;
                        int cy = y + NavGridSteps.DY[d] * t;
                        if ((uint)cx >= (uint)navCells || (uint)cy >= (uint)navCells)
                        {
                            break;
                        }

                        int c = cy * navCells + cx;
                        if (Gap(c))
                        {
                            if (hardGap[terrain[c]])
                            {
                                hard++;
                            }

                            continue;
                        }

                        if (Dry(c) && t >= 2 && hard > 0 && height[a] >= height[c])
                        {
                            int key = (((y + cy) >> 1) / spacing) * bw + (((x + cx) >> 1) / spacing);
                            var next = new Slot(a, c, t, diagonal);
                            if (!indexByBucket.TryGetValue(key, out int at))
                            {
                                indexByBucket.Add(key, order.Count);
                                order.Add(next);
                            }
                            else if (IsStrictlyShorter(next, order[at]))
                            {
                                // 桶第一次出现的位置固定；更短的候选只替换内容。
                                order[at] = next;
                            }
                        }

                        break;
                    }
                }
            }
        }

        if (navCells > ushort.MaxValue)
        {
            throw new InvalidOperationException($"地图 \"{mapId}\" 的导航格数 {navCells} 超过跳跃候选端点的 ushort。");
        }

        var jumps = new NavSurfaceJumpCandidate[order.Count];
        for (int i = 0; i < order.Count; i++)
        {
            Slot slot = order[i];
            int dropCm = (height[slot.A] - height[slot.B]).RoundToInt();
            float length = slot.Diagonal ? (float)(slot.T * Math.Sqrt(2.0)) : slot.T;
            jumps[i] = new NavSurfaceJumpCandidate
            {
                FromX = (ushort)(slot.A % navCells),
                FromY = (ushort)(slot.A / navCells),
                ToX = (ushort)(slot.B % navCells),
                ToY = (ushort)(slot.B / navCells),
                DropCm = dropCm,
                LengthCells = length,
            };
        }

        return jumps;

        bool Dry(int cell) => endpoint[terrain[cell]] && slope[cell] <= flat && bridge[cell] == 0;
        bool Gap(int cell) => bridge[cell] == 0 && !Dry(cell);
    }

    public static int CellsOf(string mapId, string field, int centimeters, int cellSizeCm)
    {
        if (centimeters <= 0)
        {
            throw new InvalidOperationException($"地图 \"{mapId}\" 的 jumpLinkBake.{field} = {centimeters}，需为 > 0。");
        }

        if (cellSizeCm <= 0)
        {
            throw new InvalidOperationException($"地图 \"{mapId}\" 的格边长 {cellSizeCm} cm，需为 > 0。");
        }

        if (centimeters % cellSizeCm != 0)
        {
            throw new InvalidOperationException(
                $"地图 \"{mapId}\" 的 jumpLinkBake.{field}（{centimeters} cm）不能被格边长 {cellSizeCm} cm 整除。");
        }

        return centimeters / cellSizeCm;
    }

    private static bool[] TerrainMask(string mapId, string field, IReadOnlyList<string>? ids, IReadOnlyList<string> terrainTypeIds)
    {
        if (ids == null || ids.Count == 0)
        {
            throw new InvalidOperationException($"地图 \"{mapId}\" 的 jumpLinkBake.{field} 不能是空数组。");
        }

        var mask = new bool[terrainTypeIds.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            string id = ids[i];
            int index = -1;
            for (int t = 0; t < terrainTypeIds.Count; t++)
            {
                if (string.Equals(terrainTypeIds[t], id, StringComparison.Ordinal))
                {
                    index = t;
                    break;
                }
            }

            if (index < 0)
            {
                throw new InvalidOperationException(
                    $"地图 \"{mapId}\" 的 jumpLinkBake.{field} 的地形 id \"{id}\" 不在 terrainTypes 里。");
            }

            mask[index] = true;
        }

        return mask;
    }

    private static bool IsStrictlyShorter(Slot next, Slot current)
    {
        long left = (long)next.T * next.T;
        long right = (long)current.T * current.T;
        if (next.Diagonal)
        {
            left *= 2;
        }

        if (current.Diagonal)
        {
            right *= 2;
        }

        return left < right;
    }

    private readonly record struct Slot(int A, int B, int T, bool Diagonal);
}
