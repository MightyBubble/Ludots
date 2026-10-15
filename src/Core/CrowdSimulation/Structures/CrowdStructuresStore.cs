using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Structures;

/// <summary>认知实体按 id 升序,id 相同时保输入序。</summary>
internal readonly struct BeliefRank : IComparable<BeliefRank>
{
    public readonly int Id;
    public readonly int Seq;
    public readonly int TplIndex;
    public readonly CrowdStructureFootprint Fp;

    public BeliefRank(int id, int seq, int tplIndex, CrowdStructureFootprint fp)
    {
        Id = id;
        Seq = seq;
        TplIndex = tplIndex;
        Fp = fp;
    }

    public int CompareTo(BeliefRank other)
    {
        int c = Id.CompareTo(other.Id);
        return c != 0 ? c : Seq.CompareTo(other.Seq);
    }
}

/// <summary>结构实体仓。id 只增。区域与阻挡按变更矩形重标。动态 place/remove 与静态阻挡走同一条重标。放置合法性在 <see cref="CrowdStructurePlacement"/>。</summary>
public sealed class CrowdStructuresStore
{
    public int NavCellsPerSide { get; }
    public int NavCellSizeCm { get; }
    public int NavCellsPerTile { get; }
    public Fix64 BlockCoverage { get; }
    internal int PortalReachNavCells => _config.Structures.PortalReachNavCells;
    internal bool[] AnchorTerrain => _config.Structures.AnchorTerrain;
    internal bool[] SpanTerrain => _config.Structures.SpanTerrain;
    /// <summary>阻挡盒的电机查询半径(最大个人半径,厘米)。</summary>
    public Fix64 ReachCm { get; }

    /// <summary>导航区域栅格 = 地形源,被 NavArea 组件按优先级覆盖(高者胜,同级后放置者胜)。</summary>
    public byte[] Area { get; private set; }
    /// <summary>阻挡栅格:被 blocker 足迹覆盖率 ≥ blockCoverage 的格。</summary>
    public byte[] Blocked { get; }
    /// <summary>初始地形区域。乐观层只覆写仍等于这份数组的格。</summary>
    public byte[] TerrainArea => _terrainArea;
    private readonly byte[] _terrainArea;
    /// <summary>建仓时的阻挡。之后放上的实体不写进这份数组。</summary>
    private readonly byte[] _terrainBlocked;

    private readonly CrowdSimulationRuntimeConfig _config;
    private readonly byte[] _terrainType;
    private readonly NavSurfaceJumpCandidate[] _jumpCandidates;
    private readonly List<int> _ids = new();
    private readonly Dictionary<int, int> _tpl = new();
    private readonly Dictionary<int, CrowdStructureFootprint> _footprint = new();
    private readonly Dictionary<int, (int AreaIndex, int Priority)> _navArea = new();
    private readonly HashSet<int> _blocker = new();
    private readonly Dictionary<int, int> _lifetime = new();
    private readonly List<int> _dueExpiryIds = new();
    private int _nextId = 1;

    /// <summary>阻挡盒 + 逐格 CSR。阻挡集变化就整表重建。</summary>
    public CrowdBlockerColliders Colliders { get; private set; }

    private readonly int[] _rasterPrio;
    private int[] _judgeCells;
    private byte[] _judgeFlags;
    private DeckSurface? _deck;

    /// <summary>最近一次放上或拆除是否改了桥面。地面结构保持 false。</summary>
    internal bool LastMutationTouchedDeck { get; private set; }

    /// <summary>挂上与重烘源同一份桥面。地图桥先栅格化再入仓，这里不重画。</summary>
    public void UseDeck(DeckSurface deck) => _deck = deck ?? throw new ArgumentNullException(nameof(deck));

