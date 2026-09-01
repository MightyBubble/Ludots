using Ludots.Core.Networking.Transport;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class DatagramPortContractTests
{
    [Test]
    public void ServerAndClientPorts_PollReceiveAndSend_WithCallerBuffers()
    {
        var server = new LoopbackServerDatagramPort();
        var client = new LoopbackClientDatagramPort(server, new ConnectionId(7));

        Span<byte> outbound = stackalloc byte[4];
        outbound[0] = 1;
        outbound[1] = 2;
        outbound[2] = 3;
        outbound[3] = 4;

        Assert.That(client.TrySend(new ChannelId(1), outbound), Is.EqualTo(DatagramSendStatus.Sent));

        Span<byte> inbound = stackalloc byte[8];
        Assert.That(server.TryReceive(inbound, out int bytes, out ConnectionId connectionId, out ChannelId channelId), Is.True);
        byte b0 = inbound[0];
        byte b1 = inbound[1];
        byte b2 = inbound[2];
        byte b3 = inbound[3];
        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.EqualTo(4));
            Assert.That(connectionId, Is.EqualTo(new ConnectionId(7)));
            Assert.That(channelId, Is.EqualTo(new ChannelId(1)));
            Assert.That(b0, Is.EqualTo(1));
            Assert.That(b1, Is.EqualTo(2));
            Assert.That(b2, Is.EqualTo(3));
            Assert.That(b3, Is.EqualTo(4));
        });

        Span<byte> reply = stackalloc byte[2];
        reply[0] = 9;
        reply[1] = 8;
        Assert.That(server.TrySend(connectionId, new ChannelId(2), reply), Is.EqualTo(DatagramSendStatus.Sent));

        Assert.That(client.TryReceive(inbound, out bytes, out channelId), Is.True);
        byte r0 = inbound[0];
        byte r1 = inbound[1];
        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.EqualTo(2));
            Assert.That(channelId, Is.EqualTo(new ChannelId(2)));
            Assert.That(r0, Is.EqualTo(9));
            Assert.That(r1, Is.EqualTo(8));
        });
    }

    [Test]
    public void ServerReceive_WhenDatagramExceedsCallerBuffer_Throws()
    {
        var server = new LoopbackServerDatagramPort();
        var client = new LoopbackClientDatagramPort(server, new ConnectionId(1));
        byte[] payload = new byte[4];
        Assert.That(client.TrySend(new ChannelId(0), payload), Is.EqualTo(DatagramSendStatus.Sent));

        byte[] tooSmall = new byte[2];
        Assert.Throws<ArgumentException>(() => server.TryReceive(tooSmall, out _, out _, out _));
    }

    private sealed class LoopbackServerDatagramPort : IServerDatagramPort
    {
        private readonly Queue<Pending> _inbound = new();
        private readonly Queue<Pending> _outbound = new();

        public void EnqueueFromClient(ConnectionId connectionId, ChannelId channelId, ReadOnlySpan<byte> payload)
        {
            _inbound.Enqueue(new Pending(connectionId, channelId, payload.ToArray()));
        }

        public bool TryDequeueToClient(ConnectionId connectionId, out ChannelId channelId, out byte[] payload)
        {
            if (_outbound.Count == 0)
            {
                channelId = default;
                payload = Array.Empty<byte>();
                return false;
            }

            Pending pending = _outbound.Peek();
            if (pending.ConnectionId != connectionId)
            {
                channelId = default;
                payload = Array.Empty<byte>();
                return false;
            }

            _outbound.Dequeue();
            channelId = pending.ChannelId;
            payload = pending.Payload;
            return true;
        }

        public bool TryReceive(Span<byte> buffer, out int bytesReceived, out ConnectionId connectionId, out ChannelId channelId)
        {
            if (_inbound.Count == 0)
            {
                bytesReceived = 0;
                connectionId = default;
                channelId = default;
                return false;
            }

            Pending pending = _inbound.Dequeue();
            if (pending.Payload.Length > buffer.Length)
            {
                throw new ArgumentException("Caller buffer is smaller than the pending datagram.", nameof(buffer));
            }

            pending.Payload.CopyTo(buffer);
            bytesReceived = pending.Payload.Length;
            connectionId = pending.ConnectionId;
            channelId = pending.ChannelId;
            return true;
        }

        public DatagramSendStatus TrySend(ConnectionId connectionId, ChannelId channelId, ReadOnlySpan<byte> payload)
        {
            _outbound.Enqueue(new Pending(connectionId, channelId, payload.ToArray()));
            return DatagramSendStatus.Sent;
        }

        private readonly record struct Pending(ConnectionId ConnectionId, ChannelId ChannelId, byte[] Payload);
    }

    private sealed class LoopbackClientDatagramPort : IClientDatagramPort
    {
        private readonly LoopbackServerDatagramPort _server;
        private readonly ConnectionId _connectionId;

        public LoopbackClientDatagramPort(LoopbackServerDatagramPort server, ConnectionId connectionId)
        {
            _server = server;
            _connectionId = connectionId;
        }

        public bool TryReceive(Span<byte> buffer, out int bytesReceived, out ChannelId channelId)
        {
            if (!_server.TryDequeueToClient(_connectionId, out channelId, out byte[] payload))
            {
                bytesReceived = 0;
                return false;
            }

            if (payload.Length > buffer.Length)
            {
                throw new ArgumentException("Caller buffer is smaller than the pending datagram.", nameof(buffer));
            }

            payload.CopyTo(buffer);
            bytesReceived = payload.Length;
            return true;
        }

        public DatagramSendStatus TrySend(ChannelId channelId, ReadOnlySpan<byte> payload)
        {
            _server.EnqueueFromClient(_connectionId, channelId, payload);
            return DatagramSendStatus.Sent;
        }
    }
}
