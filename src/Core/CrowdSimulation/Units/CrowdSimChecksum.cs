using System;
using System.Collections.Generic;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// 一条 Mix 上的四个合计断面，外加三条独立的诊断链。
/// 后一合计断面从前一断面的结果继续混。<see cref="Total"/> 等于 <see cref="Avoidance"/>。
/// <see cref="Groups"/>、<see cref="Jump"/>、<see cref="Commands"/> 各自起混，不写进合计。
/// 诊断开关关闭时这三个词为 0。
/// </summary>
public readonly struct CrowdSimStateDigest
{
    public readonly int Tick;
    public readonly ulong Position;
    public readonly ulong Motion;
    public readonly ulong State;
    public readonly ulong Avoidance;
    public readonly ulong Groups;
    public readonly ulong Jump;
    public readonly ulong Commands;

    public CrowdSimStateDigest(
        int tick, ulong position, ulong motion, ulong state, ulong avoidance,
        ulong groups = 0, ulong jump = 0, ulong commands = 0)
    {
        Tick = tick;
        Position = position;
        Motion = motion;
        State = state;
        Avoidance = avoidance;
        Groups = groups;
        Jump = jump;
        Commands = commands;
    }

    public ulong Total => Avoidance;
}

/// <summary>诊断断面掩码。只标记 groups / jump / commands，不含合计断面。</summary>
[Flags]
public enum CrowdSimDiagnosticSection : byte
{
    None = 0,
    Groups = 1,
    Jump = 2,
    Commands = 4,
}

/// <summary>
/// 两串逐 tick 摘要的分歧。合计与诊断分开报。
/// <see cref="Diverged"/> 只表示合计断面不同，或共同前缀的合计一致但条数不同。
/// 只有诊断断面不同时 <see cref="Diverged"/> 为 false，<see cref="Tick"/> 为 -1。
/// <see cref="DiagnosticTick"/> 为 -1 表示诊断断面没有差。
/// </summary>
public readonly struct CrowdSimDigestDiff
{
    public readonly bool Diverged;
    public readonly int Tick;
    public readonly string Section;
    public readonly int DiagnosticTick;
    public readonly CrowdSimDiagnosticSection Diagnostic;

    public CrowdSimDigestDiff(
        bool diverged, int tick, string section,
        int diagnosticTick = -1, CrowdSimDiagnosticSection diagnostic = default)
    {
        Diverged = diverged;
        Tick = tick;
        Section = section;
        DiagnosticTick = diagnosticTick;
        Diagnostic = diagnostic;
    }

    public static CrowdSimDigestDiff None => new(false, -1, "");
}

/// <summary>
/// 规范校验码（FNV-1a 32，Fix64 原始值）。合计词序：tick、单位数、x/y、vx/vy、slotX/slotY、
/// blend、stall、state、group、句柄、order、mode、level，再分离两轴、calm、相位。
/// 每个定点字段按原始 int64 的低 32 位再高 32 位混合。
/// 有运动栈的会话在 tick 末从内核 SoA 读(scatter 后同值)；纯部署会话读组件。两条路径值等价——SoA 即 scatter 落回前的组件值。
/// 诊断链（groups / jump / commands）用各自的域词起混，不接这条合计。默认不算。
/// 算出的诊断词若为 0，会和开关关闭撞车，这里把 0 挪到 32 位范围外，避免和算出的 1 相撞。
/// </summary>
public static class CrowdSimChecksum
{
    public const string PositionSection = "position";
    public const string MotionSection = "motion";
    public const string StateSection = "state";
    public const string AvoidanceSection = "avoidance";
    public const string GroupsSection = "groups";
    public const string JumpSection = "jump";
    public const string CommandsSection = "commands";

    /// <summary>捕获和回放用的句柄断面。不进 <see cref="Compute"/> 的合计。</summary>
    public const string HandlesSection = "units.handles";

