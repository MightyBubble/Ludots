using System.Reflection;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Simulation;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class FixedInputCapacityTests
{
    [Test]
    public void ValidateBatchFitsDatagram_AcceptsBoundaryAndRejectsOverflowWithoutTruncation()
    {
        const int maxDatagram = 1200;
        const int payloadBytes = 12;
        int maxFrames = FixedInputWireCodec.GetMaxFrameCountForDatagram(maxDatagram, payloadBytes);
        Assert.That(maxFrames, Is.GreaterThan(0));

        Assert.DoesNotThrow(() => FixedInputWireCodec.ValidateBatchFitsDatagram(maxDatagram, payloadBytes, maxFrames));
        Assert.That(
            FixedInputWireCodec.GetFramedBatchSize(payloadBytes, maxFrames),
            Is.LessThanOrEqualTo(maxDatagram));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FixedInputWireCodec.ValidateBatchFitsDatagram(maxDatagram, payloadBytes, maxFrames + 1));
    }

    [Test]
    public void ProtocolConfig_AcceptsExplicitConsumerFloor_AndFailFastOnOversizedBatch()
    {
        // Consumer sizing (e.g. Physics3D 150×64×12) is supplied explicitly — not a Core factory.
        int maxFrames = FixedInputWireCodec.GetMaxFrameCountForDatagram(1200, 12);
        var config = new FixedInputProtocolConfig(
            seatCapacity: 150,
            historyTicksPerSeat: 64,
            schemaId: 1,
            framePayloadBytes: 12,
            maxFutureTicks: 8,
            maxFramesPerBatch: Math.Min(16, maxFrames),
            maxDatagramPayloadBytes: 1200,
            sessionEpoch: 1);

        Assert.Multiple(() =>
        {
            Assert.That(config.SeatCapacity, Is.EqualTo(150));
            Assert.That(config.HistoryTicksPerSeat, Is.EqualTo(64));
            Assert.That(config.FramePayloadBytes, Is.EqualTo(12));
        });

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FixedInputProtocolConfig(
                seatCapacity: 2,
                historyTicksPerSeat: 8,
                schemaId: 1,
                framePayloadBytes: 12,
                maxFutureTicks: 4,
                maxFramesPerBatch: maxFrames + 1,
                maxDatagramPayloadBytes: 1200,
                sessionEpoch: 1));
    }

    [Test]
    public void ProtocolConfig_Default_FailsEnsureValid_AndIsRejectedByConsumingConstructors()
    {
        FixedInputProtocolConfig invalid = default;
        Assert.Throws<ArgumentOutOfRangeException>(() => invalid.EnsureValid());

        var ticks = new AuthoritativeSimulationTickState();
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuthoritativeFixedInputIngress(invalid, ticks));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedInputClientOutbox(invalid, pendingFrameCapacity: 8));
    }

    [Test]
    public void ProtocolConfig_RequiresPositivePayload_AndHistoryAtLeastMaxFuture()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FixedInputProtocolConfig(
                seatCapacity: 2,
                historyTicksPerSeat: 8,
                schemaId: 1,
                framePayloadBytes: 0,
                maxFutureTicks: 4,
                maxFramesPerBatch: 4,
                maxDatagramPayloadBytes: 1200,
                sessionEpoch: 1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FixedInputProtocolConfig(
                seatCapacity: 2,
                historyTicksPerSeat: 4,
                schemaId: 1,
                framePayloadBytes: 12,
                maxFutureTicks: 8,
                maxFramesPerBatch: 4,
                maxDatagramPayloadBytes: 1200,
                sessionEpoch: 1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FixedInputProtocolConfig(
                seatCapacity: 2,
                historyTicksPerSeat: 8,
                schemaId: 1,
                framePayloadBytes: 12,
                maxFutureTicks: 0,
                maxFramesPerBatch: 4,
                maxDatagramPayloadBytes: 1200,
                sessionEpoch: 1));
    }

    [Test]
    public void ProtocolConfig_RejectsImpossibleArrayLayouts_InConstructor()
    {
        // SeatCapacity * HistoryTicksPerSeat exceeds Array.MaxLength.
        int overflowingSeats = (Array.MaxLength / 2) + 1;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FixedInputProtocolConfig(
                seatCapacity: overflowingSeats,
                historyTicksPerSeat: 2,
                schemaId: 1,
                framePayloadBytes: 1,
                maxFutureTicks: 1,
                maxFramesPerBatch: 1,
                maxDatagramPayloadBytes: 1200,
                sessionEpoch: 1));

        // Cell count fits, but cellCount * FramePayloadBytes exceeds Array.MaxLength.
        int largeHistory = (Array.MaxLength / 2) + 1;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FixedInputProtocolConfig(
                seatCapacity: 1,
                historyTicksPerSeat: largeHistory,
                schemaId: 1,
                framePayloadBytes: 3,
                maxFutureTicks: 1,
                maxFramesPerBatch: 1,
                maxDatagramPayloadBytes: 1200,
                sessionEpoch: 1));
    }

    [Test]
    public void Acknowledgement_AlwaysFitsStandardDatagram()
    {
        Assert.DoesNotThrow(() => FixedInputWireCodec.ValidateAcknowledgementFitsDatagram(1200));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FixedInputWireCodec.ValidateAcknowledgementFitsDatagram(
                NetworkWireEnvelope.SizeInBytes + NetworkFixedInputAcknowledgement.SizeInBytes - 1));
    }

    [Test]
    public void ProtocolConfig_HasNoPhysics3DFactoryOrMinimumConstants()
    {
        Type type = typeof(FixedInputProtocolConfig);
        const BindingFlags staticPublic = BindingFlags.Public | BindingFlags.Static;
        Assert.That(type.GetMethod("CreatePhysics3DDefaultFloor", staticPublic), Is.Null);
        Assert.That(type.GetField("MinimumSupportedSeatCapacity", staticPublic), Is.Null);
        Assert.That(type.GetField("MinimumSupportedHistoryTicks", staticPublic), Is.Null);
        Assert.That(type.GetField("MinimumSupportedPayloadBytes", staticPublic), Is.Null);
    }
}
