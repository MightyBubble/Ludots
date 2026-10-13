using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Structures;

/// <summary>每次结构变更的重烘焙报告(阶段 0 数据,后续阶段补齐字段):真值逐字段对拍。</summary>
public sealed class CrowdRebakeReport
{
    public const byte KindPlace = 1;
    public const byte KindRemove = 2;

    /// <summary>op 执行 tick(阶段 0,指令内)。</summary>
    public int ExecTick { get; set; }
    /// <summary>报告发布 tick(最后阶段;切片全在指令内时与 ExecTick 相同)。</summary>
    public int ReportTick { get; set; }
    public byte Kind { get; set; }
    /// <summary>受影响 tile 并集大小(全部上下文去重)。</summary>
    public int Tiles { get; set; }
    /// <summary>看到变化的上下文数。</summary>
    public int Contexts { get; set; }
    /// <summary>其中仅代价变化(未动可走位)的上下文数。</summary>
    public int CostOnly { get; set; }
    /// <summary>本次重烘 tile 缓存命中 / 未命中次数。</summary>
    public int Hits { get; set; }
    public int Misses { get; set; }
    /// <summary>触发重规划的指令数(阶段 1 的队伍反应)。</summary>
    public int Orders { get; set; }
    /// <summary>反应后待刷新的流场队列长度。</summary>
    public int Refreshes { get; set; }
    /// <summary>被挤离阻挡的单位数 / 无处安放计数。</summary>
    public int Evicted { get; set; }
    public int Stuck { get; set; }
    /// <summary>受影响导航摘要:逐上下文脏 tile 与类别。</summary>
    public List<CrowdNavRebakeContextResult> ContextResults { get; set; } = new();
    /// <summary>受影响 tile 并集，升序。</summary>
    public List<int> UnionTiles { get; set; } = new(CrowdSimulationLimits.HotPathSpanReserve);
    /// <summary>结构足迹包围盒(厘米;报告语义,不参与对拍)。</summary>
    public (Fix64 MinX, Fix64 MinY, Fix64 MaxX, Fix64 MaxY) Bbox { get; set; }
}

/// <summary>跨 tick 推进的重烘收尾 job。阶段 0 在指令内：收尾未完 job、锁内重烘真相、TruthChanged。
/// 阶段 1 是反应/唤醒/挤离，阶段 2 是重规划并发布报告。tick 驱动保证确定性；
/// 任何新指令或结构变更先把未完 job 收尾。</summary>
public sealed class CrowdRebakeJob
{
    public (int X0, int Y0, int X1, int Y1) Rect { get; set; }
    public byte Kind { get; set; }
    public CrowdStructureFootprint Fp { get; set; }
    public List<CrowdNavRebakeContextResult> ContextResults { get; set; } = new();
    public int Contexts { get; set; }
    public int CostOnly { get; set; }
    public int Hits { get; set; }
    public int Misses { get; set; }
    public int ExecTick { get; set; }
    public int LastTick { get; set; }
    public int Stage { get; set; }
    public List<CrowdOrder> Orders { get; } = new(8);
    public int Evicted { get; set; }
    public int Stuck { get; set; }
    /// <summary>反应末的流场刷新队列长快照(阶段 1;报告字段在阶段 2 发布,队列已被每 tick 预算排空)。</summary>
    public int Refreshes { get; set; }
}

