using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ludots.Adapter.Web.Protocol;
using Ludots.Adapter.Web.Services;
using Ludots.Core.Diagnostics;

namespace Ludots.Adapter.Web.Streaming
{
    public sealed class WebTransportLayer : IDisposable
    {
        private static readonly LogChannel LogChannel = Log.RegisterChannel("WebTransport");

        private readonly WebInputBackend _inputBackend;
        private readonly WebViewController _viewController;
        private readonly WebInteractiveSessionGate _interactiveSessionGate = new();
        private readonly ConcurrentDictionary<string, ClientSession> _sessions = new();
        private volatile byte[]? _meshMapMessage;
        private volatile byte[]? _terrainSnapshotMessage;

        public bool HasClients => !_sessions.IsEmpty;
        public int ClientCount => _sessions.Count;

        public WebTransportLayer(WebInputBackend inputBackend, WebViewController viewController)
        {
            _inputBackend = inputBackend;
            _viewController = viewController;
        }

        public void SetMeshMap(Dictionary<int, string> idToKey)
        {
            int totalSize = 1 + 2;
            foreach (var kvp in idToKey)
            {
                totalSize += 4 + 2 + Encoding.UTF8.GetByteCount(kvp.Value);
            }

            var buf = new byte[totalSize];
            buf[0] = FrameProtocol.MsgTypeMeshMap;
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(1), (ushort)idToKey.Count);
            int pos = 3;
            foreach (var kvp in idToKey)
            {
                BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(pos), kvp.Key);
                pos += 4;
                int keyLen = Encoding.UTF8.GetByteCount(kvp.Value);
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(pos), (ushort)keyLen);
                pos += 2;
                Encoding.UTF8.GetBytes(kvp.Value, buf.AsSpan(pos));
                pos += keyLen;
            }

            _meshMapMessage = buf;
            BroadcastReliableMessage(buf);
        }

        public void SetTerrainSnapshot(byte[] message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (message.Length < TerrainSnapshotProtocol.HeaderSize || message[0] != FrameProtocol.MsgTypeTerrainSnapshot)
            {
                throw new ArgumentException("Terrain snapshot message does not match the web terrain protocol.", nameof(message));
            }

            _terrainSnapshotMessage = message;
            BroadcastReliableMessage(message);
        }

        public void ClearTerrainSnapshot()
        {
            if (_terrainSnapshotMessage == null)
            {
                return;
            }

            _terrainSnapshotMessage = null;
            BroadcastReliableMessage(new[] { FrameProtocol.MsgTypeTerrainClear });
        }

        public async Task HandleClientAsync(WebSocket ws, CancellationToken ct)
        {
            string id = Guid.NewGuid().ToString("N")[..8];
            var session = new ClientSession(id, ws);
            if (!_interactiveSessionGate.TryAcquire(id))
            {
                Log.Info(in LogChannel, $"Client rejected because the interactive session is occupied: {id}");
                try
                {
                    await ws.CloseAsync(
                        WebSocketCloseStatus.PolicyViolation,
                        WebInteractiveSessionGate.RejectionReason,
                        ct);
                }
                finally
                {
                    session.Dispose();
                }

                return;
            }

            if (!_sessions.TryAdd(id, session))
            {
                _interactiveSessionGate.Release(id);
                session.Dispose();
                throw new InvalidOperationException($"Web session id collision: {id}");
            }

            Log.Info(in LogChannel, $"Client connected: {id}");

            try
            {
                byte[]? meshMap = _meshMapMessage;
                if (meshMap != null)
                {
                    session.EnqueueReliableMessage(meshMap);
                }

                byte[]? terrainSnapshot = _terrainSnapshotMessage;
                if (terrainSnapshot != null)
                {
                    session.EnqueueReliableMessage(terrainSnapshot);
                }

                var receiveTask = ReceiveLoopAsync(session, ct);
                var sendTask = SendLoopAsync(session, ct);
                await Task.WhenAny(receiveTask, sendTask);
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException)
            {
            }
            finally
            {
                _sessions.TryRemove(id, out _);
                if (!_interactiveSessionGate.Release(id))
                {
                    Log.Info(in LogChannel, $"Client disconnected after losing interactive ownership: {id}");
                }
                session.Dispose();
                Log.Info(in LogChannel, $"Client disconnected: {id} (sent={session.FramesSent} bytes={session.BytesSent} dropped={session.FramesDropped})");
                if (ws.State == WebSocketState.Open)
                {
                    try
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public void BroadcastFrame(ReadOnlySpan<byte> frameData)
        {
            foreach (var kvp in _sessions)
            {
                kvp.Value.EnqueueFrame(frameData);
            }
        }

        private void BroadcastReliableMessage(byte[] message)
        {
            foreach (var kvp in _sessions)
            {
                kvp.Value.EnqueueReliableMessage(message);
            }
        }

        public List<SessionInfo> GetSessionInfo()
        {
            var list = new List<SessionInfo>();
            foreach (var kvp in _sessions)
            {
                var s = kvp.Value;
                list.Add(new SessionInfo
                {
                    Id = s.Id,
                    FramesSent = s.FramesSent,
                    BytesSent = s.BytesSent,
                    FramesDropped = s.FramesDropped,
                    ConnectedAt = s.ConnectedAt,
                });
            }

            return list;
        }

        private async Task ReceiveLoopAsync(ClientSession session, CancellationToken ct)
        {
            var buf = new byte[512];
            while (!ct.IsCancellationRequested && session.Socket.State == WebSocketState.Open)
            {
                var result = await session.Socket.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Binary && result.Count > 0)
                {
                    ProcessClientMessage(buf.AsSpan(0, result.Count));
                }
            }
        }

        private void ProcessClientMessage(ReadOnlySpan<byte> msg)
        {
            if (msg.Length == 0)
            {
                return;
            }

            switch (msg[0])
            {
                case InputProtocol.MsgTypeInputState:
                    if (msg.Length < InputProtocol.InputStateMessageSize)
                    {
                        return;
                    }

                    UpdateResolution(
                        msg,
                        InputProtocol.InputStateViewportWidthOffset,
                        InputProtocol.InputStateViewportHeightOffset);
                    _inputBackend.ApplyStateMessage(msg);
                    break;

                case InputProtocol.MsgTypePointerEvent:
                    if (msg.Length < InputProtocol.PointerEventMessageSize)
                    {
                        return;
                    }

                    UpdateResolution(
                        msg,
                        InputProtocol.PointerViewportWidthOffset,
                        InputProtocol.PointerViewportHeightOffset);
                    _inputBackend.EnqueuePointerMessage(msg);
                    break;
            }
        }

        private void UpdateResolution(ReadOnlySpan<byte> msg, int widthOffset, int heightOffset)
        {
            int width = BinaryPrimitives.ReadInt32LittleEndian(msg.Slice(widthOffset, 4));
            int height = BinaryPrimitives.ReadInt32LittleEndian(msg.Slice(heightOffset, 4));
            _viewController.SetResolution(width, height);
            _inputBackend.SyncNeutralViewport(width, height);
        }

        private async Task SendLoopAsync(ClientSession session, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && session.Socket.State == WebSocketState.Open)
            {
                if (session.TryDequeueReliableMessage(out byte[]? reliableMessage))
                {
                    await session.Socket.SendAsync(
                        new ArraySegment<byte>(reliableMessage),
                        WebSocketMessageType.Binary,
                        true,
                        ct);
                    session.RecordSent(reliableMessage.Length);
                }
                else if (session.TryDequeueFrame(out byte[]? frame, out int frameLength))
                {
                    await session.Socket.SendAsync(
                        new ArraySegment<byte>(frame, 0, frameLength),
                        WebSocketMessageType.Binary,
                        true,
                        ct);
                    session.RecordSent(frameLength);
                }
                else
                {
                    await Task.Delay(1, ct);
                }
            }
        }

        public void Dispose()
        {
            foreach (var kvp in _sessions)
            {
                try
                {
                    kvp.Value.Socket.Abort();
                }
                catch
                {
                }
            }

            _sessions.Clear();
        }

        private sealed class ClientSession : IDisposable
        {
            private readonly object _frameGate = new();
            private readonly Queue<byte[]> _reliableMessages = new();
            private byte[]? _pendingFrame;
            private byte[]? _sendingFrame;
            private int _pendingFrameLength;
            private bool _hasPendingFrame;
            private bool _disposed;

            public ClientSession(string id, WebSocket socket)
            {
                Id = id;
                Socket = socket;
            }

            public string Id { get; }
            public WebSocket Socket { get; }
            public DateTime ConnectedAt { get; } = DateTime.UtcNow;
            public long FramesSent { get; private set; }
            public long BytesSent { get; private set; }
            public long FramesDropped { get; private set; }

            public void EnqueueFrame(ReadOnlySpan<byte> frame)
            {
                lock (_frameGate)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    if (_pendingFrame == null || _pendingFrame.Length < frame.Length)
                    {
                        ReturnBuffer(ref _pendingFrame);
                        _pendingFrame = ArrayPool<byte>.Shared.Rent(frame.Length);
                    }

                    frame.CopyTo(_pendingFrame);
                    _pendingFrameLength = frame.Length;
                    if (_hasPendingFrame)
                    {
                        FramesDropped++;
                    }

                    _hasPendingFrame = true;
                }
            }

            public void EnqueueReliableMessage(byte[] message)
            {
                lock (_frameGate)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _reliableMessages.Enqueue(message);
                }
            }

            public bool TryDequeueReliableMessage([NotNullWhen(true)] out byte[]? message)
            {
                lock (_frameGate)
                {
                    if (_disposed || _reliableMessages.Count == 0)
                    {
                        message = null;
                        return false;
                    }

                    message = _reliableMessages.Dequeue();
                    return true;
                }
            }

            public bool TryDequeueFrame([NotNullWhen(true)] out byte[]? frame, out int length)
            {
                lock (_frameGate)
                {
                    if (_disposed || !_hasPendingFrame)
                    {
                        frame = null;
                        length = 0;
                        return false;
                    }

                    byte[] pendingFrame = _pendingFrame ?? throw new InvalidOperationException(
                        "Web frame queue has pending work without an owned buffer.");
                    _pendingFrame = _sendingFrame;
                    _sendingFrame = pendingFrame;
                    frame = pendingFrame;
                    length = _pendingFrameLength;
                    _pendingFrameLength = 0;
                    _hasPendingFrame = false;
                    return true;
                }
            }

            public void RecordSent(int bytes)
            {
                FramesSent++;
                BytesSent += bytes;
            }

            public void Dispose()
            {
                lock (_frameGate)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _disposed = true;
                    _reliableMessages.Clear();
                    _hasPendingFrame = false;
                    _pendingFrameLength = 0;
                    ReturnBuffer(ref _pendingFrame);
                    ReturnBuffer(ref _sendingFrame);
                }
            }

            private static void ReturnBuffer(ref byte[]? buffer)
            {
                byte[]? owned = buffer;
                buffer = null;
                if (owned != null)
                {
                    ArrayPool<byte>.Shared.Return(owned);
                }
            }
        }
    }

    public class SessionInfo
    {
        public string Id { get; set; } = "";
        public long FramesSent { get; set; }
        public long BytesSent { get; set; }
        public long FramesDropped { get; set; }
        public DateTime ConnectedAt { get; set; }
    }
}
