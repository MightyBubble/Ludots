using System.Numerics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.MovePlanning;
using NUnit.Framework;

namespace Ludots.Tests.Gas;

[TestFixture]
public sealed class MovePlanOrderLifecycleTests
{
    private const int MoveOrderTypeId = 17;

    [Test]
    public void ClusteredBatch_AssignsUniqueReceiptsAndPreservesAtomicAdmission()
    {
        using var world = World.Create();
        Entity firstSource = world.Create();
        Entity secondSource = world.Create();
        Entity[] actors = { world.Create(), world.Create(), world.Create() };
        var queue = CreateOrderQueue(capacity: 64);
        Order[] batch =
        {
            CreateOrder(actors[0], firstSource),
            CreateOrder(actors[1], firstSource),
            CreateOrder(actors[2], secondSource),
        };

        Assert.That(queue.TryEnqueueClusteredBatch(batch), Is.EqualTo(OrderSubmitResult.Queued));
        Assert.Multiple(() =>
        {
            Assert.That(batch[0].OrderId, Is.Positive);
            Assert.That(batch[1].OrderId, Is.GreaterThan(batch[0].OrderId));
            Assert.That(batch[2].OrderId, Is.GreaterThan(batch[1].OrderId));
            Assert.That(batch[0].AdmissionBatchId, Is.Positive);
            Assert.That(batch[1].AdmissionBatchId, Is.EqualTo(batch[0].AdmissionBatchId));
            Assert.That(batch[2].AdmissionBatchId, Is.EqualTo(batch[0].AdmissionBatchId));
            Assert.That(batch[0].AdmissionBatchSize, Is.EqualTo(3));
            Assert.That(batch[1].AdmissionBatchSize, Is.EqualTo(3));
            Assert.That(batch[2].AdmissionBatchSize, Is.EqualTo(3));
            Assert.That(batch[0].AdmissionBatchIndex, Is.EqualTo(0));
            Assert.That(batch[1].AdmissionBatchIndex, Is.EqualTo(1));
            Assert.That(batch[2].AdmissionBatchIndex, Is.EqualTo(2));
            Assert.That(queue.Count, Is.EqualTo(3));
        });

        var full = CreateOrderQueue(capacity: 4, resultCapacity: 16);
        for (int i = 0; i < 3; i++)
        {
            Order filler = CreateOrder(actors[0], firstSource);
            Assert.That(full.TryEnqueue(in filler), Is.True);
        }

        Order[] rejected =
        {
            CreateOrder(actors[0], firstSource),
            CreateOrder(actors[1], firstSource),
        };
        Assert.That(full.TryEnqueueClusteredBatch(rejected), Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
        Assert.Multiple(() =>
        {
            Assert.That(full.Count, Is.EqualTo(3));
            Assert.That(rejected[0].OrderId, Is.Positive);
            Assert.That(rejected[1].OrderId, Is.GreaterThan(rejected[0].OrderId));
            Assert.That(rejected[1].AdmissionBatchId, Is.EqualTo(rejected[0].AdmissionBatchId));
            Assert.That(full.AdmissionResults.TryGet(rejected[0].OrderId, OrderAdmissionStage.GlobalIntake, out var outcome), Is.True);
            Assert.That(outcome.Result, Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
        });

        Order[] duplicateActor =
        {
            CreateOrder(actors[0], firstSource),
            CreateOrder(actors[0], firstSource),
        };
        Assert.Throws<InvalidOperationException>(() => queue.TryEnqueueClusteredBatch(duplicateActor));
    }

    [Test]
    public void ClusteredBatch_MissingOrderBufferActivatesNoMembers()
    {
        using var world = World.Create();
        Entity source = world.Create();
        Entity validActor = world.Create(OrderBuffer.CreateEmpty());
        Entity invalidActor = world.Create();
        var queue = CreateOrderQueue(capacity: 64);
        Order[] batch =
        {
            CreateOrder(validActor, source),
            CreateOrder(invalidActor, source),
        };
        Assert.That(queue.TryEnqueueClusteredBatch(batch), Is.EqualTo(OrderSubmitResult.Queued));

        var orderTypes = CreateMoveOrderRegistry(SameTypePolicy.Replace);
        var system = new OrderBufferSystem(
            world,
            new DiscreteClock(),
            orderTypes,
            new OrderRuleRegistry(),
            queue.AdmissionResults,
            queue);

        queue.AdmissionResults.BeginLogicStep();
        system.Update(0f);
        queue.AdmissionResults.EndLogicStep();

        Assert.Multiple(() =>
        {
            Assert.That(world.Get<OrderBuffer>(validActor).HasActive, Is.False);
            Assert.That(system.IncomingRevision, Is.Zero);
            Assert.That(queue.Count, Is.Zero);
        });
    }

    [Test]
    public void ClusteredBatch_BlockedMemberDoesNotActivateEarlierMembers()
    {
        using var world = World.Create();
        Entity source = world.Create();
        Entity firstActor = world.Create(OrderBuffer.CreateEmpty());
        Entity blockedActor = world.Create(OrderBuffer.CreateEmpty());
        Order existing = CreateOrder(blockedActor, source);
        existing.OrderId = 99;
        world.Get<OrderBuffer>(blockedActor).SetActiveDirect(in existing, priority: 100);

        var queue = CreateOrderQueue(capacity: 64);
        Order[] batch =
        {
            CreateOrder(firstActor, source),
            CreateOrder(blockedActor, source),
        };
        Assert.That(queue.TryEnqueueClusteredBatch(batch), Is.EqualTo(OrderSubmitResult.Queued));

        var orderTypes = CreateMoveOrderRegistry(SameTypePolicy.Ignore, canInterruptSelf: false);
        var system = new OrderBufferSystem(
            world,
            new DiscreteClock(),
            orderTypes,
            new OrderRuleRegistry(),
            queue.AdmissionResults,
            queue);

        queue.AdmissionResults.BeginLogicStep();
        system.Update(0f);
        queue.AdmissionResults.EndLogicStep();

        Assert.Multiple(() =>
        {
            Assert.That(world.Get<OrderBuffer>(firstActor).HasActive, Is.False);
            Assert.That(world.Get<OrderBuffer>(blockedActor).ActiveOrder.Order.OrderId, Is.EqualTo(99));
            Assert.That(system.IncomingRevision, Is.Zero);
        });
    }

    [Test]
    public void MovePlanResult_CompletesOnlyTheMatchingActiveOrder()
    {
        using var world = World.Create();
        Entity actor = world.Create(
            OrderBuffer.CreateEmpty(),
            new OrderContinuationBuffer(),
            default(MovePlanExecutionIntent),
            default(MovePlanExecutionResult));
        var orderTypes = CreateOrderTypeRegistry();
        orderTypes.Register(new OrderTypeConfig
        {
            Key = "test.movePlan",
            OrderTypeId = MoveOrderTypeId,
            Priority = 100,
            CanInterruptSelf = true,
        });

        Order active = CreateOrder(actor, Entity.Null);
        active.OrderId = 41;
        active.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
        active.Args.Spatial.Mode = OrderCollectionMode.Single;
        active.Args.Spatial.WorldCm = new Vector3(1200f, 0f, 900f);
        world.Get<OrderBuffer>(actor).SetActiveDirect(in active, priority: 100);

        var projection = new MovePlanOrderProjectionSystem(world, MoveOrderTypeId);
        projection.Update(0f);
        MovePlanExecutionIntent intent = world.Get<MovePlanExecutionIntent>(actor);
        Assert.Multiple(() =>
        {
            Assert.That(intent.CommandGroupToken, Is.EqualTo(41));
            Assert.That(intent.TargetWorldCm, Is.EqualTo(new Vector2(1200f, 900f)));
            Assert.That(intent.Mode, Is.EqualTo(MovePlanExecutionMode.CommandGroup));
            Assert.That(intent.HasTarget, Is.EqualTo(1));
        });

        world.Set(actor, new MovePlanExecutionResult
        {
            CommandGroupToken = 40,
            Kind = MovePlanExecutionResultKind.Arrived,
        });
        var lifecycle = new MovePlanOrderLifecycleSystem(world, orderTypes, MoveOrderTypeId);
        lifecycle.Update(0f);
        Assert.That(world.Get<OrderBuffer>(actor).HasActive, Is.True);

        world.Set(actor, new MovePlanExecutionResult
        {
            CommandGroupToken = 41,
            Kind = MovePlanExecutionResultKind.Arrived,
        });
        lifecycle.Update(0f);
        Assert.Multiple(() =>
        {
            Assert.That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
            Assert.That(world.Get<MovePlanExecutionIntent>(actor).HasTarget, Is.Zero);
            Assert.That(world.Get<MovePlanExecutionResult>(actor).Kind, Is.EqualTo(MovePlanExecutionResultKind.None));
        });
    }

    [Test]
    public void SharedAdmissionBatch_ProjectsOneCommandGroupToken_AndCompletesEachMember()
    {
        using var world = World.Create();
        Entity source = world.Create();
        Entity firstActor = world.Create(
            OrderBuffer.CreateEmpty(),
            new OrderContinuationBuffer(),
            default(MovePlanExecutionIntent),
            default(MovePlanExecutionResult));
        Entity secondActor = world.Create(
            OrderBuffer.CreateEmpty(),
            new OrderContinuationBuffer(),
            default(MovePlanExecutionIntent),
            default(MovePlanExecutionResult));

        var queue = CreateOrderQueue(capacity: 16);
        Order[] batch =
        {
            CreateOrder(firstActor, source),
            CreateOrder(secondActor, source),
        };
        Assert.That(queue.TryEnqueueClusteredBatch(batch), Is.EqualTo(OrderSubmitResult.Queued));

        world.Get<OrderBuffer>(firstActor).SetActiveDirect(in batch[0], priority: 100);
        world.Get<OrderBuffer>(secondActor).SetActiveDirect(in batch[1], priority: 100);
        int expectedToken = batch[0].AdmissionBatchId;

        new MovePlanOrderProjectionSystem(world, MoveOrderTypeId).Update(0f);

        MovePlanExecutionIntent firstIntent = world.Get<MovePlanExecutionIntent>(firstActor);
        MovePlanExecutionIntent secondIntent = world.Get<MovePlanExecutionIntent>(secondActor);
        Assert.Multiple(() =>
        {
            Assert.That(expectedToken, Is.Positive);
            Assert.That(batch[1].AdmissionBatchId, Is.EqualTo(expectedToken));
            Assert.That(firstIntent.CommandGroupToken, Is.EqualTo(expectedToken));
            Assert.That(secondIntent.CommandGroupToken, Is.EqualTo(expectedToken));
            Assert.That(firstIntent.HasTarget, Is.EqualTo(1));
            Assert.That(secondIntent.HasTarget, Is.EqualTo(1));
        });

        var orderTypes = CreateMoveOrderRegistry(SameTypePolicy.Replace);
        world.Set(firstActor, new MovePlanExecutionResult
        {
            CommandGroupToken = expectedToken,
            Kind = MovePlanExecutionResultKind.Arrived,
        });
        world.Set(secondActor, new MovePlanExecutionResult
        {
            CommandGroupToken = expectedToken,
            Kind = MovePlanExecutionResultKind.Arrived,
        });

        new MovePlanOrderLifecycleSystem(world, orderTypes, MoveOrderTypeId).Update(0f);

        Assert.Multiple(() =>
        {
            Assert.That(world.Get<OrderBuffer>(firstActor).HasActive, Is.False);
            Assert.That(world.Get<OrderBuffer>(secondActor).HasActive, Is.False);
            Assert.That(orderTypes.TerminalResults.Count, Is.EqualTo(2));
            Assert.That(
                new[] { orderTypes.TerminalResults[0].OrderId, orderTypes.TerminalResults[1].OrderId },
                Is.EquivalentTo(new[] { batch[0].OrderId, batch[1].OrderId }));
        });
    }

    [Test]
    public void SharedAdmissionBatch_TokenDoesNotCollideWithPriorSingleOrder()
    {
        using var world = World.Create();
        Entity source = world.Create();
        Entity singleActor = world.Create(
            OrderBuffer.CreateEmpty(),
            new OrderContinuationBuffer(),
            default(MovePlanExecutionIntent),
            default(MovePlanExecutionResult));
        Entity firstBatchActor = world.Create(
            OrderBuffer.CreateEmpty(),
            new OrderContinuationBuffer(),
            default(MovePlanExecutionIntent),
            default(MovePlanExecutionResult));
        Entity secondBatchActor = world.Create(
            OrderBuffer.CreateEmpty(),
            new OrderContinuationBuffer(),
            default(MovePlanExecutionIntent),
            default(MovePlanExecutionResult));

        var queue = CreateOrderQueue(capacity: 16);
        Order single = CreateOrder(singleActor, source);
        Assert.That(queue.TryEnqueue(in single), Is.True);
        Assert.That(queue.TryDequeue(out single), Is.True);

        Order[] batch =
        {
            CreateOrder(firstBatchActor, source),
            CreateOrder(secondBatchActor, source),
        };
        Assert.That(queue.TryEnqueueClusteredBatch(batch), Is.EqualTo(OrderSubmitResult.Queued));

        world.Get<OrderBuffer>(singleActor).SetActiveDirect(in single, priority: 100);
        world.Get<OrderBuffer>(firstBatchActor).SetActiveDirect(in batch[0], priority: 100);
        world.Get<OrderBuffer>(secondBatchActor).SetActiveDirect(in batch[1], priority: 100);

        new MovePlanOrderProjectionSystem(world, MoveOrderTypeId).Update(0f);

        int singleToken = world.Get<MovePlanExecutionIntent>(singleActor).CommandGroupToken;
        int batchToken = world.Get<MovePlanExecutionIntent>(firstBatchActor).CommandGroupToken;
        Assert.Multiple(() =>
        {
            Assert.That(singleToken, Is.EqualTo(single.OrderId));
            Assert.That(batchToken, Is.EqualTo(batch[0].AdmissionBatchId));
            Assert.That(batchToken, Is.EqualTo(batch[0].OrderId));
            Assert.That(batchToken, Is.Not.EqualTo(singleToken));
            Assert.That(world.Get<MovePlanExecutionIntent>(secondBatchActor).CommandGroupToken, Is.EqualTo(batchToken));
        });
    }

    [Test]
    public void InvalidMovePayload_ProjectsTypedFailureAndCancelsMatchingOrder()
    {
        using var world = World.Create();
        Entity actor = world.Create(
            OrderBuffer.CreateEmpty(),
            new OrderContinuationBuffer(),
            default(MovePlanExecutionIntent),
            default(MovePlanExecutionResult));
        Order invalid = CreateOrder(actor, Entity.Null);
        invalid.OrderId = 77;
        invalid.Args.Spatial = default;
        world.Get<OrderBuffer>(actor).SetActiveDirect(in invalid, priority: 100);
        Order followUp = CreateOrder(actor, Entity.Null);
        followUp.OrderTypeId = MoveOrderTypeId;
        Assert.That(world.Get<OrderContinuationBuffer>(actor).TryAdd(invalid.OrderId, in followUp), Is.True);
        OrderTypeRegistry orderTypes = CreateMoveOrderRegistry(SameTypePolicy.Replace);

        new MovePlanOrderProjectionSystem(world, MoveOrderTypeId).Update(0f);
        MovePlanExecutionResult result = world.Get<MovePlanExecutionResult>(actor);
        Assert.Multiple(() =>
        {
            Assert.That(result.CommandGroupToken, Is.EqualTo(77));
            Assert.That(result.Kind, Is.EqualTo(MovePlanExecutionResultKind.Failed));
            Assert.That(result.FailureReason, Is.EqualTo(MovePlanFailureReason.ExecutionUnavailable));
            Assert.That(world.Get<MovePlanExecutionIntent>(actor).HasTarget, Is.Zero);
        });

        new MovePlanOrderLifecycleSystem(world, orderTypes, MoveOrderTypeId).Update(0f);

        Assert.Multiple(() =>
        {
            Assert.That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
            Assert.That(world.Get<OrderContinuationBuffer>(actor).HasEntries, Is.False);
            Assert.That(orderTypes.TerminalResults.Count, Is.EqualTo(1));
            Assert.That(orderTypes.TerminalResults[0].OrderId, Is.EqualTo(77));
            Assert.That(orderTypes.TerminalResults[0].State, Is.EqualTo(OrderTerminalState.Cancelled));
        });
    }

    [Test]
    public void Projection_DoesNotOverwriteIndividualMovePlanContract()
    {
        using var world = World.Create();
        MovePlanExecutionIntent individual = new()
        {
            TargetWorldCm = new Vector2(500f, 700f),
            StopRadiusCm = 25f,
            HasTarget = 1,
            Mode = MovePlanExecutionMode.Individual,
        };
        Entity actor = world.Create(
            OrderBuffer.CreateEmpty(),
            individual,
            default(MovePlanExecutionResult));

        new MovePlanOrderProjectionSystem(world, MoveOrderTypeId).Update(0f);

        MovePlanExecutionIntent actual = world.Get<MovePlanExecutionIntent>(actor);
        Assert.Multiple(() =>
        {
            Assert.That(actual.Mode, Is.EqualTo(MovePlanExecutionMode.Individual));
            Assert.That(actual.TargetWorldCm, Is.EqualTo(individual.TargetWorldCm));
            Assert.That(actual.StopRadiusCm, Is.EqualTo(individual.StopRadiusCm));
            Assert.That(actual.HasTarget, Is.EqualTo(1));
        });
    }

    private static Order CreateOrder(Entity actor, Entity source)
    {
        return new Order
        {
            OrderTypeId = MoveOrderTypeId,
            Actor = actor,
            CommandSource = source,
            Args = new OrderArgs
            {
                Spatial = new OrderSpatial
                {
                    Kind = OrderSpatialKind.WorldCm,
                    Mode = OrderCollectionMode.Single,
                    WorldCm = new Vector3(100f, 0f, 200f),
                },
            },
        };
    }

    private static OrderTypeRegistry CreateMoveOrderRegistry(
        SameTypePolicy sameTypePolicy,
        bool canInterruptSelf = true)
    {
        var registry = CreateOrderTypeRegistry();
        registry.Register(new OrderTypeConfig
        {
            Key = "test.movePlan",
            OrderTypeId = MoveOrderTypeId,
            Priority = 100,
            SameTypePolicy = sameTypePolicy,
            CanInterruptSelf = canInterruptSelf,
        });
        return registry;
    }

    private static OrderQueue CreateOrderQueue(int capacity, int? resultCapacity = null)
    {
        int admissionCapacity = resultCapacity ?? capacity;
        return new OrderQueue(capacity, new OrderAdmissionResultBuffer(admissionCapacity, admissionCapacity));
    }

    private static OrderTypeRegistry CreateOrderTypeRegistry()
    {
        return new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
    }
}
