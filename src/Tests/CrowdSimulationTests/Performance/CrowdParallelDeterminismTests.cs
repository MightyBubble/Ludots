using System.Text.Json.Nodes;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using CrowdSimulationTests.Parity;
using NUnit.Framework;

namespace CrowdSimulationTests.Performance;

/// <summary>
/// 同一场景用工人数 1 与 N 各跑一段。逐 tick 校验码必须相同：区间并行不改变稠密序能观察到的写。
/// </summary>
public sealed class CrowdParallelDeterminismTests
{
    [Test]
    public void WorkerCount_DoesNotChangeTickChecksums_WhenUnitsArrive()
    {
        var serial = RunScript(workerCount: 1, ticks: 80);
        var parallel = RunScript(workerCount: 4, ticks: 80);
        var remainder = RunScript(workerCount: 3, ticks: 80);

        Assert.That(serial.Arrived, Is.GreaterThan(0), "场景里应出现到达，否则接触顺序没有被覆盖");
        Assert.That(parallel.Checksums, Is.EqualTo(serial.Checksums).AsCollection);
        Assert.That(remainder.Checksums, Is.EqualTo(serial.Checksums).AsCollection);
        Assert.That(parallel.Arrived, Is.EqualTo(serial.Arrived));
        Assert.That(remainder.Arrived, Is.EqualTo(serial.Arrived));
    }

    /// <summary>
    /// 较小下标本 tick 才到达，较大下标只因撞上它才到达。倒序提交会让较大下标看不见这次到达。
    /// </summary>
    [Test]
    public void SameTickLowerIndexArrival_IsVisibleToLaterContact()
    {
        var serial = RunCascade(workerCount: 1);
        var parallel = RunCascade(workerCount: 4);
        var remainder = RunCascade(workerCount: 3);

        AssertCascade(serial);
        AssertCascade(parallel);
        AssertCascade(remainder);
        Assert.That(parallel.Checksums, Is.EqualTo(serial.Checksums).AsCollection);
        Assert.That(remainder.Checksums, Is.EqualTo(serial.Checksums).AsCollection);
    }

    private static void AssertCascade(CascadeRun run)
    {
        var w = run.Witness;
        Assert.That(w.LowerWasMoving && w.HigherWasMoving, Is.True, "摆位前两人不应已到达");
        Assert.That(w.LowerRoute, Is.LessThanOrEqualTo(w.GoalArrive), "较小下标应靠路线阈值自己到达");
        Assert.That(w.HigherRoute, Is.GreaterThan(w.GoalArrive), "较大下标不应靠路线阈值自己到达");
        Assert.That(w.HigherRoute, Is.LessThanOrEqualTo(w.Reach), "较大下标应仍在聚拢近距内，接触判定才开");
        Assert.That(w.DistanceCm, Is.LessThan(w.RadiusSumCm), "两人必须重叠，接触才成立");
        Assert.That(w.OtherArrivedOverlap, Is.EqualTo(0), "较大下标旁边不应已有更早到达的人");
        Assert.That(w.LowerArrived, Is.True, "较小下标本 tick 应到达");
        Assert.That(w.HigherArrived, Is.True,
            "较大下标只因看见更小下标本 tick 的到达才应到达；倒序提交时这一支不发生");
    }

