using System.Collections.Generic;
using System.Globalization;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// 指令集:spawn、spawnAt、order(成员句柄在入队时钉死)、迷雾面、结构面、
/// kill、scatter、rates、rebakeSlices、clearCaches。
/// 指令是 struct 数据,只在 tick 边界执行。仿真频率就是 FixedHz,不存在第二个时钟。
/// </summary>
public static class CrowdSimCommands
{
    /// <summary>指令玩家号门禁:功能位矩阵按玩家号直查(1..P),表外 id 不允许进求解器。
    /// 主门在入队时(Validate);Exec 内的调用是防御断言,兜住绕过队列直呼 Exec 的路径。</summary>
    private static void RequirePlayer(CrowdSimSession sim, int player)
    {
        int count = sim.Config.PairRules.PlayerCount;
        if (player < 1 || player > count)
        {
            throw new System.InvalidOperationException(
                $"指令 player = {player},有效范围 1..{count}(功能位矩阵按玩家号直查,表外 id 不允许)。");
        }
    }

    /// <summary>入队校验:坏指令在进日志/队列之前拒绝——拖到 Exec 时刻才抛,坏指令已入
    /// 日志再停摆,live 与回放不对称。会话域两面:玩家号表外(1..P);迷雾面指令与
    /// order.fogTerrain 在迷雾未启用(会话无 Fog)的会话上。载荷结构面(字段缺失/类型/
    /// area 恰一/obscure ticks 值)由边界 CrowdSimCommand.Parse 一次成形,不在这里。</summary>
    public static void Validate(CrowdSimSession sim, CrowdSimCommand cmd)
    {
        if (cmd.Kind is CrowdSimCommandKind.FogSight or CrowdSimCommandKind.Reveal
            or CrowdSimCommandKind.Obscure or CrowdSimCommandKind.Forget or CrowdSimCommandKind.FogShare)
        {
            RequireFog(sim, CrowdSimCommand.KindName(cmd.Kind));
        }

        switch (cmd.Kind)
        {
            case CrowdSimCommandKind.Kill:
                if (cmd.RadiusCm <= 0)
                    throw new System.InvalidOperationException("kill: radiusCm 必须 > 0。");
                if (cmd.RingScale <= Fix64.OneValue)
                    throw new System.InvalidOperationException("kill: ringScale 必须 > 1。");
                if (cmd.Impulse < Fix64.Zero)
                    throw new System.InvalidOperationException("kill: impulse 必须 ≥ 0。");
                break;
            case CrowdSimCommandKind.Rates:
                RequireMovement(sim, "rates");
                if (cmd.TickHz != sim.Config.FixedHz)
                {
                    throw new System.InvalidOperationException(
                        $"rates: tickHz = {cmd.TickHz}，会话时钟是 FixedHz {sim.Config.FixedHz}。仿真步长由 Engine/clock.json 固定，这条指令不能改 tick 频率。");
                }

                double avoidHz = cmd.AvoidHz.ToDouble();
                int stepHz = sim.Config.FixedHz * sim.SubSteps;
                if (!(avoidHz > 0d) || avoidHz > stepHz)
                {
                    throw new System.InvalidOperationException(
                        $"rates: avoidHz = {avoidHz.ToString(CultureInfo.InvariantCulture)}，需为 > 0 且不超过步进频率 {stepHz} Hz。");
                }

                break;
            case CrowdSimCommandKind.RebakeSlices:
                if (cmd.Slices < 1 || cmd.Slices > CrowdSimulationConfigValidator.MaxRebakeSlices)
                {
                    throw new System.InvalidOperationException(
                        $"rebakeSlices: slices = {cmd.Slices}，需为 1..{CrowdSimulationConfigValidator.MaxRebakeSlices}。");
                }

                break;
            case CrowdSimCommandKind.ClearCaches:
                if (sim.Planner == null)
                    throw new System.InvalidOperationException("clearCaches: 运动内核未启用。");
                break;
        }

        if (cmd.Kind is not (CrowdSimCommandKind.SpawnAt
            or CrowdSimCommandKind.Order or CrowdSimCommandKind.Reveal or CrowdSimCommandKind.Obscure
            or CrowdSimCommandKind.Forget or CrowdSimCommandKind.FogShare))
        {
            return;
        }

        RequirePlayer(sim, cmd.Player);
        if (cmd.Kind == CrowdSimCommandKind.FogShare) RequirePlayer(sim, cmd.With);
        if (cmd.Kind == CrowdSimCommandKind.Order)
        {
            if (cmd.HasFogTerrain) RequireFog(sim, "order");
            _ = Movement.CrowdFormations.ShapeOf(sim.Config.Formations, cmd.ShapeId!);
            if (cmd.Members == null || cmd.Members.Length == 0)
            {
                throw new System.InvalidOperationException("order: 成员集为空。");
            }
        }
    }

