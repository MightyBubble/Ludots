using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Movement;

/// <summary>
/// 虚拟领队:(指令 × 移动类型)一个,同层各组共享引用。
/// 沿漏斗路径以编队步速行进;朝向按 leaderTurnRate 限速,并用前视转向盯住路径前方而非下一路点。前视耗尽保持朝向。
/// 距离用 CrowdFix.Hypot。朝向转角用 Fix64Math.Atan2/Cos/Sin。
/// </summary>
public sealed class CrowdLeader
{
    private Fix64Vec2[] _path;
    private int _count;
    private Fix64 _lookAheadCm;
    private readonly HashSet<int> _wall = new();

    /// <summary>路径缓冲在构造时一次留够。活前缀是 <see cref="PathCount"/>，尾部不参与步进。</summary>
    internal CrowdLeader(int pathCapacity)
    {
        _path = new Fix64Vec2[pathCapacity];
        PaceCmPerSecond = Fix64.OneValue;
        Nav = null!;
    }

    /// <summary>复用同一只领队。路径点数超过缓冲时把缓冲加长，调用方应在预热里走过最长路径。</summary>
    internal void Reset(
        IReadOnlyList<Fix64Vec2> pathCm, int layerIdx, NavContext nav,
        bool fixedHeading, Fix64Vec2? face, Fix64 lookAheadCm, Fix64 paceCmPerSecond)
    {
        if (paceCmPerSecond <= Fix64.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(paceCmPerSecond), "领队步伐必须 > 0。");
        }

        if (pathCm.Count < 2) throw new ArgumentException("领队路径至少两个点。", nameof(pathCm));
        if (pathCm.Count > _path.Length)
        {
            _path = new Fix64Vec2[Math.Max(pathCm.Count, _path.Length * 2)];
        }

