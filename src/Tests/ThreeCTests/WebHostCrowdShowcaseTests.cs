using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Ludots.Adapter.Web;
using Ludots.Adapter.Web.Protocol;
using Ludots.Core.Client;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Scripting;
using Ludots.Launcher.Backend;
using NUnit.Framework;

namespace Ludots.Tests.ThreeC
{
    /// <summary>
    /// Boots the S4 deploy preset through the Web adapter exactly as <c>launch --adapter web</c> does
    /// (launcher plan → runtime bootstrap → <see cref="WebHostComposer"/> → <see cref="WebHostFrameDriver"/>)
    /// and drives it with a client speaking the real WebSocket wire protocol. The test thread is the host thread:
    /// the engine installs its synchronization context there, so client socket I/O runs on the thread pool.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class WebHostCrowdShowcaseTests
    {
        private const string S4Binding = "$crowd_simulation_s4_deploy_1337";
        private const int ViewportWidth = 1280;
        private const int ViewportHeight = 720;
        private const float FrameDt = 1f / 30f;

        private string _tempDirectory = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), $"ludots-web-crowd-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        [Test]
        public void S4DeployPreset_WebHost_PicksBeforeFirstPresentedFrame_ServesClient_AndAppliesBoxSelectOrder()
        {
            WebHostSetup setup = ComposeWebHost(S4Binding);
            WebHostFrameDriver driver = WebHostFrameDriver.Start(setup);
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            try
            {
                Assert.That(driver.CameraPresenter.HasPresented, Is.False,
                    "The host presents only after a tick, so the first tick's surface pick runs against the logic camera.");

                // A full frame of dt on the very first tick steps the fixed-step simulation, which picks the surface.
                driver.Frame(0.1f);
                Assert.That(setup.Engine.GameSession.CurrentTick, Is.GreaterThan(0),
                    "The first Web frame must step the fixed-step simulation before any frame was presented.");
                Assert.That(driver.CameraPresenter.HasPresented, Is.True);

                using WireClient client = WireClient.Connect(setup.Transport, cts.Token);
                client.SendState(buttonMask: 0, ViewportWidth * 0.5f, ViewportHeight * 0.5f);
                Step(driver, 30);

                WireClient.FrameSummary frame = client.ReceiveFrame(cts.Token);
                Assert.Multiple(() =>
                {
                    Assert.That(client.MeshMapReceived, Is.True, "Client must receive the mesh map before frames.");
                    Assert.That(frame.CameraFovYDeg, Is.GreaterThan(0f), "Streamed camera must carry a presented FOV.");
                    Assert.That(frame.Sections, Does.Contain(FrameProtocol.SectionUiScene),
                        "CrowdSimulationUiMod HUD must reach the client through the Web UI bridge.");
                });

                CrowdSimSession session = setup.Engine.GetService(CoreServiceKeys.CrowdSimulationSession)
                    ?? throw new AssertionException("S4 preset must publish the crowd session.");
                int player = ResolveLocalPlayer(setup);
                Assert.That(session.SelectedCollectionCount(player), Is.Zero);

                Drag(driver, client, InputProtocol.ButtonMaskLeft, (4f, 4f), (ViewportWidth - 4f, ViewportHeight - 4f));
                int selected = session.SelectedCollectionCount(player);
                Assert.That(selected, Is.GreaterThan(0), "Box select over the whole Web viewport must select visible own units.");

                int ordersBefore = MaxOrderId(session, player);
                int logBefore = session.Commands.Log.Count;
                Tap(driver, client, InputProtocol.ButtonMaskRight, (ViewportWidth * 0.5f, ViewportHeight * 0.55f));
                Step(driver, 10);

                (int Tick, CrowdSimCommand Cmd)[] issued = session.Commands.Log
                    .Skip(logBefore)
                    .Where(entry => entry.Cmd.Kind == CrowdSimCommandKind.Order && entry.Cmd.Player == player)
                    .ToArray();
                Assert.Multiple(() =>
                {
                    Assert.That(issued, Has.Length.EqualTo(1), "Right-click tap must issue exactly one order for the local player.");
                    Assert.That(issued[0].Cmd.Members, Has.Length.EqualTo(selected), "The order must carry the box selection.");
                    Assert.That(MaxOrderId(session, player), Is.GreaterThan(ordersBefore), "The order must enter the order book.");
                    Assert.That(setup.LoopStatus.IsFaulted, Is.False);
                });
            }
            finally
            {
                driver.Stop();
            }
        }