    private static Movement.CrowdMovementKernel RequireMovement(CrowdSimSession sim, string type) =>
        sim.Movement ?? throw new System.InvalidOperationException($"{type}: 运动内核未启用。");

    private const int SaltScatter = 999983;

    /// <summary>半径内移除,外圈沿离心方向加冲量。冲量 = 该单位速度 × impulse。从稠密表尾部往前删,交换搬走的行已经处理过。</summary>
    private static void Kill(CrowdSimSession sim, CrowdSimCommand cmd)
    {
        sim.Units.MaterializePending();
        Fix64 cx = Fix64.FromInt(cmd.XCm);
        Fix64 cy = Fix64.FromInt(cmd.YCm);
        Fix64 radius = Fix64.FromInt(cmd.RadiusCm);
        Fix64 ring = radius * cmd.RingScale;
        var world = sim.World;
        var units = sim.Units;
        var kernel = sim.Movement;
        for (int i = units.Count - 1; i >= 0; i--)
        {
            var entity = units.EntityAt(i);
            var pos = world.Get<WorldPositionCm>(entity).Value;
            Fix64 dx = pos.X - cx;
            Fix64 dy = pos.Y - cy;
            Fix64 dist = Movement.CrowdFix.Hypot(dx, dy);
            if (dist <= radius)
            {
                int last = units.Count - 1;
                if (i != last) kernel?.MoveAvoidanceRow(last, i);
                var unitState = world.Get<CrowdSimulationUnitState>(entity);
                kernel?.ClearMotionSlot(unitState.Slot);
                int groupId = unitState.GroupId;
                units.ReleaseDense(i);
                if (!sim.Groups.TryGet(groupId, out var group))
                {
                    throw new System.InvalidOperationException($"kill: 组 {groupId} 不存在。");
                }

                group.Count--;
                if (group.Count < 0)
                {
                    throw new System.InvalidOperationException($"kill: 组 {groupId} 计数为负。");
                }

                continue;
            }

            if (dist <= ring && cmd.Impulse > Fix64.Zero && dist > Fix64.Zero)
            {
                var agent = world.Get<CrowdSimulationAgent>(entity);
                Fix64 scale = agent.SpeedCmPerSecond * cmd.Impulse / dist;
                var kin = world.Get<CrowdSimulationKinematics>(entity);
                kin.Velocity = new Fix64Vec2(kin.Velocity.X + dx * scale, kin.Velocity.Y + dy * scale);
                world.Set(entity, kin);
            }
        }

        sim.Groups.ReleaseEmpty();
        sim.Orders.Prune(sim.Groups);
    }

