using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Structures;

/// <summary>每次结构变更的重烘焙报告(RT-04 stage 0 数据 + 后续阶段补齐):真值逐字段对拍。</summary>
public sealed class CrowdRebakeReport
{
    public const byte KindPlace = 1;
    public const byte KindRemove = 2;

    /// <summary>op 执行 tick(阶段 0,指令内)。</summary>
    public required int ExecTick { get; init; }
    /// <summary>报告发布 tick(最后阶段;切片全在指令内时与 ExecTick 相同)。</summary>
    public int ReportTick { get; set; }
    public required byte Kind { get; init; }
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
    public required List<CrowdNavRebakeContextResult> ContextResults { get; init; }
    /// <summary>受影响 tile 并集(升序;参考端 stats.dirtyTiles 同源)。</summary>
    public required List<int> UnionTiles { get; set; }
    /// <summary>结构足迹包围盒(厘米;报告语义,不参与对拍)。</summary>
    public (Fix64 MinX, Fix64 MinY, Fix64 MaxX, Fix64 MaxY) Bbox { get; set; }
}

/// <summary>跨 tick 推进的重烘收尾 job(RT-04 切片):阶段 0 = 指令内(镜像同步 + 真相重烘),
/// 阶段 1 = 队伍反应 + 唤醒 + 挤离,阶段 2 = 受影响指令重规划 + 报告发布。tick 驱动保证确定性;
/// 任何新指令或结构变更先把未完 job 收尾。</summary>
public sealed class CrowdRebakeJob
{
    public required (int X0, int Y0, int X1, int Y1) Rect { get; init; }
    public required byte Kind { get; init; }
    public required CrowdStructureFootprint Fp { get; init; }
    public required List<CrowdNavRebakeContextResult> ContextResults { get; init; }
    public required int Contexts { get; init; }
    public required int CostOnly { get; init; }
    public required int Hits { get; init; }
    public required int Misses { get; init; }
    public required int ExecTick { get; init; }
    public int LastTick { get; set; }
    public int Stage { get; set; }
    public List<CrowdOrder>? Orders { get; set; }
    public int Evicted { get; set; }
    public int Stuck { get; set; }
    /// <summary>反应末的流场刷新队列长快照(阶段 1;报告字段在阶段 2 发布,队列已被每 tick 预算排空)。</summary>
    public int Refreshes { get; set; }
}

