using System.Diagnostics;
using Ludots.Adapter.LiteNetLib;
using Ludots.Core.Hosting;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.Runtime;
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
        AssertDisabledMetrics(server.Capture(), NetworkProcessRole.AuthoritativeServer);
        AssertDisabledMetrics(client.Capture(), NetworkProcessRole.ReplicatedClient);
    }

    [Test]
    public void UnstableProfile_ReliableOrderedCommandDatagramsEventuallyArriveBothWays()
    {
        LiteNetLibFaultInjectionSettings serverFaults = CreateUnstableFaults(seed: 14);
        LiteNetLibFaultInjectionSettings clientFaults = CreateUnstableFaults(seed: 14);
        using var server = new LiteNetLibServerDatagramPort(
            listenPort: 0,
            connectionKey: "rts-duel-test",
            connectionCapacity: 2,
            datagramCapacity: 64,
            connectionEventCapacity: 8,
            maxPayloadBytes: 1200,
            channelCount: 3,
            stateChannelId: 2,
            in serverFaults);
        using var client = new LiteNetLibClientDatagramPort(
            "127.0.0.1",
            server.BoundPort,
            "rts-duel-test",
            datagramCapacity: 64,
            connectionEventCapacity: 8,
            maxPayloadBytes: 1200,
            channelCount: 3,
            stateChannelId: 2,
            in clientFaults);
        Assert.That(client.TryConnect(), Is.True);

        ConnectionId connectionId = default;
        bool serverConnected = false;
        bool clientConnected = false;
        PumpUntil(
            server,
            client,
            () =>
            {
                while (server.TryReceiveConnectionEvent(out ServerConnectionEvent serverEvent))
                {
                    if (serverEvent.Kind == TransportConnectionEventKind.Connected)
                    {
                        connectionId = serverEvent.ConnectionId;
                        serverConnected = true;
                    }
                }

                while (client.TryReceiveConnectionEvent(out ClientConnectionEvent clientEvent))
                {
                    clientConnected |= clientEvent.Kind == TransportConnectionEventKind.Connected;
                }

                return serverConnected && clientConnected;
            },
            timeoutMs: 10_000);

        var commandChannel = new ChannelId(1);
        Assert.That(client.TrySend(commandChannel, new byte[] { 1, 2, 3 }), Is.EqualTo(DatagramSendStatus.Sent));
        byte[] receiveBuffer = new byte[64];
        int bytesReceived = 0;
        ChannelId receivedChannel = default;
        PumpUntil(
            server,
            client,
            () => server.TryReceive(receiveBuffer, out bytesReceived, out _, out receivedChannel),
            timeoutMs: 10_000);
        Assert.Multiple(() =>
        {
            Assert.That(receivedChannel, Is.EqualTo(commandChannel));
            Assert.That(receiveBuffer.AsSpan(0, bytesReceived).ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
        });

        Assert.That(server.TrySend(connectionId, commandChannel, new byte[] { 4, 5, 6 }), Is.EqualTo(DatagramSendStatus.Sent));
        PumpUntil(
            server,
            client,
            () => client.TryReceive(receiveBuffer, out bytesReceived, out receivedChannel),
            timeoutMs: 10_000);
        Assert.Multiple(() =>
        {
            Assert.That(receivedChannel, Is.EqualTo(commandChannel));
            Assert.That(receiveBuffer.AsSpan(0, bytesReceived).ToArray(), Is.EqualTo(new byte[] { 4, 5, 6 }));
        });

        NetworkFaultInjectionObservationSnapshot serverObservation = server.Capture();
        NetworkFaultInjectionObservationSnapshot clientObservation = client.Capture();
        Assert.Multiple(() =>
        {
            Assert.That(serverObservation.Configuration.ProfileId, Is.EqualTo("unstable"));
            Assert.That(clientObservation.Configuration.ProfileId, Is.EqualTo("unstable"));
            Assert.That(serverObservation.DelayedInboundPacketCount, Is.GreaterThan(0));
            Assert.That(clientObservation.DelayedInboundPacketCount, Is.GreaterThan(0));
            Assert.That(serverObservation.DroppedInboundPacketCount, Is.GreaterThan(0));
            Assert.That(clientObservation.DroppedInboundPacketCount, Is.GreaterThan(0));
            Assert.That(
                serverObservation.ReorderedInboundStateDatagramCount,
                Is.EqualTo(server.SimulatedStateReorderCount));
            Assert.That(
                clientObservation.ReorderedInboundStateDatagramCount,
                Is.EqualTo(client.SimulatedStateReorderCount));
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

    [Test]
    public void ReconnectedPeer_ReceivesANewOpaqueConnectionGeneration()
    {
        using var server = CreateServer(datagramCapacity: 8);
        ConnectionId firstConnection;
        using (var firstClient = CreateClient(server.BoundPort, "rts-duel-test", datagramCapacity: 4))
        {
            ServerConnectionEvent connected = default;
            PumpUntil(
                server,
                firstClient,
                () => server.TryReceiveConnectionEvent(out connected) &&
                    connected.Kind == TransportConnectionEventKind.Connected &&
                    firstClient.TryReceiveConnectionEvent(out _));
            firstConnection = connected.ConnectionId;
        }

        using var secondClient = CreateClient(server.BoundPort, "rts-duel-test", datagramCapacity: 4);
        ServerConnectionEvent secondConnected = default;
        bool sawSecondConnected = false;
        PumpUntil(
            server,
            secondClient,
            () =>
            {
                while (server.TryReceiveConnectionEvent(out ServerConnectionEvent connectionEvent))
                {
                    if (connectionEvent.Kind == TransportConnectionEventKind.Connected)
                    {
                        secondConnected = connectionEvent;
                        sawSecondConnected = true;
                    }
                }

                return sawSecondConnected &&
                    secondClient.TryReceiveConnectionEvent(out _);
            });

        Assert.That(secondConnected.ConnectionId, Is.Not.EqualTo(firstConnection));
        Assert.That(
            server.TrySend(firstConnection, new ChannelId(0), new byte[] { 1 }),
            Is.EqualTo(DatagramSendStatus.Closed));
    }

    [Test]
    public void ReleaseTransportFaultProfile_DropsPacketsBeforeReliabilityProcessing()
    {
        using var server = CreateServer(datagramCapacity: 8);
        var config = new NetworkRuntimeConfig
        {
            NormalConnection = new NetworkFaultProfileConfig
            {
                RoundTripLatencyMs = 180,
                JitterMs = 30,
                PacketLossPermille = 1000,
            },
        };
        var host = new NetworkHostBootstrapConfig
        {
            FaultProfile = NetworkHostBootstrapConfig.NormalFaultProfile,
            FaultSeed = 709,
        };
        LiteNetLibFaultInjectionSettings faults = LiteNetLibFaultInjectionSettings.Create(config, host);
        using var client = new LiteNetLibClientDatagramPort(
            "127.0.0.1",
            server.BoundPort,
            "rts-duel-test",
            datagramCapacity: 8,
            connectionEventCapacity: 8,
            maxPayloadBytes: 1200,
            channelCount: 8,
            stateChannelId: 2,
            in faults);
        Assert.That(client.TryConnect(), Is.True);

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < 750)
        {
            server.Pump();
            client.Pump();
            while (server.TryReceiveConnectionEvent(out _)) { }
            Assert.That(client.TryReceiveConnectionEvent(out _), Is.False);
            Thread.Sleep(1);
        }

        Assert.That(client.State, Is.Not.EqualTo(Ludots.Core.Networking.Runtime.ClientConnectionControlState.Connected));
        NetworkFaultInjectionObservationSnapshot observation = client.Capture();
        Assert.Multiple(() =>
        {
            Assert.That(observation.DelayedInboundPacketCount, Is.Zero);
            Assert.That(observation.DroppedInboundPacketCount, Is.GreaterThan(0));
            Assert.That(observation.Configuration.PacketLossPermille, Is.EqualTo(1000));
        });
    }

    private static LiteNetLibServerDatagramPort CreateServer(int datagramCapacity) =>
        new(
            listenPort: 0,
            connectionKey: "rts-duel-test",
            connectionCapacity: 2,
            datagramCapacity,
            connectionEventCapacity: 8,
            maxPayloadBytes: 1200,
            channelCount: 8,
            stateChannelId: 2,
            faults: LiteNetLibFaultInjectionSettings.Disabled());

    private static LiteNetLibClientDatagramPort CreateClient(
        int port,
        string key,
        int datagramCapacity)
    {
        var client = new LiteNetLibClientDatagramPort(
            "127.0.0.1",
            port,
            key,
            datagramCapacity,
            connectionEventCapacity: 8,
            maxPayloadBytes: 1200,
            channelCount: 8,
            stateChannelId: 2,
            faults: LiteNetLibFaultInjectionSettings.Disabled());
        if (!client.TryConnect())
        {
            client.Dispose();
            throw new InvalidOperationException("Test client failed to start its explicit connection attempt.");
        }

        return client;
    }

    private static LiteNetLibFaultInjectionSettings CreateUnstableFaults(int seed)
    {
        var config = new NetworkRuntimeConfig
        {
            StatePublishRateHz = 10,
            UnstableConnection = new NetworkFaultProfileConfig
            {
                RoundTripLatencyMs = 180,
                JitterMs = 30,
                PacketLossPermille = 50,
                ReorderPermille = 20,
            },
        };
        var host = new NetworkHostBootstrapConfig
        {
            FaultProfile = NetworkHostBootstrapConfig.UnstableFaultProfile,
            FaultSeed = seed,
        };
        return LiteNetLibFaultInjectionSettings.Create(config, host);
    }

    private static void AssertDisabledMetrics(
        NetworkFaultInjectionObservationSnapshot observation,
        NetworkProcessRole expectedRole)
    {
        Assert.Multiple(() =>
        {
            Assert.That(observation.Role, Is.EqualTo(expectedRole));
            Assert.That(
                observation.Configuration.ProfileId,
                Is.EqualTo(NetworkHostBootstrapConfig.NormalFaultProfile));
            Assert.That(observation.Configuration.IsEnabled, Is.False);
            Assert.That(observation.DelayedInboundPacketCount, Is.Zero);
            Assert.That(observation.DroppedInboundPacketCount, Is.Zero);
            Assert.That(observation.ReorderedInboundStateDatagramCount, Is.Zero);
        });
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
}
