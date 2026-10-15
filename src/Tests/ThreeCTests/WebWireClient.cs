using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Ludots.Adapter.Web.Protocol;
using Ludots.Adapter.Web.Streaming;

namespace Ludots.Tests.ThreeC
{
    internal enum WirePointerAction : byte
    {
        Down = 0,
        Move = 1,
        Up = 2,
    }

    /// <summary>
    /// Browser-side half of the Web wire protocol over a loopback WebSocket. Every call blocks on the thread pool,
    /// so a test thread that owns the engine's synchronization context can drive host frames in between.
    /// </summary>
    internal sealed class WebWireClient : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly TcpClient _tcp;
        private readonly WebSocket _client;
        private readonly WebSocket _server;
        private readonly Task _serverLoop;
        private readonly CancellationTokenSource _serverCts = new();
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly byte[] _receiveBuffer = new byte[4 * 1024 * 1024];
        private readonly int _viewportWidth;
        private readonly int _viewportHeight;

        private WebWireClient(TcpListener listener, TcpClient tcp, WebSocket client, WebSocket server, WebTransportLayer transport, int viewportWidth, int viewportHeight)
        {
            _listener = listener;
            _tcp = tcp;
            _client = client;
            _server = server;
            _viewportWidth = viewportWidth;
            _viewportHeight = viewportHeight;
            _serverLoop = transport.HandleClientAsync(server, _serverCts.Token);
        }

        public bool MeshMapReceived { get; private set; }

        public static WebWireClient Connect(WebTransportLayer transport, int viewportWidth, int viewportHeight, CancellationToken ct) =>
            Task.Run(() => ConnectAsync(transport, viewportWidth, viewportHeight, ct), ct).GetAwaiter().GetResult();

        public FrameSummary ReceiveFrame(CancellationToken ct) =>
            Task.Run(() => ReceiveFrameAsync(ct), ct).GetAwaiter().GetResult();

        public (WebSocketCloseStatus? Status, string? Description) ReceiveUntilClosed(CancellationToken ct) =>
            Task.Run(() => ReceiveUntilClosedAsync(ct), ct).GetAwaiter().GetResult();

        public void Dispose() => Task.Run(DisposeAsync).GetAwaiter().GetResult();