    private const uint Offset = 2166136261u, Prime = 16777619u;
    private const uint GroupsDomain = 0x67727073u;
    private const uint JumpDomain = 0x6A756D70u;
    private const uint CommandsDomain = 0x636D6473u;
    private const uint HandlesDomain = 0x68646C73u;

    public static ulong ComputeValue(CrowdSimSession sim) => Compute(sim).Total;

    public static CrowdSimStateDigest Compute(CrowdSimSession sim)
    {
        var units = sim.Units;
        int n = units.Count;
        var movement = sim.Movement;
        uint h = Mix(Mix(Offset, (uint)sim.TickCount), (uint)n);

        // 位置。Fix64 原始值先低 32 位再高 32 位。
        for (int i = 0; i < n; i++)
        {
            var pos = Position(sim, movement, i);
            h = Mix(h, (uint)pos.X.RawValue);
            h = Mix(h, (uint)(pos.X.RawValue >> 32));
        }

        for (int i = 0; i < n; i++)
        {
            var pos = Position(sim, movement, i);
            h = Mix(h, (uint)pos.Y.RawValue);
            h = Mix(h, (uint)(pos.Y.RawValue >> 32));
        }

        ulong position = h;

        // 运动字段顺序：vx、vy、slotX、slotY、blend、stall。
        // 每个定点字段按原始 int64 低 32 位再高 32 位混入。
        for (int k = 0; k < 6; k++)
        {
            for (int i = 0; i < n; i++)
            {
                long raw = MotionRaw(sim, movement, i, k);
                h = Mix(h, (uint)raw);
                h = Mix(h, (uint)(raw >> 32));
            }
        }

        ulong motion = h;

        for (int i = 0; i < n; i++) h = Mix(h, State(sim, movement, i).State);
        for (int i = 0; i < n; i++) h = Mix(h, (uint)State(sim, movement, i).GroupId);
        for (int i = 0; i < n; i++) h = Mix(h, units.HandleAt(i));
        for (int i = 0; i < n; i++) h = Mix(h, State(sim, movement, i).Order);
        for (int i = 0; i < n; i++) h = Mix(h, State(sim, movement, i).Mode);
        for (int i = 0; i < n; i++) h = Mix(h, State(sim, movement, i).Level);
        ulong state = h;

        for (int i = 0; i < n; i++)
        {
            long sx = movement != null ? movement.Separation[i].X.RawValue : 0;
            h = Mix(h, (uint)sx);
            h = Mix(h, (uint)(sx >> 32));
        }

        for (int i = 0; i < n; i++)
        {
            long sy = movement != null ? movement.Separation[i].Y.RawValue : 0;
            h = Mix(h, (uint)sy);
            h = Mix(h, (uint)(sy >> 32));
        }

        for (int i = 0; i < n; i++)
        {
            long calm = movement != null ? movement.Calm[i] : 0;
            h = Mix(h, (uint)calm);
            h = Mix(h, (uint)(calm >> 32));
        }

        h = Mix(h, (uint)(movement?.Phase ?? 0));
        ulong avoidance = h;
        ulong groups = 0, jump = 0, commands = 0;
        if (sim.DiagnosticDigest)
        {
            groups = Seal(MixGroups(sim));
            jump = Seal(MixJump(sim, movement, n));
            commands = Seal(MixCommands(sim));
        }

        return new CrowdSimStateDigest(sim.TickCount, position, motion, state, avoidance, groups, jump, commands);
    }

