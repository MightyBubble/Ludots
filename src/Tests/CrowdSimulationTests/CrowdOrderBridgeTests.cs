using System.Numerics;
using Arch.Core;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests;

/// <summary>
/// 现场群体订单：地面点取 WorldCm 的 X 与 Z。接入在 AbilityActivation 排空，下一拍会话步进才记日志。
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

        CrowdSimulationOrderBridge.Accept(session, batch);
        Assert.That(session.Commands.Log, Is.Empty);
        Assert.That(session.Step(out _), Is.True);

        Assert.That(session.Commands.Log, Has.Count.EqualTo(1));
        var order = session.Commands.Log[0].Cmd;
        Assert.That(order.XCm, Is.EqualTo(120000));
        Assert.That(order.YCm, Is.EqualTo(800000));
        Assert.That(order.ShapeId, Is.EqualTo(CrowdSimulationOrderBridge.PlainClickShapeId));
        Assert.That(order.HasAuto, Is.False);
        Assert.That(order.Members, Has.Length.EqualTo(2));
    }

    [Test]
    public void Prefs_CopyShapeAndMagicBoxOntoTheScheduledOrder()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s1337");
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 2, 1, 0, 0), Is.EqualTo(2));
        var prefs = new CrowdPlayerOrderPrefs { ShapeId = "box", MagicBox = false };
        CrowdSimulationOrderBridge.Accept(session, new[] { OrderAt(session, 0), OrderAt(session, 1) }, prefs);
        Assert.That(session.Step(out _), Is.True);
        var order = session.Commands.Log[0].Cmd;
        Assert.That(order.HasAuto, Is.True);
        Assert.That(order.Auto, Is.False);
        Assert.That(order.ShapeId, Is.EqualTo("box"));
    }

    [Test]
    public void NextStepAfterAbilityActivation_LogsTheCrowdOrderOnce()
    {
        var (session, system, queue, results) = OpenBridge();
        results.BeginLogicStep();
        Assert.That(queue.TryEnqueueSharedBatch(Batch(session)), Is.EqualTo(OrderSubmitResult.Queued));
        system.Update(0f);
        Assert.That(session.Commands.Log, Is.Empty);
        Assert.That(session.Step(out _), Is.True);
        Assert.That(session.Commands.Log, Has.Count.EqualTo(1));
        Assert.That(session.Commands.Log[0].Cmd.XCm, Is.EqualTo(120000));
        Assert.That(session.Commands.Log[0].Cmd.YCm, Is.EqualTo(800000));
        Assert.That(session.Step(out _), Is.True);
        Assert.That(session.Commands.Log, Has.Count.EqualTo(1));
    }

    [Test]
    public void CrowdBatchBehindMoveTo_ReachesConsumerInTheSameDrain()
    {
        using var world = World.Create();
        Entity mover = world.Create(OrderBuffer.CreateEmpty());
        var results = new OrderAdmissionResultBuffer(16, 16);
        var queue = new OrderQueue(16, results);
        var types = new OrderTypeRegistry(new OrderTerminalResultBuffer(8));
        types.Register(new OrderTypeConfig
        {
            Key = "moveTo",
            OrderTypeId = 2,
            Priority = 100,
            CanInterruptSelf = true,
            AllowQueuedMode = true,
            ClearQueueOnActivate = true,
            BufferWindowMs = 500,
            SameTypePolicy = SameTypePolicy.Queue,
            MaxQueueSize = 3,
            SpatialBlackboardKey = -1,
            EntityBlackboardKey = -1,
            IntArg0BlackboardKey = -1,
        });
        var system = new OrderBufferSystem(
            world,
            new DiscreteClock(),
            types,
            new OrderRuleRegistry(),
            results,
            queue,
            closeEntityIntakeOnUpdate: false);
        int seen = 0;
        system.RegisterExternalOrderConsumer(1, batch => seen += batch.Length);
        var move = new[] { new Order { OrderTypeId = 2, PlayerId = 1, Actor = mover } };
        var crowd = new[] { new Order { OrderTypeId = 1, PlayerId = 1, Actor = mover } };
        results.BeginLogicStep();
        Assert.That(queue.TryEnqueueSharedBatch(move), Is.EqualTo(OrderSubmitResult.Queued));
        Assert.That(queue.TryEnqueueSharedBatch(crowd), Is.EqualTo(OrderSubmitResult.Queued));

        system.Update(0f);

        Assert.That(seen, Is.EqualTo(1));
        Assert.That(queue.Count, Is.EqualTo(0));
        Assert.That(results.TryGet(move[0].OrderId, OrderAdmissionStage.EntityIntake, out OrderAdmissionOutcome moveIntake), Is.True);
        Assert.That(moveIntake.Result, Is.EqualTo(OrderSubmitResult.Activated).Or.EqualTo(OrderSubmitResult.Queued));
        Assert.That(results.TryGet(crowd[0].OrderId, OrderAdmissionStage.EntityIntake, out OrderAdmissionOutcome crowdIntake), Is.True);
        Assert.That(crowdIntake.Result, Is.EqualTo(OrderSubmitResult.Activated));
    }

    [Test]
    public void MissingOrderBufferSystem_ThrowsWhenBridgeIsInstalled()
    {
        using var engine = new GameEngine();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CrowdSimulationOrderBridge.Register(engine, engine.CrowdRuntime));
        Assert.That(ex!.Message, Does.Contain("OrderBufferSystem"));
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
        system.RegisterExternalOrderConsumer(1, batch => CrowdSimulationOrderBridge.Accept(session, batch));
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
