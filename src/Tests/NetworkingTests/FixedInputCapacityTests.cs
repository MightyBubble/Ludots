using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
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
    public void ProtocolConfig_SupportsPhysics3DFloor_150x64x12_AndFailFastOnOversizedBatch()
    {
        int maxFrames = FixedInputWireCodec.GetMaxFrameCountForDatagram(1200, 12);
        FixedInputProtocolConfig config = FixedInputProtocolConfig.CreatePhysics3DDefaultFloor(
            schemaId: 1,
            sessionEpoch: 1,
            maxFutureTicks: 8,
            maxFramesPerBatch: Math.Min(16, maxFrames),
            maxDatagramPayloadBytes: 1200);

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
    public void Acknowledgement_AlwaysFitsStandardDatagram()
    {
        Assert.DoesNotThrow(() => FixedInputWireCodec.ValidateAcknowledgementFitsDatagram(1200));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FixedInputWireCodec.ValidateAcknowledgementFitsDatagram(
                NetworkWireEnvelope.SizeInBytes + NetworkFixedInputAcknowledgement.SizeInBytes - 1));
    }
}
