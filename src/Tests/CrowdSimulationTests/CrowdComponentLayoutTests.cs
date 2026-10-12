using NUnit.Framework;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Mathematics.FixedPoint;

namespace CrowdSimulationTests;

/// <summary>
/// 人群组件布局合同:热路径组件必须 unmanaged(无引用字段)。编译期断言:任何组件引入引用字段,本文件即编译失败。
/// </summary>
public sealed class CrowdComponentLayoutTests
{
    private static void MustBeUnmanaged<T>() where T : unmanaged
    {
    }

    [Test]
    public void CrowdHotPathComponents_AreUnmanaged()
    {
        MustBeUnmanaged<CrowdSimulationAgent>();
        MustBeUnmanaged<CrowdSimulationUnitState>();
        MustBeUnmanaged<CrowdSimulationKinematics>();
        MustBeUnmanaged<WorldPositionCm>();
        MustBeUnmanaged<PreviousWorldPositionCm>();
        MustBeUnmanaged<PlayerOwner>();
        MustBeUnmanaged<Fix64>();
        MustBeUnmanaged<Fix64Vec2>();
    }
}
