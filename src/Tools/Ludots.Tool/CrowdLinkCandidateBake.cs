using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Tool;

/// <summary>
/// 一次链接候选烘焙要用的高度、桥面掩码和各种类参数，以及由同一批输入算出的 bakeInputsDigest。地图没写 linkCandidateBake 时不传。
/// </summary>
public sealed class CrowdLinkCandidateBakeInput
{
    public required string MapId { get; init; }
    /// <summary>按 .navsurface 链接种类表顺序；候选的 kindIndex 就是这里的下标。</summary>
    public required IReadOnlyList<RuntimeLinkCandidateBake> Kinds { get; init; }
    public required NavHeightField Heights { get; init; }
    public required byte[] Bridge { get; init; }
    /// <summary>见 <see cref="NavSurfaceContract.BakeInputsDigest"/>。</summary>
    public required uint BakeInputsDigest { get; init; }
}

/// <summary>
/// 一种链接的地形级候选。方向表用 NavGridSteps。坡度上限的正切用机动档案 maxSlopeDeg 的同一式子：Fix64.FromDouble 再乘 Deg2Rad 后 Tan。
/// 端点和硬缝地形掩码由通行矩阵推导，见 <see cref="RuntimeLinkCandidateBake"/>。
/// </summary>
public static class CrowdLinkCandidateBake
{
    public static NavSurfaceJumpCandidate[] Bake(
        string mapId,
        int navCells,
        byte[] terrain,
        byte kindIndex,
        RuntimeLinkCandidateBake settings,
        NavHeightField heights,
        byte[] bridge,
        int cellSizeCm)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentNullException.ThrowIfNull(bridge);
        int n2 = checked(navCells * navCells);
        if (terrain.Length != n2 || heights.HeightsCm.Length != n2 || heights.Slope.Length != n2 || bridge.Length != n2)
        {
            throw new InvalidOperationException(
                $"地图 \"{mapId}\" 的链接候选输入长度不一致（导航格 {navCells}×{navCells}）。");
        }

        if (heights.NavCellsPerSide != navCells || heights.NavCellSizeCm != cellSizeCm)
        {
            throw new InvalidOperationException(
                $"地图 \"{mapId}\" 的高度场是 {heights.NavCellsPerSide} 格、边长 {heights.NavCellSizeCm} cm，导航格是 {navCells} 格、边长 {cellSizeCm} cm。");
        }

        string path = $"linkCandidateBake.{settings.KindId}";
        int maxCells = CellsOf(mapId, $"{path}.maxGapCm", settings.MaxGapCm, cellSizeCm);
        int spacing = CellsOf(mapId, $"{path}.linkSpacingCm", settings.LinkSpacingCm, cellSizeCm);
        if (maxCells >= ushort.MaxValue)
        {
            throw new InvalidOperationException($"地图 \"{mapId}\" 的 {path}.maxGapCm 换算 {maxCells} 格，超过候选 steps 的 u16。");
        }

        if (!(settings.EndpointMaxSlopeDeg > 0d && settings.EndpointMaxSlopeDeg < 90d))
        {
            throw new InvalidOperationException(
                $"地图 \"{mapId}\" 的 {path}.endpointMaxSlopeDeg = {settings.EndpointMaxSlopeDeg}，需在 (0, 90) 之间。");
        }

        bool[] endpoint = settings.EndpointTerrain;
        bool[] hardGap = settings.HardGapTerrain;
        for (int i = 0; i < terrain.Length; i++)
        {
            if (terrain[i] >= endpoint.Length || terrain[i] >= hardGap.Length)
            {
                throw new InvalidOperationException(
                    $"地图 \"{mapId}\" 的格 {i} 地形编号 {terrain[i]} 超出 {path} 的地形掩码（{endpoint.Length} 项）。");
            }
        }
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
            throw new InvalidOperationException($"地图 \"{mapId}\" 的导航格数 {navCells} 超过链接候选端点的 ushort。");
        }

        var jumps = new NavSurfaceJumpCandidate[order.Count];
        for (int i = 0; i < order.Count; i++)
        {
            Slot slot = order[i];
            jumps[i] = new NavSurfaceJumpCandidate
            {
                KindIndex = kindIndex,
                Diagonal = slot.Diagonal,
                Steps = (ushort)slot.T,
                FromX = (ushort)(slot.A % navCells),
                FromY = (ushort)(slot.A / navCells),
                ToX = (ushort)(slot.B % navCells),
                ToY = (ushort)(slot.B / navCells),
                DropCm = (height[slot.A] - height[slot.B]).RoundToInt(),
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
            throw new InvalidOperationException($"地图 \"{mapId}\" 的 {field} = {centimeters}，需为 > 0。");
        }

        if (cellSizeCm <= 0)
        {
            throw new InvalidOperationException($"地图 \"{mapId}\" 的格边长 {cellSizeCm} cm，需为 > 0。");
        }

        if (centimeters % cellSizeCm != 0)
        {
            throw new InvalidOperationException(
                $"地图 \"{mapId}\" 的 {field}（{centimeters} cm）不能被格边长 {cellSizeCm} cm 整除。");
        }

        return centimeters / cellSizeCm;
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