/// <summary>
/// 结构动态化：放置 / 拆除 / 寿命到期。放置/拆除后调用 Fog.OnTruth；真相重烘后调用 Beliefs.TruthChanged。没有单独的 belief 同步函数。
/// 阶段 0 在指令内完成仓变更与真相导航重烘（路径服务与结构变更互斥，op 按指令序生效），
/// 队伍反应与重规划按 planning.rebakeSlices 摊到后续 tick。
/// </summary>
public static class CrowdStructureOps
{
    /// <summary>放置结构实体(模板按 id 查配置表);size 为足迹边长 / 半径 / 路宽(厘米)。</summary>
    public static CrowdRebakeReport? PlaceStructure(
        CrowdSimSession sim, string templateId, Fix64 xCm, Fix64 yCm, Fix64 sizeCm, Fix64 toXCm, Fix64 toYCm)
    {
        var store = RequireStore(sim);
        int tpl = store.TemplateIndexOf(templateId);
        if (tpl < 0) throw new InvalidOperationException($"未知结构模板 \"{templateId}\"。");
        var template = sim.Config.Structures.Templates[tpl];
        var fp = CrowdStructureFootprint.Make(template, xCm, yCm, sizeCm, toXCm, toYCm);
        int expire = store.ExpireTickOf(template, sim.TickCount);
        var (id, rect) = store.PlaceEntity(tpl, fp, expire);
        sim.Fog?.OnTruth(id); // 真相实体出现 → 索引/key/truthRev/不透明位
        return StructureChanged(sim, rect, CrowdRebakeReport.KindPlace, fp);
    }

    /// <summary>拆除包含该点的最上层实体;无实体时返回 null(不触发重烘)。</summary>
    public static CrowdRebakeReport? RemoveStructureAt(CrowdSimSession sim, Fix64 xCm, Fix64 yCm)
    {
        var store = RequireStore(sim);
        int id = store.EntityAt(xCm, yCm);
        return id < 0 ? null : RemoveStructure(sim, id);
    }

    public static CrowdRebakeReport? RemoveStructure(CrowdSimSession sim, int id)
    {
        var store = RequireStore(sim);
        var removed = store.RemoveEntity(id);
        if (removed == null) return null;
        sim.Fog?.OnTruth(id); // 真相实体消失 → 残影语义由认知侧自己维持
        return StructureChanged(sim, removed.Value.Rect, CrowdRebakeReport.KindRemove, removed.Value.Fp);
    }

    /// <summary>寿命系统:到期实体在 tick 边界、tick 推进之前拆除。</summary>
    public static void ExpireStructures(CrowdSimSession sim)
    {
        var store = RequireStore(sim);
        foreach (int id in store.DueExpiries(sim.TickCount)) RemoveStructure(sim, id);
    }

    /// <summary>切片推进(all = 收尾全部阶段)。每逻辑 tick 至多一步:同一 tick 内重入
    /// (重试停摆帧)不得二次推进 job,否则管线时序随线程时序漂移。</summary>
    public static void StepRebake(CrowdSimSession sim, bool all)
    {
        var job = sim.RebakeJob;
        if (job == null) return;
        var slices = sim.Config.Planning.RebakeSlices;
        if (!all && sim.TickCount == job.LastTick) return;
        job.LastTick = sim.TickCount;
        int stages = 2;
        int n = all || slices <= 1 ? stages : (int)Math.Ceiling((double)stages / (slices - 1));
        while (n-- > 0 && job.Stage < stages)
        {
            job.Stage++;
            if (job.Stage == 1) StageReaction(sim, job);
            else StagePublish(sim, job);
        }

        if (job.Stage >= stages) sim.RebakeJob = null;
    }

    /// <summary>未完 job 立即收尾(每条指令执行前调用:任何指令都看到完整生效的结构变更)。</summary>
    public static void FinishRebake(CrowdSimSession sim) => StepRebake(sim, all: true);

