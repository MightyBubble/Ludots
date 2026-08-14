using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Session;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class RoomControlWireCodecTests
{
    [Test]
    public void ReadyIntent_RoundTripsBothStates()
    {
        foreach (NetworkRoomReadyState state in new[] { NetworkRoomReadyState.Unready, NetworkRoomReadyState.Ready })
        {
            var expected = new NetworkRoomReadyIntent(new SessionEpoch(17), state);
            Span<byte> payload = stackalloc byte[RoomControlWireCodec.ReadyIntentSizeInBytes];

            Assert.That(
                RoomControlWireCodec.TryEncodeReadyIntent(in expected, payload, out int bytesWritten),
                Is.EqualTo(NetworkWireCodecStatus.Success));
            Assert.That(bytesWritten, Is.EqualTo(RoomControlWireCodec.ReadyIntentSizeInBytes));
            Assert.That(
                RoomControlWireCodec.TryDecodeReadyIntent(payload, out NetworkRoomReadyIntent actual),
                Is.EqualTo(NetworkWireCodecStatus.Success));
            Assert.Multiple(() =>
            {
                Assert.That(actual.SessionEpoch, Is.EqualTo(expected.SessionEpoch));
                Assert.That(actual.ReadyState, Is.EqualTo(expected.ReadyState));
            });
        }
    }

    [Test]
    public void RoomSnapshot_RoundTripsFixedSeatTable()
    {
        var seats = new[]
        {
            new NetworkRoomSeatSnapshot(
                0,
                NetworkRoomSeatConnectionState.Connected,
                NetworkRoomReadyState.Ready,
                generation: 3,
                new PlayerId(1)),
            new NetworkRoomSeatSnapshot(
                1,
                NetworkRoomSeatConnectionState.AwaitingReconnect,
                NetworkRoomReadyState.Unready,
                generation: 4,
                new PlayerId(2)),
        };
        var expected = new NetworkRoomSnapshotHeader(
            new SessionEpoch(17),
            revision: 9,
            committedTick: 40,
            countdownRemainingTicks: 0,
            seatCount: 2,
            connectedSeatCount: 1,
            readySeatCount: 1,
            NetworkRoomPhase.Started);
        byte[] payload = new byte[RoomControlWireCodec.GetSnapshotPayloadSize(seats.Length)];
        var decodedSeats = new NetworkRoomSeatSnapshot[2];

        Assert.That(
            RoomControlWireCodec.TryEncodeSnapshot(in expected, seats, payload, out int bytesWritten),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(bytesWritten, Is.EqualTo(payload.Length));
        Assert.That(
            RoomControlWireCodec.TryDecodeSnapshot(payload, decodedSeats, out NetworkRoomSnapshotHeader actual, out int seatCount),
            Is.EqualTo(NetworkWireCodecStatus.Success));

        Assert.Multiple(() =>
        {
            Assert.That(seatCount, Is.EqualTo(2));
            Assert.That(actual.SessionEpoch, Is.EqualTo(expected.SessionEpoch));
            Assert.That(actual.Revision, Is.EqualTo(expected.Revision));
            Assert.That(actual.CommittedTick, Is.EqualTo(expected.CommittedTick));
            Assert.That(actual.Phase, Is.EqualTo(NetworkRoomPhase.Started));
            Assert.That(decodedSeats[0].ReadyState, Is.EqualTo(NetworkRoomReadyState.Ready));
            Assert.That(decodedSeats[1].ConnectionState, Is.EqualTo(NetworkRoomSeatConnectionState.AwaitingReconnect));
            Assert.That(decodedSeats[1].PlayerId.Value, Is.EqualTo(2));
        });
    }

    [Test]
    public void MalformedReadyIntent_IsExplicitlyRejected()
    {
        var intent = new NetworkRoomReadyIntent(new SessionEpoch(1), NetworkRoomReadyState.Ready);
        Span<byte> payload = stackalloc byte[RoomControlWireCodec.ReadyIntentSizeInBytes + 1];
        Assert.That(
            RoomControlWireCodec.TryEncodeReadyIntent(in intent, payload, out int bytesWritten),
            Is.EqualTo(NetworkWireCodecStatus.Success));

        payload[8] = 2;
        Assert.That(
            RoomControlWireCodec.TryDecodeReadyIntent(payload[..bytesWritten], out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidEnum));

        payload[8] = (byte)NetworkRoomReadyState.Ready;
        payload[9] = 1;
        Assert.That(
            RoomControlWireCodec.TryDecodeReadyIntent(payload[..bytesWritten], out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidInput));

        payload[9] = 0;
        Assert.That(
            RoomControlWireCodec.TryDecodeReadyIntent(payload[..(bytesWritten - 1)], out _),
            Is.EqualTo(NetworkWireCodecStatus.MalformedLength));
        Assert.That(
            RoomControlWireCodec.TryDecodeReadyIntent(payload, out _),
            Is.EqualTo(NetworkWireCodecStatus.TrailingBytes));
    }

    [Test]
    public void SnapshotDecode_RejectsMalformedAndInsufficientCapacityWithoutMutatingDestination()
    {
        var seats = new[]
        {
            new NetworkRoomSeatSnapshot(
                0,
                NetworkRoomSeatConnectionState.Connected,
                NetworkRoomReadyState.Unready,
                generation: 1,
                new PlayerId(1)),
            new NetworkRoomSeatSnapshot(1, NetworkRoomSeatConnectionState.Empty, NetworkRoomReadyState.Unready, 0, default),
        };
        var header = new NetworkRoomSnapshotHeader(
            new SessionEpoch(2),
            revision: 3,
            committedTick: 4,
            countdownRemainingTicks: 0,
            seatCount: 2,
            connectedSeatCount: 1,
            readySeatCount: 0,
            NetworkRoomPhase.WaitingForPlayers);
        byte[] payload = new byte[RoomControlWireCodec.GetSnapshotPayloadSize(2)];
        Assert.That(
            RoomControlWireCodec.TryEncodeSnapshot(in header, seats, payload, out _),
            Is.EqualTo(NetworkWireCodecStatus.Success));

        var sentinel = new NetworkRoomSeatSnapshot(
            0,
            NetworkRoomSeatConnectionState.Connected,
            NetworkRoomReadyState.Ready,
            generation: 9,
            new PlayerId(1));
        var tooSmall = new[] { sentinel };
        Assert.That(
            RoomControlWireCodec.TryDecodeSnapshot(payload, tooSmall, out _, out int required),
            Is.EqualTo(NetworkWireCodecStatus.CapacityExhausted));
        Assert.Multiple(() =>
        {
            Assert.That(required, Is.EqualTo(2));
            Assert.That(tooSmall[0].Generation, Is.EqualTo(9));
            Assert.That(tooSmall[0].ReadyState, Is.EqualTo(NetworkRoomReadyState.Ready));
        });

        payload[RoomControlWireCodec.SnapshotHeaderSizeInBytes + 2] = 99;
        var destination = new[] { sentinel, default(NetworkRoomSeatSnapshot) };
        Assert.That(
            RoomControlWireCodec.TryDecodeSnapshot(payload, destination, out _, out _),
            Is.EqualTo(NetworkWireCodecStatus.InvalidEnum));
        Assert.That(destination[0].Generation, Is.EqualTo(9));
    }

    [Test]
    public void CodecSteadyPath_AllocatesZeroBytesAfterWarmup()
    {
        var seats = new[]
        {
            new NetworkRoomSeatSnapshot(
                0,
                NetworkRoomSeatConnectionState.Connected,
                NetworkRoomReadyState.Ready,
                generation: 1,
                new PlayerId(1)),
            new NetworkRoomSeatSnapshot(
                1,
                NetworkRoomSeatConnectionState.Connected,
                NetworkRoomReadyState.Ready,
                generation: 1,
                new PlayerId(2)),
        };
        var header = new NetworkRoomSnapshotHeader(
            new SessionEpoch(5),
            revision: 6,
            committedTick: 7,
            countdownRemainingTicks: 90,
            seatCount: 2,
            connectedSeatCount: 2,
            readySeatCount: 2,
            NetworkRoomPhase.Countdown);
        var intent = new NetworkRoomReadyIntent(new SessionEpoch(5), NetworkRoomReadyState.Ready);
        byte[] snapshotPayload = new byte[RoomControlWireCodec.GetSnapshotPayloadSize(2)];
        byte[] intentPayload = new byte[RoomControlWireCodec.ReadyIntentSizeInBytes];
        var decodedSeats = new NetworkRoomSeatSnapshot[2];

        for (int i = 0; i < 32; i++)
        {
            _ = RoomControlWireCodec.TryEncodeReadyIntent(in intent, intentPayload, out _);
            _ = RoomControlWireCodec.TryDecodeReadyIntent(intentPayload, out _);
            _ = RoomControlWireCodec.TryEncodeSnapshot(in header, seats, snapshotPayload, out _);
            _ = RoomControlWireCodec.TryDecodeSnapshot(snapshotPayload, decodedSeats, out _, out _);
        }

        bool ok = true;
        GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            ok &= RoomControlWireCodec.TryEncodeReadyIntent(in intent, intentPayload, out _) == NetworkWireCodecStatus.Success;
            ok &= RoomControlWireCodec.TryDecodeReadyIntent(intentPayload, out _) == NetworkWireCodecStatus.Success;
            ok &= RoomControlWireCodec.TryEncodeSnapshot(in header, seats, snapshotPayload, out _) == NetworkWireCodecStatus.Success;
            ok &= RoomControlWireCodec.TryDecodeSnapshot(snapshotPayload, decodedSeats, out _, out _) == NetworkWireCodecStatus.Success;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(ok, Is.True);
        Assert.That(allocated, Is.EqualTo(0), $"Expected 0 B allocation, observed {allocated} B.");
    }
}
