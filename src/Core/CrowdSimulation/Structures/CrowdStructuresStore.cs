using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Structures;

/// <summary>结构实体仓(参考 structures/store.js 移植):组件表(模板 / 足迹 / 区域覆盖 /
/// 阻挡 / 寿命)为纯数据,id 单调递增,按 id 序遍历保证确定性;区域栅格与阻挡栅格是
/// 组件的派生态,按变更格矩形重标。地图静态阻挡物以实体身份进仓(与参考 mapGen 同形),
/// 动态 place/remove 与其共用一套重标路径。分层实体(桥面)暂不开放:见 CrowdStructureOps。</summary>
public sealed class CrowdStructuresStore
{
    public int CellCount { get; }
    public int CellSizeCm { get; }
    public int ClusterSize { get; }
    public Fix64 BlockCoverage { get; }
    /// <summary>阻挡盒的电机查询半径(最大个人半径,厘米)。</summary>
    public Fix64 ReachCm { get; }

    /// <summary>导航区域栅格 = 地形源,被 NavArea 组件按优先级覆盖(高者胜,同级后放置者胜)。</summary>
    public byte[] Area { get; private set; }
    /// <summary>阻挡栅格:被 blocker 足迹覆盖率 ≥ blockCoverage 的格。</summary>
    public byte[] Blocked { get; }
    /// <summary>地形源区域(F02 乐观层的覆写判据:area == terrainArea 的格才可被假设区域覆写)。</summary>
    public byte[] TerrainArea => _terrainArea;
    private readonly byte[] _terrainArea;

    private readonly CrowdSimulationRuntimeConfig _config;
    private readonly byte[] _terrainType;
    private readonly NavSurfaceJumpCandidate[] _jumpCandidates;
    private readonly List<int> _ids = new();
    private readonly Dictionary<int, int> _tpl = new();
    private readonly Dictionary<int, CrowdStructureFootprint> _footprint = new();
    private readonly Dictionary<int, (int AreaIndex, int Priority)> _navArea = new();
    private readonly HashSet<int> _blocker = new();
    private readonly Dictionary<int, int> _lifetime = new();
    private int _nextId = 1;

    /// <summary>阻挡盒 + 逐格 CSR(cellStart/cellItems);blocker 集变化即整体重建(参考 rebuildColliders)。</summary>
    public CrowdBlockerColliders Colliders { get; private set; } = CrowdBlockerColliders.Empty;

    private CrowdStructuresStore(CrowdSimulationRuntimeConfig config, SurfaceGrid grid)
    {
        _config = config;
        CellCount = config.NavCellCount;
        CellSizeCm = config.NavCellSizeCm;
        ClusterSize = config.Hpa.ClusterSize;
        BlockCoverage = config.Structures.BlockCoverage;
        _terrainType = grid.TerrainType;
        _jumpCandidates = grid.JumpCandidates;
        int n2 = CellCount * CellCount;
        _terrainArea = (byte[])grid.Area.Clone();
        Area = (byte[])grid.Area.Clone();
        Blocked = (byte[])grid.Blocked.Clone();
        Fix64 maxRadius = Fix64.Zero;
        foreach (var p in config.Profiles) maxRadius = Fix64.Max(maxRadius, p.PersonalRadiusCm);
        ReachCm = maxRadius;
    }

    /// <summary>建仓:静态地图阻挡物先按模板 0(rect 阻挡)入仓获得实体 id,初始栅格取
    /// SurfaceGrid 的既算结果(与其覆盖公式逐格同源)。</summary>
    public static CrowdStructuresStore Build(
        CrowdSimulationRuntimeConfig config,
        SurfaceGrid grid,
        IReadOnlyList<BlockerFootprint> blockers)
    {
        var store = new CrowdStructuresStore(config, grid);
        foreach (var b in blockers)
        {
            int id = store._nextId++;
            store._ids.Add(id);
            store._tpl[id] = 0;
            store._footprint[id] = new CrowdStructureFootprint
            {
                Shape = CrowdStructureShape.Rect,
                X = Fix64.FromInt(b.XCm),
                Y = Fix64.FromInt(b.YCm),
                Hx = Fix64.FromInt(b.HalfSizeCm),
                Hy = Fix64.FromInt(b.HalfSizeCm),
            };
            store._blocker.Add(id);
        }

        store.RebuildColliders();
        return store;
    }