    /// <summary>阶段 0(指令内):仓已变更,做真相导航增量重烘。路径服务侧与这里共用同一份
    /// 导航态,互斥锁保证在途计算不与重烘交错(op 按指令序生效)。</summary>
    private static CrowdRebakeReport? StructureChanged(
        CrowdSimSession sim, (int X0, int Y0, int X1, int Y1) rect, byte kind, CrowdStructureFootprint fp)
    {
        FinishRebake(sim);
        var cache = sim.NavTileCache!;
        int h0 = cache.Hits, m0 = cache.Misses;
        // 无头 BuildSession 不挂规划器。有路径服务时重烘必须进它的互斥域。
        var call = new RebakeCall(sim, rect, cache);
        List<CrowdNavRebakeContextResult> dirty = sim.Planner is { } planner
            ? planner.Service.RunExclusive(call, RebakeExclusive)
            : RebakeExclusive(call);
        // 真相重烘完调用 TruthChanged。全拷贝变体的数组不跟真相重烘一起改。
        sim.Beliefs?.TruthChanged();
        int contexts = 0, costOnly = 0;
        foreach (var r in dirty)
        {
            if (r.DirtyTiles.Count == 0) continue;
            contexts++;
            if (r.Kind == CrowdNavRebake.KindCost) costOnly++;
        }

        var job = sim.RebakeScratch ??= new CrowdRebakeJob();
        job.Rect = rect;
        job.Kind = kind;
        job.Fp = fp;
        job.ContextResults = dirty;
        job.Contexts = contexts;
        job.CostOnly = costOnly;
        job.Hits = cache.Hits - h0;
        job.Misses = cache.Misses - m0;
        job.ExecTick = sim.TickCount;
        job.LastTick = sim.TickCount;
        job.Stage = 0;
        job.Orders.Clear();
        job.Evicted = 0;
        job.Stuck = 0;
        job.Refreshes = 0;
        sim.RebakeJob = job;
        if (sim.Config.Planning.RebakeSlices > 1) return null;
        FinishRebake(sim);
        return sim.LastRebakeReport;
    }

    private readonly struct RebakeCall
    {
        public readonly CrowdSimSession Sim;
        public readonly (int X0, int Y0, int X1, int Y1) Rect;
        public readonly NavTileCache Cache;

        public RebakeCall(CrowdSimSession sim, (int X0, int Y0, int X1, int Y1) rect, NavTileCache cache)
        {
            Sim = sim;
            Rect = rect;
            Cache = cache;
        }
    }

    private static readonly Func<RebakeCall, List<CrowdNavRebakeContextResult>> RebakeExclusive = static call =>
    {
        var navs = call.Sim.Navs as Dictionary<int, NavContext>
            ?? throw new InvalidOperationException("导航表必须是 Dictionary，重烘按具体枚举器遍历。");
        var results = CrowdNavRebake.RebakeContexts(navs, call.Sim.RebakeSources!, call.Sim.Structures!, call.Rect, call.Cache);
        if (call.Sim.VerifyIncrementalNav)
        {
            CrowdNavRebake.VerifyAgainstFullBake(call.Sim.Navs, call.Sim.RebakeSources!, call.Sim.Structures!);
        }

        return results;
    };

    /// <summary>阶段 1:队伍反应(重规划清单 + 流场刷新)、唤醒变更附近休眠单位、挤离新阻挡。</summary>
    private static void StageReaction(CrowdSimSession sim, CrowdRebakeJob job)
    {
        if (sim.Planner is { } planner)
        {
            var built = planner.ReactRebake(job.ContextResults);
            job.Orders.Clear();
            for (int i = 0; i < built.Count; i++) job.Orders.Add(built[i]);
            // 反应结束时记下刷新队列长度。每 tick 预算会在步末排空队列，所以报告不在阶段 2 再读队列。
            job.Refreshes = planner.RefreshQueueCount;
        }
        WakeNear(sim, job.Rect);
        job.Evicted = EvictBlocked(sim, job.Rect);
        job.Stuck = sim.EvictStuck;
    }

