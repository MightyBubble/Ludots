using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests.Uat;

/// <summary>子步数要按区域速度除数小于 1 的最快区域算：便宜道路上单个积分步的位移不超过 maxStepNavCells 格；所需子步超过上限时截断可观测。</summary>
[TestFixture]
public sealed class SubStepUatTests
{
    private const double RoadCost = 0.2, ClampRoadCost = 0.01, Tol = 1.25;
    private const int RoadCells = 60, Count = 30, Ticks = 240, StartSearchRings = 40;
    private const string RoadArea = "road";

    [Test]
    public void CheapRoad_StepDisplacementStaysUnderMaxStep()
    {
        using var scene = CrowdUatKit.Scene(Bodies(RoadCost));
        var sim = scene.Session;
        var cfg = sim.Config;
        int cs = cfg.NavCellSizeCm, n = cfg.NavCellsPerSide;
        Fix64 lim = Fix64.FromInt(cs) * cfg.Movement.MaxStepNavCells;
        Fix64 roadCost = Fix64.FromDouble(RoadCost);
        Assert.That(OldNeeded(cfg), Is.EqualTo(1), "场景无效：只按速度算的旧推导不是正好 1 个子步");

        double fastest = (FastestSpeed(cfg) * cfg.Movement.SpeedCapRatio * sim.SimDt / sim.SubSteps).ToDouble();
        double stepCells = fastest / cs;

        var nav = scene.Nav;
        var store = sim.Structures!;
        var roadTemplate = cfg.Structures.Templates[store.TemplateIndexOf("road")];
        double wcm = scene.WorldCm.ToDouble();

        (double X, double Y)? Direction(Fix64 sx, Fix64 sy)
        {
            int c0 = CrowdSimulationSpace.CellAt(sx, sy, cs, n);
            for (int k = 0; k < 16; k++)
            {
                double ang = k * Math.PI / 8, dx = Math.Cos(ang), dy = Math.Sin(ang);
                bool ok = true;
                for (int s = 0; s <= RoadCells && ok; s++)
                {
                    double x = sx.ToDouble() + dx * s * cs, y = sy.ToDouble() + dy * s * cs;
                    int cell = CrowdSimulationSpace.CellAt(Fix64.FromDouble(x), Fix64.FromDouble(y), cs, n);
                    ok = x > cs && y > cs && x < wcm - cs && y < wcm - cs && nav.Passable[cell] != 0 && nav.Comp[cell] == nav.Comp[c0];
                }

                if (!ok) continue;
                var fp = CrowdStructureFootprint.Make(roadTemplate, sx, sy, Fix64.FromInt(6 * cs),
                    sx + Fix64.FromDouble(dx * RoadCells * cs), sy + Fix64.FromDouble(dy * RoadCells * cs));
                if (store.JudgePlacement(roadTemplate, fp) == CrowdPlacementReject.None) return (dx, dy);
            }

            return null;
        }

        // 放置指令拒收与已有结构重叠的足印（沙盒的 place 不判合法性）。地图中心放不下时，
        // 起点按环逐格外移，直到有一条能合法放下的道路；道路长度、宽度、代价不变。
        Fix64 half = scene.WorldCm / 2;
        var (sx, sy) = CrowdUatKit.Spot(sim, nav, half, half);
        var dir = Direction(sx, sy);
        int ring = 0;
        for (ring = 1; dir == null && ring <= StartSearchRings; ring++)
        {
            for (int oy = -ring; oy <= ring && dir == null; oy++)
            {
                for (int ox = -ring; ox <= ring && dir == null; ox++)
                {
                    if (Math.Max(Math.Abs(ox), Math.Abs(oy)) != ring) continue;
                    var (tx, ty) = CrowdUatKit.Spot(sim, nav, half + scene.Cells(ox), half + scene.Cells(oy));
                    dir = Direction(tx, ty);
                    if (dir != null) (sx, sy) = (tx, ty);
                }
            }
        }

        double startOffsetCells = CrowdFix.Hypot(sx - half, sy - half).ToDouble() / cs;
        Assert.That(dir, Is.Not.Null, $"场景无效：找不到 {RoadCells} 格长、可放下道路的可通行直线");
        Fix64 ex = sx + Fix64.FromDouble(dir!.Value.X * RoadCells * cs), ey = sy + Fix64.FromDouble(dir.Value.Y * RoadCells * cs);
        sim.Commands.Submit(sim, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.PlaceStructure,
            TemplateId = "road",
            XCm = (int)sx.ToLong(),
            YCm = (int)sy.ToLong(),
            SizeCm = 6 * cs,
            ToXCm = (int)ex.ToLong(),
            ToYCm = (int)ey.ToLong(),
        }, CrowdSimCommands.Exec);
        Assert.That(sim.LastPlacementReject, Is.EqualTo(CrowdPlacementReject.None), "场景无效：道路放置被拒");
        bool Cheap(Fix64 x, Fix64 y) => nav.Cost[CrowdSimulationSpace.CellAt(x, y, cs, n)] <= roadCost;
        int cheap = 0;
        for (int s = 0; s <= RoadCells; s++)
        {
            if (Cheap(sx + Fix64.FromDouble(dir.Value.X * s * cs), sy + Fix64.FromDouble(dir.Value.Y * s * cs))) cheap++;
        }