    /// <summary>
    /// 按 tick 对齐两串摘要，同时给出合计的第一处分歧和诊断的第一处分歧。
    /// 合计断面按 position、motion、state、avoidance 取第一个。
    /// 诊断掩码是该诊断 tick 上有差的 groups / jump / commands。
    /// 共同前缀的合计一致但条数不同时，合计 Tick 为 -2，Section 为空。
    /// </summary>
    public static CrowdSimDigestDiff DiffDigest(
        IReadOnlyList<CrowdSimStateDigest> left,
        IReadOnlyList<CrowdSimStateDigest> right)
    {
        int common = Math.Min(left.Count, right.Count);
        bool chained = false;
        int chainedTick = -1;
        string section = "";
        bool diagnostic = false;
        int diagnosticTick = -1;
        var mask = CrowdSimDiagnosticSection.None;
        for (int i = 0; i < common; i++)
        {
            bool chainedHere = TryFirstSection(left[i], right[i], out string name, out CrowdSimDiagnosticSection flags);
            if (!chained && chainedHere)
            {
                chained = true;
                chainedTick = left[i].Tick;
                section = name;
            }

            if (!diagnostic && flags != CrowdSimDiagnosticSection.None)
            {
                diagnostic = true;
                diagnosticTick = left[i].Tick;
                mask = flags;
            }

            if (chained && diagnostic) break;
        }

        if (!chained && left.Count != right.Count)
            return new CrowdSimDigestDiff(true, -2, "", diagnostic ? diagnosticTick : -1, mask);
        if (!chained && !diagnostic) return CrowdSimDigestDiff.None;
        return new CrowdSimDigestDiff(chained, chained ? chainedTick : -1, section, diagnostic ? diagnosticTick : -1, mask);
    }

    /// <summary>
    /// 合计断面有差时返回 true，<paramref name="section"/> 是 position、motion、state、avoidance 的第一个。
    /// <paramref name="diagnostic"/> 是这一对摘要上有差的诊断断面；只有诊断有差时返回 false。
    /// </summary>
    public static bool TryFirstSection(
        in CrowdSimStateDigest left,
        in CrowdSimStateDigest right,
        out string section,
        out CrowdSimDiagnosticSection diagnostic)
    {
        diagnostic = CrowdSimDiagnosticSection.None;
        if (left.Groups != right.Groups) diagnostic |= CrowdSimDiagnosticSection.Groups;
        if (left.Jump != right.Jump) diagnostic |= CrowdSimDiagnosticSection.Jump;
        if (left.Commands != right.Commands) diagnostic |= CrowdSimDiagnosticSection.Commands;

        if (left.Position != right.Position) { section = PositionSection; return true; }
        if (left.Motion != right.Motion) { section = MotionSection; return true; }
        if (left.State != right.State) { section = StateSection; return true; }
        if (left.Avoidance != right.Avoidance) { section = AvoidanceSection; return true; }
        section = "";
        return false;
    }

    /// <summary>诊断掩码的短名。空掩码返回空串。返回的都是字面量。</summary>
    public static string FormatDiagnostic(CrowdSimDiagnosticSection flags)
    {
        bool groups = (flags & CrowdSimDiagnosticSection.Groups) != 0;
        bool jump = (flags & CrowdSimDiagnosticSection.Jump) != 0;
        bool commands = (flags & CrowdSimDiagnosticSection.Commands) != 0;
        if (groups && jump && commands) return "groups,jump,commands";
        if (groups && jump) return "groups,jump";
        if (groups && commands) return "groups,commands";
        if (jump && commands) return "jump,commands";
        if (groups) return GroupsSection;
        if (jump) return JumpSection;
        if (commands) return CommandsSection;
        return "";
    }

    /// <summary>展示/对拍把合计打成 8 位十六进制。</summary>
    public static string Format(ulong checksum) => checksum.ToString("x8");

    /// <summary>
    /// 句柄池断面：稠密序上的句柄和实体，再加上空闲栈。
    /// 不写进 <see cref="Compute"/>，部署真值的每 tick 合计保持原词序。
    /// </summary>
    public static ulong MixHandles(CrowdSimSession sim)
    {
        var units = sim.Units;
        int n = units.Count;
        uint h = Mix(HandlesDomain, (uint)n);
        for (int i = 0; i < n; i++)
        {
            var entity = units.EntityAt(i);
            h = Mix(h, units.HandleAt(i));
            h = Mix(h, (uint)entity.Id);
            h = Mix(h, (uint)entity.Version);
        }

        h = Mix(h, (uint)units.Capacity);
        h = Mix(h, (uint)units.FreeTop);
        for (int i = 0; i < units.FreeTop; i++) h = Mix(h, (uint)units.FreeSlotAt(i));
        return Seal(h);
    }