/// <summary>
/// 结构动态化 op 管线(sim/structureOps.js 移植):放置 / 拆除 / 寿命到期 →
/// 阶段 0 指令内完成仓变更与真相导航重烘(路径服务与结构变更互斥,RT-05 的指令序契约),
/// 队伍反应与重规划按 planning.rebakeSlices 摊到后续 tick。参考端此处还有迷雾 belief 同步
/// (fog.onTruth / syncBeliefs)——迷雾属 F02,未移植,此处为显式留空挂点。
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
        sim.Fog?.OnTruth(id); // F02:真相实体出现 → 索引/key/truthRev/不透明位
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
        sim.Fog?.OnTruth(id); // F02:真相实体消失 → 残影语义由认知侧自己维持
        return StructureChanged(sim, removed.Value.Rect, CrowdRebakeReport.KindRemove, removed.Value.Fp);
    }

    /// <summary>寿命系统:到期实体在 tick 边界、tick 推进之前拆除。</summary>
    public static void ExpireStructures(CrowdSimSession sim)
    {
        var store = RequireStore(sim);
        foreach (int id in store.DueExpiries(sim.TickCount)) RemoveStructure(sim, id);
    }

    /// <summary>RT-04 切片推进(all = 收尾全部阶段)。每逻辑 tick 至多一步:同一 tick 内重入
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
    /// 导航态,互斥锁保证在途计算不与重烘交错(RT-05:op 按指令序生效)。</summary>
    private static CrowdRebakeReport? StructureChanged(
        CrowdSimSession sim, (int X0, int Y0, int X1, int Y1) rect, byte kind, CrowdStructureFootprint fp)
    {
        FinishRebake(sim);
        var planner = sim.Planner!;
        var cache = sim.NavTileCache!;
        int h0 = cache.Hits, m0 = cache.Misses;
        List<CrowdNavRebakeContextResult> dirty;
        var cache0 = cache;
        dirty = planner.Service.RunExclusive(() =>
        {
            var results = CrowdNavRebake.RebakeContexts(sim.Navs.Values, sim.RebakeSources!, sim.Structures!, rect, cache0);
            if (sim.VerifyIncrementalNav)
            {
                CrowdNavRebake.VerifyAgainstFullBake(sim.Navs, sim.RebakeSources!, sim.Structures!);
            }

            return results;
        });
        // F02:真相重烘完 → 认知分歧重推导(参考端 truthChanged 同点)。参考端的
        // detach/settle(变体在真相重烘前私享 tile)不需要——C# 变体是全拷贝(裁定 ①),
        // 真相重烘写不到变体自有的数组。
        sim.Beliefs?.TruthChanged();
        int contexts = 0, costOnly = 0;
        foreach (var r in dirty)
        {
            if (r.DirtyTiles.Count == 0) continue;
            contexts++;
            if (r.Kind == CrowdNavRebake.KindCost) costOnly++;
        }

        var job = new CrowdRebakeJob
        {
            Rect = rect,
            Kind = kind,
            Fp = fp,
            ContextResults = dirty,
            Contexts = contexts,
            CostOnly = costOnly,
            Hits = cache.Hits - h0,
            Misses = cache.Misses - m0,
            ExecTick = sim.TickCount,
            LastTick = sim.TickCount,
            Stage = 0,
        };
        sim.RebakeJob = job;
        if (sim.Config.Planning.RebakeSlices > 1) return null;
        FinishRebake(sim);
        return sim.LastRebakeReport;
    }

    /// <summary>阶段 1:队伍反应(重规划清单 + 流场刷新)、唤醒变更附近休眠单位、挤离新阻挡。</summary>
    private static void StageReaction(CrowdSimSession sim, CrowdRebakeJob job)
    {
        var planner = sim.Planner!;
        job.Orders = planner.ReactRebake(job.ContextResults);
        // 刷新队列长在反应末快照(参考端 s0.refreshes 同点);每 tick 预算会在步末排空队列
        job.Refreshes = planner.RefreshQueueCount;
        WakeNear(sim, job.Rect);
        job.Evicted = EvictBlocked(sim, job.Rect);
    }

    /// <summary>阶段 2:受影响指令重规划(固定生效帧)+ 发布报告。</summary>
    private static void StagePublish(CrowdSimSession sim, CrowdRebakeJob job)
    {
        var planner = sim.Planner!;
        if (job.Orders is { Count: > 0 } orders) planner.Plan(orders, sim.TickCount);
        var bbox = job.Fp.Bbox();
        var report = new CrowdRebakeReport
        {
            ExecTick = job.ExecTick,
            // 报告可见的 tick 边界:阶段 2 在本 tick 推进中完成,步末 TickCount 再 +1——
            // 与参考端采样(advance 返回后的 tickCount)同值
            ReportTick = sim.TickCount + 1,
            Kind = job.Kind,
            ContextResults = job.ContextResults,
            Contexts = job.Contexts,
            CostOnly = job.CostOnly,
            Hits = job.Hits,
            Misses = job.Misses,
            Orders = job.Orders?.Count ?? 0,
            Refreshes = job.Refreshes,
            Evicted = job.Evicted,
            Stuck = job.Stuck,
            Bbox = bbox,
            UnionTiles = new List<int>(),
        };
        int union = 0;
        var seen = new HashSet<int>();
        var unionTiles = new List<int>();
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

        unionTiles.Sort();
        report.Tiles = union;
        report.UnionTiles = unionTiles;
        sim.LastRebakeReport = report;
        sim.NotifyRebake(report);
    }

    /// <summary>变更格矩形附近的休眠单位唤醒(wakeNear 移植):各自上下文腐蚀半径 + 自身半径 +
    /// 最大个人半径(触到的邻居上限),量到矩形边——不是对所有单位用同一最宽 reach。</summary>
    public static void WakeNear(CrowdSimSession sim, (int X0, int Y0, int X1, int Y1) rect)
    {
        var kernel = sim.Movement;
        if (kernel == null) return;
        int cs = sim.Config.NavCellSizeCm;
        Fix64 rMax = Fix64.Zero;
        foreach (var p in sim.Config.Profiles) rMax = Fix64.Max(rMax, p.PersonalRadiusCm);
        Fix64 ax = Fix64.FromInt(rect.X0 * cs), ay = Fix64.FromInt(rect.Y0 * cs);
        Fix64 bx = Fix64.FromInt(rect.X1 * cs), by = Fix64.FromInt(rect.Y1 * cs);
        var units = sim.Units;
        for (int i = 0; i < units.Count; i++)
        {
            if (kernel.Calm[i] == 0) continue;
            var entity = units.EntityAt(i);
            var state = sim.World.Get<CrowdSimulationUnitState>(entity);
            int clearance = 1;
            if (sim.Groups.TryGet(state.GroupId, out var g)) clearance = sim.Navs[g.BodyNavId].ClearanceCells;
            Fix64 pad = Fix64.FromInt(clearance * cs) + units.PersonalRadiusCmAt(i) + rMax;
            var pos = sim.World.Get<Components.WorldPositionCm>(entity).Value;
            Fix64 dx = pos.X < ax ? ax - pos.X : pos.X > bx ? pos.X - bx : Fix64.Zero;
            Fix64 dy = pos.Y < ay ? ay - pos.Y : pos.Y > by ? pos.Y - by : Fix64.Zero;
            if (dx * dx + dy * dy <= pad * pad) kernel.Calm[i] = 0;
        }
    }

    /// <summary>站在刚变不可走地面上的单位挤到最近可走格中心(evictBlocked 移植):
    /// 只有变更矩形外扩最大重烘 reach(2 × 腐蚀半径)内的单位需要检查;跳跃中单位查落点、
    /// 改落点与全长,不查脚下。无处可放计入 stuck(D44)。</summary>
    public static int EvictBlocked(CrowdSimSession sim, (int X0, int Y0, int X1, int Y1) rect)
    {
        var kernel = sim.Movement;
        var store = sim.Structures;
        if (kernel == null || store == null) return 0;
        int n = sim.Config.NavCellCount, cs = sim.Config.NavCellSizeCm;
        int padCells = 0;
        foreach (var nav in sim.Navs.Values) padCells = Math.Max(padCells, 2 * nav.ClearanceCells);
        Fix64 ax = Fix64.FromInt(Math.Max(0, rect.X0 - padCells) * cs);
        Fix64 ay = Fix64.FromInt(Math.Max(0, rect.Y0 - padCells) * cs);
        Fix64 bx = Fix64.FromInt(Math.Min(n, rect.X1 + padCells) * cs);
        Fix64 by = Fix64.FromInt(Math.Min(n, rect.Y1 + padCells) * cs);
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
            int cell = CrowdDeployment.CellAt(px, py, n, cs);
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

            Fix64 x = Fix64.FromInt(c % n * cs + cs / 2);
            Fix64 y = Fix64.FromInt(c / n * cs + cs / 2);
            if (jumping)
            {
                kin.JumpToCm = new Fix64Vec2(x, y);
                Fix64 len = CrowdFix.Hypot(x - kin.RestCm.X, y - kin.RestCm.Y);
                // 参考端下限 1e-3 米 = 0.1 厘米;Fix64 raw = 0.1 × 2^32 ≈ 429496730,有依据取整
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
        return moved;
    }

    private static CrowdStructuresStore RequireStore(CrowdSimSession sim) =>
        sim.Structures ?? throw new InvalidOperationException("会话没有结构仓:结构指令需要先装配 CrowdStructuresStore。");
}
