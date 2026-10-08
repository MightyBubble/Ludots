using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// 初始部署与点名生成(sim/population.js 的 spawn / spawnAt 移植,Fix64 为准):
/// 每条指令从自己加盐的 mulberry32 流取值;出生点抖动、编组中心、散布位置、
/// 格内落点的每个取值顺序与参考实现逐一对应——同一份指令流,两端部署逐位一致。
/// </summary>
public static class CrowdDeployment
{
    private const int SaltSpawnSeed = 31, SaltSpawn = 7919, SaltSpawnAt = 15485863;

    /// <summary>批量部署(count 上限 = 容量;类型过滤 = 全部非 special 单位模板)。</summary>
    public static int Spawn(CrowdSimSession sim, int count)
    {
        var cfg = sim.Config;
        var dp = cfg.Deploy;
        var rng = CrowdSimRng.Create((long)cfg.WorldSeed * SaltSpawnSeed + (long)++sim.SpawnSeq * SaltSpawn);
        int n = cfg.NavCellCount;
        var cellSize = Fix64.FromInt(cfg.NavCellSizeCm);

        // D31:单位先清——后续任何异常都不会留下指着已清组的活单位
        sim.Units.Clear();
        sim.Groups.Reset();
        sim.SetSelectedCount(0);

        var types = new List<int>();
        for (int t = 0; t < cfg.UnitTypes.Count; t++)
        {
            if (!cfg.UnitTypes[t].Special) types.Add(t);
        }

        var radiusClasses = DistinctRadiusClasses(cfg);
        int playerCount = cfg.Deploy.Bases.Count;
        var perPlayer = new List<CrowdNavGroupSet.Group>[playerCount];
        var centers = new Dictionary<int, List<int>>();
        var typeOf = new Dictionary<int, int>();
        for (int p = 0; p < playerCount; p++)
        {
            var list = new List<CrowdNavGroupSet.Group>();
            var b = cfg.Deploy.Bases[p];
            int bx = (int)(b.XCm / cfg.NavCellSizeCm), by = (int)(b.YCm / cfg.NavCellSizeCm);
            foreach (int t in types)
            {
                for (int r = 0; r < radiusClasses.Count; r++)
                {
                    int l = cfg.UnitTypes[t].AgentTypeIndex;
                    var nav = sim.NavFor(l, r);
                    if (nav.Cells.Length == 0) continue;
                    var cs = new List<int>();
                    for (int k = 0; k < dp.CentersPerGroup; k++)
                    {
                        int x = ClampN(bx + DetRound((rng.Next() * 2 - Fix64.OneValue) * dp.BaseJitterCells), n);
                        int y = ClampN(by + DetRound((rng.Next() * 2 - Fix64.OneValue) * dp.BaseJitterCells), n);
                        int c = NearestPassable(nav, y * n + x);
                        if (c >= 0) cs.Add(c);
                    }

                    if (cs.Count == 0) continue;
                    var g = sim.Groups.Alloc(p, l, r, nav.Id);
                    centers[g.Id] = cs;
                    typeOf[g.Id] = t;
                    list.Add(g);
                }
            }

            perPlayer[p] = list;
        }

        int total = Math.Min(count, sim.Units.Capacity), tries = cfg.Spawn.PlacementTries;
        int made = 0, skipped = 0;
        for (int k = 0; k < total; k++)
        {
            int p = k % playerCount;
            var list = perPlayer[p];
            if (list.Count == 0) { skipped++; continue; } // D35:该玩家没有可用的 类型×半径 组合
            var g = list[(int)(rng.Next() * list.Count)];
            var cs = centers[g.Id];
            int c = cs[(int)(rng.Next() * cs.Count)];
            int cx = c % n, cy = c / n;
            int cell = -1;
            for (int t = 0; t < tries && cell < 0; t++)
            {
                int x = cx + DetRound((rng.Next() + rng.Next() - Fix64.OneValue) * dp.SpreadCells);
                int y = cy + DetRound((rng.Next() + rng.Next() - Fix64.OneValue) * dp.SpreadCells);
                if (x >= 0 && y >= 0 && x < n && y < n && sim.NavFor(g.LayerIdx, g.RIdx).Passable[y * n + x] != 0) cell = y * n + x;
            }

            if (AddUnit(sim, cell < 0 ? c : cell, rng, g, typeOf[g.Id], cellSize) >= 0) made++;
        }

        if (skipped > 0) sim.NotifySpawnSkip(skipped, total);
        sim.Groups.ReleaseEmpty();
        return made;
    }

