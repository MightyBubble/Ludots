using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests.Uat;

/// <summary>一个 UAT 场景：带运动内核的会话、出生方块所在导航与方块中心。释放时关掉路径服务。</summary>
internal sealed class CrowdUatScene : IDisposable
{
    public CrowdUatScene(CrowdSimulationRuntimeConfig runtime, CrowdSimSession session, PathQueryService service, NavContext nav)
    {
        Runtime = runtime;
        Session = session;
        Service = service;
        Nav = nav;
    }

    public CrowdSimulationRuntimeConfig Runtime { get; }
    public CrowdSimSession Session { get; }
    public PathQueryService Service { get; }
    /// <summary>玩家 1 的 0 号兵种、0 号半径级的导航。</summary>
    public NavContext Nav { get; }
    public Fix64 Sx { get; internal set; }
    public Fix64 Sy { get; internal set; }
    public int CellCm => Session.Config.NavCellSizeCm;
    public int CellsPerSide => Session.Config.NavCellsPerSide;
    public Fix64 WorldCm => Fix64.FromInt(CellsPerSide) * Fix64.FromInt(CellCm);
    /// <summary>格数换成厘米（场景尺寸按格给）。</summary>
    public Fix64 Cells(int n) => Fix64.FromInt(n) * Fix64.FromInt(CellCm);

    public void Dispose() => Service.Dispose();
}

/// <summary>编队类 UAT 的公用场景工具：在 S1 默认世界上摆一个方块、下令、推进、找最新指令和它的质心。</summary>
internal static class CrowdUatKit
{
    /// <summary>沙盒默认世界的种子图。</summary>
    public const string DefaultSeed = "s1337";
    /// <summary>沙盒的玩家 0。Ludots 玩家号从 1 起。</summary>
    public const int Player = 1;

    /// <summary>带运动内核、阻塞等答复的空会话（还没有单位）。<paramref name="tuneConfig"/> 在加载前改合并后的配置 JSON。</summary>
    public static CrowdUatScene Scene(Action<JsonObject>? tuneConfig = null, string seed = DefaultSeed)
    {
        var (runtime, session) = tuneConfig == null
            ? S4DeployTruthTests.BuildSession(seed)
            : S4DeployTruthTests.BuildSession(seed, tuneConfig);
        var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        var nav = session.NavFor(runtime.UnitTypes[0].AgentTypeIndex, 0);
        return new CrowdUatScene(runtime, session, service, nav);
    }