    public int TemplateIndexOf(string templateId)
    {
        var templates = _config.Structures.Templates;
        for (int i = 0; i < templates.Count; i++)
        {
            if (templates[i].Id == templateId) return i;
        }

        return -1;
    }

    /// <summary>实体 id 的放置序视图(索引与认知初始注入按此序遍历)。</summary>
    public IReadOnlyList<int> EntityIds => _ids;

    public bool TryGetFootprint(int id, out CrowdStructureFootprint fp)
    {
        if (_footprint.TryGetValue(id, out fp!)) return true;
        fp = default;
        return false;
    }

    public bool TryGetTemplate(int id, out int tplIndex) => _tpl.TryGetValue(id, out tplIndex!);

    /// <summary>认知字段快照(beliefNav.js register 的 belief field,F02):地形源起底 + believed
    /// 组件表带原 id 入仓,再逐实体足迹格矩形重标。起底必须是地形源而非真相活栅格——
    /// 真相里只有单侧知道的实体(新放建筑)已落在活栅格上,认知侧要靠"地形+believed 重标"
    /// 把它擦掉(参考端从真相栅格起底 + 分歧矩形复位,两者等价:分歧外的格只被共同实体覆盖)。
    /// 不跑寿命;碰撞索引重建一次(马达永不读认知仓,只为仓的自洽)。</summary>
    public static CrowdStructuresStore BuildBeliefField(
        CrowdSimulationRuntimeConfig config,
        SurfaceGrid terrain,
        IReadOnlyList<(int Id, int TplIndex, CrowdStructureFootprint Fp)> entities)
    {
        var store = new CrowdStructuresStore(config, terrain);
        store.Area = (byte[])store._terrainArea.Clone();
        Array.Clear(store.Blocked);
        var sorted = entities.OrderBy(e => e.Id).ToArray();
        foreach (var (id, tplIndex, fp) in sorted)
        {
            var tpl = config.Structures.Templates[tplIndex];
            store._ids.Add(id);
            store._tpl[id] = tplIndex;
            store._footprint[id] = fp;
            if (tpl.AreaIndex is { } area) store._navArea[id] = (area, tpl.Priority);
            if (tpl.Blocker) store._blocker.Add(id);
            if (id >= store._nextId) store._nextId = id + 1;
        }

        foreach (var (_, _, fp) in sorted)
        {
            store.RasterRect(fp.CellRectOf(store.CellSizeCm, store.CellCount));
        }

        store.RebuildColliders();
        return store;
    }

    /// <summary>乐观层换区域面(F02 fieldFor:assumedArea 覆写后的视图;belief 仓私有,整体换数组)。</summary>
    public void ReplaceArea(byte[] area) => Area = area;

    /// <summary>地图桥实体入仓(F02):桥以实体身份进认知(可见/残影语义),但不进地面
    /// 栅格与碰撞——桥面层由 UpperLayerBake 独立烘焙(与 S1–S7 的地面口径零接触)。id 按装载序
    /// 续在阻挡物之后,与导出端"参考 id 序写出"的地图实体序同构。</summary>
    public void InstallMapBridges(int tplIndex, IReadOnlyList<CrowdSimulationBridgeSpan> spans)
    {
        foreach (var span in spans)
        {
            int id = _nextId++;
            _ids.Add(id);
            _tpl[id] = tplIndex;
            _footprint[id] = CrowdStructureFootprint.Bridge(span);
        }
    }