    /// <summary>每个有单位的出生玩家一条 box 指令。目标从本指令自己的加盐流里取,成员按稠密序。</summary>
    private static void Scatter(CrowdSimSession sim)
    {
        sim.Units.MaterializePending();
        var cfg = sim.Config;
        var rng = CrowdSimRng.Create((long)cfg.WorldSeed + (long)++sim.ScatterSeq * SaltScatter);
        Fix64 world = Fix64.FromInt(cfg.NavCellsPerSide) * Fix64.FromInt(cfg.NavCellSizeCm);
        var bases = cfg.Deploy.Bases;
        for (int i = 0; i < bases.Count; i++)
        {
            int player = bases[i].PlayerId;
            int count = sim.Units.CopyOwnHandles(player, sim.MemberCapture);
            if (count == 0) continue;
            Fix64 x = rng.Next() * world;
            Fix64 y = rng.Next() * world;
            Movement.CrowdIssueOrder.Issue(
                sim, x, y, player, "box", null, null, Fix64.Zero, sim.MemberCapture.AsSpan(0, count));
        }
    }

    /// <summary>迷雾面指令在没有 Fog 时入队即拒。Fog 只在 EnableMovement 且已有结构仓时建立。</summary>
    private static void RequireFog(CrowdSimSession sim, string type)
    {
        _ = sim.Fog ?? throw new System.InvalidOperationException($"{type}: 迷雾未启用(会话没有运动内核)。");
    }

    public static object? Exec(CrowdSimSession sim, CrowdSimCommand cmd)
    {
        // 每条指令前收尾未完的重烘 job。指令看到的结构变更必须完整生效。
        // 只在结构 op 前收尾会让紧随的非结构指令的报告 tick 错位一拍。
        if (sim.RebakeJob != null) Structures.CrowdStructureOps.FinishRebake(sim);
        switch (cmd.Kind)
        {
            case CrowdSimCommandKind.Spawn:
                return CrowdDeployment.Spawn(sim, cmd.Count);
            case CrowdSimCommandKind.SpawnAt:
                RequirePlayer(sim, cmd.Player);
                return CrowdDeployment.SpawnAt(
                    sim,
                    Fix64.FromInt(cmd.XCm),
                    Fix64.FromInt(cmd.YCm),
                    cmd.Count,
                    cmd.Player,
                    cmd.UnitType,
                    cmd.RIdx);
            case CrowdSimCommandKind.Order:
                RequirePlayer(sim, cmd.Player);
                // 乐观迷雾开关随下令提交，写进指令日志，回放可复现。切换后立刻同步认知。
                if (cmd.HasFogTerrain)
                {
                    var fog = sim.Fog ?? throw new System.InvalidOperationException("order: 迷雾未启用(会话没有运动内核)。");
                    if (fog.Terrain != cmd.FogTerrain)
                    {
                        fog.Terrain = cmd.FogTerrain;
                        Fog.CrowdBeliefSync.Sync(sim);
                    }
                }

                // 模式是值类型。tick 的 Flush 把返回值收成 object，直接 return 会装箱。
                Movement.CrowdIssueOrder.Issue(
                    sim,
                    Fix64.FromInt(cmd.XCm),
                    Fix64.FromInt(cmd.YCm),
                    cmd.Player,
                    cmd.ShapeId!,
                    cmd.HasAuto ? cmd.Auto : null,
                    cmd.HasFace ? new Fix64Vec2(cmd.FaceX, cmd.FaceY) : null,
                    Fix64.FromInt(cmd.WidthCm),
                    cmd.Members);
                return null;
            case CrowdSimCommandKind.FogSight:
                (sim.Fog ?? throw new System.InvalidOperationException("fogSight: 迷雾未启用(会话没有运动内核)。")).Los =
                    cmd.On;
                return null;
            case CrowdSimCommandKind.Reveal:
            case CrowdSimCommandKind.Obscure:
            case CrowdSimCommandKind.Forget:
            case CrowdSimCommandKind.FogShare:
                return FogCmd(sim, cmd);
            case CrowdSimCommandKind.PlaceStructure:
                return Structures.CrowdStructureOps.PlaceStructure(
                    sim,
                    cmd.TemplateId!,
                    Fix64.FromInt(cmd.XCm),
                    Fix64.FromInt(cmd.YCm),
                    Fix64.FromInt(cmd.SizeCm),
                    Fix64.FromInt(cmd.ToXCm),
                    Fix64.FromInt(cmd.ToYCm));
            case CrowdSimCommandKind.RemoveStructureAt:
                return Structures.CrowdStructureOps.RemoveStructureAt(
                    sim,
                    Fix64.FromInt(cmd.XCm),
                    Fix64.FromInt(cmd.YCm));
            case CrowdSimCommandKind.Kill:
                Kill(sim, cmd);
                return null;
            case CrowdSimCommandKind.Scatter:
                Scatter(sim);
                return null;
            case CrowdSimCommandKind.Rates:
            {
                var movement = RequireMovement(sim, "rates");
                int stepHz = sim.Config.FixedHz * sim.SubSteps;
                movement.Stride = Movement.CrowdMovementKernel.AvoidanceStride(stepHz, cmd.AvoidHz.ToDouble());
                movement.Phase = 0;
                sim.SetAvoidHz(cmd.AvoidHz);
                return null;
            }
            case CrowdSimCommandKind.RebakeSlices:
                sim.RebakeSliceCount = cmd.Slices;
                return null;
            case CrowdSimCommandKind.ClearCaches:
                (sim.Planner ?? throw new System.InvalidOperationException("clearCaches: 运动内核未启用。")).InvalidateFlows();
                return null;
            default:
                throw new System.InvalidOperationException($"未知指令 {CrowdSimCommand.KindName(cmd.Kind)}。");
        }
    }

