using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Ludots.Adapter.Web.Services;
using Ludots.Core.Diagnostics;

namespace Ludots.Adapter.Web.Streaming
{
    public sealed class WebTransportLayer : IDisposable
    {
        private static readonly LogChannel LogChannel = Log.RegisterChannel("WebTransport");

        private readonly WebInputBackend _inputBackend;
        private readonly WebViewController _viewController;
        private readonly ConcurrentDictionary<string, ClientSession> _sessions = new();

        public bool HasClients => !_sessions.IsEmpty;
        public int ClientCount => _sessions.Count;

        public WebTransportLayer(WebInputBackend inputBackend, WebViewController viewController)
        {
            _inputBackend = inputBackend;
            _viewController = viewController;
        }

        public async Task HandleClientAsync(WebSocket ws, CancellationToken ct)
        {
            string id = Guid.NewGuid().ToString("N")[..8];
            var session = new ClientSession(id, ws);
            _sessions[id] = session;
            Log.Info(in LogChannel, $"Client connected: {id}");

            try
            {
                var receiveTask = ReceiveLoopAsync(session, ct);
                var sendTask = SendLoopAsync(session, ct);
                await Task.WhenAny(receiveTask, sendTask);
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            finally
            {
                _sessions.TryRemove(id, out _);
                Log.Info(in LogChannel, $"Client disconnected: {id} (sent={session.FramesSent} bytes={session.BytesSent} dropped={session.FramesDropped})");
                if (ws.State == WebSocketState.Open)
                {
                    try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); }
                    catch { }
                }
            }
        }

        public void BroadcastFrame(ReadOnlySpan<byte> frameData)
        {
            byte[] copy = frameData.ToArray();
            foreach (var kvp in _sessions)
                kvp.Value.EnqueueFrame(copy);
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
            var buf = new byte[256];
            while (!ct.IsCancellationRequested && session.Socket.State == WebSocketState.Open)
            {
                var result = await session.Socket.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
                if (result.MessageType == WebSocketMessageType.Binary && result.Count > 0)
                    ProcessClientMessage(buf.AsSpan(0, result.Count));
            }
        }

        private void ProcessClientMessage(ReadOnlySpan<byte> msg)
        {
            if (msg.Length == 0) return;
            byte msgType = msg[0];

            if (msgType == Protocol.InputProtocol.MsgTypeInput)
                _inputBackend.ApplyMessage(msg);
        }

        private async Task SendLoopAsync(ClientSession session, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && session.Socket.State == WebSocketState.Open)
            {
                byte[]? frame = session.DequeueFrame();
                if (frame != null)
                {
                    await session.Socket.SendAsync(
                        new ArraySegment<byte>(frame), WebSocketMessageType.Binary, true, ct);
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
                try { kvp.Value.Socket.Abort(); } catch { }
            }
            _sessions.Clear();
        }

        private sealed class ClientSession
        {
            private volatile byte[]? _pendingFrame;

            public string Id { get; }
            public WebSocket Socket { get; }
            public DateTime ConnectedAt { get; } = DateTime.UtcNow;
            public long FramesSent { get; private set; }
            public long BytesSent { get; private set; }
            public long FramesDropped { get; private set; }

            public ClientSession(string id, WebSocket socket) { Id = id; Socket = socket; }

            public void EnqueueFrame(byte[] frame)
            {
                if (Interlocked.Exchange(ref _pendingFrame, frame) != null)
                    FramesDropped++;
            }

            public byte[]? DequeueFrame() => Interlocked.Exchange(ref _pendingFrame, null);

            public void RecordSent(int bytes) { FramesSent++; BytesSent += bytes; }
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