        Assert.That(cheap, Is.GreaterThanOrEqualTo(RoadCells / 2), $"场景无效：道路格代价未生效（{cheap}/{RoadCells}）");

        CrowdUatKit.SpawnAt(sim, sx, sy, Count, rIdx: 0);
        CrowdUatKit.SetUnitSpeed(sim, cfg.Profiles[0].SpeedCmPerSecond);
        CrowdUatKit.Order(sim, ex, ey);
        Fix64 peak = Fix64.Zero;
        var pos = new Dictionary<uint, Fix64Vec2>();
        void Snap()
        {
            for (int i = 0; i < sim.Units.Count; i++) pos[sim.Units.HandleAt(i)] = CrowdUatKit.PositionAt(sim, i);
        }

        Snap();
        for (int t = 0; t < Ticks; t++)
        {
            CrowdUatKit.Step(sim);
            for (int i = 0; i < sim.Units.Count; i++)
            {
                if (!pos.TryGetValue(sim.Units.HandleAt(i), out var p) || CrowdUatKit.StateAt(sim, i).State == (byte)CrowdUnitState.Traversing) continue;
                var q = CrowdUatKit.PositionAt(sim, i);
                if (Cheap(q.X, q.Y) && Cheap(p.X, p.Y)) peak = Fix64.Max(peak, CrowdFix.Hypot(q.X - p.X, q.Y - p.Y));
            }

            Snap();
        }