    /// <summary>阶段 2:受影响指令重规划(固定生效帧)+ 发布报告。</summary>
    private static void StagePublish(CrowdSimSession sim, CrowdRebakeJob job)
    {
        if (sim.Planner is { } planner && job.Orders.Count > 0)
        {
            planner.Plan(job.Orders, sim.TickCount);
        }
        var bbox = job.Fp.Bbox();
        var report = sim.ReportScratch ??= new CrowdRebakeReport();
        report.ExecTick = job.ExecTick;
        // 阶段 2 在本 tick 的 TickCount++ 之前完成，报告 tick 写成 TickCount + 1
        report.ReportTick = sim.TickCount + 1;
        report.Kind = job.Kind;
        report.ContextResults = job.ContextResults;
        report.Contexts = job.Contexts;
        report.CostOnly = job.CostOnly;
        report.Hits = job.Hits;
        report.Misses = job.Misses;
        report.Orders = job.Orders.Count;
        report.Refreshes = job.Refreshes;
        report.Evicted = job.Evicted;
        report.Stuck = job.Stuck;
        report.Bbox = bbox;
        int union = 0;
        var seen = sim.RebakeUnionSeen;
        seen.Clear();
        var unionTiles = report.UnionTiles;
        unionTiles.Clear();
        foreach (var r in job.ContextResults)
        {
            foreach (int t in r.DirtyTiles)
            {
                if (seen.Add(t))
                {
                    union++;
                    unionTiles.Add(t);
                }
            }
        }

        for (int i = 1; i < unionTiles.Count; i++)
        {
            int value = unionTiles[i];
            int j = i - 1;
            while (j >= 0 && unionTiles[j] > value)
            {
                unionTiles[j + 1] = unionTiles[j];
                j--;
            }

            unionTiles[j + 1] = value;
        }
        report.Tiles = union;
        sim.LastRebakeReport = report;
        sim.NotifyRebake(report);
    }

    /// <summary>变更格矩形附近的休眠单位唤醒：各自净空格 × 格长 + 自身个人半径 + 全体最大个人半径，量到矩形边。</summary>
    public static void WakeNear(CrowdSimSession sim, (int X0, int Y0, int X1, int Y1) rect)
    {
        var kernel = sim.Movement;
        if (kernel == null) return;
        int navCellSizeCm = sim.Config.NavCellSizeCm;
        Fix64 rMax = Fix64.Zero;
        var profiles = sim.Config.Profiles;
        for (int i = 0; i < profiles.Count; i++) rMax = Fix64.Max(rMax, profiles[i].PersonalRadiusCm);
        Fix64 ax = Fix64.FromInt(rect.X0 * navCellSizeCm), ay = Fix64.FromInt(rect.Y0 * navCellSizeCm);
        Fix64 bx = Fix64.FromInt(rect.X1 * navCellSizeCm), by = Fix64.FromInt(rect.Y1 * navCellSizeCm);
        var units = sim.Units;
        for (int i = 0; i < units.Count; i++)
        {
            if (kernel.Calm[i] == 0) continue;
            var entity = units.EntityAt(i);
            var state = sim.World.Get<CrowdSimulationUnitState>(entity);
            int clearance = 1;
            if (sim.Groups.TryGet(state.GroupId, out var g)) clearance = sim.Navs[g.BodyNavId].ClearanceNavCells;
            Fix64 pad = Fix64.FromInt(clearance * navCellSizeCm) + units.PersonalRadiusCmAt(i) + rMax;
            var pos = sim.World.Get<Components.WorldPositionCm>(entity).Value;
            Fix64 dx = pos.X < ax ? ax - pos.X : pos.X > bx ? pos.X - bx : Fix64.Zero;
            Fix64 dy = pos.Y < ay ? ay - pos.Y : pos.Y > by ? pos.Y - by : Fix64.Zero;
            if (dx * dx + dy * dy <= pad * pad) kernel.Calm[i] = 0;
        }
    }