    /// <summary>按固定词序收集有差的分区。调用方的列表只追加，不清空。</summary>
    public static void CollectDifferences(
        in CrowdSimStateDigest left,
        ulong leftHandles,
        in CrowdSimStateDigest right,
        ulong rightHandles,
        List<string> names)
    {
        if (left.Position != right.Position) names.Add(PositionSection);
        if (left.Motion != right.Motion) names.Add(MotionSection);
        if (left.State != right.State) names.Add(StateSection);
        if (left.Avoidance != right.Avoidance) names.Add(AvoidanceSection);
        if (left.Groups != right.Groups) names.Add(GroupsSection);
        if (left.Jump != right.Jump) names.Add(JumpSection);
        if (left.Commands != right.Commands) names.Add(CommandsSection);
        if (leftHandles != rightHandles) names.Add(HandlesSection);
    }

    internal static uint Mix(uint h, uint v) => (h ^ v) * Prime;

    internal static ulong Seal(uint h) => h == 0 ? 1ul << 32 : h;

    internal static uint MixI64(uint h, long raw) => Mix(Mix(h, (uint)raw), (uint)(raw >> 32));

    private static uint MixGroups(CrowdSimSession sim)
    {
        uint h = MixGroupRows(sim);
        h = MixOrderBook(h, sim.Orders);
        h = sim.Planner != null ? sim.Planner.MixInFlight(h) : Mix(h, 0);
        return h;
    }

    internal static uint MixGroupRows(CrowdSimSession sim)
    {
        var groups = sim.Groups.Groups;
        uint h = Mix(GroupsDomain, (uint)groups.Count);
        for (int i = 0; i < groups.Count; i++)
        {
            CrowdNavGroupSet.Group? g = groups[i];
            if (g == null)
            {
                h = Mix(h, 0);
                continue;
            }

            h = Mix(h, 1);
            h = Mix(h, (uint)g.Id);
            h = Mix(h, (uint)g.OrderId);
            h = Mix(h, (uint)g.Goal);
            h = Mix(h, (uint)g.GoalComp);
            h = Mix(h, g.Planning ? 1u : 0u);
            h = Mix(h, (uint)g.StateSeq);
            h = Mix(h, g.LosAll ? 1u : 0u);
            h = MixLeader(h, g.Leader);
            h = MixLeader(h, g.PrevLeader);
            h = MixFlow(h, g.Flow);
        }

        return h;
    }

    private static uint MixLeader(uint h, CrowdLeader? leader)
    {
        if (leader == null) return Mix(h, 0);
        h = Mix(h, 1);
        h = MixI64(h, leader.XCm.RawValue);
        h = MixI64(h, leader.YCm.RawValue);
        h = MixI64(h, leader.Vx.RawValue);
        h = MixI64(h, leader.Vy.RawValue);
        h = MixI64(h, leader.Hx.RawValue);
        h = MixI64(h, leader.Hy.RawValue);
        h = Mix(h, (uint)leader.PathCount);
        for (int i = 0; i < leader.PathCount; i++)
        {
            Fix64Vec2 point = leader.PathAt(i);
            h = MixI64(h, point.X.RawValue);
            h = MixI64(h, point.Y.RawValue);
        }

        h = Mix(h, (uint)leader.Seg);
        h = Mix(h, leader.Done ? 1u : 0u);
        return h;
    }

    private static uint MixFlow(uint h, FlowField? flow)
    {
        if (flow == null) return Mix(h, 0);
        h = Mix(h, 1);
        h = Mix(h, (uint)flow.BuildRevision);
        h = Mix(h, (uint)flow.NavId);
        h = Mix(h, (uint)flow.Goal);
        h = Mix(h, (uint)flow.Reached);
        if (flow.Len != null && (uint)flow.Goal < (uint)flow.Len.Length)
            h = MixI64(h, flow.Len[flow.Goal].RawValue);
        return h;
    }

