using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Orders;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Features.OrderLifecycle;

[TestFixture]
public sealed class TypedOrderBuilderTests
{
    [Test]
    public void CreateCastAbility_EncodesNamedSlotAndRejectsInvalidSlot()
    {
        using var world = World.Create();
        Entity actor = world.Create();
        Entity target = world.Create();

        Order order = OrderBuilder.CreateCastAbility(
            orderTypeId: 100,
            playerId: 2,
            actor,
            target,
            targetContext: Entity.Null,
            abilitySlotIndex: 3,
            OrderSubmitMode.Queued,
            submitStep: 17);

        Assert.That(order.OrderTypeId, Is.EqualTo(100));
        Assert.That(order.PlayerId, Is.EqualTo(2));
        Assert.That(order.Actor, Is.EqualTo(actor));
        Assert.That(order.Target, Is.EqualTo(target));
        Assert.That(order.SubmitMode, Is.EqualTo(OrderSubmitMode.Queued));
        Assert.That(order.SubmitStep, Is.EqualTo(17));
        Assert.That(order.Args.I0, Is.EqualTo(3));

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            OrderBuilder.CreateCastAbility(
                orderTypeId: 100,
                playerId: 2,
                actor,
                target,
                targetContext: Entity.Null,
                abilitySlotIndex: -1,
                OrderSubmitMode.Immediate,
                submitStep: 17))!;
        Assert.That(ex.Message, Does.Contain("ORDER.BUILDER.ERR.InvalidAbilitySlot"));
    }

    [Test]
    public void CreateMoveToWorldCm_EncodesNamedDestinationAndRejectsTargetEntity()
    {
        using var world = World.Create();
        Entity actor = world.Create();
        Entity target = world.Create();
        var destination = new Vector3(120f, 0f, -45f);

        Order order = OrderBuilder.CreateMoveToWorldCm(
            orderTypeId: 101,
            playerId: 4,
            actor,
            destination,
            OrderSubmitMode.Immediate,
            submitStep: 18);

        Assert.That(order.OrderTypeId, Is.EqualTo(101));
        Assert.That(order.Actor, Is.EqualTo(actor));
        Assert.That(order.Target, Is.EqualTo(Entity.Null));
        Assert.That(order.Args.Spatial.Kind, Is.EqualTo(OrderSpatialKind.WorldCm));
        Assert.That(order.Args.Spatial.Mode, Is.EqualTo(OrderCollectionMode.Single));
        Assert.That(order.Args.Spatial.WorldCm, Is.EqualTo(destination));

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            OrderBuilder.CreateMoveToWorldCm(
                orderTypeId: 101,
                playerId: 4,
                actor,
                target,
                destination,
                OrderSubmitMode.Immediate,
                submitStep: 18))!;
        Assert.That(ex.Message, Does.Contain("ORDER.BUILDER.ERR.MoveTargetEntityForbidden"));
    }

    [Test]
    public void CreateStop_RejectsTargetOrSpatialPayload()
    {
        using var world = World.Create();
        Entity actor = world.Create();
        Entity target = world.Create();

        Order order = OrderBuilder.CreateStop(
            orderTypeId: 102,
            playerId: 1,
            actor,
            OrderSubmitMode.Immediate,
            submitStep: 19);

        Assert.That(order.OrderTypeId, Is.EqualTo(102));
        Assert.That(order.Actor, Is.EqualTo(actor));
        Assert.That(order.Target, Is.EqualTo(Entity.Null));
        Assert.That(order.Args.Spatial.Kind, Is.EqualTo(OrderSpatialKind.None));

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            OrderBuilder.CreateStop(
                orderTypeId: 102,
                playerId: 1,
                actor,
                target,
                OrderSubmitMode.Immediate,
                submitStep: 19))!;
        Assert.That(ex.Message, Does.Contain("ORDER.BUILDER.ERR.StopTargetForbidden"));
    }
}