        private static async Task<WebWireClient> ConnectAsync(WebTransportLayer transport, int viewportWidth, int viewportHeight, CancellationToken ct)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var tcp = new TcpClient();
            Task<TcpClient> accept = listener.AcceptTcpClientAsync(ct).AsTask();
            await tcp.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, ct);
            TcpClient serverTcp = await accept;
            WebSocket server = WebSocket.CreateFromStream(serverTcp.GetStream(), isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.Zero);
            WebSocket client = WebSocket.CreateFromStream(tcp.GetStream(), isServer: false, subProtocol: null, keepAliveInterval: TimeSpan.Zero);
            return new WebWireClient(listener, tcp, client, server, transport, viewportWidth, viewportHeight);
        }

        public void SendState(int buttonMask, float x, float y)
        {
            var msg = new byte[InputProtocol.InputStateMessageSize];
            msg[0] = InputProtocol.MsgTypeInputState;
            BinaryPrimitives.WriteInt32LittleEndian(msg.AsSpan(InputProtocol.InputStateButtonMaskOffset), buttonMask);
            BinaryPrimitives.WriteSingleLittleEndian(msg.AsSpan(InputProtocol.InputStateMouseXOffset), x);
            BinaryPrimitives.WriteSingleLittleEndian(msg.AsSpan(InputProtocol.InputStateMouseYOffset), y);
            BinaryPrimitives.WriteInt32LittleEndian(msg.AsSpan(InputProtocol.InputStateViewportWidthOffset), _viewportWidth);
            BinaryPrimitives.WriteInt32LittleEndian(msg.AsSpan(InputProtocol.InputStateViewportHeightOffset), _viewportHeight);
            Send(msg);
        }

        public void SendPointer(WirePointerAction action, int buttonMask, float x, float y)
        {
            var msg = new byte[InputProtocol.PointerEventMessageSize];
            msg[0] = InputProtocol.MsgTypePointerEvent;
            msg[InputProtocol.PointerActionOffset] = (byte)action;
            BinaryPrimitives.WriteInt32LittleEndian(msg.AsSpan(InputProtocol.PointerButtonMaskOffset), buttonMask);
            BinaryPrimitives.WriteSingleLittleEndian(msg.AsSpan(InputProtocol.PointerXOffset), x);
            BinaryPrimitives.WriteSingleLittleEndian(msg.AsSpan(InputProtocol.PointerYOffset), y);
            BinaryPrimitives.WriteInt32LittleEndian(msg.AsSpan(InputProtocol.PointerViewportWidthOffset), _viewportWidth);
            BinaryPrimitives.WriteInt32LittleEndian(msg.AsSpan(InputProtocol.PointerViewportHeightOffset), _viewportHeight);
            Send(msg);
        }

        private void Send(byte[] msg)
        {
            _sendLock.Wait();
            try
            {
                Task.Run(() => _client.SendAsync(msg, WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None))
                    .GetAwaiter()
                    .GetResult();
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task<FrameSummary> ReceiveFrameAsync(CancellationToken ct)
        {
            while (true)
            {
                (WebSocketMessageType type, int length) = await ReceiveMessageAsync(ct);
                if (type == WebSocketMessageType.Close)
                {
                    throw new InvalidOperationException(
                        $"Server closed the connection ({_client.CloseStatus}): {_client.CloseStatusDescription}");
                }

                if (length == 0)
                {
                    continue;
                }

                if (_receiveBuffer[0] == FrameProtocol.MsgTypeMeshMap)
                {
                    MeshMapReceived = true;
                    continue;
                }

                if (_receiveBuffer[0] == FrameProtocol.MsgTypeFrame)
                {
                    return FrameSummary.Parse(_receiveBuffer.AsSpan(0, length));
                }
            }
        }

        private async Task<(WebSocketCloseStatus? Status, string? Description)> ReceiveUntilClosedAsync(CancellationToken ct)
        {
            while (true)
            {
                (WebSocketMessageType type, _) = await ReceiveMessageAsync(ct);
                if (type == WebSocketMessageType.Close)
                {
                    return (_client.CloseStatus, _client.CloseStatusDescription);
                }
            }
        }

        private async Task<(WebSocketMessageType Type, int Length)> ReceiveMessageAsync(CancellationToken ct)
        {
            int length = 0;
            WebSocketReceiveResult result;
            do
            {
                result = await _client.ReceiveAsync(new ArraySegment<byte>(_receiveBuffer, length, _receiveBuffer.Length - length), ct);
                length += result.Count;
            }
            while (!result.EndOfMessage);

            return (result.MessageType, length);
        }

        private async Task DisposeAsync()
        {
            _serverCts.Cancel();
            _client.Abort();
            _server.Abort();
            try
            {
                await _serverLoop;
            }
            catch (OperationCanceledException)
            {
            }

            _client.Dispose();
            _server.Dispose();
            _tcp.Dispose();
            _listener.Stop();
            _serverCts.Dispose();
            _sendLock.Dispose();
        }

        public readonly record struct FrameSummary(float CameraFovYDeg, IReadOnlyList<byte> Sections)
        {
            public static FrameSummary Parse(ReadOnlySpan<byte> frame)
            {
                var sections = new List<byte>();
                float fov = 0f;
                int pos = FrameProtocol.FrameHeaderSize;
                while (pos < frame.Length)
                {
                    byte section = frame[pos];
                    if (section == FrameProtocol.SectionEnd)
                    {
                        break;
                    }

                    int byteLength = BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(pos + 3, 4));
                    int payload = pos + FrameProtocol.SectionHeaderSize;
                    if (section == FrameProtocol.SectionCamera)
                    {
                        fov = BinaryPrimitives.ReadSingleLittleEndian(frame.Slice(payload + (9 * sizeof(float)), 4));
                    }

                    sections.Add(section);
                    pos = payload + byteLength;
                }

                return new FrameSummary(fov, sections);
            }
        }
    }
}