    internal static uint MixOrderBook(uint h, CrowdOrderBook book)
    {
        var list = book.List;
        h = Mix(h, (uint)list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            CrowdOrder order = list[i];
            h = Mix(h, (uint)order.Id);
            h = Mix(h, (uint)order.Player);
            h = Mix(h, (uint)order.Mode);
            h = Mix(h, (uint)order.ClickCell);
            h = Mix(h, (uint)order.ReachCells);
            CrowdOrderDedupKey key = order.DedupKey;
            h = MixString(h, key.ShapeId);
            h = Mix(h, key.AutoHasValue ? 1u : 0u);
            h = Mix(h, key.Auto ? 1u : 0u);
            h = Mix(h, key.HasFace ? 1u : 0u);
            h = MixI64(h, key.FaceXRaw);
            h = MixI64(h, key.FaceYRaw);
            h = MixI64(h, key.WidthRaw);
            h = Mix(h, (uint)order.Groups.Count);
            for (int g = 0; g < order.Groups.Count; g++)
            {
                h = Mix(h, (uint)order.Groups[g].GroupId);
                h = Mix(h, (uint)order.Groups[g].GoalCell);
            }
        }

        return h;
    }

    internal static uint MixJump(CrowdSimSession sim, CrowdMovementKernel? movement, int n)
    {
        uint h = Mix(JumpDomain, (uint)n);
        for (int i = 0; i < n; i++)
        {
            CrowdSimulationKinematics kin;
            if (movement != null)
            {
                kin = movement.Kins[i];
            }
            else
            {
                var entity = sim.Units.EntityAt(i);
                if (!sim.World.Has<CrowdSimulationKinematics>(entity))
                {
                    h = MixI64(h, 0);
                    h = MixI64(h, 0);
                    h = MixI64(h, 0);
                    h = MixI64(h, 0);
                    h = MixI64(h, 0);
                    h = MixI64(h, 0);
                    continue;
                }

                kin = sim.World.Get<CrowdSimulationKinematics>(entity);
            }

            h = MixI64(h, kin.RestCm.X.RawValue);
            h = MixI64(h, kin.RestCm.Y.RawValue);
            h = MixI64(h, kin.JumpToCm.X.RawValue);
            h = MixI64(h, kin.JumpToCm.Y.RawValue);
            h = MixI64(h, kin.JumpT.RawValue);
            h = MixI64(h, kin.JumpLengthCm.RawValue);
        }

        return h;
    }

    internal static uint MixCommands(CrowdSimSession sim)
    {
        var queue = sim.Commands;
        int n = queue.PendingCount;
        uint h = Mix(CommandsDomain, (uint)n);
        for (int i = 0; i < n; i++)
        {
            var entry = queue.PendingAt(i);
            h = Mix(h, (uint)entry.Tick);
            h = MixCommand(h, entry.Cmd);
        }

        return h;
    }