        private WebHostSetup ComposeWebHost(string binding)
        {
            string repoRoot = FindRepoRoot();
            string preferencesPath = Path.Combine(_tempDirectory, "preferences.json");
            string userConfigPath = Path.Combine(_tempDirectory, "config.overlay.json");
            File.WriteAllText(preferencesPath, "{}");
            File.WriteAllText(userConfigPath, "{}");
            var service = new LauncherService(
                repoRoot,
                Path.Combine(repoRoot, "launcher.config.json"),
                Path.Combine(repoRoot, "launcher.presets.json"),
                preferencesPath,
                userConfigPath);
            LauncherLaunchPlan plan = service.Resolve(new[] { binding }, LauncherPlatformIds.Web, LauncherBuildMode.Never).Plan;
            Assert.That(plan.AdapterId, Is.EqualTo(LauncherPlatformIds.Web));
            Assert.That(File.Exists(plan.GraphArtifactPath), Is.True, "Resolve must materialize the Web launch graph.");

            string bootstrapPath = service.WriteBootstrap(plan);
            return WebHostComposer.Compose(plan.AppOutputDirectory, bootstrapPath);
        }

        private static int ResolveLocalPlayer(WebHostSetup setup)
        {
            ClientLocalSeatRegistry seats = setup.Engine.GetService(CoreServiceKeys.ClientLocalSeatRegistry)
                ?? throw new AssertionException("S4 preset must publish the local seat registry.");
            Assert.That(seats.TryGetSoleSeat(out ClientLocalSeat seat), Is.True);
            Assert.That(seat.PossessedPlayerId, Is.GreaterThan(0));
            return seat.PossessedPlayerId;
        }

        private static int MaxOrderId(CrowdSimSession session, int player)
        {
            int max = -1;
            IReadOnlyList<CrowdOrder> orders = session.Orders.List;
            for (int i = 0; i < orders.Count; i++)
            {
                if (orders[i].Player == player && orders[i].Id > max)
                {
                    max = orders[i].Id;
                }
            }

            return max;
        }

        private static void Drag(WebHostFrameDriver driver, WireClient client, int button, (float X, float Y) from, (float X, float Y) to)
        {
            client.SendPointer(WirePointerAction.Move, 0, from.X, from.Y);
            client.SendState(0, from.X, from.Y);
            Step(driver, 2);
            client.SendPointer(WirePointerAction.Down, button, from.X, from.Y);
            client.SendState(button, from.X, from.Y);
            Step(driver, 2);
            const int moves = 8;
            for (int i = 1; i <= moves; i++)
            {
                float x = from.X + ((to.X - from.X) * i / moves);
                float y = from.Y + ((to.Y - from.Y) * i / moves);
                client.SendPointer(WirePointerAction.Move, button, x, y);
                client.SendState(button, x, y);
                Step(driver, 1);
            }

            client.SendPointer(WirePointerAction.Up, 0, to.X, to.Y);
            client.SendState(0, to.X, to.Y);
            Step(driver, 4);
        }

        private static void Tap(WebHostFrameDriver driver, WireClient client, int button, (float X, float Y) at)
        {
            client.SendPointer(WirePointerAction.Move, 0, at.X, at.Y);
            client.SendState(0, at.X, at.Y);
            Step(driver, 2);
            client.SendPointer(WirePointerAction.Down, button, at.X, at.Y);
            client.SendState(button, at.X, at.Y);
            Step(driver, 2);
            client.SendPointer(WirePointerAction.Up, 0, at.X, at.Y);
            client.SendState(0, at.X, at.Y);
            Step(driver, 2);
        }

