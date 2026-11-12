using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Diagnostics;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Vision;

namespace Ludots.Core.CrowdSimulation.Fog;

/// <summary>
/// 迷雾 + 认知,按视野组:sharesVisionWith 两个方向都成立的玩家并成一组,
/// 迷雾与认知按组共享,不按玩家。每组在粗格(FogCellsPerSide = ceil(NavCellsPerSide / NavCellsPerFogCell) 的 fog 格)上
/// 有一份 FogField:可见格按 fog.rateHz 重建(组按 round-robin 摊到周期各 tick,单位只在本组槽位 tick 盖章),
/// 已探索粘滞。belief = 实体 id → 末次所见快照 {tpl, fp}(地图初始实体全员已知;运行时
/// 实体见即知;真值已拆的实体留残影,直到其底层再被看见);key = 认知实体集的交换 XOR 哈希
/// ——同 key 的组共享一个认知槽(槽归属永远经集合相等确认,哈希碰撞不可能错分)。
/// 确定性:只由 tick 计数与单位状态驱动。马达永远走真相;规划侧经 CrowdBeliefSync 消费认知。
/// </summary>
public sealed class CrowdFog
{
    private const int TileTag = unchecked((int)0x40000000); // tile 哈希不与实体 id 相撞

    public readonly CrowdStructuresStore Store;
    public readonly int NavCellsPerFogCell;          // 一个 fog 格 = NavCellsPerFogCell×NavCellsPerFogCell 导航格
    public readonly int FogCellsPerSide;          // fog 格边长
    public readonly int FogCellSizeCm;      // fog 格边长(厘米)
    public readonly int NavCellsPerSide;          // 导航格边长
    public readonly int VisionGroupCount;          // 视野组数
    public readonly int NavCellsPerTile, NavTilesPerSide;       // tile 边长(导航格)/ tile 网格边长
    /// <summary>每组残影(认知有、真相无足迹)。与认知同步增减,不进校验码。可见/已探索计数在 FogField。</summary>
    public readonly int[] GhostCount;
    public readonly uint[] Version, Key, TileKey, Seen, Gone;
    /// <summary>认知实体集变更计数，以及实体集或 tile 集变更计数。</summary>
    public readonly long[] EntRev, Rev;
    public long TruthRev;
    public uint TruthKey;
    public readonly byte[] GroupOf; // 玩家表序下标 → 组
    /// <summary>每组的认知实体表:id → (模板下标, 足迹快照)。</summary>
    public readonly Dictionary<int, (int TplIndex, CrowdStructureFootprint Fp)>[] Belief;
    /// <summary>地形知识:组探索过的导航 tile 集(乐观迷雾开启时,未探索 tile 按可通行假设)。</summary>
    public readonly HashSet<int>[] Tiles;
    public bool Terrain;            // 开局取 fog.optimisticTerrain；order 的 fogTerrain 可改，并随指令日志回放
    public bool Los;                // fogSight 命令切换
    public readonly int EyeCm;
    public int Period;

    /// <summary>在途遮蔽:{组, 格集, until tick},按指令序。</summary>
    private List<(int GroupIndex, int[] Cells, int Until)> _obscured = new();
    public readonly ushort[] ObsN;  // 每组每格的活跃遮蔽条目数
    private readonly FogFieldStore _fieldStore;
    private readonly FogField[] _vision;
    private readonly bool[] _retired;
    private readonly int[] _disc;
    private readonly int[] _stampCells;
    private readonly byte[] _stampSeen;
    private readonly int[] _stampCount;
    private readonly byte[] _visibleMask;

    // 索引:fog 格 → 足迹 bbox 触格的实体 id 集(真值 + 仍被信的残影)。
    // 每格的集在构造时留好;放置只往已有集里加 id,不再按格 new。
    private readonly HashSet<int>[] _idx;
    private List<int>?[] _cellsOf = Array.Empty<List<int>>();
    private readonly Stack<List<int>> _cellListPool = new();
    // 逐 tick 复用暂存(对账/遗忘/遮蔽到期;调用序不嵌套,清空即借出)
    // _reconcileDone 的初始容量是开局实体数 + HotPathSlotReserve。之后放置的实体超过该容量时 HashSet 仍会扩容。_cellsOf 在 id 越界时 Array.Resize。
    private readonly HashSet<int> _reconcileDone;
    private readonly List<int> _reconcileForget = new();
    private readonly List<(int GroupIndex, int[] Cells, int Until)> _obscureKeep = new();
    private Fix64[]? _hAvgM, _hMaxM;  // LOS:fog 格均高 / 最高高(米,Fix64)
    private readonly Fix64 _eyeM;   // 眼高(米)
    private readonly byte[] _opaque;
    private bool _opaqueDirty;
    public int Refreshes;           // 诊断:刷新次数