    private static uint MixCommand(uint h, CrowdSimCommand cmd)
    {
        h = Mix(h, (uint)cmd.Kind);
        h = Mix(h, (uint)cmd.Player);
        h = Mix(h, (uint)cmd.With);
        h = Mix(h, (uint)cmd.Count);
        h = Mix(h, (uint)cmd.UnitType);
        h = Mix(h, (uint)cmd.RIdx);
        h = Mix(h, (uint)cmd.XCm);
        h = Mix(h, (uint)cmd.YCm);
        uint[]? members = cmd.Members;
        if (members == null)
        {
            h = Mix(h, uint.MaxValue);
        }
        else
        {
            h = Mix(h, (uint)members.Length);
            for (int i = 0; i < members.Length; i++) h = Mix(h, members[i]);
        }

        h = Mix(h, (uint)cmd.WidthCm);
        h = Mix(h, (uint)cmd.Ticks);
        h = Mix(h, (uint)cmd.SizeCm);
        h = Mix(h, (uint)cmd.ToXCm);
        h = Mix(h, (uint)cmd.ToYCm);
        h = Mix(h, cmd.On ? 1u : 0u);
        h = Mix(h, cmd.HasAuto ? 1u : 0u);
        h = Mix(h, cmd.Auto ? 1u : 0u);
        h = Mix(h, cmd.HasFace ? 1u : 0u);
        h = MixI64(h, cmd.FaceX.RawValue);
        h = MixI64(h, cmd.FaceY.RawValue);
        h = Mix(h, cmd.HasFogTerrain ? 1u : 0u);
        h = Mix(h, cmd.FogTerrain ? 1u : 0u);
        h = MixString(h, cmd.ShapeId);
        h = MixString(h, cmd.TemplateId);
        h = Mix(h, (uint)cmd.RadiusCm);
        h = MixI64(h, cmd.RingScale.RawValue);
        h = MixI64(h, cmd.Impulse.RawValue);
        h = Mix(h, (uint)cmd.TickHz);
        h = MixI64(h, cmd.AvoidHz.RawValue);
        h = Mix(h, (uint)cmd.Slices);
        return MixArea(h, cmd.Area);
    }

    private static uint MixArea(uint h, CrowdFogShape? area)
    {
        if (area == null) return Mix(h, 0);
        h = Mix(h, 1);
        h = MixDoubles(h, area.Rect);
        h = MixDoubles(h, area.Circle);
        if (area.Poly == null) return Mix(h, 0);
        h = Mix(h, (uint)area.Poly.Length);
        for (int i = 0; i < area.Poly.Length; i++) h = MixDoubles(h, area.Poly[i]);
        return h;
    }

    private static uint MixDoubles(uint h, double[]? values)
    {
        if (values == null) return Mix(h, 0);
        h = Mix(h, (uint)values.Length);
        for (int i = 0; i < values.Length; i++)
            h = MixI64(h, BitConverter.DoubleToInt64Bits(values[i]));
        return h;
    }

    private static uint MixString(uint h, string? text)
    {
        if (text == null) return Mix(h, uint.MaxValue);
        h = Mix(h, (uint)text.Length);
        for (int i = 0; i < text.Length; i++) h = Mix(h, text[i]);
        return h;
    }

    internal static Fix64Vec2 Position(CrowdSimSession sim, CrowdMovementKernel? movement, int dense) =>
        movement != null ? movement.Positions[dense]
            : sim.World.Get<WorldPositionCm>(sim.Units.EntityAt(dense)).Value;

    internal static CrowdSimulationUnitState State(CrowdSimSession sim, CrowdMovementKernel? movement, int dense) =>
        movement != null ? movement.States[dense]
            : sim.World.Get<CrowdSimulationUnitState>(sim.Units.EntityAt(dense));

    internal static long MotionRaw(CrowdSimSession sim, CrowdMovementKernel? movement, int dense, int field)
    {
        if (movement != null)
        {
            ref var kin = ref movement.Kins[dense];
            return field switch
            {
                0 => kin.Velocity.X.RawValue,
                1 => kin.Velocity.Y.RawValue,
                2 => kin.SlotOffsetCm.X.RawValue,
                3 => kin.SlotOffsetCm.Y.RawValue,
                4 => kin.Blend.RawValue,
                _ => kin.StallSeconds.RawValue,
            };
        }

        var entity = sim.Units.EntityAt(dense);
        if (!sim.World.Has<CrowdSimulationKinematics>(entity)) return 0;
        var component = sim.World.Get<CrowdSimulationKinematics>(entity);
        return field switch
        {
            0 => component.Velocity.X.RawValue,
            1 => component.Velocity.Y.RawValue,
            2 => component.SlotOffsetCm.X.RawValue,
            3 => component.SlotOffsetCm.Y.RawValue,
            4 => component.Blend.RawValue,
            _ => component.StallSeconds.RawValue,
        };
    }
}
