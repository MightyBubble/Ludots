using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// 初始部署与点名生成，长度用 Fix64。
/// 每条指令从自己加盐的 mulberry32 流取值。出生点抖动、编组中心、散布位置、
/// 格内落点按固定顺序取值，同一份指令流部署结果逐位一致。
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
        int navCellsPerSide = cfg.NavCellsPerSide;
        var navCellSizeCm = Fix64.FromInt(cfg.NavCellSizeCm);

        // 单位先清——后续任何异常都不会留下指着已清组的活单位。
        // 生成请求在本方法返回前排空，同一条 Flush 里更后的指令看得到实体。
        sim.Units.Clear();
        sim.Groups.Reset();
        try
        {

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
            for (int t = 0; t < cfg.UnitTypes.Count; t++)
            {
                for (int r = 0; r < radiusClasses.Count; r++)
                {
                    int l = cfg.UnitTypes[t].AgentTypeIndex;
                    if (ProfileOf(cfg, l, r).Special)
                    {
                        continue;
                    }
                    var nav = sim.NavFor(l, r);
                    if (nav.CellCount == 0) continue;
                    var centerCells = new List<int>();
                    for (int k = 0; k < dp.CentersPerGroup; k++)
                    {
                        int x = ClampN(bx + DetRound((rng.Next() * 2 - Fix64.OneValue) * dp.BaseJitterNavCells), navCellsPerSide);
                        int y = ClampN(by + DetRound((rng.Next() * 2 - Fix64.OneValue) * dp.BaseJitterNavCells), navCellsPerSide);
                        int c = NearestPassable(nav, y * navCellsPerSide + x);
                        if (c >= 0) centerCells.Add(c);
                    }

                    if (centerCells.Count == 0) continue;
                    var g = sim.Groups.Alloc(p, l, r, nav.Id);
                    sim.AttachGroupNav(g); // 规划句柄挂玩家视野组当前槽(马达句柄恒真相)
                    centers[g.Id] = centerCells;
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
            if (list.Count == 0) { skipped++; continue; } // 该玩家没有可用的 类型×半径 组合
            var g = list[(int)(rng.Next() * list.Count)];
            var groupCenters = centers[g.Id];
            int c = groupCenters[(int)(rng.Next() * groupCenters.Count)];
            int cx = c % navCellsPerSide, cy = c / navCellsPerSide;
            int cell = -1;
            for (int t = 0; t < tries && cell < 0; t++)
            {
                int x = cx + DetRound((rng.Next() + rng.Next() - Fix64.OneValue) * dp.SpreadNavCells);
                int y = cy + DetRound((rng.Next() + rng.Next() - Fix64.OneValue) * dp.SpreadNavCells);
                if (x >= 0 && y >= 0 && x < navCellsPerSide && y < navCellsPerSide && sim.NavFor(g.LayerIdx, g.RIdx).Passable[y * navCellsPerSide + x] != 0) cell = y * navCellsPerSide + x;
            }

            if (AddUnit(sim, cell < 0 ? c : cell, rng, g, typeOf[g.Id], navCellSizeCm) >= 0) made++;
        }

        if (skipped > 0) sim.NotifySpawnSkip(skipped, total);
        sim.Groups.ReleaseEmpty();
        return made;
        }
        finally
        {
            sim.Units.MaterializePending();
        }
    }

    /// <summary>点名生成：在某点周围为某玩家生成 count 个 (t, r) 单位。</summary>
    public static int SpawnAt(CrowdSimSession sim, Fix64 wx, Fix64 wy, int count, int player, int unitType, int rIdx)
    {
        var cfg = sim.Config;
        int l = cfg.UnitTypes[unitType].AgentTypeIndex;
        var nav = sim.NavFor(l, rIdx);
        int navCellsPerSide = cfg.NavCellsPerSide;
        var navCellSizeCm = Fix64.FromInt(cfg.NavCellSizeCm);
        int c0 = NearestPassable(nav, CrowdSimulationSpace.CellAtStrict(wx, wy, cfg.NavCellSizeCm, navCellsPerSide));
        if (c0 < 0 || sim.Units.Count >= sim.Units.Capacity) return 0;
        try
        {
        var rng = CrowdSimRng.Create((long)cfg.WorldSeed + (long)++sim.SpawnSeq * SaltSpawnAt);
        // player 为 Ludots 玩家号(1..P)。组下标用 PairRules.IndexByPlayerId。
        var g = sim.Groups.Alloc(cfg.PairRules.IndexByPlayerId[player], l, rIdx, nav.Id);
        sim.AttachGroupNav(g); // 规划句柄挂玩家视野组当前槽(马达句柄恒真相)
        int comp = nav.Comp[c0];
        var profile = ProfileOf(cfg, l, rIdx);
        var spread = Fix64.Max(Fix64.OneValue,
            CrowdReferenceMeters.SpawnSpreadNavCells(count, profile.PersonalRadiusCm, cfg.Spawn.GroupSpacingScale, navCellSizeCm));
        int cx0 = c0 % navCellsPerSide, cy0 = c0 / navCellsPerSide, made = 0;
        for (; made < count && sim.Units.Count < sim.Units.Capacity; made++)
        {
            int cell = c0;
            int triesUsed = 0;
            for (int t = 0; t < cfg.Spawn.PlacementTries; t++)
            {
                triesUsed++;
                int x = cx0 + DetRound((rng.Next() * 2 - Fix64.OneValue) * spread);
                int y = cy0 + DetRound((rng.Next() * 2 - Fix64.OneValue) * spread);
                bool ok = x >= 0 && y >= 0 && x < navCellsPerSide && y < navCellsPerSide && nav.Passable[y * navCellsPerSide + x] != 0 && nav.Comp[y * navCellsPerSide + x] == comp;
                if (ok)
                {
                    cell = y * navCellsPerSide + x;
                    break;
                }
            }

            AddUnit(sim, cell, rng, g, unitType, navCellSizeCm);
        }

        sim.Groups.ReleaseEmpty();
        return made;
        }
        finally
        {
            sim.Units.MaterializePending();
        }
    }

    // t = 单位模板下标;其移动类型必须是组的导航层(满容量只影响计数,不留半生成状态)
    private static int AddUnit(CrowdSimSession sim, int cell, CrowdSimRng rng, CrowdNavGroupSet.Group g, int t, Fix64 navCellSizeCm)
    {
        var cfg = sim.Config;
        int navCellsPerSide = cfg.NavCellsPerSide;
        var e = cfg.Spawn.NavCellInsetFraction;
        var w = Fix64.OneValue - 2 * e;
        var x = (Fix64.FromInt(cell % navCellsPerSide) + e + rng.Next() * w) * navCellSizeCm;
        var y = (Fix64.FromInt(cell / navCellsPerSide) + e + rng.Next() * w) * navCellSizeCm;
        var profile = ProfileOf(cfg, g.LayerIdx, g.RIdx);
        int profileIndex = ProfileIndexOf(cfg, g.LayerIdx, g.RIdx);
        // PlayerOwner 用 Ludots 玩家号(1..P;组内索引 0 基只服务于 RNG 与编号序)
        var unit = new CrowdUnitSpawnRequest(
            x, y,
            t, g.RIdx,
            g.Id,
            cfg.Deploy.Bases[g.Player].PlayerId,
            profile.RadiusCm,
            profileIndex,
            profile.AgentTypeIndex);
        int dense = sim.Units.Add(in unit);
        if (dense < 0) return dense;
        // 槽位复用不带陈旧推挤;新单位 calm=0,首次求解前必醒
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

    /// <summary><see cref="ProfileOf"/> 在 <see cref="CrowdSimulationRuntimeConfig.Profiles"/> 里的下标。</summary>
    public static int ProfileIndexOf(CrowdSimulationRuntimeConfig cfg, int agentTypeIndex, int rIdx)
    {
        var radiusClasses = DistinctRadiusClasses(cfg);
        int radiusCm = radiusClasses[rIdx];
        for (int i = 0; i < cfg.Profiles.Count; i++)
        {
            var p = cfg.Profiles[i];
            if (p.AgentTypeIndex == agentTypeIndex && (int)p.RadiusCm.ToInt() == radiusCm) return i;
        }

        throw new InvalidOperationException($"代理体型缺失: 移动类型 {agentTypeIndex} × 半径级 {rIdx}。");
    }

    /// <summary>全体半径级，radiusCm 升序去重。</summary>
    public static IReadOnlyList<int> DistinctRadiusClasses(CrowdSimulationRuntimeConfig cfg) => cfg.RadiusClasses;

    /// <summary>单位模板 id → (单位行, 半径级)。表里没有这个 id 即报错。</summary>
    public static (int UnitType, int RadiusIndex) LocateTemplate(CrowdSimulationRuntimeConfig cfg, string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new InvalidOperationException("单位模板 id 为空。");
        }

        var radiusClasses = DistinctRadiusClasses(cfg);
        for (int t = 0; t < cfg.UnitTypes.Count; t++)
        {
            var templates = cfg.UnitTypes[t].TemplatesByRadiusCm;
            if (templates == null)
            {
                continue;
            }

            foreach (var pair in templates)
            {
                if (!string.Equals(pair.Value, templateId, StringComparison.Ordinal))
                {
                    continue;
                }

                return (t, IndexOfRadiusClass(radiusClasses, pair.Key));
            }
        }

        throw new InvalidOperationException($"未知单位模板 \"{templateId}\"。");
    }

    public static int IndexOfRadiusClass(IReadOnlyList<int> radiusClasses, int radiusCm)
    {
        for (int i = 0; i < radiusClasses.Count; i++)
        {
            if (radiusClasses[i] == radiusCm) return i;
        }

        throw new InvalidOperationException($"半径级表缺半径 {radiusCm}。");
    }

    /// <summary>最近可走格：逐环外扩，严格小于才换，平局留先见者。</summary>
    public static int NearestPassable(NavContext nav, int cell)
    {
        int navCellsPerSide = nav.NavCellsPerSide;
        if (cell >= 0 && cell < navCellsPerSide * navCellsPerSide && nav.Passable[cell] != 0) return cell;
        if (cell < 0 || cell >= navCellsPerSide * navCellsPerSide) return -1;
        int cx = cell % navCellsPerSide, cy = cell / navCellsPerSide;
        for (int r = 1; r < navCellsPerSide; r++)
        {
            int best = -1, bestD = int.MaxValue;
            for (int y = cy - r; y <= cy + r; y++)
            {
                if (y < 0 || y >= navCellsPerSide) continue;
                int step = (y == cy - r || y == cy + r) ? 1 : 2 * r;
                for (int x = cx - r; x <= cx + r; x += step)
                {
                    if (x < 0 || x >= navCellsPerSide) continue;
                    int i = y * navCellsPerSide + x;
                    if (nav.Passable[i] == 0) continue;
                    int d = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                    if (d < bestD) { bestD = d; best = i; }
                }
            }

            if (best >= 0) return best;
        }

        return -1;
    }

    /// <summary>round-half-up：floor(v + 0.5)。</summary>
    public static int DetRound(Fix64 v) => (int)Fix64.Floor(v + Fix64.HalfValue).ToLong();

    private static int ClampN(int v, int navCellsPerSide) => Math.Min(navCellsPerSide - 1, Math.Max(0, v));
}
