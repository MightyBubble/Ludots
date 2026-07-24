using System.Diagnostics;
using Ludots.Adapter.LiteNetLib;
using Ludots.Core.Networking.Transport;
using NUnit.Framework;

namespace Ludots.Tests.NetworkingAdapter;

[TestFixture]
public sealed class LiteNetLibDatagramPortTests
{
    [Test]
    public void RealUdpConnection_ReportsEventsAndMovesReliableOrderedDatagramsBothWays()
    {
        using var server = CreateServer(datagramCapacity: 8);
        using var client = CreateClient(server.BoundPort, "rts-duel-test", datagramCapacity: 8);

        PumpUntil(
            server,
            client,
            () =>
                server.TryReceiveConnectionEvent(out ServerConnectionEvent serverEvent) &&
                serverEvent.Kind == TransportConnectionEventKind.Connected &&
                client.TryReceiveConnectionEvent(out ClientConnectionEvent clientEvent) &&
                clientEvent.Kind == TransportConnectionEventKind.Connected);

        Span<byte> clientPayload = stackalloc byte[] { 1, 2, 3, 4 };
        Assert.That(
            client.TrySend(new ChannelId(3), clientPayload),
            Is.EqualTo(DatagramSendStatus.Sent));

        byte[] receiveBuffer = new byte[64];
        ConnectionId connectionId = default;
        ChannelId channelId = default;
        int bytesReceived = 0;
        PumpUntil(
            server,
            client,
            () => server.TryReceive(
                receiveBuffer,
                out bytesReceived,
                out connectionId,
                out channelId));

        Assert.Multiple(() =>
        {
            Assert.That(bytesReceived, Is.EqualTo(4));
            Assert.That(channelId, Is.EqualTo(new ChannelId(3)));
            Assert.That(receiveBuffer.AsSpan(0, bytesReceived).ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        });

        Span<byte> serverPayload = stackalloc byte[] { 9, 8, 7 };
        Assert.That(
            server.TrySend(connectionId, new ChannelId(4), serverPayload),
            Is.EqualTo(DatagramSendStatus.Sent));

        PumpUntil(
            server,
            client,
            () => client.TryReceive(receiveBuffer, out bytesReceived, out channelId));
        Assert.Multiple(() =>
        {
            Assert.That(bytesReceived, Is.EqualTo(3));
            Assert.That(channelId, Is.EqualTo(new ChannelId(4)));
            Assert.That(receiveBuffer.AsSpan(0, bytesReceived).ToArray(), Is.EqualTo(new byte[] { 9, 8, 7 }));
        });
    }

    [Test]
    public void WrongConnectionKey_IsRejectedWithoutCreatingServerSeat()
    {
        using var server = CreateServer(datagramCapacity: 4);
        using var client = CreateClient(server.BoundPort, "wrong-key", datagramCapacity: 4);

        ClientConnectionEvent disconnected = default;
        PumpUntil(
            server,
            client,
            () =>
                client.TryReceiveConnectionEvent(out disconnected) &&
                disconnected.Kind == TransportConnectionEventKind.Disconnected);

        Assert.Multiple(() =>
        {
            Assert.That(disconnected.DisconnectReason, Is.EqualTo(TransportDisconnectReason.Rejected));
            Assert.That(server.TryReceiveConnectionEvent(out _), Is.False);
        });
    }

    [Test]
    public void ReceiveCapacityViolation_FailsLoudlyDuringPump()
    {
        using var server = CreateServer(datagramCapacity: 1);
        using var client = CreateClient(server.BoundPort, "rts-duel-test", datagramCapacity: 4);
        PumpUntil(
            server,
            client,
            () =>
                server.TryReceiveConnectionEvent(out _) &&
                client.TryReceiveConnectionEvent(out _));

        Assert.That(client.TrySend(new ChannelId(1), new byte[] { 1 }), Is.EqualTo(DatagramSendStatus.Sent));
        Assert.That(client.TrySend(new ChannelId(1), new byte[] { 2 }), Is.EqualTo(DatagramSendStatus.Sent));

        Assert.That(
            () => PumpUntil(server, client, () => false, timeoutMs: 1000),
            Throws.InvalidOperationException.With.Message.Contains("Datagram receive capacity"));
    }

    private static LiteNetLibServerDatagramPort CreateServer(int datagramCapacity) =>
        new(
            listenPort: 0,
            connectionKey: "rts-duel-test",
            connectionCapacity: 2,
            datagramCapacity,
            connectionEventCapacity: 8,
            maxPayloadBytes: 1200,
            channelCount: 8);

    private static LiteNetLibClientDatagramPort CreateClient(
        int port,
        string key,
        int datagramCapacity) =>
        new(
            "127.0.0.1",
            port,
            key,
            datagramCapacity,
            connectionEventCapacity: 8,
            maxPayloadBytes: 1200,
            channelCount: 8);

    private static void PumpUntil(
        LiteNetLibServerDatagramPort server,
        LiteNetLibClientDatagramPort client,
        Func<bool> condition,
        int timeoutMs = 3000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            server.Pump();
            client.Pump();
            if (condition()) return;
            Thread.Sleep(1);
        }

        Assert.Fail($"Condition was not reached within {timeoutMs} ms.");
    }
}
