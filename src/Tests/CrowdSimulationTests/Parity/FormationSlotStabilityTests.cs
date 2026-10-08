using System;
using System.Linq;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// TR-03 阵型排序稳定性:JS sort 稳定,平局保"第一处排序的产出序";Array.Sort 不稳定,
/// ≥16 元素走 introsort 会打乱平局序。20 个单位 dx=0(d/f 全平局, introsort 规模),
/// s 按 ±100 交错成对相等:第一处排序必须保输入序,行内 s 平局必须保该产出序——
/// 槽位 X 沿稳定序严格递增。
/// </summary>
[TestFixture]
public class FormationSlotStabilityTests
{
    private const int Count = 20;

    [Test]
    public void TiedSlots_KeepFirstSortOutputOrder()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(500000), Fix64.FromInt(500000), Count, 1, 0, 0),
            Is.EqualTo(Count));

        // 传送成 dx=0(d/f 全平局),y 按 ±100 交错(s 成对相等)
        for (int i = 0; i < Count; i++)
        {
            long y = 500000L + (i % 2 == 0 ? -100L : 100L);
            session.World.Set(session.Units.EntityAt(i),
                new WorldPositionCm { Value = new Fix64Vec2(Fix64.FromInt(500000), Fix64.FromInt(y)) });
        }

        var members = Enumerable.Range(0, Count).ToArray();
        var shape = runtime.Formations[0];
        CrowdFormations.AssignSlots(session, members, Fix64.OneValue, Fix64.Zero, Fix64.FromDouble(1.1), shape, Fix64.FromInt(10000000));

        // 稳定序 = 第二排序的结果:s 升序(偶数组在前),组内保输入序
        var expected = Enumerable.Range(0, Count).OrderBy(i => i % 2).ThenBy(i => i).ToArray();
        double slotX(int i) => session.World.Get<CrowdSimulationKinematics>(session.Units.EntityAt(i)).SlotOffsetCm.X.ToDouble();
        for (int k = 1; k < Count; k++)
        {
            Assert.That(slotX(expected[k - 1]), Is.LessThan(slotX(expected[k])),
                $"稳定序第 {k} 位(单位 {expected[k]})槽位未严格递增——平局序被打乱");
        }
    }
}
