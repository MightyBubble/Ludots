using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ludots.Adapter.Web.Protocol;
using Ludots.Adapter.Web.Services;
using Ludots.Core.Diagnostics;
using Ludots.Core.Presentation.Assets;

namespace Ludots.Adapter.Web.Streaming
{
    public readonly struct WebMaterialMapEntry
    {
        public WebMaterialMapEntry(
            int id,
            string key,
            MaterialAssetDomain domain,
            MaterialAssetFlags flags,
            string[] sourceUris)
        {
            Id = id;
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Domain = domain;
            Flags = flags;
            SourceUris = sourceUris ?? Array.Empty<string>();
        }

        public int Id { get; }
        public string Key { get; }
        public MaterialAssetDomain Domain { get; }
        public MaterialAssetFlags Flags { get; }
        public string[] SourceUris { get; }
    }

    public sealed class WebTransportLayer : IDisposable
    {
        private static readonly LogChannel LogChannel = Log.RegisterChannel("WebTransport");

        private readonly WebInputBackend _inputBackend;
        private readonly WebViewController _viewController;
        private readonly ConcurrentDictionary<string, ClientSession> _sessions = new();
        private volatile byte[]? _meshMapMessage;
        private volatile byte[]? _materialMapMessage;

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
        }

        public void SetMaterialMap(IReadOnlyList<WebMaterialMapEntry> entries)
        {
            _materialMapMessage = EncodeMaterialMap(entries);
        }

        public static byte[] EncodeMaterialMap(IReadOnlyList<WebMaterialMapEntry> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            int totalSize = 1 + 2;
            for (int i = 0; i < entries.Count; i++)
            {
                WebMaterialMapEntry entry = entries[i];
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    throw new InvalidOperationException($"Web material map entry at index {i} has no key.");
                }

                if (entry.SourceUris.Length == 0)
                {
                    throw new InvalidOperationException($"Web material '{entry.Key}' has no host sourceUris.");
                }

                totalSize += 4 + 1 + 2 + 2 + Encoding.UTF8.GetByteCount(entry.Key) + 2;
                for (int uriIndex = 0; uriIndex < entry.SourceUris.Length; uriIndex++)
                {
                    string uri = entry.SourceUris[uriIndex];
                    if (string.IsNullOrWhiteSpace(uri))
                    {
                        throw new InvalidOperationException($"Web material '{entry.Key}' sourceUris[{uriIndex}] is empty.");
                    }

                    totalSize += 2 + Encoding.UTF8.GetByteCount(uri);
                }
            }

            var buf = new byte[totalSize];
            buf[0] = FrameProtocol.MsgTypeMaterialMap;
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(1), checked((ushort)entries.Count));
            int pos = 3;
            for (int i = 0; i < entries.Count; i++)
            {
                WebMaterialMapEntry entry = entries[i];
                BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(pos), entry.Id);
                pos += 4;
                buf[pos++] = (byte)entry.Domain;
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(pos), (ushort)entry.Flags);
                pos += 2;
                WriteUtf8String(buf, ref pos, entry.Key);
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(pos), checked((ushort)entry.SourceUris.Length));
                pos += 2;
                for (int uriIndex = 0; uriIndex < entry.SourceUris.Length; uriIndex++)
                {
                    WriteUtf8String(buf, ref pos, entry.SourceUris[uriIndex]);
                }
            }

            return buf;
        }

        public async Task HandleClientAsync(WebSocket ws, CancellationToken ct)
        {
            string id = Guid.NewGuid().ToString("N")[..8];
            var session = new ClientSession(id, ws);
            _sessions[id] = session;
            Log.Info(in LogChannel, $"Client connected: {id}");

            try
            {
                byte[]? meshMap = _meshMapMessage;
                if (meshMap != null)
                {
                    await ws.SendAsync(
                        new ArraySegment<byte>(meshMap),
                        WebSocketMessageType.Binary,
                        true,
                        ct);
                }

                byte[]? materialMap = _materialMapMessage;
                if (materialMap != null)
                {
                    await ws.SendAsync(
                        new ArraySegment<byte>(materialMap),
                        WebSocketMessageType.Binary,
                        true,
                        ct);
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
            byte[] copy = frameData.ToArray();
            foreach (var kvp in _sessions)
            {
                kvp.Value.EnqueueFrame(copy);
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

        private static void WriteUtf8String(byte[] buffer, ref int pos, string value)
        {
            int byteCount = Encoding.UTF8.GetByteCount(value);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(pos), checked((ushort)byteCount));
            pos += 2;
            Encoding.UTF8.GetBytes(value, buffer.AsSpan(pos));
            pos += byteCount;
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
                byte[]? frame = session.DequeueFrame();
                if (frame != null)
                {
                    await session.Socket.SendAsync(
                        new ArraySegment<byte>(frame),
                        WebSocketMessageType.Binary,
                        true,
                        ct);
                    session.RecordSent(frame.Length);
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

        private sealed class ClientSession
        {
            private volatile byte[]? _pendingFrame;

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

            public void EnqueueFrame(byte[] frame)
            {
                if (Interlocked.Exchange(ref _pendingFrame, frame) != null)
                {
                    FramesDropped++;
                }
            }

            public byte[]? DequeueFrame() => Interlocked.Exchange(ref _pendingFrame, null);

            public void RecordSent(int bytes)
            {
                FramesSent++;
                BytesSent += bytes;
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