    /// <summary>带运动内核的空会话，体型表换成 <paramref name="bodies"/>。</summary>
    public static CrowdUatScene Scene(CrowdTestBodies bodies, Action<JsonObject>? tuneConfig = null, string seed = DefaultSeed)
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession(seed, tuneConfig, bodies);
        var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        return new CrowdUatScene(runtime, session, service, session.NavFor(runtime.UnitTypes[0].AgentTypeIndex, 0));
    }

    /// <summary>演示体型表按半径级（升序）逐级换半径。个人半径由装载按避让倍数重算。</summary>
    public static CrowdTestBodies DemoBodiesWithRadii(params int[] radiusCmByClass)
        => DemoBodies(radiusCmByClass, speedCmPerSec: null, tuneTraversal: null);

    /// <summary>演示体型与模板；<paramref name="speedCmPerSec"/> 给定时所有单位模板换成这一速度，<paramref name="tuneTraversal"/> 在演示 traversal.json 合并结果上改值。</summary>
    public static CrowdTestBodies DemoBodies(int[]? radiusCmByClass, Fix64? speedCmPerSec, Action<JsonObject>? tuneTraversal)
    {
        var profiles = System.Text.Json.JsonSerializer.Deserialize<List<AgentProfileConfig>>(
            System.IO.File.ReadAllText(System.IO.Path.Combine("assets", "agent_profiles.json")),
            Ludots.Core.Config.StrictJsonOptions.CreateCamelCase())!;
        var classes = profiles.Select(p => (int)p.RadiusCm).Distinct().OrderBy(r => r).ToArray();
        if (radiusCmByClass != null && classes.Length != radiusCmByClass.Length)
            throw new ArgumentException($"演示体型有 {classes.Length} 个半径级，给了 {radiusCmByClass.Length} 个半径。");
        foreach (var p in profiles)
        {
            if (radiusCmByClass != null) p.RadiusCm = radiusCmByClass[Array.IndexOf(classes, (int)p.RadiusCm)];
        }

        var templates = TestDefaults.DemoTemplates().Select(t => new CrowdUnitTemplateFacts
        {
            TemplateId = t.TemplateId,
            ProfileId = t.ProfileId,
            SpeedCmPerSec = speedCmPerSec ?? t.SpeedCmPerSec,
            PushPriority = t.PushPriority,
            AutoDeploy = t.AutoDeploy,
        }).ToList();
        CrowdTraversalConfig? traversal = null;
        if (tuneTraversal != null)
        {
            var json = TestDefaults.DefaultTraversalJson();
            tuneTraversal(json);
            traversal = TestDefaults.ParseTraversal(json);
        }

        return new CrowdTestBodies(new AgentProfileRegistry(profiles), templates, traversal);
    }

    /// <summary>地图中心最近的可走格上生成 <paramref name="count"/> 个玩家 1 的 0 号兵种。</summary>
    public static CrowdUatScene Block(int count, Action<JsonObject>? tuneConfig = null, string seed = DefaultSeed)
    {
        var scene = Scene(tuneConfig, seed);
        Fix64 half = scene.WorldCm / 2;
        var (sx, sy) = Spot(scene.Session, scene.Nav, half, half);
        scene.Sx = sx;
        scene.Sy = sy;
        SpawnAt(scene.Session, sx, sy, count, rIdx: 0);
        return scene;
    }

    public static int SpawnAt(CrowdSimSession session, Fix64 xCm, Fix64 yCm, int count, int rIdx, int unitType = 0, int player = Player)
        => (int)session.Commands.Submit(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.SpawnAt,
            Player = player,
            XCm = (int)xCm.ToLong(),
            YCm = (int)yCm.ToLong(),
            Count = count,
            UnitType = unitType,
            RIdx = rIdx,
        }, CrowdSimCommands.Exec)!;

    /// <summary>离 (x, y) 最近的可走格的中心。</summary>
    public static (Fix64 X, Fix64 Y) Spot(CrowdSimSession session, NavContext nav, Fix64 xCm, Fix64 yCm)
    {
        int n = session.Config.NavCellsPerSide, cs = session.Config.NavCellSizeCm;
        int c = CrowdDeployment.NearestPassable(nav, CrowdSimulationSpace.CellAt(xCm, yCm, cs, n));
        if (c < 0) throw new InvalidOperationException("场景无效：找不到可走格。");
        return CellCentre(session, c);
    }

    public static (Fix64 X, Fix64 Y) CellCentre(CrowdSimSession session, int cell)
    {
        int n = session.Config.NavCellsPerSide, cs = session.Config.NavCellSizeCm;
        return (Fix64.FromInt((cell % n) * cs + cs / 2), Fix64.FromInt((cell / n) * cs + cs / 2));
    }

    /// <summary>玩家全体的 box 阵型移动令，关 magic box、无朝向、无线宽。当前 tick 执行。</summary>
    public static void Order(CrowdSimSession session, Fix64 xCm, Fix64 yCm, int player = Player)
    {
        var members = new uint[session.Units.Count];
        int count = session.Units.CopyOwnHandles(player, members);
        session.Commands.Submit<object?>(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Order,
            Player = player,
            XCm = (int)xCm.ToLong(),
            YCm = (int)yCm.ToLong(),
            ShapeId = "box",
            HasAuto = true,
            Auto = false,
            Members = members.AsSpan(0, count).ToArray(),
        }, CrowdSimCommands.Exec);
    }

    public static CrowdOrder? Newest(CrowdSimSession session)
    {
        var list = session.Orders.List;
        return list.Count == 0 ? null : list[list.Count - 1];
    }

    /// <summary>推进 n 个 tick；任一 tick 停摆即抛错。</summary>
    public static void Step(CrowdSimSession session, int n = 1)
    {
        for (int t = 0; t < n; t++)
        {
            if (!session.Step(out _)) throw new InvalidOperationException($"tick {session.TickCount} 停滞");
        }
    }

    /// <summary>推进到最新指令的领队路径落地（出现第一只领队）；<paramref name="before"/> 在每个 tick 前取一次值，随结果带回落地那一 tick 前的值。超过 <paramref name="max"/> tick 返回 null。</summary>
    public static CrowdUatLanding<T>? UntilLanded<T>(CrowdSimSession session, int max, Func<T>? before)
    {
        Step(session);
        var o = Newest(session) ?? throw new InvalidOperationException("场景无效：没有指令。");
        for (int t = 1; t < max; t++)
        {
            T pre = before != null ? before() : default!;
            Step(session);
            if (o.Leaders.Count > 0) return new CrowdUatLanding<T>(o, o.Leaders[0], pre);
        }

        return null;
    }

    public static CrowdUatLanding<object?>? UntilLanded(CrowdSimSession session, int max)
        => UntilLanded<object?>(session, max, before: null);

    /// <summary>跟随指令 <paramref name="order"/> 的单位的质心；没有跟随者返回 null。</summary>
    public static (Fix64 X, Fix64 Y)? Centroid(CrowdSimSession session, CrowdOrder order)
    {
        var units = session.Units;
        Fix64 x = Fix64.Zero, y = Fix64.Zero;
        int n = 0;
        for (int i = 0; i < units.Count; i++)
        {
            var e = units.EntityAt(i);
            if (session.World.Get<CrowdSimulationUnitState>(e).Order != (uint)order.Id) continue;
            var p = session.World.Get<WorldPositionCm>(e).Value;
            x += p.X;
            y += p.Y;
            n++;
        }

        return n == 0 ? null : (x / n, y / n);
    }

    /// <summary>单位实体的速度来自实体模板，不读 <see cref="CrowdTestBodies"/> 的模板事实；换了模板速度的场景生成后用它把在场单位写成同一速度。</summary>
    public static void SetUnitSpeed(CrowdSimSession session, Fix64 speedCmPerSec)
    {
        session.Units.MaterializePending();
        for (int i = 0; i < session.Units.Count; i++)
        {
            var e = session.Units.EntityAt(i);
            var agent = session.World.Get<Ludots.Core.CrowdSimulation.CrowdSimulationAgent>(e);
            agent.SpeedCmPerSec = speedCmPerSec;
            session.World.Set(e, agent);
        }
    }

    public static Fix64Vec2 PositionAt(CrowdSimSession session, int dense)
        => session.World.Get<WorldPositionCm>(session.Units.EntityAt(dense)).Value;

    public static CrowdSimulationUnitState StateAt(CrowdSimSession session, int dense)
        => session.World.Get<CrowdSimulationUnitState>(session.Units.EntityAt(dense));
}

/// <summary>领队落地：指令、它的第一只领队、落地 tick 之前取到的值。</summary>
internal sealed record CrowdUatLanding<T>(CrowdOrder Order, CrowdLeader Leader, T Pre);