    private CrowdStructuresStore(CrowdSimulationRuntimeConfig config, SurfaceGrid grid)
    {
        _config = config;
        NavCellsPerSide = config.NavCellsPerSide;
        NavCellSizeCm = config.NavCellSizeCm;
        NavCellsPerTile = config.Hpa.NavCellsPerTile;
        BlockCoverage = config.Structures.BlockCoverage;
        _terrainType = grid.TerrainType;
        _jumpCandidates = grid.JumpCandidates;
        int n2 = NavCellsPerSide * NavCellsPerSide;
        _terrainArea = (byte[])grid.Area.Clone();
        _terrainBlocked = (byte[])grid.Blocked.Clone();
        Area = (byte[])grid.Area.Clone();
        Blocked = (byte[])grid.Blocked.Clone();
        Fix64 maxRadius = Fix64.Zero;
        foreach (var p in config.Profiles) maxRadius = Fix64.Max(maxRadius, p.PersonalRadiusCm);
        ReachCm = maxRadius;
        Colliders = CrowdBlockerColliders.CreateReusable(NavCellsPerSide);
        _rasterPrio = new int[n2];
        _judgeCells = new int[n2];
        _judgeFlags = new byte[n2];
        _ids.EnsureCapacity(CrowdSimulationLimits.HotPathSlotReserve);
        _tpl.EnsureCapacity(CrowdSimulationLimits.HotPathSlotReserve);
        _footprint.EnsureCapacity(CrowdSimulationLimits.HotPathSlotReserve);
        _navArea.EnsureCapacity(CrowdSimulationLimits.HotPathSlotReserve);
        _blocker.EnsureCapacity(CrowdSimulationLimits.HotPathSlotReserve);
        _lifetime.EnsureCapacity(CrowdSimulationLimits.HotPathSlotReserve);
    }