    /// <summary>放置实体(组件自模板)并重标足迹格矩形;返回 (id, 格矩形)。</summary>
    public (int Id, (int X0, int Y0, int X1, int Y1) Rect) PlaceEntity(int tplIndex, CrowdStructureFootprint fp, int expireTick)
    {
        var tpl = _config.Structures.Templates[tplIndex];
        if (tpl.Layered)
        {
            throw new InvalidOperationException(
                $"结构模板 {tpl.Id} 为分层实体(桥面):动态上层烘焙未随本阶段开放,放置被拒绝。");
        }

        int id = _nextId++;
        _ids.Add(id);
        _tpl[id] = tplIndex;
        _footprint[id] = fp;
        if (tpl.AreaIndex is { } area) _navArea[id] = (area, tpl.Priority);
        if (tpl.Blocker) _blocker.Add(id);
        if (expireTick >= 0) _lifetime[id] = expireTick;
        return Commit(id, fp);
    }

    /// <summary>移除实体;id 不存在返回 null。返回 (id, 足迹, 格矩形)。</summary>
    public (int Id, CrowdStructureFootprint Fp, (int X0, int Y0, int X1, int Y1) Rect)? RemoveEntity(int id)
    {
        if (!_footprint.TryGetValue(id, out var fp)) return null;
        bool wasBlocker = _blocker.Remove(id);
        _ids.Remove(id);
        _tpl.Remove(id);
        _footprint.Remove(id);
        _navArea.Remove(id);
        _lifetime.Remove(id);
        return (id, fp, CommitRect(id, fp, wasBlocker));
    }

    /// <summary>包含该点的最上层(最新)实体;无则 -1。</summary>
    public int EntityAt(Fix64 x, Fix64 y)
    {
        int best = -1;
        foreach (int id in _ids) // _ids 是放置序 List(有序容器):同点多次覆盖取后写 = 最新实体
        {
            if (_footprint.TryGetValue(id, out var fp) && fp.Covers(x, y)) best = id;
        }

        return best;
    }

    /// <summary>tick 时刻到期的实体(id 升序)。</summary>
    public List<int> DueExpiries(int tick)
    {
        var due = new List<int>();
        foreach (int id in _ids) // _ids 放置序 = id 升序(id 只增,删除保序);有序容器
        {
            if (_lifetime.TryGetValue(id, out int expire) && expire <= tick) due.Add(id);
        }

        return due;
    }

    public CrowdStructureFootprint FootprintOf(int id) => _footprint[id];

    /// <summary>当前派生栅格的只读快照面(全量对拍烘焙用;Area/Blocked 引用本仓活数组)。</summary>
    public SurfaceGrid SnapshotSurface() => new()
    {
        CellCount = CellCount,
        CellSizeCm = CellSizeCm,
        TerrainType = _terrainType,
        Area = Area,
        Blocked = Blocked,
        JumpCandidates = _jumpCandidates,
    };

    /// <summary>寿命到期 tick:placeTick + ceil(lifetimeSec / (timeScale/fixedHz)),按参考端的
    /// 原始 double 公式逐位同值(整数 tick 契约,不走 Fix64——Fix64 simDt 的下取整会把
    /// 恰好整除的商推过线,首个分歧:4 秒寿命在 simDt=1/3 时 12 变 13)。</summary>
    public int ExpireTickOf(RuntimeStructureTemplate tpl, int placeTick)
    {
        if (tpl.LifetimeSecRaw is not { } life) return -1;
        return placeTick + (int)Math.Ceiling(life / (_config.Sim.TimeScaleRaw / _config.FixedHz));
    }

    private (int Id, (int X0, int Y0, int X1, int Y1) Rect) Commit(int id, CrowdStructureFootprint fp)
    {
        var rect = RasterRect(fp.CellRectOf(CellSizeCm, CellCount));
        RebuildColliders();
        return (id, rect);
    }

    private (int X0, int Y0, int X1, int Y1) CommitRect(int id, CrowdStructureFootprint fp, bool wasBlocker)
    {
        var rect = RasterRect(fp.CellRectOf(CellSizeCm, CellCount));
        if (wasBlocker) RebuildColliders();
        return rect;
    }