    /// <summary>迷雾/知识面命令。主体 = 玩家(实体域
    /// 语法糖)→ 其视野组,组内玩家同享。area 恰为 rect、circle 或 poly 之一。形状坐标已经是厘米，Parse 不乘 100。
    /// 命令只改迷雾数据;认知同步在下一 tick 管线拾起(fogShare 的 force 除外——并组当场标记)。</summary>
    private static object? FogCmd(CrowdSimSession sim, CrowdSimCommand cmd)
    {
        string type = CrowdSimCommand.KindName(cmd.Kind);
        var fog = sim.Fog ?? throw new System.InvalidOperationException($"{type}: 迷雾未启用(会话没有运动内核)。");
        var pairRules = sim.Config.PairRules;
        if (cmd.Player < 1 || cmd.Player > pairRules.PlayerCount)
        {
            throw new System.InvalidOperationException($"{type}: 无效玩家 {cmd.Player}。");
        }

        int g = fog.GroupOf[pairRules.IndexByPlayerId[cmd.Player]];
        if (cmd.Kind == CrowdSimCommandKind.FogShare)
        {
            if (cmd.With < 1 || cmd.With > pairRules.PlayerCount)
            {
                throw new System.InvalidOperationException($"fogShare: 无效玩家 {cmd.With}。");
            }

            // on=false 的拆组在边界 Parse 拒收(坏指令不进日志);Exec 只见并组方向。
            int to = fog.MergeGroups(g, fog.GroupOf[pairRules.IndexByPlayerId[cmd.With]]);
            if (to >= 0)
            {
                var force = sim.Belief!.Force ??= new HashSet<int>();
                force.Add(to);
            }

            return to;
        }

        var shape = cmd.Area ?? throw new System.InvalidOperationException($"{type}: 缺少 area 形状。");
        var (cells, box) = Fog.CrowdFogArea.AreaCells(fog.FogCellsPerSide, fog.FogCellSizeCm, shape);
        if (cmd.Kind == CrowdSimCommandKind.Reveal) fog.RevealArea(g, cells);
        else if (cmd.Kind == CrowdSimCommandKind.Forget) fog.ForgetArea(g, cells, box);
        else
        {
            if (cmd.Ticks < 1)
            {
                throw new System.InvalidOperationException("obscure: ticks 需为 ≥1 的整数。");
            }

            fog.ObscureArea(g, cells, sim.TickCount + cmd.Ticks);
        }

        return g;
    }
}