    /// <summary>建仓。静态阻挡用模板 id building 的当前下标。模板表按 id 排序，下标不是常量 0。初始栅格取 SurfaceGrid 的既算结果。</summary>
    public static CrowdStructuresStore Build(
        CrowdSimulationRuntimeConfig config,
        SurfaceGrid grid,
        IReadOnlyList<BlockerFootprint> blockers)
    {
        var store = new CrowdStructuresStore(config, grid);
        int buildingTemplate = -1;
        var templates = config.Structures.Templates;
        for (int i = 0; i < templates.Count; i++)
        {
            if (templates[i].Id == "building")
            {
                buildingTemplate = i;
                break;
            }
        }

        if (blockers.Count > 0 && buildingTemplate < 0)
        {
            throw new InvalidOperationException("地图阻挡物需要结构模板 building（rect 阻挡）。");
        }

        foreach (var b in blockers)
        {
            int id = store._nextId++;
            store._ids.Add(id);
            store._tpl[id] = buildingTemplate;
            store._footprint[id] = new CrowdStructureFootprint
            {
                Shape = CrowdStructureShape.Rect,
                CenterXCm = Fix64.FromInt(b.XCm),
                CenterYCm = Fix64.FromInt(b.YCm),
                Hx = Fix64.FromInt(b.HalfSizeCm),
                Hy = Fix64.FromInt(b.HalfSizeCm),
            };
            store._blocker.Add(id);
        }

        store.RebuildColliders();
        store.ReserveEntitySlots();
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

    internal void CopyLiveEntities(List<(int Id, int TplIndex, CrowdStructureFootprint Fp)> destination)
    {
        destination.Clear();
        for (int i = 0; i < _ids.Count; i++)
        {
            int id = _ids[i];
            destination.Add((id, _tpl[id], _footprint[id]));
        }
    }

    /// <summary>模板、区域、阻挡、寿命与实体 id 互相覆盖。分层实体不进地面组件。</summary>
    internal void VerifyComponentContract(int tick)
    {
        var templates = _config.Structures.Templates;
        var live = new HashSet<int>(_ids.Count);
        for (int i = 0; i < _ids.Count; i++)
        {
            int id = _ids[i];
            if (!live.Add(id))
            {
                throw Component(tick, id, $"实体 {id} 在放置序里出现了两次");
            }

            if (!_tpl.TryGetValue(id, out int tplIndex) || !_footprint.ContainsKey(id))
            {
                throw Component(tick, id, $"实体 {id} 缺模板或足迹");
            }

            if ((uint)tplIndex >= (uint)templates.Count)
            {
                throw Component(tick, id, $"实体 {id} 的模板下标 {tplIndex} 越界");
            }

            var tpl = templates[tplIndex];
            if (tpl.Layered)
            {
                if (_navArea.ContainsKey(id) || _blocker.Contains(id))
                {
                    throw Component(tick, id, $"分层实体 {id} 写进了地面区域或阻挡");
                }
            }
            else if (tpl.AreaIndex is { } area)
            {
                if (!_navArea.TryGetValue(id, out var row) || row.AreaIndex != area || row.Priority != tpl.Priority)
                {
                    throw Component(tick, id, $"实体 {id} 的区域组件与模板 {tpl.Id} 不一致");
                }
            }
            else if (_navArea.ContainsKey(id))
            {
                throw Component(tick, id, $"实体 {id} 的模板 {tpl.Id} 没有区域，组件里却有");
            }

            if (!tpl.Layered && _blocker.Contains(id) != tpl.Blocker)
            {
                throw Component(tick, id, $"实体 {id} 的阻挡组件与模板 {tpl.Id} 不一致");
            }

            if (_lifetime.ContainsKey(id) && tpl.LifetimeSecRaw == null)
            {
                throw Component(tick, id, $"实体 {id} 的模板 {tpl.Id} 没有寿命，寿命表里却有");
            }
        }

        RejectOrphans(tick, live, _tpl.Keys, "模板");
        RejectOrphans(tick, live, _footprint.Keys, "足迹");
        RejectOrphans(tick, live, _navArea.Keys, "区域");
        RejectOrphans(tick, live, _blocker, "阻挡");
        RejectOrphans(tick, live, _lifetime.Keys, "寿命");
    }

    private void RejectOrphans(int tick, HashSet<int> live, IEnumerable<int> keys, string table)
    {
        foreach (int id in keys)
        {
            if (!live.Contains(id))
            {
                throw Component(tick, id, $"{table}表引用了已不在仓里的实体 {id}");
            }
        }
    }

    private static CrowdSimulationInvariantException Component(int tick, int entityId, string detail) =>
        new($"tick {tick} {detail}", tick, -1, entityId, -1, -1, -1, -1, -1);

    public bool TryGetFootprint(int id, out CrowdStructureFootprint fp)
    {
        if (_footprint.TryGetValue(id, out fp!)) return true;
        fp = default;
        return false;
    }

    public bool TryGetTemplate(int id, out int tplIndex) => _tpl.TryGetValue(id, out tplIndex!);

    /// <summary>
    /// 认知场。区域与阻挡从传入的地形源起（建仓时的映射和阻挡，不含此后放上的实体），
    /// 再按实体 id 序重标 believed 足迹。分层实体留在表里，不涂地面——桥面不走这张栅格。
    /// 不读构建时的真相活栅格。不跑寿命。
    /// </summary>
    internal static CrowdStructuresStore BuildBeliefField(
        CrowdSimulationRuntimeConfig config,
        SurfaceGrid terrain,
        IReadOnlyList<(int Id, int TplIndex, CrowdStructureFootprint Fp)> entities,
        ref BeliefRank[] ranks)
    {
        var store = new CrowdStructuresStore(config, terrain);
        store.Area = (byte[])store._terrainArea.Clone();
        Array.Copy(store._terrainBlocked, store.Blocked, store.Blocked.Length);
        int n = entities.Count;
        StableOrder.Ensure(ref ranks, n);
        for (int i = 0; i < n; i++)
        {
            var row = entities[i];
            ranks[i] = new BeliefRank(row.Id, i, row.TplIndex, row.Fp);
        }

        if (n > 1) Array.Sort(ranks, 0, n);
        for (int i = 0; i < n; i++)
        {
            int id = ranks[i].Id;
            int tplIndex = ranks[i].TplIndex;
            var fp = ranks[i].Fp;
            var tpl = config.Structures.Templates[tplIndex];
            store._ids.Add(id);
            store._tpl[id] = tplIndex;
            store._footprint[id] = fp;
            if (!tpl.Layered)
            {
                if (tpl.AreaIndex is { } area) store._navArea[id] = (area, tpl.Priority);
                if (tpl.Blocker) store._blocker.Add(id);
            }
            if (id >= store._nextId) store._nextId = id + 1;
        }

        for (int i = 0; i < n; i++)
        {
            store.RasterRect(ranks[i].Fp.CellRectOf(store.NavCellSizeCm, store.NavCellsPerSide));
        }

        store.RebuildColliders();
        store.ReserveEntitySlots();
        return store;
    }

    /// <summary>换掉区域数组。调用方要保证这是该仓私有的一份。</summary>
    public void ReplaceArea(byte[] area) => Area = area;

    /// <summary>地图上的桥按模板 <c>bridge</c> 入仓，按列表顺序续号。</summary>
    public void InstallMapBridges(IReadOnlyList<BridgeDeckRecord> bridges)
    {
        if (bridges.Count == 0) return;
        int tpl = TemplateIndexOf("bridge");
        if (tpl < 0)
        {
            throw new InvalidOperationException($"地图上有 {bridges.Count} 座桥，需要结构模板 bridge（path 分层）。");
        }

        var spans = new CrowdSimulationBridgeSpan[bridges.Count];
        for (int i = 0; i < spans.Length; i++) spans[i] = bridges[i].Span;
        InstallMapBridges(tpl, spans);
    }

    /// <summary>地图桥实体入仓。桥进入认知，不进地面栅格与碰撞。桥面由 UpperLayerBake 烘焙。按调用方给出的 span 顺序续号。</summary>
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

    /// <summary>放置实体(组件自模板)并重标足迹格矩形;返回 (id, 格矩形)。分层实体只重画桥面。</summary>
    public (int Id, (int X0, int Y0, int X1, int Y1) Rect) PlaceEntity(int tplIndex, CrowdStructureFootprint fp, int expireTick)
    {
        LastMutationTouchedDeck = false;
        var tpl = _config.Structures.Templates[tplIndex];
        if (tpl.Layered && _deck == null)
        {
            throw new InvalidOperationException("结构仓没有桥面层。");
        }

        int id = _nextId++;
        _ids.Add(id);
        _tpl[id] = tplIndex;
        _footprint[id] = fp;
        if (expireTick >= 0) _lifetime[id] = expireTick;
        if (tpl.Layered)
        {
            LastMutationTouchedDeck = true;
            var rect = fp.CellRectOf(NavCellSizeCm, NavCellsPerSide);
            RestampDeck(rect);
            return (id, rect);
        }

        if (tpl.AreaIndex is { } area) _navArea[id] = (area, tpl.Priority);
        if (tpl.Blocker) _blocker.Add(id);
        return Commit(id, fp);
    }

    /// <summary>移除实体;id 不存在返回 null。返回 (id, 足迹, 格矩形)。</summary>
    public (int Id, CrowdStructureFootprint Fp, (int X0, int Y0, int X1, int Y1) Rect)? RemoveEntity(int id)
    {
        if (!_footprint.TryGetValue(id, out var fp)) return null;
        bool layered = _config.Structures.Templates[_tpl[id]].Layered;
        if (layered && _deck == null)
        {
            throw new InvalidOperationException("结构仓没有桥面层。");
        }

        LastMutationTouchedDeck = false;
        bool wasBlocker = _blocker.Remove(id);
        _ids.Remove(id);
        _tpl.Remove(id);
        _footprint.Remove(id);
        _navArea.Remove(id);
        _lifetime.Remove(id);
        if (!layered) return (id, fp, CommitRect(id, fp, wasBlocker));
        LastMutationTouchedDeck = true;
        var rect = fp.CellRectOf(NavCellSizeCm, NavCellsPerSide);
        RestampDeck(rect);
        return (id, fp, rect);
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

    /// <summary>tick 时刻到期的实体(id 升序)。无寿命实体返回空跨度;有寿命时跨度指向本仓缓冲,下一次调用前读完。</summary>
    public ReadOnlySpan<int> DueExpiries(int tick)
    {
        if (_lifetime.Count == 0) return ReadOnlySpan<int>.Empty;
        _dueExpiryIds.Clear();
        foreach (int id in _ids) // _ids 放置序 = id 升序(id 只增,删除保序);有序容器
        {
            if (_lifetime.TryGetValue(id, out int expire) && expire <= tick) _dueExpiryIds.Add(id);
        }

        return CollectionsMarshal.AsSpan(_dueExpiryIds);
    }

    public CrowdStructureFootprint FootprintOf(int id) => _footprint[id];

    internal int LiveCount => _ids.Count;

    internal int LiveId(int index) => _ids[index];

    internal RuntimeStructureTemplate LiveTemplate(int id) => _config.Structures.Templates[_tpl[id]];

    internal byte TerrainIndex(int cell) => _terrainType[cell];

    internal CrowdPlacementReject JudgePlacement(RuntimeStructureTemplate template, CrowdStructureFootprint footprint) =>
        CrowdStructurePlacement.Judge(this, template, footprint, ref _judgeCells, ref _judgeFlags, out _);

    /// <summary>地面模板写进这一格：区域看格心，阻挡看覆盖份额。</summary>
    internal bool GroundCellHit(RuntimeStructureTemplate tpl, CrowdStructureFootprint fp, int x, int y)
    {
        if (tpl.Layered) return false;
        bool hit = false;
        var cell = Fix64.FromInt(NavCellSizeCm);
        if (tpl.AreaIndex != null)
        {
            var cx = (Fix64.FromInt(x) + Fix64.HalfValue) * cell;
            var cy = (Fix64.FromInt(y) + Fix64.HalfValue) * cell;
            hit = fp.Covers(cx, cy);
        }

        if (tpl.Blocker)
        {
            var (bx0, by0, bx1, by1) = fp.Bbox();
            if (CoverShare(bx0, bx1, x, cell) * CoverShare(by0, by1, y, cell) >= BlockCoverage) hit = true;
        }

        return hit;
    }

    /// <summary>地形源。区域是建仓时的地形映射，阻挡是建仓时的阻挡，都不含此后放上的实体。</summary>
    internal SurfaceGrid TerrainSurface() => new()
    {
        NavCellsPerSide = NavCellsPerSide,
        NavCellSizeCm = NavCellSizeCm,
        TerrainType = _terrainType,
        Area = _terrainArea,
        Blocked = _terrainBlocked,
        JumpCandidates = _jumpCandidates,
    };

    /// <summary>当前派生栅格的只读快照面(全量对拍烘焙用;Area/Blocked 引用本仓活数组)。</summary>
    public SurfaceGrid SnapshotSurface() => new()
    {
        NavCellsPerSide = NavCellsPerSide,
        NavCellSizeCm = NavCellSizeCm,
        TerrainType = _terrainType,
        Area = Area,
        Blocked = Blocked,
        JumpCandidates = _jumpCandidates,
    };

    /// <summary>寿命到期 tick:placeTick + ceil(lifetimeSec × FixedHz / simSecondsPerTickSecond)。
    /// decimal 做精确有理数。30 Hz、simSecondsPerTickSecond = 10 时 4 秒仍是 12 tick、900 秒仍是 2700 tick。</summary>
    public int ExpireTickOf(RuntimeStructureTemplate tpl, int placeTick)
    {
        if (tpl.LifetimeSecRaw is not { } life) return -1;
        return placeTick + LifetimeTicks(life, _config.FixedHz, _config.Sim.SimSecondsPerTickSecondRaw);
    }

    public static int LifetimeTicks(double lifetimeSec, int fixedHz, double simSecondsPerTickSecond)
    {
        decimal ticks = decimal.Ceiling((decimal)lifetimeSec * fixedHz / (decimal)simSecondsPerTickSecond);
        if (ticks > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"结构寿命 {lifetimeSec.ToString(System.Globalization.CultureInfo.InvariantCulture)} 秒换算超过 int（fixedHz={fixedHz}，simSecondsPerTickSecond={simSecondsPerTickSecond.ToString(System.Globalization.CultureInfo.InvariantCulture)}）。");
        }

        return (int)ticks;
    }

    private (int Id, (int X0, int Y0, int X1, int Y1) Rect) Commit(int id, CrowdStructureFootprint fp)
    {
        var rect = RasterRect(fp.CellRectOf(NavCellSizeCm, NavCellsPerSide));
        RebuildColliders();
        return (id, rect);
    }

    private (int X0, int Y0, int X1, int Y1) CommitRect(int id, CrowdStructureFootprint fp, bool wasBlocker)
    {
        var rect = RasterRect(fp.CellRectOf(NavCellSizeCm, NavCellsPerSide));
        if (wasBlocker) RebuildColliders();
        return rect;
    }

    /// <summary>格矩形内的区域与阻挡重标。同优先级后写胜，必须按 _ids 放置序，不能枚举 _navArea。</summary>
    public (int X0, int Y0, int X1, int Y1) RasterRect((int X0, int Y0, int X1, int Y1) rect)
    {
        int navCellsPerSide = NavCellsPerSide;
        Fix64 navCellSizeCm = Fix64.FromInt(NavCellSizeCm);
        int w = rect.X1 - rect.X0;
        if (w <= 0 || rect.Y1 <= rect.Y0) return rect;

        int used = w * (rect.Y1 - rect.Y0);
        var prio = _rasterPrio;
        Array.Fill(prio, -1, 0, used);
        for (int y = rect.Y0; y < rect.Y1; y++)
        {
            int row = y * navCellsPerSide;
            for (int x = rect.X0; x < rect.X1; x++)
            {
                Area[row + x] = _terrainArea[row + x];
                // 先回到建仓时的阻挡，再由仍在场的足迹盖 1。清成 0 会把地形阻挡擦掉，拆除后和全量重标对不上。
                Blocked[row + x] = _terrainBlocked[row + x];
            }
        }

        // 同优先级后写胜。必须按 _ids 放置序。_navArea 有删除，字典枚举序会乱，不能直接枚举。
        foreach (int id in _ids)
        {
            if (!_navArea.TryGetValue(id, out var navArea)) continue;
            var (areaIdx, priority) = navArea;
            var fp = _footprint[id];
            var (a, b, c, d) = fp.CellRectOf(NavCellSizeCm, navCellsPerSide);
            for (int y = Math.Max(b, rect.Y0); y < Math.Min(d, rect.Y1); y++)
            {
                for (int x = Math.Max(a, rect.X0); x < Math.Min(c, rect.X1); x++)
                {
                    int k = (y - rect.Y0) * w + (x - rect.X0);
                    Fix64 cx = (Fix64.FromInt(x) + Fix64.HalfValue) * navCellSizeCm;
                    Fix64 cy = (Fix64.FromInt(y) + Fix64.HalfValue) * navCellSizeCm;
                    if (priority >= prio[k] && fp.Covers(cx, cy))
                    {
                        prio[k] = priority;
                        Area[y * navCellsPerSide + x] = (byte)areaIdx;
                    }
                }
            }
        }

        foreach (int id in _ids) // 顺序无关:阻挡写 1 幂等;_ids 放置序(有序容器)
        {
            if (!_blocker.Contains(id)) continue;
            var fp = _footprint[id];
            var (bx0, by0, bx1, by1) = fp.Bbox();
            var (a, b, c, d) = fp.CellRectOf(NavCellSizeCm, navCellsPerSide);
            for (int y = Math.Max(b, rect.Y0); y < Math.Min(d, rect.Y1); y++)
            {
                for (int x = Math.Max(a, rect.X0); x < Math.Min(c, rect.X1); x++)
                {
                    Fix64 coverX = CoverShare(bx0, bx1, x, navCellSizeCm);
                    Fix64 coverY = CoverShare(by0, by1, y, navCellSizeCm);
                    if (coverX * coverY >= BlockCoverage) Blocked[y * navCellsPerSide + x] = 1;
                }
            }
        }

        return rect;
    }

    /// <summary>格 c 与区间 [a0,a1) 的重叠份额(轴);与 SurfaceGrid.Build 的阻挡覆盖公式同源。</summary>
    private Fix64 CoverShare(Fix64 a0, Fix64 a1, int c, Fix64 navCellSizeCm)
    {
        Fix64 cellLo = Fix64.FromInt(c) * navCellSizeCm;
        Fix64 cellHi = cellLo + navCellSizeCm;
        Fix64 overlap = Fix64.Min(a1, cellHi) - Fix64.Max(a0, cellLo);
        return overlap > Fix64.Zero ? overlap / navCellSizeCm : Fix64.Zero;
    }

    /// <summary>阻挡盒 CSR 整体重建。注册格是包围盒再外扩 ReachCm。写入仓内已有索引，不换对象。</summary>
    public void RebuildColliders()
    {
        var colliders = Colliders;
        colliders.BeginRebuild();
        foreach (int id in _ids) // 顺序无关:盒序进 CSR 后查询与序无关;_ids 放置序(有序容器)
        {
            if (!_blocker.Contains(id)) continue;
            var f = _footprint[id];
            colliders.AddBox(f.CenterXCm, f.CenterYCm, f.Hx, f.Hy, NavCellsPerSide, NavCellSizeCm, ReachCm);
        }

        colliders.CommitRebuild(NavCellsPerSide, NavCellSizeCm, ReachCm);
    }

    /// <summary>桥面格、桥头和含桥面的 tile 集合与按分层实体全量重画的结果一致。</summary>
    internal void VerifyDeckContract(int tick)
    {
        int layered = 0;
        for (int i = 0; i < _ids.Count; i++)
        {
            if (_config.Structures.Templates[_tpl[_ids[i]]].Layered) layered++;
        }

        if (_deck == null)
        {
            if (layered == 0) return;
            throw Component(tick, -1, "结构仓没有桥面层。");
        }

        int n2 = NavCellsPerSide * NavCellsPerSide;
        var area = new byte[n2];
        var portal = new byte[n2];
        PaintLayered(area, portal, (0, 0, NavCellsPerSide, NavCellsPerSide), clear: false);
        for (int i = 0; i < n2; i++)
        {
            if (_deck.AreaIndex[i] == area[i] && _deck.PortalFlag[i] == portal[i]) continue;
            throw Component(tick, -1,
                $"桥面格与分层实体全量栅格不一致 @格 {i} 区域 {_deck.AreaIndex[i]}/{area[i]} 桥头 {_deck.PortalFlag[i]}/{portal[i]}");
        }

        int tileSize = NavCellsPerTile;
        int tilesPerSide = NavCellsPerSide / tileSize;
        var expected = new HashSet<int>();
        for (int i = 0; i < n2; i++)
        {
            if (area[i] == 0) continue;
            int x = i % NavCellsPerSide;
            int y = i / NavCellsPerSide;
            expected.Add((y / tileSize) * tilesPerSide + x / tileSize);
        }

        var actual = new HashSet<int>(_deck.TileOrder);
        if (actual.Count != _deck.TileOrder.Length || !actual.SetEquals(expected))
        {
            throw Component(tick, -1, $"桥面 tile 集合与分层实体不一致：仓内 {_deck.TileOrder.Length}，全量 {expected.Count}。");
        }
    }

    /// <summary>矩形内先清再按放置序重画。后放的区域盖住先放的，portal 只置位。</summary>
    private void RestampDeck((int X0, int Y0, int X1, int Y1) rect)
    {
        if (_deck == null) throw new InvalidOperationException("结构仓没有桥面层。");
        if (rect.X1 <= rect.X0 || rect.Y1 <= rect.Y0) return;
        PaintLayered(_deck.AreaIndex, _deck.PortalFlag, rect, clear: true);
        RefreshTileOrder(rect);
    }

    private void PaintLayered(byte[] area, byte[] portal, (int X0, int Y0, int X1, int Y1) clip, bool clear)
    {
        if (clear)
        {
            int side = NavCellsPerSide;
            for (int y = clip.Y0; y < clip.Y1; y++)
            {
                int row = y * side;
                for (int x = clip.X0; x < clip.X1; x++)
                {
                    area[row + x] = 0;
                    portal[row + x] = 0;
                }
            }
        }

        var reach = CrowdReferenceMeters.TruncatedCmToM(PortalReachNavCells * NavCellSizeCm);
        int x1 = clip.X1 - 1;
        int y1 = clip.Y1 - 1;
        for (int i = 0; i < _ids.Count; i++)
        {
            int id = _ids[i];
            var tpl = _config.Structures.Templates[_tpl[id]];
            if (!tpl.Layered) continue;
            if (tpl.AreaIndex is not int areaIndex)
            {
                throw new InvalidOperationException($"分层模板 {tpl.Id} 没有导航区域。");
            }

            var fp = _footprint[id];
            var span = DeckSpanGeometry.FromCentimeters(
                fp.X0.ToInt(), fp.Y0.ToInt(), fp.X1.ToInt(), fp.Y1.ToInt(), fp.WidthCm.ToInt(),
                NavCellsPerSide, NavCellSizeCm, reach);
            int xa = Math.Max(span.MinX, clip.X0);
            int ya = Math.Max(span.MinY, clip.Y0);
            int xb = Math.Min(span.MaxX, x1);
            int yb = Math.Min(span.MaxY, y1);
            for (int y = ya; y <= yb; y++)
            {
                for (int x = xa; x <= xb; x++)
                {
                    if (!span.Hit(x, y, out var endDist)) continue;
                    int cell = y * NavCellsPerSide + x;
                    area[cell] = (byte)(areaIndex + 1);
                    if (span.IsPortal(endDist)) portal[cell] = 1;
                }
            }
        }
    }

    private void RefreshTileOrder((int X0, int Y0, int X1, int Y1) rect)
    {
        int tileSize = NavCellsPerTile;
        int tilesPerSide = NavCellsPerSide / tileSize;
        int tx0 = rect.X0 / tileSize;
        int ty0 = rect.Y0 / tileSize;
        int tx1 = (rect.X1 - 1) / tileSize;
        int ty1 = (rect.Y1 - 1) / tileSize;
        var old = _deck!.TileOrder;
        var next = new List<int>(old.Length + 4);
        var kept = new HashSet<int>();
        for (int i = 0; i < old.Length; i++)
        {
            int tile = old[i];
            int tx = tile % tilesPerSide;
            int ty = tile / tilesPerSide;
            bool touched = tx >= tx0 && tx <= tx1 && ty >= ty0 && ty <= ty1;
            if (touched && !TileHasDeck(tx, ty, tileSize)) continue;
            next.Add(tile);
            kept.Add(tile);
        }

        for (int ty = ty0; ty <= ty1; ty++)
        {
            for (int tx = tx0; tx <= tx1; tx++)
            {
                int tile = ty * tilesPerSide + tx;
                if (kept.Contains(tile) || !TileHasDeck(tx, ty, tileSize)) continue;
                next.Add(tile);
            }
        }

        _deck.TileOrder = next.Count == 0 ? Array.Empty<int>() : next.ToArray();
    }

    private bool TileHasDeck(int tx, int ty, int tileSize)
    {
        var area = _deck!.AreaIndex;
        int side = NavCellsPerSide;
        int x0 = tx * tileSize;
        int y0 = ty * tileSize;
        for (int y = y0; y < y0 + tileSize; y++)
        {
            int row = y * side;
            for (int x = x0; x < x0 + tileSize; x++)
            {
                if (area[row + x] != 0) return true;
            }
        }

        return false;
    }

    /// <summary>实体表在装载后留出空位。tick 内新增的少数实体命中已有槽,不再扩容。</summary>
    private void ReserveEntitySlots()
    {
        int cap = _ids.Count + CrowdSimulationLimits.HotPathSlotReserve;
        _ids.EnsureCapacity(cap);
        _tpl.EnsureCapacity(cap);
        _footprint.EnsureCapacity(cap);
        _navArea.EnsureCapacity(cap);
        _blocker.EnsureCapacity(cap);
        _lifetime.EnsureCapacity(cap);
        _dueExpiryIds.EnsureCapacity(CrowdSimulationLimits.HotPathSlotReserve);
    }
}