    public CrowdFog(
        CrowdSimulationRuntimeConfig config,
        CrowdPairRules pairRules,
        CrowdStructuresStore store,
        FogLayerRegistry layers,
        FogFieldStore fields)
    {
        Store = store;
        if (fields == null)
        {
            throw new ArgumentNullException(nameof(fields));
        }

        var fc = config.Fog;
        NavCellsPerSide = config.NavCellsPerSide;
        NavCellsPerFogCell = fc.NavCellsPerFogCell;
        FogCellsPerSide = (NavCellsPerSide + NavCellsPerFogCell - 1) / NavCellsPerFogCell;
        FogCellSizeCm = fc.FogCellSizeCm;
        Los = fc.LineOfSight;
        Terrain = fc.OptimisticTerrain;
        EyeCm = fc.EyeCm;
        _eyeM = CrowdReferenceMeters.CmToMPrecise(EyeCm);
        NavCellsPerTile = config.Hpa.NavCellsPerTile;
        NavTilesPerSide = NavCellsPerSide / NavCellsPerTile;
        SetPeriod(config.FixedHz, fc.RateHz.ToDouble());

        GroupOf = BuildVisionGroups(pairRules.SharesVisionByPair, pairRules.PlayerCount, out int visionGroupCount);
        VisionGroupCount = visionGroupCount;
        int f2 = FogCellsPerSide * FogCellsPerSide;
        _opaque = new byte[f2];
        int updateHz = Math.Max(1, (int)Math.Round(fc.RateHz.ToDouble(), MidpointRounding.AwayFromZero));
        FogLayerDefinition layer = layers.Get(CrowdFogLayerBinding.Require(layers, FogCellSizeCm, updateHz));
        _fieldStore = fields;
        _vision = new FogField[VisionGroupCount];
        _retired = new bool[VisionGroupCount];
        for (int g = 0; g < VisionGroupCount; g++)
        {
            FogField field = fields.GetOrCreate(g + 1, layer);
            field.EnsureCellBlock(FogCellsPerSide);
            _vision[g] = field;
        }

        _stampCells = new int[VisionGroupCount * f2];
        _stampSeen = new byte[VisionGroupCount * f2];
        _stampCount = new int[VisionGroupCount];
        _visibleMask = new byte[f2];
        GhostCount = new int[VisionGroupCount];
        Version = new uint[VisionGroupCount];
        Key = new uint[VisionGroupCount];
        TileKey = new uint[VisionGroupCount];
        Seen = new uint[VisionGroupCount];
        Gone = new uint[VisionGroupCount];
        EntRev = new long[VisionGroupCount];
        Rev = new long[VisionGroupCount];
        ObsN = new ushort[VisionGroupCount * f2];
        _reconcileDone = new HashSet<int>(Math.Max(16, store.EntityIds.Count + CrowdSimulationLimits.HotPathSlotReserve));
        _idx = new HashSet<int>[f2];
        for (int c = 0; c < f2; c++) _idx[c] = new HashSet<int>(4);
        int cellSlots = Math.Max(16, store.EntityIds.Count + CrowdSimulationLimits.HotPathSlotReserve);
        _cellsOf = new List<int>?[cellSlots];
        for (int i = 0; i < CrowdSimulationLimits.HotPathSlotReserve; i++) _cellListPool.Push(new List<int>(8));
        Belief = new Dictionary<int, (int, CrowdStructureFootprint)>[VisionGroupCount];
        Tiles = new HashSet<int>[VisionGroupCount];
        int beliefCap = Math.Max(16, store.EntityIds.Count + CrowdSimulationLimits.HotPathSlotReserve);
        for (int g = 0; g < VisionGroupCount; g++)
        {
            Belief[g] = new Dictionary<int, (int, CrowdStructureFootprint)>(beliefCap);
            Tiles[g] = new HashSet<int>(NavTilesPerSide * NavTilesPerSide);
        }

        _disc = VisionBulkStamp.BuildOffsets((int)fc.VisionCm.ToLong(), FogCellSizeCm);

        // 初始认知:地图实体全员已知 + 索引
        foreach (int id in store.EntityIds)
        {
            var fp = store.FootprintOf(id);
            store.TryGetTemplate(id, out int tpl);
            for (int g = 0; g < VisionGroupCount; g++)
            {
                Belief[g][id] = (tpl, fp);
                Key[g] ^= Mix(id);
            }

            IndexAdd(id, fp);
        }

        TruthKey = Key[0];
        _opaqueDirty = true;
    }

