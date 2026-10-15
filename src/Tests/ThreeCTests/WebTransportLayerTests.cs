using System;
using System.Net.WebSockets;
using System.Threading;
using Ludots.Adapter.Web.Protocol;
using Ludots.Adapter.Web.Services;
using Ludots.Adapter.Web.Streaming;
using NUnit.Framework;

namespace Ludots.Tests.ThreeC
{
    [TestFixture]
    public sealed class WebTransportLayerTests
    {
        private const int ViewportWidth = 1280;
        private const int ViewportHeight = 720;

        [Test]
        public void PointerTransitionBeforeStateSnapshot_KeepsConnectionStreaming()
        {
            using var transport = new WebTransportLayer(new WebInputBackend(), new WebViewController());
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using WebWireClient client = WebWireClient.Connect(transport, ViewportWidth, ViewportHeight, cts.Token);

            client.SendPointer(WirePointerAction.Down, InputProtocol.ButtonMaskLeft, 100f, 100f);
            client.SendState(InputProtocol.ButtonMaskLeft, 100f, 100f);
            client.SendPointer(WirePointerAction.Up, 0, 100f, 100f);
            client.SendState(0, 100f, 100f);

            WebWireClient.FrameSummary frame = BroadcastUntilReceived(transport, client, cts.Token);
            Assert.That(frame.Sections, Is.Empty);
            Assert.That(transport.ClientCount, Is.EqualTo(1));
        }

        [Test]
        public void StateSnapshotAheadOfPointerDown_ClosesWithProtocolErrorInsteadOfDroppingSilently()
        {
            using var transport = new WebTransportLayer(new WebInputBackend(), new WebViewController());
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using WebWireClient client = WebWireClient.Connect(transport, ViewportWidth, ViewportHeight, cts.Token);

            client.SendState(InputProtocol.ButtonMaskLeft, 100f, 100f);
            client.SendPointer(WirePointerAction.Down, InputProtocol.ButtonMaskLeft, 100f, 100f);

            (WebSocketCloseStatus? status, string? description) = client.ReceiveUntilClosed(cts.Token);
            Assert.Multiple(() =>
            {
                Assert.That(status, Is.EqualTo(WebSocketCloseStatus.InvalidPayloadData));
                Assert.That(description, Does.Contain("exactly one button"));
            });
        }

        private static WebWireClient.FrameSummary BroadcastUntilReceived(WebTransportLayer transport, WebWireClient client, CancellationToken ct)
        {
            var emptyFrame = new byte[FrameProtocol.FrameHeaderSize + 1];
            emptyFrame[0] = FrameProtocol.MsgTypeFrame;
            emptyFrame[FrameProtocol.FrameHeaderSize] = FrameProtocol.SectionEnd;
            using var pump = new Timer(_ => transport.BroadcastFrame(emptyFrame), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(20));
            return client.ReceiveFrame(ct);
        }
    }
}