    /// <summary>点名生成(spawnAt 移植):在某点周围为某玩家生成 count 个 (t, r) 单位。</summary>
    public static int SpawnAt(CrowdSimSession sim, Fix64 wx, Fix64 wy, int count, int player, int unitType, int rIdx)
    {
        var cfg = sim.Config;
        int l = cfg.UnitTypes[unitType].AgentTypeIndex;
        var nav = sim.NavFor(l, rIdx);
        int n = cfg.NavCellCount;
        var cellSize = Fix64.FromInt(cfg.NavCellSizeCm);
        int c0 = NearestPassable(nav, CellAt(wx, wy, n, cfg.NavCellSizeCm));
        if (c0 < 0 || sim.Units.Count >= sim.Units.Capacity) return 0;
        var rng = CrowdSimRng.Create((long)cfg.WorldSeed + (long)++sim.SpawnSeq * SaltSpawnAt);
        // player 为 Ludots 玩家号(1..P);组内索引 0 基(与批量部署同一编号序)
        var g = sim.Groups.Alloc(player - 1, l, rIdx, nav.Id);
        int comp = nav.Comp[c0];
        var profile = ProfileOf(cfg, l, rIdx);
        var spread = Fix64.Max(Fix64.OneValue,
            Fix64Math.Sqrt(Fix64.FromInt(count)) * profile.PersonalRadiusCm / 100 * cfg.Spawn.ClusterSpacing / (cellSize / 100));
        int cx0 = c0 % n, cy0 = c0 / n, made = 0;
        for (; made < count && sim.Units.Count < sim.Units.Capacity; made++)
        {
            int cell = c0;
            int triesUsed = 0;
            for (int t = 0; t < cfg.Spawn.PlacementTries; t++)
            {
                triesUsed++;
                int x = cx0 + DetRound((rng.Next() * 2 - Fix64.OneValue) * spread);
                int y = cy0 + DetRound((rng.Next() * 2 - Fix64.OneValue) * spread);
                bool ok = x >= 0 && y >= 0 && x < n && y < n && nav.Passable[y * n + x] != 0 && nav.Comp[y * n + x] == comp;
                if (ok)
                {
                    cell = y * n + x;
                    break;
                }
            }

            AddUnit(sim, cell, rng, g, unitType, cellSize);
        }

        sim.Groups.ReleaseEmpty();
        return made;
    }

    // t = 单位模板下标;其移动类型必须是组的导航层(D54:满容量只影响计数,不留半生成状态)
    private static int AddUnit(CrowdSimSession sim, int cell, CrowdSimRng rng, CrowdNavGroupSet.Group g, int t, Fix64 cellSize)
    {
        var cfg = sim.Config;
        int n = cfg.NavCellCount;
        var e = cfg.Spawn.CellInset;
        var w = Fix64.OneValue - 2 * e;
        var x = (Fix64.FromInt(cell % n) + e + rng.Next() * w) * cellSize;
        var y = (Fix64.FromInt(cell / n) + e + rng.Next() * w) * cellSize;
        var profile = ProfileOf(cfg, g.LayerIdx, g.RIdx);
        // PlayerOwner 用 Ludots 玩家号(1..P;组内索引 0 基只服务于 RNG 与编号序)
        var unit = new CrowdUnitSpawnRequest(
            x, y,
            t, g.RIdx,
            g.Id,
            cfg.Deploy.Bases[g.Player].PlayerId,
            profile.Id,
            profile.RadiusCm,
            profile.PersonalRadiusCm,
            cfg.AgentTypes[g.LayerIdx].SpeedCmPerSecond,
            profile.PushPriority);
        int dense = sim.Units.Add(in unit);
        if (dense < 0) return dense;
        // D54:槽位复用不带陈旧推挤;新单位 calm=0,首次求解前必醒
        sim.Movement?.ClearPush(dense);
        g.Count++;
        return dense;
    }

    /// <summary>(移动类型, 半径级) → 代理体型;半径级 = 半径在全体半径级中的升序名次。</summary>
    public static RuntimeAgentProfile ProfileOf(CrowdSimulationRuntimeConfig cfg, int agentTypeIndex, int rIdx)
    {
        var radiusClasses = DistinctRadiusClasses(cfg);
        for (int i = 0; i < cfg.Profiles.Count; i++)
        {
            var p = cfg.Profiles[i];
            if (p.AgentTypeIndex == agentTypeIndex && (int)p.RadiusCm.ToInt() == radiusClasses[rIdx]) return p;
        }

        throw new InvalidOperationException($"代理体型缺失: 移动类型 {agentTypeIndex} × 半径级 {rIdx}。");
    }

    /// <summary>全体半径级(升序去重的 radiusCm 表;与参考实现 radiusClasses 的顺序对应)。</summary>
    public static List<int> DistinctRadiusClasses(CrowdSimulationRuntimeConfig cfg)
    {
        var set = new SortedSet<int>();
        foreach (var p in cfg.Profiles) set.Add((int)p.RadiusCm.ToInt());
        return new List<int>(set);
    }

    public static int CellAt(Fix64 wx, Fix64 wy, int n, int cellSizeCm)
    {
        int x = (int)(wx / Fix64.FromInt(cellSizeCm)).ToInt(), y = (int)(wy / Fix64.FromInt(cellSizeCm)).ToInt();
        if (x < 0 || y < 0 || x >= n || y >= n) return -1;
        return y * n + x;
    }

    /// <summary>最近可走格(nearestPassable 移植:逐环外扩,严格小于才换,首优即平局的先见者)。</summary>
    public static int NearestPassable(NavContext nav, int cell)
    {
        int n = nav.CellCount;
        if (cell >= 0 && cell < n * n && nav.Passable[cell] != 0) return cell;
        if (cell < 0 || cell >= n * n) return -1;
        int cx = cell % n, cy = cell / n;
        for (int r = 1; r < n; r++)
        {
            int best = -1, bestD = int.MaxValue;
            for (int y = cy - r; y <= cy + r; y++)
            {
                if (y < 0 || y >= n) continue;
                int step = (y == cy - r || y == cy + r) ? 1 : 2 * r;
                for (int x = cx - r; x <= cx + r; x += step)
                {
                    if (x < 0 || x >= n) continue;
                    int i = y * n + x;
                    if (nav.Passable[i] == 0) continue;
                    int d = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                    if (d < bestD) { bestD = d; best = i; }
                }
            }

            if (best >= 0) return best;
        }

        return -1;
    }

    /// <summary>detRound 移植(round-half-up:floor(v + 0.5))。</summary>
    public static int DetRound(Fix64 v) => (int)Fix64.Floor(v + Fix64.HalfValue).ToLong();

    private static int ClampN(int v, int n) => Math.Min(n - 1, Math.Max(0, v));
}
