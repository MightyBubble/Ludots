using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Features.OrderLifecycle;

[TestFixture]
public sealed class OrderTerminalResultLedgerTests
{
    [Test]
    public void TerminalOutcome_RemainsQueryableAcrossFramesUntilConsumedAndReleased()
    {
        var buffer = new OrderTerminalResultBuffer(capacity: 4);
        var actor = Entity.Null;

        buffer.Retain(101);
        buffer.Write(new OrderTerminalOutcome(
            orderId: 101,
            orderTypeId: 7,
            OrderTerminalState.Completed,
            OrderFailureReason.None,
            actor));

        buffer.Clear();
        buffer.Clear();
        buffer.Clear();

        Assert.That(buffer.TryGet(101, out OrderTerminalOutcome peeked), Is.True);
        Assert.That(peeked.State, Is.EqualTo(OrderTerminalState.Completed));
        Assert.That(buffer.TryConsume(101, out OrderTerminalOutcome consumed), Is.True);
        Assert.That(consumed.OrderId, Is.EqualTo(101));
        Assert.That(buffer.TryGet(101, out _), Is.False);
        Assert.That(buffer.ReleaseConsumed(101), Is.True);
    }

    [Test]
    public void TerminalOutcome_RejectsDuplicateUnknownAndRepeatedConsumption()
    {
        var buffer = new OrderTerminalResultBuffer(capacity: 4);

        buffer.Write(new OrderTerminalOutcome(
            orderId: 201,
            orderTypeId: 9,
            OrderTerminalState.Failed,
            OrderFailureReason.SubmissionValidationRejected,
            Entity.Null));

        InvalidOperationException duplicate = Assert.Throws<InvalidOperationException>(() =>
            buffer.Write(new OrderTerminalOutcome(
                orderId: 201,
                orderTypeId: 9,
                OrderTerminalState.Cancelled,
                OrderFailureReason.None,
                Entity.Null)))!;
        Assert.That(duplicate.Message, Does.Contain("ORDER.TERMINAL.ERR.DuplicateOrderId"));

        Assert.That(buffer.TryConsume(999, out _), Is.False);
        Assert.That(buffer.TryConsume(201, out _), Is.True);
        Assert.That(buffer.TryConsume(201, out _), Is.False);

        InvalidOperationException repeated = Assert.Throws<InvalidOperationException>(() =>
            buffer.Consume(201))!;
        Assert.That(repeated.Message, Does.Contain("ORDER.TERMINAL.ERR.AlreadyConsumed"));

        InvalidOperationException unknownRelease = Assert.Throws<InvalidOperationException>(() =>
            buffer.ReleaseConsumed(999))!;
        Assert.That(unknownRelease.Message, Does.Contain("ORDER.TERMINAL.ERR.UnknownOrderId"));
        Assert.That(buffer.ReleaseConsumed(201), Is.True);
        InvalidOperationException repeatedRelease = Assert.Throws<InvalidOperationException>(() =>
            buffer.ReleaseConsumed(201))!;
        Assert.That(repeatedRelease.Message, Does.Contain("ORDER.TERMINAL.ERR.UnknownOrderId"));
    }

    [Test]
    public void TerminalOutcome_RetainBeforeTerminalCanBeReleasedBeforeWrite()
    {
        var buffer = new OrderTerminalResultBuffer(capacity: 1);

        buffer.Retain(251);
        Assert.That(buffer.IsAwaitingRetainedOutcome(251), Is.True);
        Assert.That(buffer.Release(251), Is.True);

        buffer.Retain(252);
        InvalidOperationException duplicate = Assert.Throws<InvalidOperationException>(() =>
            buffer.Retain(252))!;
        Assert.That(duplicate.Message, Does.Contain("ORDER.TERMINAL.ERR.DuplicateOrderId"));
        Assert.That(buffer.Release(252), Is.True);
    }

    [Test]
    public void TerminalOutcome_CapacityIsStableLedgerCapacity()
    {
        var buffer = new OrderTerminalResultBuffer(capacity: 2);

        buffer.Write(new OrderTerminalOutcome(
            orderId: 301,
            orderTypeId: 11,
            OrderTerminalState.Completed,
            OrderFailureReason.None,
            Entity.Null));
        buffer.Write(new OrderTerminalOutcome(
            orderId: 302,
            orderTypeId: 11,
            OrderTerminalState.Completed,
            OrderFailureReason.None,
            Entity.Null));

        InvalidOperationException full = Assert.Throws<InvalidOperationException>(() =>
            buffer.Write(new OrderTerminalOutcome(
                orderId: 303,
                orderTypeId: 11,
                OrderTerminalState.Completed,
                OrderFailureReason.None,
                Entity.Null)))!;
        Assert.That(full.Message, Does.Contain("ORDER.TERMINAL.ERR.ResultCapacityExceeded"));

        Assert.That(buffer.TryConsume(301, out _), Is.True);
        Assert.That(buffer.ReleaseConsumed(301), Is.True);
        buffer.Clear();

        buffer.Write(new OrderTerminalOutcome(
            orderId: 303,
            orderTypeId: 11,
            OrderTerminalState.Completed,
            OrderFailureReason.None,
            Entity.Null));
        Assert.That(buffer.TryGet(303, out _), Is.True);
    }

    [Test]
    public void TerminalOutcome_WithoutRetainedConsumer_RemainsUntilExplicitRelease()
    {
        var buffer = new OrderTerminalResultBuffer(capacity: 2);

        buffer.Write(new OrderTerminalOutcome(
            orderId: 401,
            orderTypeId: 11,
            OrderTerminalState.Completed,
            OrderFailureReason.None,
            Entity.Null));
        buffer.Write(new OrderTerminalOutcome(
            orderId: 402,
            orderTypeId: 11,
            OrderTerminalState.Completed,
            OrderFailureReason.None,
            Entity.Null));

        Assert.That(buffer.LedgerCount, Is.EqualTo(2));

        buffer.Clear();
        buffer.Clear();
        buffer.Clear();

        Assert.That(buffer.LedgerCount, Is.EqualTo(2));
        Assert.That(buffer.TryGet(401, out OrderTerminalOutcome outcome), Is.True);
        Assert.That(outcome.State, Is.EqualTo(OrderTerminalState.Completed));

        Assert.That(buffer.TryConsume(401, out _), Is.True);
        Assert.That(buffer.ReleaseConsumed(401), Is.True);
        Assert.That(buffer.Release(402), Is.True);

        buffer.Write(new OrderTerminalOutcome(
            orderId: 403,
            orderTypeId: 11,
            OrderTerminalState.Completed,
            OrderFailureReason.None,
            Entity.Null));

        Assert.That(buffer.TryGet(403, out _), Is.True);
    }
}