    private static ScriptRun RunScript(int workerCount, int ticks)
    {
        using var scope = Open(workerCount);
        var session = scope.Session;
        string dir = Path.Combine(S1SurfaceTruthTests.SeedDir("s1337"), "CrowdSimulation", "parity");
        var meta = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "s5-trajectory-truth.json")))!;
        var script = meta["script"]!.AsArray()
            .Select(e => new CrowdCommand(e!["tick"]!.GetValue<int>(), CrowdSimCommand.Parse(e!["cmd"]!)))
            .ToArray();
        session.Commands.Schedule(session, script);

        var checksums = new List<ulong>(ticks);
        session.Advance(ticks, checksums);
        int arrived = 0;
        for (int i = 0; i < session.Units.Count; i++)
        {
            if (session.Movement!.States[i].State == (byte)CrowdUnitState.Arrived) arrived++;
        }

        return new ScriptRun(checksums, arrived);
    }

    private static CascadeRun RunCascade(int workerCount)
    {
        using var scope = Open(workerCount);
        var session = scope.Session;
        session.Commands.Schedule(session, new[]
        {
            new CrowdCommand(0, CrowdSimCommand.Parse(new JsonObject
            {
                ["type"] = "spawnAt", ["player"] = 1, ["xCm"] = 480000, ["yCm"] = 560000,
                ["count"] = 220, ["unitType"] = 0, ["rIdx"] = 0,
            })),
            new CrowdCommand(2, CrowdSimCommand.Parse(new JsonObject
            {
                ["type"] = "order", ["player"] = 1, ["xCm"] = 486250, ["yCm"] = 560000, ["shape"] = "box",
            })),
        });

        var checksums = new List<ulong>(80);
        CrowdNavGroupSet.Group? group = null;
        CrowdOrder? order = null;
        for (int guard = 0; guard < 80; guard++)
        {
            if (!session.Step(out var hash)) throw new InvalidOperationException("聚拢场景推进停摆。");
            checksums.Add(hash);
            if (session.Units.Count == 0) continue;
            var st = session.World.Get<CrowdSimulationUnitState>(session.Units.EntityAt(0));
            if (!session.Groups.TryGet(st.GroupId, out var g) || g.Flow == null) continue;
            order = session.Orders.List.FirstOrDefault(o => o.Id == g.OrderId);
            if (order is { Mode: CrowdOrderMode.Converge, ReachCells: >= 2 })
            {
                group = g;
                break;
            }
        }

        if (group?.Flow == null || order == null)
        {
            throw new InvalidOperationException(
                $"聚拢流场没有在 80 tick 内就绪(units={session.Units.Count}, reach={order?.ReachCells}, mode={order?.Mode})。");
        }

        var pair = FindStraddle(session, group, order);
        ParkOthers(session);
        Place(session, 0, pair.Inner, CrowdUnitState.Moving);
        Place(session, 1, pair.Outer, CrowdUnitState.Moving);
        var witnessBefore = Snapshot(session, pair);
        if (!session.Step(out var placed)) throw new InvalidOperationException("摆位后的 tick 停摆。");
        checksums.Add(placed);

        var after0 = session.World.Get<CrowdSimulationUnitState>(session.Units.EntityAt(0));
        var after1 = session.World.Get<CrowdSimulationUnitState>(session.Units.EntityAt(1));
        var witness = witnessBefore with
        {
            LowerArrived = after0.State == (byte)CrowdUnitState.Arrived,
            HigherArrived = after1.State == (byte)CrowdUnitState.Arrived,
        };
        return new CascadeRun(checksums, witness);
    }

    private static SessionScope Open(int workerCount)
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        runtime.Sim.ParallelWorkerCount = workerCount;
        var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(120), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        session.TruthNavFrozen = true;
        return new SessionScope(session, service);
    }

    private static Straddle FindStraddle(CrowdSimSession session, CrowdNavGroupSet.Group group, CrowdOrder order)
    {
        var flow = group.Flow ?? throw new InvalidOperationException("组没有流场。");
        var nav = session.Navs[group.BodyNavId];
        int n = session.Config.NavCellCount;
        int cs = session.Config.NavCellSizeCm;
        if (group.Goal < 0) throw new InvalidOperationException($"组 {group.Id} 没有目标格。");
        var goalArrive = session.Movement!.GoalArriveCells;
        var reach = Fix64.FromInt(order.ReachCells);
        double radiusSum = (session.Units.PersonalRadiusCmAt(0) + session.Units.PersonalRadiusCmAt(1)).ToDouble();
        var found = new List<Candidate>(512);
        var dirs = new (int X, int Y)[] { (1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };
        int gcx = group.Goal % n;
        int gcy = group.Goal / n;
        for (int d = 0; d < dirs.Length; d++)
        {
            double len = Math.Sqrt(dirs[d].X * dirs[d].X + dirs[d].Y * dirs[d].Y);
            for (double dist = cs * 0.3; dist <= cs * 2.6; dist += 200)
            {
                double x = (gcx + 0.5) * cs + dirs[d].X / len * dist;
                double y = (gcy + 0.5) * cs + dirs[d].Y / len * dist;
                if (TryCandidate(flow, nav, n, cs, x, y, out var candidate)) found.Add(candidate);
            }
        }

        Candidate inner = default, outer = default;
        double bestDist = double.MaxValue;
        bool hit = false;
        for (int a = 0; a < found.Count; a++)
        {
            if (found[a].Route > goalArrive || found[a].Route < goalArrive * Fix64.FromDouble(0.55)) continue;
            for (int b = 0; b < found.Count; b++)
            {
                if (found[b].Route <= goalArrive || found[b].Route > reach) continue;
                if (found[b].Link) continue;
                double dx = found[a].X.ToDouble() - found[b].X.ToDouble();
                double dy = found[a].Y.ToDouble() - found[b].Y.ToDouble();
                double distCm = Math.Sqrt(dx * dx + dy * dy);
                if (distCm >= radiusSum * 0.92 || distCm < radiusSum * 0.45) continue;
                if (distCm < bestDist)
                {
                    bestDist = distCm;
                    inner = found[a];
                    outer = found[b];
                    hit = true;
                }
            }
        }

        if (!hit)
        {
            throw new InvalidOperationException(
                $"没有找到跨过路线到达阈值又互相重叠的两点(goal={group.Goal}, reach={order.ReachCells}, samples={found.Count}, radiusSum={radiusSum:F0})。");
        }

        return new Straddle(inner.X, inner.Y, outer.X, outer.Y, inner.Route.ToDouble(), outer.Route.ToDouble(),
            goalArrive.ToDouble(), reach.ToDouble(), radiusSum, bestDist);
    }

    private static bool TryCandidate(Ludots.Core.CrowdSimulation.Nav.FlowField flow, Ludots.Core.CrowdSimulation.Nav.NavContext nav, int n, int cs, double x, double y, out Candidate candidate)
    {
        candidate = default;
        if (x < 0 || y < 0) return false;
        var px = Fix64.FromDouble(x);
        var py = Fix64.FromDouble(y);
        int cx = (int)(px / Fix64.FromInt(cs)).ToLong();
        int cy = (int)(py / Fix64.FromInt(cs)).ToLong();
        if (cx < 0 || cy < 0 || cx >= n || cy >= n) return false;
        int cell = cy * n + cx;
        if (nav.Passable[cell] == 0 || nav.Portal[cell] != 0) return false;
        var route = CrowdFlowSample.RouteLength(flow, n, cs, px, py, cell, 0);
        if (route >= CrowdFlowSample.Unreachable / 4) return false;
        bool link = flow.Lk != null && flow.Lk[cell] >= 0;
        candidate = new Candidate(px, py, route, link);
        return true;
    }

    private static CascadeWitness Snapshot(CrowdSimSession session, Straddle pair)
    {
        var s0 = session.World.Get<CrowdSimulationUnitState>(session.Units.EntityAt(0));
        var s1 = session.World.Get<CrowdSimulationUnitState>(session.Units.EntityAt(1));
        int others = 0;
        double r0 = session.Units.PersonalRadiusCmAt(0).ToDouble();
        for (int i = 2; i < session.Units.Count; i++)
        {
            var st = session.World.Get<CrowdSimulationUnitState>(session.Units.EntityAt(i));
            if (st.State != (byte)CrowdUnitState.Arrived) continue;
            var p = session.World.Get<WorldPositionCm>(session.Units.EntityAt(i)).Value;
            double dx = p.X.ToDouble() - pair.Outer.X.ToDouble();
            double dy = p.Y.ToDouble() - pair.Outer.Y.ToDouble();
            double ri = session.Units.PersonalRadiusCmAt(i).ToDouble();
            if (dx * dx + dy * dy < (r0 + ri) * (r0 + ri)) others++;
        }

        return new CascadeWitness(
            s0.State == (byte)CrowdUnitState.Moving,
            s1.State == (byte)CrowdUnitState.Moving,
            false, false,
            pair.InnerRoute, pair.OuterRoute, pair.GoalArrive, pair.Reach, pair.DistanceCm, pair.RadiusSumCm,
            others);
    }

    private static void ParkOthers(CrowdSimSession session)
    {
        for (int i = 2; i < session.Units.Count; i++)
        {
            double x = 8000 + (i % 40) * 2800;
            double y = 8000 + (i / 40) * 2800;
            Place(session, i, new Fix64Vec2(Fix64.FromDouble(x), Fix64.FromDouble(y)), CrowdUnitState.Idle);
        }
    }

    private static void Place(CrowdSimSession session, int dense, Fix64Vec2 pos, CrowdUnitState state)
    {
        var e = session.Units.EntityAt(dense);
        var world = session.World;
        var wp = world.Get<WorldPositionCm>(e);
        wp.Value = pos;
        world.Set(e, wp);
        var st = world.Get<CrowdSimulationUnitState>(e);
        st.State = (byte)state;
        world.Set(e, st);
        var kin = world.Get<CrowdSimulationKinematics>(e);
        kin.Velocity = Fix64Vec2.Zero;
        kin.Blend = Fix64.Zero;
        kin.StallSeconds = Fix64.Zero;
        world.Set(e, kin);
    }

    private readonly record struct Candidate(Fix64 X, Fix64 Y, Fix64 Route, bool Link);

    private readonly record struct Straddle(
        Fix64 InnerX, Fix64 InnerY, Fix64 OuterX, Fix64 OuterY,
        double InnerRoute, double OuterRoute, double GoalArrive, double Reach, double RadiusSumCm, double DistanceCm)
    {
        public Fix64Vec2 Inner => new(InnerX, InnerY);
        public Fix64Vec2 Outer => new(OuterX, OuterY);
    }

    private readonly record struct CascadeWitness(
        bool LowerWasMoving, bool HigherWasMoving, bool LowerArrived, bool HigherArrived,
        double LowerRoute, double HigherRoute, double GoalArrive, double Reach,
        double DistanceCm, double RadiusSumCm, int OtherArrivedOverlap);

    private readonly record struct ScriptRun(List<ulong> Checksums, int Arrived);
    private readonly record struct CascadeRun(List<ulong> Checksums, CascadeWitness Witness);

    private readonly record struct SessionScope(CrowdSimSession Session, PathQueryService Service) : IDisposable
    {
        public void Dispose() => Service.Dispose();
    }
}
