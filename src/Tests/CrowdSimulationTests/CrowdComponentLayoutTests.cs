using NUnit.Framework;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Mathematics.FixedPoint;

namespace CrowdSimulationTests;

/// <summary>
/// 人群组件布局合同:热路径组件必须 unmanaged(无引用字段)——这是 tick 级 SoA 舞台
/// (gather→子步→scatter)可整块搬移的前提。编译期断言:任何组件引入引用字段,本文件
/// 即编译失败。唯一例外 CrowdSimulationAgent 是配置载体(字符串 id/可空覆写),不进
/// 子步热环——gather 每 tick 解析其 Fix64 值进 SoA。
/// </summary>
public sealed class CrowdComponentLayoutTests
{
    private static void MustBeUnmanaged<T>() where T : unmanaged
    {
    }

    [Test]
    public void CrowdHotPathComponents_AreUnmanaged()
    {
        MustBeUnmanaged<CrowdSimulationUnitState>();
        MustBeUnmanaged<CrowdSimulationKinematics>();
        MustBeUnmanaged<WorldPositionCm>();
        MustBeUnmanaged<PreviousWorldPositionCm>();
        MustBeUnmanaged<PlayerOwner>();
        MustBeUnmanaged<Fix64>();
        MustBeUnmanaged<Fix64Vec2>();
    }
}
