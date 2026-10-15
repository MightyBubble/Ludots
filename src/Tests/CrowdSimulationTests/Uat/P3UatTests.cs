using System.Collections.Generic;
using System.Linq;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests.Uat;

/// <summary>单位模板表的顺序与移动类型表相反时，玩家视图隐藏一个模板只隐藏它那个移动类型的单位。</summary>
[TestFixture]
public sealed class P3UatTests
{
    private const int PerType = 3;

    [Test]
    public void ReversedTemplates_HidingOneTemplateHidesOnlyItsAgentType()
    {
        var bodies = CrowdUatKit.DemoBodies(null, null, null);
        bodies.Templates.Reverse();
        var (_, session) = S4DeployTruthTests.BuildSession(CrowdUatKit.DefaultSeed, null, bodies);
        var cfg = session.Config;
        int types = cfg.UnitTypes.Count;
        Assert.That(types, Is.GreaterThan(1), "场景无效：只有一个单位模板");
        Assert.That(bodies.Templates[0].AgentTypeId, Is.Not.EqualTo(cfg.AgentTypes[0].Id),
            "场景无效：反序后第一个模板仍属于第一个移动类型");
        for (int t = 0; t < types; t++)
        {
            int l = cfg.UnitTypes[t].AgentTypeIndex;
            Assert.That(cfg.AgentTypes[l].Id, Is.EqualTo(cfg.UnitTypes[t].Id), $"模板 {t} 指向的移动类型 {l} 不是它自己的");
        }

        Fix64 half = Fix64.FromInt(cfg.NavCellsPerSide * cfg.NavCellSizeCm / 2);
        for (int t = 0; t < types; t++)
        {
            Assert.That(CrowdDeployment.SpawnAt(session, half, half, PerType, CrowdUatKit.Player, t, 0), Is.EqualTo(PerType),
                $"模板 {t} 没生出 {PerType} 个单位");
        }

        session.Presentation.HiddenTemplateTagId = 3;
        var host = new CrowdPlayerViewHost(null, CrowdPlayerViewConfig.Load(CrowdPlayerViewTests.ViewJson(), "player-view"));
        host.Bind(session);
        var demo = new S4DeployDemoRuntime(null);
        var input = new FrozenInputActionReader();
        var buffer = new uint[session.Units.Capacity];
        var errors = new List<string>();
        for (int t = 0; t < types; t++)
        {
            if (t > 0)
            {
                Press(host, session, demo, input, "cycleLayer");
            }

            Press(host, session, demo, input, "toggleLayer");
            int hidden = cfg.UnitTypes[t].AgentTypeIndex;
            var shown = new HashSet<uint>(buffer.Take(session.Units.CopyOwnHandles(CrowdUatKit.Player, buffer)));
            for (int i = 0; i < session.Units.Count; i++)
            {
                int l = session.World.Get<CrowdSimulationAgent>(session.Units.EntityAt(i)).AgentTypeIndex;
                bool want = l != hidden;
                if (shown.Contains(session.Units.HandleAt(i)) != want)
                {
                    errors.Add($"隐藏模板 {t}：移动类型 {l} 的单位显示={!want}，应为 {want}");
                }
            }

            Press(host, session, demo, input, "toggleLayer");
            Assert.That(session.Units.CopyOwnHandles(CrowdUatKit.Player, buffer), Is.EqualTo(types * PerType),
                $"模板 {t} 重新打开后单位没全部回来");
        }

        Assert.That(errors, Is.Empty, string.Join("\n", errors.Take(5)));
    }

    private static void Press(CrowdPlayerViewHost host, CrowdSimSession session, S4DeployDemoRuntime demo, FrozenInputActionReader input, string action)
    {
        input.Clear();
        input.SetActionState(action, System.Numerics.Vector3.One, true, true, false);
        host.ApplyInput(session, input, false, 0, 0, demo);
        input.Clear();
    }
}