        double peakStepCells = (peak / sim.SubSteps).ToDouble() / cs, peakTickCells = peak.ToDouble() / cs;
        TestContext.Out.WriteLine(
            $"startOffsetCells={startOffsetCells:F1} dir=({dir.Value.X:F3},{dir.Value.Y:F3}) subSteps={sim.SubSteps} fastestStepCells={stepCells:F3} roadCells={cheap} peakTickCells={peakTickCells:F3} peakStepCells={peakStepCells:F3} limit={cfg.Movement.MaxStepNavCells.ToDouble() * Tol:F3}");
        Assert.Multiple(() =>
        {
            Assert.That(fastest, Is.LessThanOrEqualTo(lim.ToDouble() * (1 + 1e-6)),
                $"子步数 {sim.SubSteps} 不足：最快单步位移 {stepCells:F2} 格 > 上限 {cfg.Movement.MaxStepNavCells.ToDouble()} 格");
            Assert.That(peak, Is.GreaterThan(Fix64.Zero), "场景无效：没有单位在道路上移动");
            Assert.That((peak / sim.SubSteps).ToDouble(), Is.LessThanOrEqualTo(lim.ToDouble() * Tol),
                $"道路上单步位移 {peakStepCells:F2} 格 > 上限 {cfg.Movement.MaxStepNavCells.ToDouble()} 格（薄于此的墙可被一步跨过）");
            Assert.DoesNotThrow(() => CrowdSimulationValidator.ValidateSim(sim));
        });
    }

    [Test]
    public void ClampTier_IsObservable()
    {
        using var scene = CrowdUatKit.Scene(Bodies(ClampRoadCost));
        var sim = scene.Session;
        int cap = sim.Config.Movement.MaxSubSteps;
        int needed = Needed(sim.Config, FastestSpeed(sim.Config));
        TestContext.Out.WriteLine($"needed={needed} subSteps={sim.SubSteps} clamped={sim.Telemetry.SubStepClamped}");
        Assert.That(needed, Is.GreaterThan(cap), $"场景无效：所需子步 {needed} 未超过上限 {cap}");
        Assert.That(sim.SubSteps, Is.EqualTo(cap), $"子步数 {sim.SubSteps} 应截断为 {cap}");
        Assert.That(sim.Telemetry.SubStepClamped, Is.GreaterThanOrEqualTo(1), "截断未计入遥测 SubStepClamped——截断静默");
    }

    /// <summary>所有单位模板取只按速度推导正好 1 个子步的速度，所有能走道路的移动类型把道路代价和速度除数都改成 <paramref name="roadCost"/>。</summary>
    private static Parity.CrowdTestBodies Bodies(double roadCost)
    {
        var runtime = Parity.S1SurfaceTruthTests.LoadRuntime(CrowdUatKit.DefaultSeed);
        Fix64 simDt = runtime.Sim.SimSecondsPerTickSecond / Fix64.FromInt(runtime.FixedHz);
        Fix64 speed = Fix64.FromInt(runtime.NavCellSizeCm) * runtime.Movement.MaxStepNavCells / (runtime.Movement.SpeedCapRatio * simDt);
        while (Needed(runtime, speed) > 1) speed = Fix64.FromRaw(speed.RawValue - 1);
        return CrowdUatKit.DemoBodies(null, speed, json =>
        {
            foreach (var agent in json["agentTypes"]!.AsObject())
            {
                if (agent.Value!["cells"]![RoadArea] is JsonObject road)
                {
                    road["cost"] = roadCost;
                    road["speedDivisor"] = roadCost;
                }
            }
        });
    }

    /// <summary>最快可能速度：模板速度 ÷ 该移动类型最小的可走区域速度除数。</summary>
    private static Fix64 FastestSpeed(CrowdSimulationRuntimeConfig cfg)
    {
        Fix64 best = Fix64.Zero;
        foreach (var p in cfg.Profiles)
        {
            Fix64 min = Fix64.MaxValue;
            foreach (var d in cfg.AgentTypes[p.AgentTypeIndex].SpeedDivisorByArea)
            {
                if (d > Fix64.Zero && d < min) min = d;
            }

            best = Fix64.Max(best, p.SpeedCmPerSecond / min);
        }

        return best;
    }

    private static int Needed(CrowdSimulationRuntimeConfig cfg, Fix64 speed)
    {
        Fix64 simDt = cfg.Sim.SimSecondsPerTickSecond / Fix64.FromInt(cfg.FixedHz);
        return Math.Max(1, (int)Fix64.Ceiling(speed * cfg.Movement.SpeedCapRatio * simDt /
            (Fix64.FromInt(cfg.NavCellSizeCm) * cfg.Movement.MaxStepNavCells)).ToLong());
    }

    private static int OldNeeded(CrowdSimulationRuntimeConfig cfg)
    {
        Fix64 max = Fix64.Zero;
        foreach (var p in cfg.Profiles) max = Fix64.Max(max, p.SpeedCmPerSecond);
        return Needed(cfg, max);
    }
}
