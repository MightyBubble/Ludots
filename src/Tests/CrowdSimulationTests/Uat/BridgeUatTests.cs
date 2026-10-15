using System.Collections.Generic;
using System.Text;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests.Uat;

/// <summary>地图上的每座桥都能被一队陆军从一岸走上桥面再走到对岸，全程仿真不变量成立。</summary>
[TestFixture]
public sealed class BridgeUatTests
{
    private const int Count = 20;
    private const int BeyondCells = 4;
    private const int MaxTicks = 3000;
    private const int IdleCheckEvery = 30;

    [Test]
    public void EveryMapBridge_IsCrossedOverTheDeck()
    {
        using var scene = CrowdUatKit.Scene();
        var session = scene.Session;
        var nav = scene.Nav;
        var bridges = CrowdUatBridges.LayeredIds(session);
        int worldCm = (int)scene.WorldCm.ToLong();
        var errors = new List<string>();
        var detail = new StringBuilder();
        int tried = 0, crossed = 0, ticks = 0;
        foreach (int id in bridges)
        {
            var route = CrowdUatBridges.Route(session, nav, id, BeyondCells);
            if (route == null)
            {
                detail.AppendLine($"桥 {id} SKIP（端点无可行地面）");
                continue;
            }

            tried++;
            if (session.Units.Count > 0) KillAll(session, worldCm);
            if (CrowdUatKit.SpawnAt(session, route.A.X, route.A.Y, Count, rIdx: 0) == 0)
            {
                errors.Add($"桥 {id}：起点无法部署");
                detail.AppendLine($"桥 {id} FAIL spawn");
                continue;
            }

            CrowdUatKit.Order(session, route.B.X, route.B.Y);
            bool deck = false;
            int t = 0;
            for (; t < MaxTicks; t++)
            {
                CrowdUatKit.Step(session);
                for (int i = 0; i < session.Units.Count; i++)
                {
                    if (CrowdUatKit.StateAt(session, i).Level != 0) deck = true;
                }

                if (t % IdleCheckEvery == 0 && session.Units.Count > 0 && AllSettled(session)) break;
            }

            ticks += t;
            int n = session.Units.Count, far = 0;
            for (int i = 0; i < n; i++)
            {
                var p = CrowdUatKit.PositionAt(session, i);
                if ((p.X - route.Footprint.X1) * route.Ux + (p.Y - route.Footprint.Y1) * route.Uy > Fix64.Zero) far++;
            }

            bool pass = deck && n > 0 && far * 10 >= 8 * n;
            detail.AppendLine($"桥 {id} {(pass ? "PASS" : "FAIL")} 上桥={deck} 对岸 {far}/{n} ticks={t}");
            if (pass) crossed++;
            else errors.Add($"桥 {id}：上桥={deck} 到达对岸 {far}/{n} {Describe(session, nav, route)}");

            try
            {
                CrowdSimulationValidator.ValidateSim(session);
            }
            catch (CrowdSimulationInvariantException e)
            {
                errors.Add(e.Message);
                break;
            }
        }

        TestContext.Out.WriteLine($"[uat-bridge] crossed={crossed}/{tried} bridges={bridges.Count} ticks={ticks}");
        TestContext.Out.Write(detail.ToString());
        Assert.That(tried, Is.GreaterThan(0), "地图上没有可测试的桥");
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
        Assert.That(crossed, Is.EqualTo(tried));
    }

    private static bool AllSettled(CrowdSimSession session)
    {
        for (int i = 0; i < session.Units.Count; i++)
        {
            byte state = CrowdUatKit.StateAt(session, i).State;
            if (state == (byte)CrowdUnitState.Moving || state == (byte)CrowdUnitState.Traversing) return false;
        }

        return true;
    }

    private static void KillAll(CrowdSimSession session, int worldCm)
        => session.Commands.Submit<object?>(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Kill,
            XCm = worldCm / 2,
            YCm = worldCm / 2,
            RadiusCm = worldCm,
            RingScale = Fix64.FromInt(2),
            Impulse = Fix64.Zero,
        }, CrowdSimCommands.Exec);

    private static string Describe(CrowdSimSession session, NavContext nav, CrowdUatBridgeRoute route)
    {
        var fp = route.Footprint;
        int cs = session.Config.NavCellSizeCm;
        int Cell(Fix64 x, Fix64 y) => CrowdUatBridges.CellOf(session, x, y);
        int Up(int c) => nav.UpComp == null ? -1 : nav.UpComp[c];
        var deck = new StringBuilder();
        foreach (int f in new[] { 0, 1, 2 })
        {
            Fix64 x = fp.X0 + (fp.X1 - fp.X0) * f / 2, y = fp.Y0 + (fp.Y1 - fp.Y0) * f / 2;
            if (deck.Length > 0) deck.Append(',');
            deck.Append(Up(Cell(x, y)));
        }

        int end = Cell(fp.X1, fp.Y1);
        return $"宽={fp.WidthCm.ToDouble() / cs:F1}格 长={route.LengthCm.ToDouble() / cs:F1}格 " +
               $"comp={nav.Comp[Cell(route.A.X, route.A.Y)]}/{nav.Comp[Cell(route.B.X, route.B.Y)]} 桥面={deck} " +
               $"端地面={nav.Comp[Cell(fp.X0, fp.Y0)]}/{nav.Comp[end]} 端格阻挡={session.Structures!.Blocked[end]}";
    }
}
