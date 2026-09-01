using System.Text.Json;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Hosting;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Presentation.Components;
using NUnit.Framework;

namespace Ludots.Tests.Hosting;

[TestFixture]
public sealed class NetworkReadinessArtifactRuntimeTests
{
    [Test]
    public void Server_PublishesOnlyAfterLifecyclePump_AndTracksConnectedSeats()
    {
        using World world = World.Create();
        var inner = new TestServerRuntime();
        var observer = CreateObserver();
        var sink = new RecordingSink();
        using var runtime = new NetworkReadinessArtifactRuntime(world, inner, observer, sink);

        runtime.Activate();
        Assert.That(sink.PublishCount, Is.Zero);

        runtime.PumpTransport();
        Assert.That(sink.PublishCount, Is.Zero);

        ObserveServerRoom(observer, sessionEpoch: 91, revision: 1, connected: false);
        runtime.PumpTransport();
        runtime.PumpTransport();
        Assert.Multiple(() =>
        {
            Assert.That(sink.PublishCount, Is.EqualTo(1));
            Assert.That(sink.Last.RuntimeReady, Is.True);
            Assert.That(sink.Last.SessionEstablished, Is.False);
            Assert.That(sink.Last.SessionEpoch, Is.EqualTo(91));
            Assert.That(sink.Last.ConnectedSeatCount, Is.Zero);
        });

        var seat = new SessionSeatBinding(0, 1, new PlayerId(1));
        observer.OnServerSeatConnected(in seat, reconnected: false);
        ObserveServerRoom(observer, sessionEpoch: 91, revision: 2, connected: true);
        runtime.PumpTransport();
        Assert.Multiple(() =>
        {
            Assert.That(sink.PublishCount, Is.EqualTo(2));
            Assert.That(sink.Last.ConnectedSeatCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Client_DoesNotReportSessionBeforeFullSnapshotCommit()
    {
        using World world = World.Create();
        Entity mirror = CreateRenderableMirror(world, slot: 0);
        AttachPerformerPayload(world, mirror);
        var inner = new TestClientRuntime
        {
            ConnectionState = ReplicatedClientConnectionState.Connected,
            HasEstablishedSession = true,
            IsAwaitingFullSnapshot = true,
        };
        var sink = new RecordingSink();
        var observer = CreateObserver();
        ObserveAcceptedClientHandshake(observer, sessionEpoch: 42);
        using var runtime = new NetworkReadinessArtifactRuntime(world, inner, observer, sink);

        runtime.Activate();
        runtime.PumpTransport();
        Assert.Multiple(() =>
        {
            Assert.That(sink.Last.SessionEstablished, Is.False);
            Assert.That(sink.Last.SessionEpoch, Is.EqualTo(42));
            Assert.That(sink.Last.ReplicatedMirrorCount, Is.EqualTo(1));
            Assert.That(sink.Last.RenderableMirrorCount, Is.EqualTo(1));
        });

        inner.IsAwaitingFullSnapshot = false;
        inner.LastCommittedTick = 42;
        runtime.PumpReplicatedClient(1f / 60f);
        Assert.That(sink.Last.SessionEstablished, Is.True);
    }

    [Test]
    public void Client_MirrorWithoutFormalPerformerOutput_CannotBecomeRenderableReady()
    {
        using World world = World.Create();
        Entity mirror = CreateRenderableMirror(world, slot: 0);
        var inner = new TestClientRuntime
        {
            ConnectionState = ReplicatedClientConnectionState.Connected,
            HasEstablishedSession = true,
            IsAwaitingFullSnapshot = false,
            LastCommittedTick = 7,
        };
        var sink = new RecordingSink();
        var observer = CreateObserver();
        ObserveAcceptedClientHandshake(observer, sessionEpoch: 7);
        using var runtime = new NetworkReadinessArtifactRuntime(world, inner, observer, sink);

        runtime.Activate();
        runtime.PumpReplicatedClient(1f / 60f);
        Assert.Multiple(() =>
        {
            Assert.That(sink.Last.SessionEstablished, Is.True);
            Assert.That(sink.Last.SessionEpoch, Is.EqualTo(7));
            Assert.That(sink.Last.ReplicatedMirrorCount, Is.EqualTo(1));
            Assert.That(sink.Last.RenderableMirrorCount, Is.Zero);
        });

        AttachPerformerPayload(world, mirror);
        runtime.PumpTransport();
        Assert.Multiple(() =>
        {
            Assert.That(sink.PublishCount, Is.EqualTo(2));
            Assert.That(sink.Last.RenderableMirrorCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Client_ContinuesReporting_WhenAdditionalRenderableMirrorsArriveWithoutAConnectionStateChange()
    {
        using World world = World.Create();
        Entity firstMirror = CreateRenderableMirror(world, slot: 0);
        AttachPerformerPayload(world, firstMirror);
        var inner = new TestClientRuntime
        {
            ConnectionState = ReplicatedClientConnectionState.Connected,
            HasEstablishedSession = true,
            IsAwaitingFullSnapshot = false,
            LastCommittedTick = 7,
        };
        var sink = new RecordingSink();
        var observer = CreateObserver();
        ObserveAcceptedClientHandshake(observer, sessionEpoch: 7);
        using var runtime = new NetworkReadinessArtifactRuntime(world, inner, observer, sink);

        runtime.Activate();
        runtime.PumpReplicatedClient(1f / 60f);
        Assert.Multiple(() =>
        {
            Assert.That(sink.Last.ReplicatedMirrorCount, Is.EqualTo(1));
            Assert.That(sink.Last.RenderableMirrorCount, Is.EqualTo(1));
        });

        for (int slot = 1; slot < 8; slot++)
        {
            Entity mirror = CreateRenderableMirror(world, slot);
            if (slot < 7)
            {
                AttachPerformerPayload(world, mirror);
            }
        }

        runtime.PumpTransport();
        Assert.Multiple(() =>
        {
            Assert.That(sink.PublishCount, Is.EqualTo(2));
            Assert.That(sink.Last.ReplicatedMirrorCount, Is.EqualTo(8));
            Assert.That(sink.Last.RenderableMirrorCount, Is.EqualTo(7));
        });
    }

    [Test]
    public void AtomicArtifact_CleansStaleFiles_WritesExactSchema_AndDeletesOnDispose()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ludots-network-readiness-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "client-a.json");
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, "stale");
        File.WriteAllText(path + ".tmp", "stale-temp");
        try
        {
            var artifact = new AtomicJsonNetworkReadinessArtifact(path);
            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(path), Is.False);
                Assert.That(File.Exists(path + ".tmp"), Is.False);
            });

            var snapshot = new NetworkReadinessSnapshot(
                NetworkProcessRole.ReplicatedClient,
                runtimeReady: true,
                sessionEstablished: true,
                sessionEpoch: 709,
                replicatedMirrorCount: 11,
                renderableMirrorCount: 10,
                connectedSeatCount: 2);
            artifact.Publish(in snapshot);

            using (JsonDocument json = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement root = json.RootElement;
                Assert.Multiple(() =>
                {
                    Assert.That(root.EnumerateObject().Count(), Is.EqualTo(9));
                    Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
                    Assert.That(root.GetProperty("processRole").GetString(), Is.EqualTo("replicatedClient"));
                    Assert.That(root.GetProperty("runtimeReady").GetBoolean(), Is.True);
                    Assert.That(root.GetProperty("sessionEstablished").GetBoolean(), Is.True);
                    Assert.That(root.GetProperty("sessionEpoch").GetUInt64(), Is.EqualTo(709));
                    Assert.That(root.GetProperty("connectedSeatCount").GetInt32(), Is.EqualTo(2));
                    Assert.That(root.GetProperty("replicatedMirrorCount").GetInt32(), Is.EqualTo(11));
                    Assert.That(root.GetProperty("renderableMirrorCount").GetInt32(), Is.EqualTo(10));
                    string updatedAtText = root.GetProperty("updatedAtUtc").GetString()!;
                    DateTimeOffset updatedAt = DateTimeOffset.Parse(updatedAtText);
                    Assert.That(updatedAtText, Does.EndWith("Z"));
                    Assert.That(updatedAt.Offset, Is.EqualTo(TimeSpan.Zero));
                });
            }

            artifact.Dispose();
            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(path), Is.False);
                Assert.That(File.Exists(path + ".tmp"), Is.False);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static NetworkRuntimeStateObserver CreateObserver() => new(
        seatCapacity: 2,
        clientAdmissionHistoryCapacity: 2,
        maxActorsPerCommandBatch: 2);

    private static Entity CreateRenderableMirror(World world, int slot)
    {
        return world.Create(
            new ReplicationMirrorIdentity(new NetworkEntityHandle(slot, 1)),
            default(WorldPositionCm),
            default(PreviousWorldPositionCm),
            VisualTransform.Default);
    }

    private static void AttachPerformerPayload(World world, Entity mirror)
    {
        world.Add(mirror, new PresentationOwnerHasPerformerPayload
        {
            Count = 4,
            RootCount = 1,
        });
    }

    private static void ObserveAcceptedClientHandshake(
        NetworkRuntimeStateObserver observer,
        ulong sessionEpoch)
    {
        var seat = new SessionSeatBinding(0, 1, new PlayerId(1));
        var response = SessionHandshakeResponse.Accept(
            in seat,
            new ReconnectToken(1, 2),
            new ProtocolVersion(1, 0),
            ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 1 }),
            new SessionEpoch(sessionEpoch),
            nextClientBatchSequence: 1);
        observer.OnClientHandshake(in response);
    }

    private static void ObserveServerRoom(
        NetworkRuntimeStateObserver observer,
        ulong sessionEpoch,
        ulong revision,
        bool connected)
    {
        var seats = new[]
        {
            new NetworkRoomSeatSnapshot(
                0,
                NetworkRoomSeatConnectionState.Empty,
                NetworkRoomReadyState.Unready,
                generation: 0,
                default),
            new NetworkRoomSeatSnapshot(
                1,
                NetworkRoomSeatConnectionState.Empty,
                NetworkRoomReadyState.Unready,
                generation: 0,
                default),
        };
        if (connected)
        {
            seats[0] = new NetworkRoomSeatSnapshot(
                0,
                NetworkRoomSeatConnectionState.Connected,
                NetworkRoomReadyState.Unready,
                generation: 1,
                new PlayerId(1));
        }

        var header = new NetworkRoomSnapshotHeader(
            new SessionEpoch(sessionEpoch),
            revision,
            committedTick: 0,
            countdownRemainingTicks: 0,
            seatCount: 2,
            connectedSeatCount: connected ? (ushort)1 : (ushort)0,
            readySeatCount: 0,
            NetworkRoomPhase.WaitingForPlayers);
        observer.OnServerRoomSnapshot(in header, seats);
    }

    private sealed class RecordingSink : INetworkReadinessArtifactSink
    {
        public int PublishCount { get; private set; }
        public NetworkReadinessSnapshot Last { get; private set; }

        public void Publish(in NetworkReadinessSnapshot snapshot)
        {
            PublishCount++;
            Last = snapshot;
        }

        public void Dispose() { }
    }

    private sealed class TestServerRuntime : INetworkRuntimePort
    {
        public NetworkProcessRole Role => NetworkProcessRole.AuthoritativeServer;
        public void Activate() { }
        public void PumpTransport() { }
        public void BeforeAuthoritativeTick(uint executingTick) { }
        public void AfterAuthoritativeCommit(uint committedTick) { }
        public void PumpReplicatedClient(float frameDeltaTime) { }
        public void Dispose() { }
    }

    private sealed class TestClientRuntime :
        INetworkRuntimePort,
        IReplicatedClientRuntimeStatus,
        IPresentationInterpolationSource
    {
        public NetworkProcessRole Role => NetworkProcessRole.ReplicatedClient;
        public ReplicatedClientConnectionState ConnectionState { get; set; }
        public bool HasEstablishedSession { get; set; }
        public bool IsAwaitingFullSnapshot { get; set; }
        public bool IsFaulted => false;
        public uint LastCommittedTick { get; set; }
        public float ReconnectWindowRemainingSeconds => 30f;
        public int RoundTripTimeMilliseconds => 0;
        public float InterpolationAlpha => 1f;
        public void Activate() { }
        public void PumpTransport() { }
        public void BeforeAuthoritativeTick(uint executingTick) { }
        public void AfterAuthoritativeCommit(uint committedTick) { }
        public void PumpReplicatedClient(float frameDeltaTime) { }
        public void Dispose() { }
    }
}
