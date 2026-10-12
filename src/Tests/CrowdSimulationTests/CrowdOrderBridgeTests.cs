using System.Numerics;
using Arch.Core;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests;

/// <summary>
/// 现场群体订单：地面点取 WorldCm 的 X 与 Z，并在会话步进前由外部消费者收走。
/// </summary>
public sealed class CrowdOrderBridgeTests
{
    [Test]
    public void PlainRightClick_ReadsWorldCmXAndZAsIntegers()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s1337");
        int spawned = CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 2, 1, 0, 0);
        Assert.That(spawned, Is.EqualTo(2));
        var batch = new[]
        {
            OrderAt(session, 0),
            OrderAt(session, 1),
        };

        CrowdSimulationOrderBridgeSystem.Accept(session, batch);

        Assert.That(session.Commands.Log, Has.Count.EqualTo(1));
        var order = session.Commands.Log[0].Cmd;
        Assert.That(order.XCm, Is.EqualTo(120000));
        Assert.That(order.YCm, Is.EqualTo(800000));
        Assert.That(order.ShapeId, Is.EqualTo(CrowdSimulationOrderBridgeSystem.PlainClickShapeId));
        Assert.That(order.Members, Has.Length.EqualTo(2));
    }

    [Test]
    public void StepWithoutDispatch_DoesNotLogTheCrowdOrder()
    {
        var (session, system, queue, results) = OpenBridge();
        results.BeginLogicStep();
        Assert.That(queue.TryEnqueueSharedBatch(Batch(session)), Is.EqualTo(OrderSubmitResult.Queued));
        Assert.That(session.Step(out _), Is.True);
        Assert.That(session.Commands.Log, Is.Empty);
        Assert.That(queue.Count, Is.EqualTo(2));
    }

    [Test]
    public void DispatchAtTickZero_LogsOnce_AndStepDoesNotDuplicate()
    {
        var (session, system, queue, results) = OpenBridge();
        Assert.That(session.TickCount, Is.EqualTo(0));
        results.BeginLogicStep();
        Assert.That(queue.TryEnqueueSharedBatch(Batch(session)), Is.EqualTo(OrderSubmitResult.Queued));
        system.DispatchExternalOrderConsumers();
        Assert.That(session.Commands.Log, Has.Count.EqualTo(1));
        Assert.That(session.Commands.Log[0].Cmd.XCm, Is.EqualTo(120000));
        Assert.That(session.Commands.Log[0].Cmd.YCm, Is.EqualTo(800000));
        Assert.That(session.TickCount, Is.EqualTo(0));
        Assert.That(session.Step(out _), Is.True);
        Assert.That(session.Commands.Log, Has.Count.EqualTo(1));
        Assert.That(session.TickCount, Is.EqualTo(1));
    }

    private static (CrowdSimSession Session, OrderBufferSystem System, OrderQueue Queue, OrderAdmissionResultBuffer Results) OpenBridge()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s1337");
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 2, 1, 0, 0), Is.EqualTo(2));
        var results = new OrderAdmissionResultBuffer(16, 16);
        var queue = new OrderQueue(16, results);
        var system = new OrderBufferSystem(
            session.World,
            new DiscreteClock(),
            new OrderTypeRegistry(new OrderTerminalResultBuffer(8)),
            new OrderRuleRegistry(),
            results,
            queue,
            closeEntityIntakeOnUpdate: false);
        system.RegisterExternalOrderConsumer(1, batch => CrowdSimulationOrderBridgeSystem.Accept(session, batch));
        return (session, system, queue, results);
    }

    private static Order[] Batch(CrowdSimSession session) => new[] { OrderAt(session, 0), OrderAt(session, 1) };

    private static Order OrderAt(CrowdSimSession session, int dense) => new()
    {
        OrderTypeId = 1,
        PlayerId = 1,
        Actor = session.Units.EntityAt(dense),
        Args = OrderArgs.CreateSingleWorldCm(new Vector3(120000f, 0f, 800000f)),
    };
}
