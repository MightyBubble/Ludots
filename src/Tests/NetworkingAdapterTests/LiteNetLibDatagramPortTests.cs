using System.Diagnostics;
using LiteNetLib;
using Ludots.Adapter.LiteNetLib;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Transport;
using NUnit.Framework;

namespace Ludots.Tests.NetworkingAdapter;

[TestFixture]
public sealed class LiteNetLibDatagramPortTests
{
    [Test]
    public void RealUdpConnection_UsesReliableControlAndCommandsAndSequencedStateBothWays()
    {
        using var server = CreateServer(datagramCapacity: 8);
        using var client = CreateClient(server.BoundPort, "rts-duel-test", datagramCapacity: 8);
        Assert.That(client.State, Is.EqualTo(ClientConnectionControlState.Disconnected));
        Assert.That(client.TryConnect(), Is.True);

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
            client.TrySend(new ChannelId(0), clientPayload),
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
            Assert.That(channelId, Is.EqualTo(new ChannelId(0)));
            Assert.That(receiveBuffer.AsSpan(0, bytesReceived).ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        });

        Assert.That(client.TrySend(new ChannelId(1), new byte[] { 5 }), Is.EqualTo(DatagramSendStatus.Sent));
        PumpUntil(server, client, () => server.TryReceive(receiveBuffer, out bytesReceived, out connectionId, out channelId));
        Assert.That(channelId, Is.EqualTo(new ChannelId(1)));
        Assert.That(client.TrySend(new ChannelId(2), new byte[] { 6 }), Is.EqualTo(DatagramSendStatus.Sent));
        PumpUntil(server, client, () => server.TryReceive(receiveBuffer, out bytesReceived, out connectionId, out channelId));
        Assert.That(channelId, Is.EqualTo(new ChannelId(2)));

        Span<byte> serverPayload = stackalloc byte[] { 9, 8, 7 };
        Assert.That(
            server.TrySend(connectionId, new ChannelId(0), serverPayload),
            Is.EqualTo(DatagramSendStatus.Sent));

        PumpUntil(
            server,
            client,
            () => client.TryReceive(receiveBuffer, out bytesReceived, out channelId));
        Assert.Multiple(() =>
        {
            Assert.That(bytesReceived, Is.EqualTo(3));
            Assert.That(channelId, Is.EqualTo(new ChannelId(0)));
            Assert.That(receiveBuffer.AsSpan(0, bytesReceived).ToArray(), Is.EqualTo(new byte[] { 9, 8, 7 }));
        });

        Assert.That(server.TrySend(connectionId, new ChannelId(1), new byte[] { 6 }), Is.EqualTo(DatagramSendStatus.Sent));
        PumpUntil(server, client, () => client.TryReceive(receiveBuffer, out bytesReceived, out channelId));
        Assert.That(channelId, Is.EqualTo(new ChannelId(1)));
        Assert.That(server.TrySend(connectionId, new ChannelId(2), new byte[] { 5 }), Is.EqualTo(DatagramSendStatus.Sent));
        PumpUntil(server, client, () => client.TryReceive(receiveBuffer, out bytesReceived, out channelId));
        Assert.That(channelId, Is.EqualTo(new ChannelId(2)));