        _count = pathCm.Count;
        for (int i = 0; i < _count; i++) _path[i] = pathCm[i];
        _wall.Clear();
        PaceCmPerSecond = paceCmPerSecond;
        LayerIdx = layerIdx;
        Nav = nav;
        Fixed = fixedHeading;
        Face = face;
        _lookAheadCm = lookAheadCm;
        XCm = _path[0].X;
        YCm = _path[0].Y;
        Seg = 1;
        Done = false;
        Mirror = false;
        Vx = Fix64.Zero;
        Vy = Fix64.Zero;
        Hx = Fix64.OneValue;
        Hy = Fix64.Zero;
        if (_count >= 2 && !fixedHeading)
        {
            var heading = InitialHeading();
            Hx = heading.X;
            Hy = heading.Y;
        }
    }

    public int LayerIdx { get; private set; }
    /// <summary>同组已生成单位的模板速度最小值（厘米/秒）。</summary>
    public Fix64 PaceCmPerSecond { get; private set; }
    public NavContext Nav { get; internal set; }
    public bool Fixed { get; private set; }
    public Fix64Vec2? Face { get; private set; }
    public Fix64 XCm { get; private set; }
    public Fix64 YCm { get; private set; }
    /// <summary>领队速度(前馈给槽位跟踪;厘米/秒)。</summary>
    public Fix64 Vx { get; private set; } = Fix64.Zero;
    public Fix64 Vy { get; private set; } = Fix64.Zero;
    /// <summary>朝向(单位向量)。</summary>
    public Fix64 Hx { get; private set; }
    public Fix64 Hy { get; private set; }
    public int Seg { get; private set; }
    public bool Done { get; private set; }
    /// <summary>反转后原地掉头:行序镜像,旧前排不必穿过队伍再到前面。</summary>
    public bool Mirror { get; private set; }
    internal int PathCount => _count;
    internal Fix64Vec2 PathAt(int index) => _path[index];
    internal bool IsWall(int cell) => _wall.Contains(cell);

    /// <summary>
    /// 规划时路线已贴靠或穿越的不可通行格（墙角、跳跃链接）记为基线，阻挡判定只认基线之外的新阻挡；
    /// 否则任何无关的结构变化都会让沿墙角拉直的路线被判为受阻而重新规划。路径定稿（起点覆写）之后调用。
    /// </summary>
    internal void RecordWalls(NavContext nav, int navCellSizeCm, int navCellsPerSide)
    {
        _wall.Clear();
        Fix64 step = Fix64.FromInt(navCellSizeCm) / 2, e = Fix64.FromInt(navCellSizeCm) / 4;
        var pass = nav.Passable;
        var up = nav.UpPass;
        for (int k = 1; k < _count; k++)
        {
            Fix64 ax = _path[k - 1].X, ay = _path[k - 1].Y, bx = _path[k].X, by = _path[k].Y;
            int m = Math.Max(1, (int)Fix64.Ceiling(CrowdFix.Hypot(bx - ax, by - ay) / step).ToLong());
            for (int s = 0; s <= m; s++)
            {
                Fix64 x = ax + (bx - ax) * s / m, y = ay + (by - ay) * s / m;
                AddWall(pass, up, CrowdSimulationSpace.CellAt(x - e, y - e, navCellSizeCm, navCellsPerSide));
                AddWall(pass, up, CrowdSimulationSpace.CellAt(x + e, y - e, navCellSizeCm, navCellsPerSide));
                AddWall(pass, up, CrowdSimulationSpace.CellAt(x - e, y + e, navCellSizeCm, navCellsPerSide));
                AddWall(pass, up, CrowdSimulationSpace.CellAt(x + e, y + e, navCellSizeCm, navCellsPerSide));
            }
        }
    }

    private void AddWall(byte[] pass, byte[] up, int cell)
    {
        if (pass[cell] == 0 && up[cell] == 0) _wall.Add(cell);
    }

    /// <summary>起点覆写到当前质心(请求时刻的起点格只作 8 邻域参照)。</summary>
    public void SetStart(Fix64 xCm, Fix64 yCm)
    {
        _path[0] = new Fix64Vec2(xCm, yCm);
        XCm = xCm;
        YCm = yCm;
    }

    /// <summary>
    /// 继承上一任领队(反转/同向):方向点积 &lt; mirrorFlipDot 的反转令 mirror 翻转并继承;
    /// 近似同向(点积 &gt; headingInheritDot)且非固定朝向时继承旧朝向,避免整队绕领队自拧。
    /// </summary>
    public void InheritFrom(CrowdLeader prev, Fix64 mirrorFlipDot, Fix64 headingInheritDot)
    {
        Fix64 dot = prev.Hx * Hx + prev.Hy * Hy;
        Mirror = prev.Mirror != (dot < mirrorFlipDot);
        if (!Fixed && dot > headingInheritDot)
        {
            Hx = prev.Hx;
            Hy = prev.Hy;
        }
    }

    private static readonly Fix64 Eps = Fix64.FromFloat(1e-6f);

    private Fix64Vec2 InitialHeading()
    {
        if (_count >= 3)
        {
            var a = AimPoint(1, _path[0].X, _path[0].Y, _lookAheadCm);
            if (a.Full)
            {
                Fix64 dx = a.X - _path[0].X, dy = a.Y - _path[0].Y;
                Fix64 l = CrowdFix.Hypot(dx, dy);
                if (l > Eps) return new Fix64Vec2(dx / l, dy / l);
            }
        }

        if (_count >= 2)
        {
            Fix64 dx = _path[1].X - _path[0].X, dy = _path[1].Y - _path[0].Y;
            Fix64 l = CrowdFix.Hypot(dx, dy);
            if (l > Eps) return new Fix64Vec2(dx / l, dy / l);
        }

        return new Fix64Vec2(Fix64.OneValue, Fix64.Zero);
    }

    /// <summary>路径上从 seg 段起、距 (x,y) look 厘米处的瞄准点;Full=false 表示路径提前耗尽(调用方保持朝向)。</summary>
    private (Fix64 X, Fix64 Y, bool Full) AimPoint(int seg, Fix64 x, Fix64 y, Fix64 look)
    {
        Fix64 dx = _path[seg].X - x, dy = _path[seg].Y - y;
        Fix64 d = CrowdFix.Hypot(dx, dy);
        if (d >= look)
        {
            Fix64 t = look / d;
            return (x + dx * t, y + dy * t, true);
        }

        if (seg + 1 >= _count) return (_path[seg].X, _path[seg].Y, false);
        Fix64 rem = look - d;
        Fix64 ax = _path[seg].X, ay = _path[seg].Y;
        for (int k = seg + 1; k < _count; k++)
        {
            Fix64 nx = _path[k].X, ny = _path[k].Y;
            Fix64 sx = nx - ax, sy = ny - ay;
            Fix64 sl = CrowdFix.Hypot(sx, sy);
            if (sl <= Fix64.Zero) { ax = nx; ay = ny; continue; }
            if (sl >= rem) return (ax + (sx / sl) * rem, ay + (sy / sl) * rem, true);
            ax = nx;
            ay = ny;
            rem -= sl;
        }

        return (ax, ay, false);
    }

    /// <summary>步进一个子步(dt 秒;pace = leaderSpeed;turnK = min(1, dt×leaderTurnRate);faceStep = 拖线朝向的转角步长)。</summary>
    public void Step(Fix64 speedCmPerSecond, Fix64 pace, Fix64 turnK, Fix64 faceStep, Fix64 dt, int navCellSizeCm, int navCellsPerSide)
    {
        if (Done)
        {
            Vx = Fix64.Zero;
            Vy = Fix64.Zero;
            return;
        }

        int n = _count;
        Fix64 ox = XCm, oy = YCm;
        // 桥面层:地面不可走但桥面可走时,步速吃桥面代价而不是水下代价
        int cell = CrowdSimulationSpace.CellAt(XCm, YCm, navCellSizeCm, navCellsPerSide);
        Fix64 div = Nav.Passable[cell] != 0 ? Nav.SpeedDivisor[cell] : Nav.UpPass[cell] != 0 ? Nav.UpSpeedDivisor[cell] : Fix64.OneValue;
        Fix64 rem = (speedCmPerSecond / (div == Fix64.Zero ? Fix64.OneValue : div)) * pace * dt;
        while (rem > Fix64.Zero && Seg < n)
        {
            Fix64 tx = _path[Seg].X - XCm, ty = _path[Seg].Y - YCm;
            Fix64 d = CrowdFix.Hypot(tx, ty);
            if (d > Eps && !Fixed)
            {
                var aim = AimPoint(Seg, XCm, YCm, _lookAheadCm);
                if (aim.Full)
                {
                    Fix64 rdx = aim.X - XCm, rdy = aim.Y - YCm;
                    Fix64 rl = CrowdFix.Hypot(rdx, rdy);
                    if (rl > Eps)
                    {
                        Fix64 hx = Hx + (rdx / rl - Hx) * turnK;
                        Fix64 hy = Hy + (rdy / rl - Hy) * turnK;
                        Fix64 hl = CrowdFix.Hypot(hx, hy);
                        if (hl > Eps)
                        {
                            Hx = hx / hl;
                            Hy = hy / hl;
                        }
                    }
                }
                // 前视耗尽(距终点不足 lookAhead):保持朝向——末端拐角不拖转整队
            }

            if (d <= rem)
            {
                XCm = _path[Seg].X;
                YCm = _path[Seg].Y;
                rem -= d;
                Seg++;
            }
            else
            {
                XCm += (tx / d) * rem;
                YCm += (ty / d) * rem;
                rem = Fix64.Zero;
            }
        }

        Vx = (XCm - ox) / dt;
        Vy = (YCm - oy) / dt;
        if (Seg < n) return;
        if (Face is not { } f)
        {
            Done = true;
            return;
        }

        // 拖线指令:原地转向到指定朝向后才报完成
        Fix64 ang = Fix64Math.Atan2(Hx * f.Y - Hy * f.X, Hx * f.X + Hy * f.Y);
        if (Fix64.Abs(ang) <= faceStep)
        {
            Hx = f.X;
            Hy = f.Y;
            Done = true;
            return;
        }

        Fix64 a = (ang > Fix64.Zero ? faceStep : -faceStep);
        Fix64 c = Fix64Math.Cos(a), s = Fix64Math.Sin(a);
        Fix64 hx0 = Hx;
        Hx = hx0 * c - Hy * s;
        Hy = hx0 * s + Hy * c;
    }
}