        private static void Step(WebHostFrameDriver driver, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                // Client messages arrive on the transport's receive loop; give it time to land them before the frame reads input.
                Thread.Sleep(5);
                driver.Frame(FrameDt);
            }
        }

        private static string FindRepoRoot()
        {
            string? directory = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                if (File.Exists(Path.Combine(directory, "showcase.registry.json")))
                {
                    return directory;
                }

                directory = Directory.GetParent(directory)?.FullName;
            }

            throw new InvalidOperationException("Unable to locate repository root.");
        }

        private enum WirePointerAction : byte
        {
            Down = 0,
            Move = 1,
            Up = 2,
        }

        /// <summary>Browser-side half of the Web wire protocol over a loopback WebSocket.</summary>
        private sealed class WireClient : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly TcpClient _tcp;
            private readonly WebSocket _client;
            private readonly WebSocket _server;
            private readonly Task _serverLoop;
            private readonly CancellationTokenSource _serverCts = new();
            private readonly SemaphoreSlim _sendLock = new(1, 1);
            private readonly byte[] _receiveBuffer = new byte[4 * 1024 * 1024];

            private WireClient(TcpListener listener, TcpClient tcp, WebSocket client, WebSocket server, Ludots.Adapter.Web.Streaming.WebTransportLayer transport)
            {
                _listener = listener;
                _tcp = tcp;
                _client = client;
                _server = server;
                _serverLoop = transport.HandleClientAsync(server, _serverCts.Token);
            }

            public bool MeshMapReceived { get; private set; }

            public static WireClient Connect(Ludots.Adapter.Web.Streaming.WebTransportLayer transport, CancellationToken ct) =>
                Task.Run(() => ConnectAsync(transport, ct), ct).GetAwaiter().GetResult();

            public FrameSummary ReceiveFrame(CancellationToken ct) =>
                Task.Run(() => ReceiveFrameAsync(ct), ct).GetAwaiter().GetResult();

            public void Dispose() => Task.Run(DisposeAsync).GetAwaiter().GetResult();

            private static async Task<WireClient> ConnectAsync(Ludots.Adapter.Web.Streaming.WebTransportLayer transport, CancellationToken ct)
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var tcp = new TcpClient();
                Task<TcpClient> accept = listener.AcceptTcpClientAsync(ct).AsTask();
                await tcp.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, ct);
                TcpClient serverTcp = await accept;
                WebSocket server = WebSocket.CreateFromStream(serverTcp.GetStream(), isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.Zero);
                WebSocket client = WebSocket.CreateFromStream(tcp.GetStream(), isServer: false, subProtocol: null, keepAliveInterval: TimeSpan.Zero);
                return new WireClient(listener, tcp, client, server, transport);
            }

            public void SendState(int buttonMask, float x, float y)
            {
                var msg = new byte[InputProtocol.InputStateMessageSize];
                msg[0] = InputProtocol.MsgTypeInputState;
                BinaryPrimitives.WriteInt32LittleEndian(msg.AsSpan(InputProtocol.InputStateButtonMaskOffset), buttonMask);
                BinaryPrimitives.WriteSingleLittleEndian(msg.AsSpan(InputProtocol.InputStateMouseXOffset), x);
                BinaryPrimitives.WriteSingleLittleEndian(msg.AsSpan(InputProtocol.InputStateMouseYOffset), y);
                BinaryPrimitives.WriteInt32LittleEndian(msg.AsSpan(InputProtocol.InputStateViewportWidthOffset), ViewportWidth);
                BinaryPrimitives.WriteInt32LittleEndian(msg.AsSpan(InputProtocol.InputStateViewportHeightOffset), ViewportHeight);
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
                BinaryPrimitives.WriteInt32LittleEndian(msg.AsSpan(InputProtocol.PointerViewportWidthOffset), ViewportWidth);
                BinaryPrimitives.WriteInt32LittleEndian(msg.AsSpan(InputProtocol.PointerViewportHeightOffset), ViewportHeight);
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
                    int length = 0;
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _client.ReceiveAsync(new ArraySegment<byte>(_receiveBuffer, length, _receiveBuffer.Length - length), ct);
                        length += result.Count;
                    }
                    while (!result.EndOfMessage);

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
}