    /// <summary>两个方向都为真才连边。并查集根取小索引,组号按根的首见序,合组传递。</summary>
    internal static byte[] BuildVisionGroups(bool[] shares, int playerCount, out int visionGroupCount)
    {
        var root = new int[playerCount];
        for (int i = 0; i < playerCount; i++) root[i] = i;
        for (int a = 0; a < playerCount; a++)
        {
            for (int b = a + 1; b < playerCount; b++)
            {
                if (shares[a * playerCount + b] && shares[b * playerCount + a])
                {
                    root[Math.Max(Find(a), Find(b))] = Math.Min(Find(a), Find(b));
                }
            }
        }

        var idsByRoot = new Dictionary<int, int>();
        var groupOf = new byte[playerCount];
        for (int i = 0; i < playerCount; i++)
        {
            int rt = Find(i);
            if (!idsByRoot.TryGetValue(rt, out int g)) idsByRoot[rt] = g = idsByRoot.Count;
            groupOf[i] = (byte)g;
        }

        visionGroupCount = idsByRoot.Count;
        return groupOf;

        int Find(int i) => root[i] == i ? i : root[i] = Find(root[i]);
    }

    /// <summary>LOS 高度表(米,Fix64)。会话在迷雾装配时注入。
    /// 逐格一次 CmToMPrecise，格内累加，均值一次除法。</summary>
    public void EnsureHeights(NavHeightField heights)
    {
        if (_hAvgM != null) return;
        _hAvgM = new Fix64[FogCellsPerSide * FogCellsPerSide];
        _hMaxM = new Fix64[FogCellsPerSide * FogCellsPerSide];
        var cnt = new int[FogCellsPerSide * FogCellsPerSide];
        for (int y = 0; y < NavCellsPerSide; y++)
        {
            for (int x = 0; x < NavCellsPerSide; x++)
            {
                int c = y / NavCellsPerFogCell * FogCellsPerSide + x / NavCellsPerFogCell;
                Fix64 h = CrowdReferenceMeters.CmToMPrecise(heights.HeightsCm[y * NavCellsPerSide + x]);
                _hAvgM[c] += h;
                cnt[c]++;
                if (h > _hMaxM[c]) _hMaxM[c] = h;
            }
        }

        for (int c = 0; c < FogCellsPerSide * FogCellsPerSide; c++) _hAvgM[c] /= Fix64.FromInt(cnt[c]);
    }

    /// <summary>刷新周期 = max(1, round(tickHz / rateHz))。rateHz 大于步进频率则抛错。</summary>
    public void SetPeriod(int tickHz, double rateHz)
    {
        Period = PeriodTicks(tickHz, rateHz);
        double actualHz = tickHz / (double)Period;
        Log.Info(in LogChannels.Engine,
            $"CrowdSimulation fog: configured {rateHz.ToString(CultureInfo.InvariantCulture)} Hz, period {Period} ticks, actual {actualHz.ToString("0.###", CultureInfo.InvariantCulture)} Hz.");
    }

    internal static int PeriodTicks(int tickHz, double rateHz)
    {
        if (tickHz < 1)
        {
            throw new InvalidOperationException($"迷雾刷新需要步进频率 ≥ 1 Hz（当前 {tickHz}）。");
        }

        if (!(rateHz > 0))
        {
            throw new InvalidOperationException(
                $"CrowdSimulationConfig.json: fog.rateHz = {rateHz.ToString(CultureInfo.InvariantCulture)}，需为 > 0。");
        }

        if (rateHz > tickHz)
        {
            throw new InvalidOperationException(
                $"CrowdSimulationConfig.json: fog.rateHz = {rateHz.ToString(CultureInfo.InvariantCulture)} 超过步进频率 {tickHz} Hz。");
        }

        return Math.Max(1, (int)Math.Floor(tickHz / rateHz + 0.5));
    }

    /// <summary>真值实体出现/消失(放置、拆除、寿命到期)——真相方调用。</summary>
    public void OnTruth(int id)
    {
        TruthKey ^= Mix(id);
        TruthRev++;
        _opaqueDirty = true;
        if (Store.TryGetFootprint(id, out var fp))
        {
            IndexAdd(id, fp);
            return;
        }

        bool believed = false;
        for (int g = 0; g < VisionGroupCount; g++)
        {
            if (!Belief[g].ContainsKey(id)) continue;
            believed = true;
            GhostCount[g]++;
        }

        if (!believed) IndexDel(id);
    }

    private void IndexAdd(int id, CrowdStructureFootprint fp)
    {
        var (a, b, c, d) = fp.Bbox();
        if (!_cellListPool.TryPop(out var cells)) cells = new List<int>(8);
        cells.Clear();
        Fix64 fogCellCm = Fix64.FromInt(FogCellSizeCm);
        int x0 = Math.Max(0, (int)(a / fogCellCm).ToLong()), y0 = Math.Max(0, (int)(b / fogCellCm).ToLong());
        int x1 = Math.Min(FogCellsPerSide - 1, (int)(c / fogCellCm).ToLong()), y1 = Math.Min(FogCellsPerSide - 1, (int)(d / fogCellCm).ToLong());
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                int k = y * FogCellsPerSide + x;
                _idx[k].Add(id);
                cells.Add(k);
            }
        }