    /// <summary>格矩形 [x0,y0,x1,y1) 内的区域与阻挡重标(参考 rasterRect):
    /// 先复位为地形源,再按 id 序让 NavArea 高优先级者覆盖、blocker 足迹按覆盖份额标阻挡。</summary>
    public (int X0, int Y0, int X1, int Y1) RasterRect((int X0, int Y0, int X1, int Y1) rect)
    {
        int n = CellCount;
        Fix64 cs = Fix64.FromInt(CellSizeCm);
        int w = rect.X1 - rect.X0;
        if (w <= 0 || rect.Y1 <= rect.Y0) return rect;

        var prio = new int[w * (rect.Y1 - rect.Y0)];
        Array.Fill(prio, -1);
        for (int y = rect.Y0; y < rect.Y1; y++)
        {
            int row = y * n;
            for (int x = rect.X0; x < rect.X1; x++)
            {
                Area[row + x] = _terrainArea[row + x];
                Blocked[row + x] = 0;
            }
        }

        // 顺序相关:同优先级实体重叠时后写胜——按放置序(_ids)遍历,与参考端 Map 插入序
        // 同语义;_navArea 有删除,字典枚举序在删除后会乱,不能直接枚举。
        foreach (int id in _ids)
        {
            if (!_navArea.TryGetValue(id, out var navArea)) continue;
            var (areaIdx, priority) = navArea;
            var fp = _footprint[id];
            var (a, b, c, d) = fp.CellRectOf(CellSizeCm, n);
            for (int y = Math.Max(b, rect.Y0); y < Math.Min(d, rect.Y1); y++)
            {
                for (int x = Math.Max(a, rect.X0); x < Math.Min(c, rect.X1); x++)
                {
                    int k = (y - rect.Y0) * w + (x - rect.X0);
                    Fix64 cx = (Fix64.FromInt(x) + Fix64.HalfValue) * cs;
                    Fix64 cy = (Fix64.FromInt(y) + Fix64.HalfValue) * cs;
                    if (priority >= prio[k] && fp.Covers(cx, cy))
                    {
                        prio[k] = priority;
                        Area[y * n + x] = (byte)areaIdx;
                    }
                }
            }
        }

        foreach (int id in _ids) // 顺序无关:阻挡写 1 幂等;_ids 放置序(有序容器)
        {
            if (!_blocker.Contains(id)) continue;
            var fp = _footprint[id];
            var (bx0, by0, bx1, by1) = fp.Bbox();
            var (a, b, c, d) = fp.CellRectOf(CellSizeCm, n);
            for (int y = Math.Max(b, rect.Y0); y < Math.Min(d, rect.Y1); y++)
            {
                for (int x = Math.Max(a, rect.X0); x < Math.Min(c, rect.X1); x++)
                {
                    Fix64 coverX = CoverShare(bx0, bx1, x, cs);
                    Fix64 coverY = CoverShare(by0, by1, y, cs);
                    if (coverX * coverY >= BlockCoverage) Blocked[y * n + x] = 1;
                }
            }
        }

        return rect;
    }

    /// <summary>格 c 与区间 [a0,a1) 的重叠份额(轴);与 SurfaceGrid.Build 的阻挡覆盖公式同源。</summary>
    private Fix64 CoverShare(Fix64 a0, Fix64 a1, int c, Fix64 cs)
    {
        Fix64 cellLo = Fix64.FromInt(c) * cs;
        Fix64 cellHi = cellLo + cs;
        Fix64 overlap = Fix64.Min(a1, cellHi) - Fix64.Max(a0, cellLo);
        return overlap > Fix64.Zero ? overlap / cs : Fix64.Zero;
    }

    /// <summary>阻挡盒 CSR 整体重建(参考 rebuildColliders):注册格矩形按包围盒外扩 reach
    /// (电机接触查询半径),与参考 cellRectOf(rect, cs, N, reach) 同形。</summary>
    public void RebuildColliders()
    {
        var boxes = new List<(Fix64 X, Fix64 Y, Fix64 Hx, Fix64 Hy)>();
        foreach (int id in _ids) // 顺序无关:盒序进 CSR 后查询与序无关;_ids 放置序(有序容器)
        {
            if (!_blocker.Contains(id)) continue;
            var f = _footprint[id];
            boxes.Add((f.X, f.Y, f.Hx, f.Hy));
        }

        Colliders = CrowdBlockerColliders.BuildFromBoxes(boxes, CellCount, CellSizeCm, ReachCm);
    }
}
