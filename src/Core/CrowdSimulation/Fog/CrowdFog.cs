using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Diagnostics;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Fog;

/// <summary>
/// 迷雾 + 认知(belief),按视野组(fog/fog.js 移植):sharesVisionWith 两个方向都成立的玩家并成一组,
/// 迷雾与认知按组共享,不按玩家。每组在粗格(FogCellsPerSide = ceil(NavCellsPerSide / NavCellsPerFogCell) 的 fog 格)上:
/// visible 每 fog.rateHz 重建(组按 round-robin 摊到周期各 tick,单位只在本组槽位 tick 盖章);
/// explored 粘滞;belief = 实体 id → 末次所见快照 {tpl, fp}(地图初始实体全员已知;运行时
/// 实体见即知;真值已拆的实体留残影,直到其地面再被看见);key = 认知实体集的交换 XOR 哈希
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
    public readonly byte[] Visible, Explored, Occ;   // VisionGroupCount × FogCellsPerSide²
    /// <summary>每组派生计数:可见格、已探索格、残影(认知有、真相无足迹)。与格子/认知同步增减,不进校验码。</summary>
    public readonly int[] VisibleCount, ExploredCount, GhostCount;
    public readonly uint[] Version, Key, TileKey, Seen, Gone;
    /// <summary>BV-6 精确变更计数(参考端 Float64 计数器):认知实体集变 / 实体集或 tile 集变。</summary>
    public readonly long[] EntRev, Rev;
    public long TruthRev;
    public uint TruthKey;
    public readonly byte[] GroupOf; // 玩家表序下标 → 组
    /// <summary>每组的认知实体表:id → (模板下标, 足迹快照)。</summary>
    public readonly Dictionary<int, (int TplIndex, CrowdStructureFootprint Fp)>[] Belief;
    /// <summary>地形知识:组探索过的导航 tile 集(乐观迷雾开启时,未探索 tile 按可通行假设)。</summary>
    public readonly HashSet<int>[] Tiles;
    public bool Terrain;            // order 的 fogTerrain 选项切换(随指令日志回放)
    public bool Los;                // fogSight 命令切换
    public readonly int EyeCm;
    public int Period;

    /// <summary>在途遮蔽:{组, 格集, until tick},按指令序。</summary>
    private List<(int GroupIndex, int[] Cells, int Until)> _obscured = new();
    public readonly ushort[] ObsN;  // 每组每格的活跃遮蔽条目数

    // 索引:fog 格 → 足迹 bbox 触格的实体 id 集(真值 + 仍被信的残影)
    private readonly Dictionary<int, HashSet<int>> _idx = new();
    private readonly Dictionary<int, List<int>> _cellsOf = new();
    // 逐 tick 复用暂存(对账/遗忘/遮蔽到期;调用序不嵌套,清空即借出)
    private readonly HashSet<int> _reconcileDone = new();
    private readonly List<int> _reconcileForget = new();
    private readonly List<(int GroupIndex, int[] Cells, int Until)> _obscureKeep = new();
    private readonly int[] _disc;   // 视盘偏移(dx, dy 平铺;R = visionCm / fcs 的圆盘)
    private Fix64[]? _hAvgM, _hMaxM;  // LOS:fog 格均高 / 最高高(米,Fix64)
    private readonly Fix64 _eyeM;   // 眼高(米)
    private byte[]? _opaque;
    private bool _opaqueDirty;
    public int Refreshes;           // 诊断:刷新次数

    public CrowdFog(CrowdSimulationRuntimeConfig config, CrowdPairRules pairRules, CrowdStructuresStore store)
    {
        Store = store;
        var fc = config.Fog;
        NavCellsPerSide = config.NavCellsPerSide;
        NavCellsPerFogCell = fc.NavCellsPerFogCell;
        FogCellsPerSide = (NavCellsPerSide + NavCellsPerFogCell - 1) / NavCellsPerFogCell;
        FogCellSizeCm = fc.FogCellSizeCm;
        Los = fc.LineOfSight;
        EyeCm = fc.EyeCm;
        _eyeM = CrowdReferenceMeters.CmToMPrecise(EyeCm);
        NavCellsPerTile = config.Hpa.NavCellsPerTile;
        NavTilesPerSide = NavCellsPerSide / NavCellsPerTile;
        SetPeriod(config.FixedHz, fc.RateHz.ToDouble());

        GroupOf = BuildVisionGroups(pairRules.SharesVisionByPair, pairRules.PlayerCount, out int visionGroupCount);
        VisionGroupCount = visionGroupCount;
        int f2 = FogCellsPerSide * FogCellsPerSide;
        Visible = new byte[VisionGroupCount * f2];
        Explored = new byte[VisionGroupCount * f2];
        Occ = new byte[VisionGroupCount * f2];
        VisibleCount = new int[VisionGroupCount];
        ExploredCount = new int[VisionGroupCount];
        GhostCount = new int[VisionGroupCount];
        Version = new uint[VisionGroupCount];
        Key = new uint[VisionGroupCount];
        TileKey = new uint[VisionGroupCount];
        Seen = new uint[VisionGroupCount];
        Gone = new uint[VisionGroupCount];
        EntRev = new long[VisionGroupCount];
        Rev = new long[VisionGroupCount];
        ObsN = new ushort[VisionGroupCount * f2];
        Belief = new Dictionary<int, (int, CrowdStructureFootprint)>[VisionGroupCount];
        Tiles = new HashSet<int>[VisionGroupCount];
        for (int g = 0; g < VisionGroupCount; g++)
        {
            Belief[g] = new Dictionary<int, (int, CrowdStructureFootprint)>();
            Tiles[g] = new HashSet<int>();
        }

        double radius = fc.VisionCm.ToDouble() / FogCellSizeCm;
        int r = (int)Math.Ceiling(radius);
        // 视盘平铺(dx0, dy0, dx1, dy1, …;dy 外层 dx 内层,与参考端遍历序一致)
        var disc = new List<int>();
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy <= radius * radius)
                {
                    disc.Add(dx);
                    disc.Add(dy);
                }
            }
        }

        _disc = disc.ToArray();

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

    /// <summary>LOS 高度表(米,Fix64):开启视线遮挡时按需注入;未注入而 LOS 开 = 调用方装配缺口。
    /// 逐格 cm→米一次 DivPrecise、格内累加(网格和精确)、均值一次除法——与参考端
    /// __L31_FIX64_LOS__ 补丁的逐 op 求值树同构。</summary>
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

    /// <summary>刷新周期(tick)= max(1, round(tickHz / rateHz))。rateHz 超过步进频率报错,不再钳位。</summary>
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
        var cells = new List<int>();
        Fix64 fogCellCm = Fix64.FromInt(FogCellSizeCm);
        int x0 = Math.Max(0, (int)(a / fogCellCm).ToLong()), y0 = Math.Max(0, (int)(b / fogCellCm).ToLong());
        int x1 = Math.Min(FogCellsPerSide - 1, (int)(c / fogCellCm).ToLong()), y1 = Math.Min(FogCellsPerSide - 1, (int)(d / fogCellCm).ToLong());
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                int k = y * FogCellsPerSide + x;
                if (!_idx.TryGetValue(k, out var s)) _idx[k] = s = new HashSet<int>();
                s.Add(id);
                cells.Add(k);
            }
        }

        _cellsOf[id] = cells;
    }

    private void IndexDel(int id)
    {
        if (!_cellsOf.TryGetValue(id, out var cells)) return;
        foreach (int k in cells)
        {
            if (_idx.TryGetValue(k, out var s))
            {
                s.Remove(id);
                if (s.Count == 0) _idx.Remove(k);
            }
        }

        _cellsOf.Remove(id);
    }

    /// <summary>一个 tick:刷新本 tick 槽位的组(单位位置取 Fix64 厘米域,格号向零截断)。</summary>
    public void Update(int tick, CrowdSimSession sim)
    {
        int slot = tick % Period;
        int f2 = FogCellsPerSide * FogCellsPerSide;
        if (_obscured.Count > 0) ExpireObscured(tick);
        bool any = false;
        for (int g = slot; g < VisionGroupCount; g += Period)
        {
            Array.Clear(Occ, g * f2, f2);
            any = true;
        }

        if (!any) return;

        var units = sim.Units;
        var movement = sim.Movement;
        if (movement == null)
        {
            throw new InvalidOperationException("迷雾更新需要运动内核里的位置和玩家下标。");
        }

        var positions = movement.Positions;
        var players = movement.PlayerIdx;
        int unitCount = units.Count;
        for (int i = 0; i < unitCount; i++)
        {
            int g = GroupOf[players[i]];
            if (g % Period != slot) continue;
            var p = positions[i];
            // 双向夹紧(负坐标会写进上一组的切片)
            int fx = Math.Max(0, Math.Min(FogCellsPerSide - 1, (int)(p.X / Fix64.FromInt(FogCellSizeCm)).ToLong()));
            int fy = Math.Max(0, Math.Min(FogCellsPerSide - 1, (int)(p.Y / Fix64.FromInt(FogCellSizeCm)).ToLong()));
            Occ[g * f2 + fy * FogCellsPerSide + fx] = 1;
        }

        int refreshesBefore = Refreshes;
        for (int g = slot; g < VisionGroupCount; g += Period) Refresh(g);
        int groups = Refreshes - refreshesBefore;
        if (groups > 0) sim.Telemetry.MarkFogRefresh(groups);
    }

    private void Refresh(int g)
    {
        int f2 = FogCellsPerSide * FogCellsPerSide, o = g * f2;
        Array.Clear(Visible, o, f2);
        if (Los && _opaqueDirty) BuildOpaque();
        bool hide = _obscured.Count > 0;
        int visible = 0;
        for (int c = 0; c < f2; c++)
        {
            if (Occ[o + c] == 0) continue;
            int cx = c % FogCellsPerSide, cy = c / FogCellsPerSide;
            for (int k = 0; k < _disc.Length; k += 2)
            {
                int x = cx + _disc[k], y = cy + _disc[k + 1];
                if (x >= 0 && y >= 0 && x < FogCellsPerSide && y < FogCellsPerSide && (!Los || Sight(cx, cy, x, y)) && !(hide && ObsN[o + y * FogCellsPerSide + x] > 0))
                {
                    int cell = o + y * FogCellsPerSide + x;
                    if (Visible[cell] == 0)
                    {
                        Visible[cell] = 1;
                        visible++;
                    }

                    if (Explored[cell] == 0)
                    {
                        Explored[cell] = 1;
                        ExploredCount[g]++;
                        Explore(g, x, y);
                    }
                }
            }
        }

        VisibleCount[g] = visible;
        Reconcile(g);
        Version[g]++;
        Refreshes++;
    }

    /// <summary>含任何阻挡导航格的 fog 格不透明(真值实体变更后重建)。</summary>
    private void BuildOpaque()
    {
        Array.Clear(_opaque!);
        var blocked = Store.Blocked;
        for (int i = 0; i < NavCellsPerSide * NavCellsPerSide; i++)
        {
            if (blocked[i] != 0) _opaque![i / NavCellsPerSide / NavCellsPerFogCell * FogCellsPerSide + i % NavCellsPerSide / NavCellsPerFogCell] = 1;
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
            if (_opaque![c] != 0 || _hMaxM![c] > eye + Fix64Math.MulExact(rise, f)) return false;
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
        int f2 = FogCellsPerSide * FogCellsPerSide, o = g * f2;
        var done = _reconcileDone;
        var forget = _reconcileForget;
        done.Clear();
        forget.Clear();
        for (int c = 0; c < f2; c++)
        {
            if (Visible[o + c] != 0) ReconcileCell(g, c, done, forget);
        }

        DropUnbelieved(forget);
    }

    private void ReconcileCell(int g, int c, HashSet<int> done, List<int> forget)
    {
        if (!_idx.TryGetValue(c, out var ids)) return;
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

    // ── 面命令(引擎原语;调用方 sim.exec)。形状 = CrowdFogArea 解析结果。 ──
    // 命令只改迷雾数据;认知同步在下一 tick 的管线里拾起(beliefSync)。

    /// <summary>如同组刚看过该区域:explored + 真相对账(不点 visible)。</summary>
    public void RevealArea(int g, int[] cells)
    {
        int o = g * FogCellsPerSide * FogCellsPerSide;
        var done = _reconcileDone;
        var forget = _reconcileForget;
        done.Clear();
        forget.Clear();
        foreach (int c in cells)
        {
            if (Explored[o + c] == 0)
            {
                Explored[o + c] = 1;
                ExploredCount[g]++;
                Explore(g, c % FogCellsPerSide, c / FogCellsPerSide);
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
        _obscured.Add((g, cells, until));
        foreach (int c in cells)
        {
            int cell = o + c;
            if (Visible[cell] != 0)
            {
                Visible[cell] = 0;
                VisibleCount[g]--;
            }

            ObsN[cell]++;
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
        int o = g * FogCellsPerSide * FogCellsPerSide;
        var member = new HashSet<int>(cells);
        var ids = new HashSet<int>();
        foreach (int c in cells)
        {
            int cell = o + c;
            if (Explored[cell] != 0)
            {
                Explored[cell] = 0;
                ExploredCount[g]--;
            }

            if (_idx.TryGetValue(c, out var s)) ids.UnionWith(s);
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
                        if (Explored[o + fy * FogCellsPerSide + fx] != 0)
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
            if (!_cellsOf.TryGetValue(id, out var idCells) || !idCells.All(member.Contains)) continue;
            Belief[g].Remove(id);
            if (!Store.TryGetFootprint(id, out _)) GhostCount[g]--;
            Key[g] ^= Mix(id);
            EntRev[g]++;
            Rev[g]++;
            dropped.Add(id);
        }

        DropUnbelieved(dropped);
    }

    /// <summary>共享视野:组 b 并入 a(低组号存活,与构造的 root[max]=min 同款)。visible OR、
    /// explored OR 并触发 tile 入集、认知实体并(只有一侧见过的已拆实体留残影直到地面再见)。
    /// 被并掉的组无玩家,认知同步会把它停在真相槽。返回存活组;同组返回 -1。
    /// 拆组不支持(union-find 不能删;参考端 TODO 同款)。</summary>
    public int MergeGroups(int a, int b)
    {
        if (a == b) return -1;
        int to = Math.Min(a, b), from = Math.Max(a, b);
        int f2 = FogCellsPerSide * FogCellsPerSide, ot = to * f2, of = from * f2;
        for (int c = 0; c < f2; c++)
        {
            if (Visible[of + c] != 0 && Visible[ot + c] == 0)
            {
                Visible[ot + c] = 1;
                VisibleCount[to]++;
            }

            if (Explored[of + c] != 0 && Explored[ot + c] == 0)
            {
                Explored[ot + c] = 1;
                ExploredCount[to]++;
                Explore(to, c % FogCellsPerSide, c / FogCellsPerSide);
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
                int cell = ot + c;
                if (Visible[cell] != 0)
                {
                    Visible[cell] = 0;
                    VisibleCount[to]--;
                }
            }

            _obscured[i] = (to, e.Item2, e.Item3);
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

    /// <summary>不同认知的组数(= 规划侧需要的认知变体槽上限参考)。</summary>
    public int BeliefCount() => Enumerable.Range(0, VisionGroupCount).Select(BeliefKey).Distinct().Count();

    /// <summary>呈现查询面(W2 渲染底座只读):0 = 未探索,1 = 已探索,2 = 可见。</summary>
    public byte[] View(int g)
    {
        int f2 = FogCellsPerSide * FogCellsPerSide, o = g * f2;
        var res = new byte[f2];
        for (int c = 0; c < f2; c++) res[c] = Visible[o + c] != 0 ? (byte)2 : Explored[o + c];
        return res;
    }

    /// <summary>参考端 mix(32 位雪崩哈希;JS Math.imul = NavTilesPerSide# unchecked int 乘)。</summary>
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