        if ((uint)id >= (uint)_cellsOf.Length)
        {
            int n = _cellsOf.Length;
            while ((uint)id >= (uint)n) n *= 2;
            Array.Resize(ref _cellsOf, n);
        }

        _cellsOf[id] = cells;
    }

    private void IndexDel(int id)
    {
        if ((uint)id >= (uint)_cellsOf.Length || _cellsOf[id] is not { } cells) return;
        foreach (int k in cells) _idx[k].Remove(id);
        cells.Clear();
        _cellListPool.Push(cells);
        _cellsOf[id] = null;
    }

    private static bool CellsInside(List<int> cells, HashSet<int> member)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            if (!member.Contains(cells[i])) return false;
        }

        return true;
    }

    public int VisibleCountOf(int group) => _vision[group].VisibleCount;

    public int ExploredCountOf(int group) => _vision[group].ExploredInclusiveCount;

    public FogField VisionField(int group) => _vision[group];

    internal FogFieldStore Fields => _fieldStore;

    public bool ScopeRetired(int group) => (uint)group < (uint)_retired.Length && _retired[group];

    public CellVisibility CellVisibilityOf(int group, int cell)
    {
        return _vision[group].GetVisibility(new FogCell(cell % FogCellsPerSide, cell / FogCellsPerSide));
    }

    /// <summary>一个 tick:刷新本 tick 槽位的组(单位位置取 Fix64 厘米域,格号向零截断)。</summary>
    public void Update(int tick, CrowdSimSession sim)
    {
        int slot = tick % Period;
        int side = FogCellsPerSide;
        int f2 = side * side;
        if (_obscured.Count > 0) ExpireObscured(tick);
        bool any = false;
        for (int g = slot; g < VisionGroupCount; g += Period)
        {
            if (_retired[g]) continue;
            Array.Clear(_stampSeen, g * f2, f2);
            _stampCount[g] = 0;
            any = true;
        }

        if (!any) return;

        var movement = sim.Movement;
        if (movement == null)
        {
            throw new InvalidOperationException("迷雾更新需要运动内核里的位置和玩家下标。");
        }

        var positions = movement.Positions;
        var players = movement.PlayerIdx;
        int unitCount = sim.Units.Count;
        for (int i = 0; i < unitCount; i++)
        {
            int g = GroupOf[players[i]];
            if (_retired[g] || g % Period != slot) continue;
            var p = positions[i];
            int fx = Math.Max(0, Math.Min(side - 1, (int)(p.X / Fix64.FromInt(FogCellSizeCm)).ToLong()));
            int fy = Math.Max(0, Math.Min(side - 1, (int)(p.Y / Fix64.FromInt(FogCellSizeCm)).ToLong()));
            int local = fy * side + fx;
            int mark = g * f2 + local;
            if (_stampSeen[mark] != 0) continue;
            _stampSeen[mark] = 1;
            _stampCells[g * f2 + _stampCount[g]++] = local;
        }

        int refreshesBefore = Refreshes;
        for (int g = slot; g < VisionGroupCount; g += Period)
        {
            if (_retired[g]) continue;
            Refresh(g);
        }
        int groups = Refreshes - refreshesBefore;
        if (groups > 0) sim.Telemetry.MarkFogRefresh(groups);
    }

    private void Refresh(int g)
    {
        int side = FogCellsPerSide;
        int f2 = side * side;
        if (Los && _opaqueDirty) BuildOpaque();
        ReadOnlySpan<int> occupied = _stampCells.AsSpan(g * f2, _stampCount[g]);
        ReadOnlySpan<ushort> obscured = _obscured.Count > 0 ? ObsN.AsSpan(g * f2, f2) : default;
        FogField field = _vision[g];
        if (Los)
        {
            var filter = new LosStamp(this, g);
            VisionBulkStamp.Stamp(field, side, occupied, _disc, obscured, _visibleMask, ref filter);
        }
        else
        {
            var filter = new OpenStamp(this, g);
            VisionBulkStamp.StampOpen(field, side, occupied, _disc, obscured, _visibleMask, ref filter);
        }

        Reconcile(g);
        Version[g]++;
        Refreshes++;
    }

    private struct OpenStamp : IVisionStampFilter
    {
        public OpenStamp(CrowdFog fog, int group)
        {
            Fog = fog;
            Group = group;
        }

        public CrowdFog Fog;
        public int Group;

        public readonly bool Allows(int originX, int originY, int cellX, int cellY) => true;

        public readonly void OnFirstExplore(int cellX, int cellY) => Fog.Explore(Group, cellX, cellY);
    }

    private struct LosStamp : IVisionStampFilter
    {
        public LosStamp(CrowdFog fog, int group)
        {
            Fog = fog;
            Group = group;
        }

        public CrowdFog Fog;
        public int Group;

        public readonly bool Allows(int originX, int originY, int cellX, int cellY) => Fog.Sight(originX, originY, cellX, cellY);

        public readonly void OnFirstExplore(int cellX, int cellY) => Fog.Explore(Group, cellX, cellY);
    }

    /// <summary>含任何阻挡导航格的 fog 格不透明(真值实体变更后重建)。</summary>
    private void BuildOpaque()
    {
        Array.Clear(_opaque);
        var blocked = Store.Blocked;
        for (int i = 0; i < NavCellsPerSide * NavCellsPerSide; i++)
        {
            if (blocked[i] != 0) _opaque[i / NavCellsPerSide / NavCellsPerFogCell * FogCellsPerSide + i % NavCellsPerSide / NavCellsPerFogCell] = 1;
        }

        _opaqueDirty = false;
    }

    /// <summary>视线(半格步进采样,Fix64):严格中间格不得不透明、不得高出眼—目标高度线;
    /// 端点不挡。除法取精确商向零截断,乘法取精确积向零截断。rise·f 走 MulExact——rise 可为负,
    /// operator* 对负数向下取整,余量非 0 时与向零截断差 1 个最小单位;dy·f 的 dy 恒整数,直乘本就精确。</summary>
    private bool Sight(int cx, int cy, int tx, int ty)
    {
        int dx = tx - cx, dy = ty - cy;
        int n = Math.Max(Math.Abs(dx), Math.Abs(dy)) * 2;
        if (n <= 2) return true;
        int v = cy * FogCellsPerSide + cx, t = ty * FogCellsPerSide + tx;
        Fix64 eye = _hAvgM![v] + _eyeM;
        Fix64 rise = _hAvgM[t] - eye;
        for (int s = 1; s < n; s++)
        {
            Fix64 f = Fix64.FromInt(s) / Fix64.FromInt(n);
            int c = (int)((Fix64.FromInt(cy) + Fix64.HalfValue + dy * f).ToLong() * FogCellsPerSide + (Fix64.FromInt(cx) + Fix64.HalfValue + dx * f).ToLong());
            if (c == v || c == t) continue;
            if (_opaque[c] != 0 || _hMaxM![c] > eye + Fix64Math.MulExact(rise, f)) return false;
        }

        return true;
    }

    /// <summary>fog 格 (x, y) 首次探索 → 它覆盖的每个导航 tile 都进组的地形知识(一格可跨多 tile)。</summary>
    private void Explore(int g, int x, int y)
    {
        int Tile(int v) => Math.Min(NavTilesPerSide - 1, v / NavCellsPerTile);
        int tx0 = Tile(x * NavCellsPerFogCell), tx1 = Tile((x + 1) * NavCellsPerFogCell - 1);
        int ty0 = Tile(y * NavCellsPerFogCell), ty1 = Tile((y + 1) * NavCellsPerFogCell - 1);
        for (int ty = ty0; ty <= ty1; ty++)
        {
            for (int tx = tx0; tx <= tx1; tx++)
            {
                int t = ty * NavTilesPerSide + tx;
                if (!Tiles[g].Add(t)) continue;
                TileKey[g] ^= Mix(TileTag + t);
                Rev[g]++;
            }
        }
    }

    /// <summary>槽共享的认知 key:实体集,乐观迷雾开启时再并 tile 集。</summary>
    public uint BeliefKey(int g) => unchecked(Terrain ? Key[g] ^ TileKey[g] : Key[g]);

    /// <summary>组知道真实地形(乐观关,或 tile 全探索)。</summary>
    public bool KnowsTerrain(int g) => !Terrain || Tiles[g].Count == NavTilesPerSide * NavTilesPerSide;

    /// <summary>组认知里假设可通行的未探索 tile 集(null = 乐观关 / 全知)。</summary>
    public List<int>? Unexplored(int g)
    {
        if (KnowsTerrain(g)) return null;
        var s = Tiles[g];
        var res = new List<int>();
        for (int t = 0; t < NavTilesPerSide * NavTilesPerSide; t++)
        {
            if (!s.Contains(t)) res.Add(t);
        }

        return res;
    }

    /// <summary>真相对认知(在组看见的地方):新实体入信,消失的出信。
    /// 只读可见格索引到的实体(bbox 触可见格 ⇔ 足迹被看见)。</summary>
    private void Reconcile(int g)
    {
        int f2 = FogCellsPerSide * FogCellsPerSide;
        var done = _reconcileDone;
        var forget = _reconcileForget;
        done.Clear();
        forget.Clear();
        for (int c = 0; c < f2; c++)
        {
            if (_visibleMask[c] != 0)
            {
                ReconcileCell(g, c, done, forget);
            }
        }

        DropUnbelieved(forget);
    }

    private void ReconcileCell(int g, int c, HashSet<int> done, List<int> forget)
    {
        var ids = _idx[c];
        if (ids.Count == 0) return;
        foreach (int id in ids)
        {
            if (!done.Add(id)) continue;
            if (Store.TryGetFootprint(id, out var fp))
            {
                if (Belief[g].ContainsKey(id)) continue;
                Store.TryGetTemplate(id, out int tpl);
                Belief[g][id] = (tpl, fp);
                Key[g] ^= Mix(id);
                Seen[g]++;
            }
            else if (Belief[g].ContainsKey(id))
            {
                Belief[g].Remove(id);
                Key[g] ^= Mix(id);
                Gone[g]++;
                forget.Add(id);
                GhostCount[g]--;
            }
            else
            {
                continue;
            }

            EntRev[g]++;
            Rev[g]++;
        }
    }

    private void DropUnbelieved(List<int> ids)
    {
        foreach (int id in ids)
        {
            if (!Store.TryGetFootprint(id, out _) && !BeliefHas(id)) IndexDel(id);
        }
    }

    // 面命令改这份雾场。认知同步在下一步 CrowdBeliefSync.Sync。

    /// <summary>如同组刚看过该区域:explored + 真相对账(不点 visible)。</summary>
    public void RevealArea(int g, int[] cells)
    {
        FogField field = _vision[g];
        var done = _reconcileDone;
        var forget = _reconcileForget;
        done.Clear();
        forget.Clear();
        foreach (int c in cells)
        {
            int x = c % FogCellsPerSide;
            int y = c / FogCellsPerSide;
            var cell = new FogCell(x, y);
            if (field.GetVisibility(cell) == CellVisibility.Unseen)
            {
                field.SetExplored(cell);
                Explore(g, x, y);
            }

            ReconcileCell(g, c, done, forget);
        }

        DropUnbelieved(forget);
    }

    /// <summary>该区域的当前视野被遮到 until tick:visible 即刻清,重建被 obsN 门住;
    /// explored 与认知不动(遮的是当下,不是记忆)。</summary>
    public void ObscureArea(int g, int[] cells, int until)
    {
        int o = g * FogCellsPerSide * FogCellsPerSide;
        FogField field = _vision[g];
        _obscured.Add((g, cells, until));
        foreach (int c in cells)
        {
            var cell = new FogCell(c % FogCellsPerSide, c / FogCellsPerSide);
            if (field.GetVisibility(cell) == CellVisibility.Visible)
            {
                field.SetExplored(cell);
            }

            ObsN[o + c]++;
        }
    }

    private void ExpireObscured(int tick)
    {
        var keep = _obscureKeep;
        keep.Clear();
        foreach (var e in _obscured)
        {
            if (e.Item3 > tick)
            {
                keep.Add(e);
                continue;
            }

            int o = e.Item1 * FogCellsPerSide * FogCellsPerSide;
            foreach (int c in e.Item2) ObsN[o + c]--;
        }

        // 借出暂存不换本体:keep 若直接成为 _obscured,下次到期的 Clear 会清掉在途表
        _obscured.Clear();
        foreach (var e in keep) _obscured.Add(e);
    }

    /// <summary>组失去对该区域的所知:explored 位、不再被任何 explored fog 格覆盖的 tile、
    /// 足迹全落在区域内的认知实体。</summary>
    public void ForgetArea(int g, int[] cells, (int X0, int Y0, int X1, int Y1) box)
    {
        FogField field = _vision[g];
        var member = new HashSet<int>(cells);
        var ids = new HashSet<int>();
        foreach (int c in cells)
        {
            var cell = new FogCell(c % FogCellsPerSide, c / FogCellsPerSide);
            CellVisibility visibility = field.GetVisibility(cell);
            if (visibility == CellVisibility.Explored || visibility == CellVisibility.Visible)
            {
                field.SetVisibility(cell, CellVisibility.Unseen);
            }

            if (_idx[c].Count > 0) ids.UnionWith(_idx[c]);
        }

        int Tile(int v) => Math.Min(NavTilesPerSide - 1, v / NavCellsPerTile);
        int Fcell(int v) => Math.Min(FogCellsPerSide - 1, v / NavCellsPerFogCell);
        for (int ty = Tile(box.Y0 * NavCellsPerFogCell); ty <= Tile((box.Y1 + 1) * NavCellsPerFogCell - 1); ty++)
        {
            for (int tx = Tile(box.X0 * NavCellsPerFogCell); tx <= Tile((box.X1 + 1) * NavCellsPerFogCell - 1); tx++)
            {
                int t = ty * NavTilesPerSide + tx;
                if (!Tiles[g].Contains(t)) continue;
                bool still = false;
                for (int fy = Fcell(ty * NavCellsPerTile); fy <= Fcell((ty + 1) * NavCellsPerTile - 1) && !still; fy++)
                {
                    for (int fx = Fcell(tx * NavCellsPerTile); fx <= Fcell((tx + 1) * NavCellsPerTile - 1); fx++)
                    {
                        if (Known(field, fx, fy))
                        {
                            still = true;
                            break;
                        }
                    }
                }

                if (still) continue;
                Tiles[g].Remove(t);
                TileKey[g] ^= Mix(TileTag + t);
                Rev[g]++;
            }
        }

        var dropped = new List<int>();
        var ordered = new List<int>(ids);
        ordered.Sort(); // 集合成员唯一,键即决胜键
        foreach (int id in ordered)
        {
            if (!Belief[g].ContainsKey(id)) continue;
            if ((uint)id >= (uint)_cellsOf.Length || _cellsOf[id] is not { } idCells || !CellsInside(idCells, member)) continue;
            Belief[g].Remove(id);
            if (!Store.TryGetFootprint(id, out _)) GhostCount[g]--;
            Key[g] ^= Mix(id);
            EntRev[g]++;
            Rev[g]++;
            dropped.Add(id);
        }

        DropUnbelieved(dropped);
    }

    /// <summary>共享视野:组 b 并入 a(低组号存活,与构造的 root[max]=min 同款)。可见和已探索按格 OR，
    /// 新可见或新已探索触发 tile 入集；信念并进存活组(只有一侧见过的已拆实体留残影直到底层再见)。
    /// 源 scope 从雾场摘掉，之后不再盖章；玩家改挂存活组。被并掉的组无玩家，认知同步把它停在真相槽。
    /// 返回存活组;同组返回 -1。不支持拆组。</summary>
    public int MergeGroups(int a, int b)
    {
        if (a == b) return -1;
        int to = Math.Min(a, b), from = Math.Max(a, b);
        int f2 = FogCellsPerSide * FogCellsPerSide, ot = to * f2, of = from * f2;
        FogField toField = _vision[to];
        FogField fromField = _vision[from];
        for (int c = 0; c < f2; c++)
        {
            int x = c % FogCellsPerSide;
            int y = c / FogCellsPerSide;
            var cell = new FogCell(x, y);
            CellVisibility src = fromField.GetVisibility(cell);
            CellVisibility dst = toField.GetVisibility(cell);
            if (src == CellVisibility.Visible && dst != CellVisibility.Visible)
            {
                toField.SetVisibility(cell, CellVisibility.Visible);
                if (dst == CellVisibility.Unseen) Explore(to, x, y);
            }
            else if (src == CellVisibility.Explored && dst == CellVisibility.Unseen)
            {
                toField.SetExplored(cell);
                Explore(to, x, y);
            }
        }

        var merged = new List<int>(Belief[from].Keys);
        merged.Sort(); // 键唯一,排序即全序
        foreach (int id in merged)
        {
            if (Belief[to].ContainsKey(id)) continue;
            Belief[to][id] = Belief[from][id];
            if (!Store.TryGetFootprint(id, out _)) GhostCount[to]++;
            Key[to] ^= Mix(id);
            EntRev[to]++;
            Rev[to]++;
        }

        for (int p = 0; p < GroupOf.Length; p++)
        {
            if (GroupOf[p] == from) GroupOf[p] = (byte)to;
        }

        // 被并掉组的在途遮蔽条目转挂存活组
        for (int i = 0; i < _obscured.Count; i++)
        {
            var e = _obscured[i];
            if (e.Item1 != from) continue;
            foreach (int c in e.Item2)
            {
                ObsN[of + c]--;
                ObsN[ot + c]++;
                var cell = new FogCell(c % FogCellsPerSide, c / FogCellsPerSide);
                if (toField.GetVisibility(cell) == CellVisibility.Visible)
                {
                    toField.SetExplored(cell);
                }
            }

            _obscured[i] = (to, e.Item2, e.Item3);
        }

        _retired[from] = true;
        if (!_fieldStore.Remove(fromField.ScopeKeyId, fromField.LayerId))
        {
            throw new InvalidOperationException(
                $"视野组 {from} 的雾场 scope {fromField.ScopeKeyId} 不在存储里，无法退役。");
        }

        return to;
    }

    private bool BeliefHas(int id)
    {
        for (int g = 0; g < VisionGroupCount; g++)
        {
            if (Belief[g].ContainsKey(id)) return true;
        }

        return false;
    }

    /// <summary>没有玩家的组(被并掉)。</summary>
    public bool Empty(int g) => !GroupOf.Contains((byte)g);

    /// <summary>不同 BeliefKey 的个数。碰撞会把两套认知算成一个。槽分配仍用集合相等确认。</summary>
    public int BeliefCount() => Enumerable.Range(0, VisionGroupCount).Select(BeliefKey).Distinct().Count();

    /// <summary>呈现查询面(渲染底座只读):0 = 未探索,1 = 已探索,2 = 可见。</summary>
    public byte[] View(int g)
    {
        int f2 = FogCellsPerSide * FogCellsPerSide;
        FogField field = _vision[g];
        var res = new byte[f2];
        for (int c = 0; c < f2; c++)
        {
            CellVisibility visibility = field.GetVisibility(new FogCell(c % FogCellsPerSide, c / FogCellsPerSide));
            res[c] = visibility == CellVisibility.Visible ? (byte)2 : visibility == CellVisibility.Explored ? (byte)1 : (byte)0;
        }

        return res;
    }

    private static bool Known(FogField field, int x, int y)
    {
        CellVisibility visibility = field.GetVisibility(new FogCell(x, y));
        return visibility == CellVisibility.Explored || visibility == CellVisibility.Visible;
    }

    /// <summary>32 位雪崩。乘法在 unchecked 下进行。</summary>
    public static uint Mix(int x)
    {
        unchecked
        {
            int v = x ^ (int)((uint)x >> 16);
            v *= unchecked((int)0x7feb352du);
            v ^= (int)((uint)v >> 15);
            v *= unchecked((int)0x846ca68bu);
            v ^= (int)((uint)v >> 16);
            return (uint)v;
        }
    }
}