        Assert.That(
            server.TrySend(connectionId, new ChannelId(0), new byte[] { 4, 2 }),
            Is.EqualTo(DatagramSendStatus.Sent));
        server.DisconnectAfterReliableFlush(connectionId);
        Assert.DoesNotThrow(() => server.DisconnectAfterReliableFlush(connectionId));
        bool receivedFinalControl = false;
        bool receivedDisconnect = false;
        ClientConnectionEvent disconnectEvent = default;
        PumpUntil(
            server,
            client,
            () =>
            {
                if (!receivedFinalControl)
                {
                    receivedFinalControl = client.TryReceive(receiveBuffer, out bytesReceived, out channelId);
                }

                if (!receivedDisconnect)
                {
                    receivedDisconnect = client.TryReceiveConnectionEvent(out disconnectEvent);
                }

                return receivedFinalControl && receivedDisconnect;
            });
        Assert.Multiple(() =>
        {
            Assert.That(channelId, Is.EqualTo(new ChannelId(0)));
            Assert.That(receiveBuffer.AsSpan(0, bytesReceived).ToArray(), Is.EqualTo(new byte[] { 4, 2 }));
            Assert.That(disconnectEvent.Kind, Is.EqualTo(TransportConnectionEventKind.Disconnected));
        });
    }

    [Test]
    public void DisconnectAfterReliableFlush_WhenTransportIsAlreadyClosing_IsIdempotentUntilDisconnectEvent()
    {
        using var server = CreateServer(datagramCapacity: 4);
        using var client = CreateClient(server.BoundPort, "rts-duel-test", datagramCapacity: 4);
        Assert.That(client.TryConnect(), Is.True);

        ConnectionId connectionId = default;
        PumpUntil(
            server,
            client,
            () =>
            {
                if (!server.TryReceiveConnectionEvent(out ServerConnectionEvent serverEvent) ||
                    serverEvent.Kind != TransportConnectionEventKind.Connected)
                {
                    return false;
                }

                connectionId = serverEvent.ConnectionId;
                return client.TryReceiveConnectionEvent(out ClientConnectionEvent clientEvent) &&
                       clientEvent.Kind == TransportConnectionEventKind.Connected;
            });

        server.DisconnectAfterReliableFlush(connectionId);
        server.Pump();

        Assert.DoesNotThrow(() => server.DisconnectAfterReliableFlush(connectionId));

        PumpUntil(
            server,
            client,
            () => client.TryReceiveConnectionEvent(out ClientConnectionEvent clientEvent) &&
                  clientEvent.Kind == TransportConnectionEventKind.Disconnected);
    }

    [Test]
    public void WrongConnectionKey_IsRejectedWithoutCreatingServerSeat()
    {
        using var server = CreateServer(datagramCapacity: 4);
        using var client = CreateClient(server.BoundPort, "wrong-key", datagramCapacity: 4);
        Assert.That(client.TryConnect(), Is.True);

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
        Assert.That(client.TryConnect(), Is.True);
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

    [Test]
    public void ServerReceive_StateChannelWithReliableOrderedDelivery_FailsLoudly()
    {
        using var server = CreateServer(datagramCapacity: 4);
        var listener = new EventBasedNetListener();
        var rawClient = new NetManager(listener)
        {
            AutoRecycle = true,
            ChannelsCount = 8,
        };
        try
        {
            Assert.That(rawClient.Start(), Is.True);

            NetPeer? serverPeer = null;
            listener.PeerConnectedEvent += peer => serverPeer = peer;
            Assert.That(rawClient.Connect("127.0.0.1", server.BoundPort, "rts-duel-test"), Is.Not.Null);

            PumpUntil(
                server,
                rawClient,
                () => serverPeer != null && server.TryReceiveConnectionEvent(out _));

            serverPeer!.Send(new byte[] { 1 }, channelNumber: 2, DeliveryMethod.ReliableOrdered);

            Assert.That(
                () => PumpUntil(server, rawClient, () => false, timeoutMs: 1000),
                Throws.InvalidOperationException.With.Message.Contains("Sequenced"));
        }
        finally
        {
            rawClient.Stop();
        }
    }

    [Test]
    public void ClientReceive_StateChannelWithReliableOrderedDelivery_FailsLoudly()
    {
        var listener = new EventBasedNetListener();
        var rawServer = new NetManager(listener)
        {
            AutoRecycle = true,
            ChannelsCount = 8,
        };
        listener.ConnectionRequestEvent += request => request.AcceptIfKey("rts-duel-test");
        NetPeer? clientPeer = null;
        listener.PeerConnectedEvent += peer => clientPeer = peer;
        try
        {
            Assert.That(rawServer.Start(0), Is.True);
            using var client = CreateClient(rawServer.LocalPort, "rts-duel-test", datagramCapacity: 4);
            Assert.That(client.TryConnect(), Is.True);
            PumpUntil(
                rawServer,
                client,
                () => clientPeer != null && client.TryReceiveConnectionEvent(out _));

            clientPeer!.Send(new byte[] { 1 }, channelNumber: 2, DeliveryMethod.ReliableOrdered);

            Assert.That(
                () => PumpUntil(rawServer, client, () => false, timeoutMs: 1000),
                Throws.InvalidOperationException.With.Message.Contains("Sequenced"));
        }
        finally
        {
            rawServer.Stop();
        }
    }

    [Test]
    public void ReliableDisconnectFlushTimeout_FailsLoudlyWithoutDroppingTheConnection()
    {
        using var server = CreateServer(datagramCapacity: 4, reliableFlushTimeoutMilliseconds: 1);
        var listener = new EventBasedNetListener();
        var rawClient = new NetManager(listener)
        {
            AutoRecycle = true,
            ChannelsCount = 8,
        };

        Assert.That(rawClient.Start(), Is.True);
        Assert.That(rawClient.Connect("127.0.0.1", server.BoundPort, "rts-duel-test"), Is.Not.Null);
        ConnectionId connectionId = default;
        PumpUntil(
            server,
            rawClient,
            () => server.TryReceiveConnectionEvent(out ServerConnectionEvent connected) &&
                  CaptureConnection(in connected, out connectionId));

        rawClient.Stop(sendDisconnectMessages: false);
        Assert.That(
            server.TrySend(connectionId, new ChannelId(0), new byte[] { 1, 2, 3 }),
            Is.EqualTo(DatagramSendStatus.Sent));
        server.DisconnectAfterReliableFlush(connectionId);
        Thread.Sleep(10);

        Assert.That(
            server.Pump,
            Throws.InvalidOperationException
                .With.Message.Contains($"connection {connectionId.Value}")
                .And.Message.Contains("reliable deliveries remain unacknowledged"));
        Assert.That(
            server.TrySend(connectionId, new ChannelId(0), new byte[] { 4 }),
            Is.EqualTo(DatagramSendStatus.Sent));
    }

    private static LiteNetLibServerDatagramPort CreateServer(
        int datagramCapacity,
        int reliableFlushTimeoutMilliseconds = 4_000) =>
        new(
            listenPort: 0,
            connectionKey: "rts-duel-test",
            connectionCapacity: 2,
            datagramCapacity,
            connectionEventCapacity: 8,
            maxPayloadBytes: 1200,
            maxConnectAttempts: 10,
            disconnectTimeoutMilliseconds: 5_000,
            reliableDisconnectFlushTimeoutMilliseconds: reliableFlushTimeoutMilliseconds,
            channelCount: 8,
            controlChannel: new ChannelId(0),
            commandChannel: new ChannelId(1),
            stateChannel: new ChannelId(2));

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
            maxConnectAttempts: 10,
            disconnectTimeoutMilliseconds: 5_000,
            channelCount: 8,
            controlChannel: new ChannelId(0),
            commandChannel: new ChannelId(1),
            stateChannel: new ChannelId(2));

    private static bool CaptureConnection(in ServerConnectionEvent connectionEvent, out ConnectionId connectionId)
    {
        connectionId = connectionEvent.ConnectionId;
        return connectionEvent.Kind == TransportConnectionEventKind.Connected;
    }

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

    private static void PumpUntil(
        LiteNetLibServerDatagramPort server,
        NetManager client,
        Func<bool> condition,
        int timeoutMs = 3000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            server.Pump();
            client.PollEvents();
            if (condition()) return;
            Thread.Sleep(1);
        }

        Assert.Fail($"Condition was not reached within {timeoutMs} ms.");
    }

    private static void PumpUntil(
        NetManager server,
        LiteNetLibClientDatagramPort client,
        Func<bool> condition,
        int timeoutMs = 3000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            server.PollEvents();
            client.Pump();
            if (condition()) return;
            Thread.Sleep(1);
        }

        Assert.Fail($"Condition was not reached within {timeoutMs} ms.");
    }
}