    /// <summary>站在刚变不可走地面上的单位挤到最近可走格中心。外扩 2 × 最大净空；跳跃单位改落点。无处可放计入 stuck。</summary>
    public static int EvictBlocked(CrowdSimSession sim, (int X0, int Y0, int X1, int Y1) rect)
    {
        var kernel = sim.Movement;
        var store = sim.Structures;
        if (kernel == null || store == null) return 0;
        int navCellsPerSide = sim.Config.NavCellsPerSide, navCellSizeCm = sim.Config.NavCellSizeCm;
        int padCells = 0;
        var navs = sim.Navs as Dictionary<int, NavContext>
            ?? throw new InvalidOperationException("导航表必须是 Dictionary，挤离按具体枚举器遍历。");
        foreach (var kv in navs) padCells = Math.Max(padCells, 2 * kv.Value.ClearanceNavCells);
        Fix64 ax = Fix64.FromInt(Math.Max(0, rect.X0 - padCells) * navCellSizeCm);
        Fix64 ay = Fix64.FromInt(Math.Max(0, rect.Y0 - padCells) * navCellSizeCm);
        Fix64 bx = Fix64.FromInt(Math.Min(navCellsPerSide, rect.X1 + padCells) * navCellSizeCm);
        Fix64 by = Fix64.FromInt(Math.Min(navCellsPerSide, rect.Y1 + padCells) * navCellSizeCm);
        var units = sim.Units;
        int moved = 0, stuck = 0;
        for (int i = 0; i < units.Count; i++)
        {
            var entity = units.EntityAt(i);
            var state = sim.World.Get<CrowdSimulationUnitState>(entity);
            var kin = sim.World.Get<CrowdSimulationKinematics>(entity);
            bool jumping = state.State == (byte)CrowdUnitState.Jump;
            var px = jumping ? kin.JumpToCm.X : sim.World.Get<Components.WorldPositionCm>(entity).Value.X;
            var py = jumping ? kin.JumpToCm.Y : sim.World.Get<Components.WorldPositionCm>(entity).Value.Y;
            if (px < ax || px >= bx || py < ay || py >= by) continue;
            if (!sim.Groups.TryGet(state.GroupId, out var g)) continue;
            var nav = sim.Navs[g.BodyNavId];
            int cell = CrowdSimulationSpace.CellAtStrict(px, py, navCellSizeCm, navCellsPerSide);
            if (state.Level != 0)
            {
                if (nav.UpPass[cell] != 0) continue;
                state.Level = 0;
                sim.World.Set(entity, state);
                kernel.Calm[i] = 0;
            }

            if (nav.Passable[cell] != 0) continue;
            int c = CrowdDeployment.NearestPassable(nav, cell);
            if (c < 0)
            {
                stuck++;
                continue;
            }

            Fix64 x = Fix64.FromInt(c % navCellsPerSide * navCellSizeCm + navCellSizeCm / 2);
            Fix64 y = Fix64.FromInt(c / navCellsPerSide * navCellSizeCm + navCellSizeCm / 2);
            if (jumping)
            {
                kin.JumpToCm = new Fix64Vec2(x, y);
                Fix64 len = CrowdFix.Hypot(x - kin.RestCm.X, y - kin.RestCm.Y);
                // 下限 1e-3 米 = 0.1 厘米;Fix64 raw = 0.1 × 2^32 ≈ 429496730,有依据取整
                kin.JumpLengthCm = Fix64.Max(Fix64.FromRaw(429496730), len);
                sim.World.Set(entity, kin);
            }
            else
            {
                sim.World.Set(entity, new Components.WorldPositionCm { Value = new Fix64Vec2(x, y) });
                kin.RestCm = new Fix64Vec2(x, y);
                kin.Velocity = Fix64Vec2.Zero;
                sim.World.Set(entity, kin);
            }

            kernel.Calm[i] = 0;
            moved++;
        }

        sim.EvictStuck = stuck;
        if (stuck > 0) sim.Telemetry.MarkEvictStuck(stuck);
        return moved;
    }

    private static CrowdStructuresStore RequireStore(CrowdSimSession sim) =>
        sim.Structures ?? throw new InvalidOperationException("会话没有结构仓:结构指令需要先装配 CrowdStructuresStore。");
}
