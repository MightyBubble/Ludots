using Ludots.Adapter.LiteNetLib;
using Arch.Core;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Hosting;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using NUnit.Framework;

namespace Ludots.Tests.NetworkingAdapter;

[TestFixture]
public sealed class DeferredNetworkRuntimePortTests
{
    [Test]
    public void ReplicatedClient_ActivatesExactlyOnce_AndPublishesRuntimeContracts()
    {
        int factoryCalls = 0;
        var inner = new TestClientRuntime
        {
            InterpolationAlpha = 0.375f,
            LastCommittedTick = 709,
        };
        using var deferred = new DeferredNetworkRuntimePort(
            NetworkProcessRole.ReplicatedClient,
            () =>
            {
                factoryCalls++;
                return new DeferredNetworkRuntimeComposition(
                    inner,
                    new TestFaultInjectionMetrics(NetworkProcessRole.ReplicatedClient),
                    () => { });
            });

        Assert.That(factoryCalls, Is.Zero);
        Assert.That(
            () => deferred.PumpTransport(),
            Throws.InvalidOperationException.With.Message.Contains("activated"));
        Assert.That(
            () => deferred.Capture(),
            Throws.InvalidOperationException.With.Message.Contains("until the network runtime is activated"));

        deferred.Activate();
        deferred.Activate();

        Assert.Multiple(() =>
        {
            Assert.That(factoryCalls, Is.EqualTo(1));
            Assert.That(inner.ActivationCount, Is.EqualTo(1));
            Assert.That(deferred.HasEstablishedSession, Is.True);
            Assert.That(deferred.LastCommittedTick, Is.EqualTo(709));
            Assert.That(((IPresentationInterpolationSource)deferred).InterpolationAlpha, Is.EqualTo(0.375f));
            Assert.That(deferred.Capture().Role, Is.EqualTo(NetworkProcessRole.ReplicatedClient));
        });
    }

    [Test]
    public void Activate_WhenFactoryReturnsWrongRole_DisposesInnerAndFailsLoudly()
    {
        var inner = new TestServerRuntime();
        using var deferred = new DeferredNetworkRuntimePort(
            NetworkProcessRole.ReplicatedClient,
            () => new DeferredNetworkRuntimeComposition(
                inner,
                new TestFaultInjectionMetrics(NetworkProcessRole.AuthoritativeServer),
                () => { }));

        Assert.That(
            () => deferred.Activate(),
            Throws.InvalidOperationException.With.Message.Contains("expected ReplicatedClient"));
        Assert.That(inner.IsDisposed, Is.True);
    }

    [Test]
    public void Activate_WhenInnerActivationFails_IsTerminalAndNeverPublishesServices()
    {
        int factoryCalls = 0;
        int publishCalls = 0;
        var inner = new TestClientRuntime { ActivationFailure = new InvalidOperationException("activation failed") };
        using var deferred = new DeferredNetworkRuntimePort(
            NetworkProcessRole.ReplicatedClient,
            () =>
            {
                factoryCalls++;
                return new DeferredNetworkRuntimeComposition(
                    inner,
                    new TestFaultInjectionMetrics(NetworkProcessRole.ReplicatedClient),
                    () => publishCalls++);
            });

        Assert.That(
            () => deferred.Activate(),
            Throws.InvalidOperationException.With.Message.EqualTo("activation failed"));
        Assert.That(
            () => deferred.Activate(),
            Throws.InvalidOperationException.With.Message.Contains("cannot retry"));
        Assert.Multiple(() =>
        {
            Assert.That(factoryCalls, Is.EqualTo(1));
            Assert.That(publishCalls, Is.Zero);
            Assert.That(inner.IsDisposed, Is.True);
        });
    }

    [Test]
    public void Capture_WhenMetricsRoleDiffersFromRuntime_FailsLoudly()
    {
        var inner = new TestClientRuntime();
        using var deferred = new DeferredNetworkRuntimePort(
            NetworkProcessRole.ReplicatedClient,
            () => new DeferredNetworkRuntimeComposition(
                inner,
                new TestFaultInjectionMetrics(NetworkProcessRole.AuthoritativeServer),
                () => { }));

        deferred.Activate();

        Assert.That(
            () => deferred.Capture(),
            Throws.InvalidOperationException.With.Message.Contains("does not match runtime role"));
    }

    [Test]
    public void ReadinessDecorator_WritesOnlyAfterDeferredRuntimeIsActivatedAndPumped()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ludots-adapter-readiness-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "client.json");
        using World world = World.Create();
        var inner = new TestClientRuntime();
        var deferred = new DeferredNetworkRuntimePort(
            NetworkProcessRole.ReplicatedClient,
            () => new DeferredNetworkRuntimeComposition(
                inner,
                new TestFaultInjectionMetrics(NetworkProcessRole.ReplicatedClient),
                () => { }));
        var observer = new NetworkRuntimeStateObserver(2, 2, 2);
        var seat = new SessionSeatBinding(0, 1, new PlayerId(1));
        var handshake = SessionHandshakeResponse.Accept(
            in seat,
            new ReconnectToken(1, 2),
            new ProtocolVersion(1, 0),
            ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 1 }),
            new SessionEpoch(709),
            nextClientBatchSequence: 1);
        observer.OnClientHandshake(in handshake);
        try
        {
            using var runtime = new NetworkReadinessArtifactRuntime(world, deferred, observer, path);
            runtime.Activate();
            Assert.That(File.Exists(path), Is.False);

            runtime.PumpTransport();
            Assert.That(File.Exists(path), Is.True);

            runtime.Dispose();
            Assert.That(File.Exists(path), Is.False);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class TestClientRuntime : INetworkRuntimePort, IReplicatedClientRuntimeStatus, IPresentationInterpolationSource
    {
        public NetworkProcessRole Role => NetworkProcessRole.ReplicatedClient;
        public ReplicatedClientConnectionState ConnectionState => ReplicatedClientConnectionState.Connected;
        public bool HasEstablishedSession => true;
        public bool IsAwaitingFullSnapshot => false;
        public bool IsFaulted => false;
        public uint LastCommittedTick { get; set; }
        public float ReconnectWindowRemainingSeconds => 30f;
        public int RoundTripTimeMilliseconds => 12;
        public float InterpolationAlpha { get; set; }
        public int ActivationCount { get; private set; }
        public Exception? ActivationFailure { get; set; }
        public bool IsDisposed { get; private set; }

        public void Activate()
        {
            ActivationCount++;
            if (ActivationFailure != null)
            {
                throw ActivationFailure;
            }
        }
        public void PumpTransport() { }
        public void BeforeAuthoritativeTick(uint executingTick) { }
        public void AfterAuthoritativeCommit(uint committedTick) { }
        public void PumpReplicatedClient(float frameDeltaTime) { }
        public void Dispose() => IsDisposed = true;
    }

    private sealed class TestServerRuntime : INetworkRuntimePort
    {
        public NetworkProcessRole Role => NetworkProcessRole.AuthoritativeServer;
        public bool IsDisposed { get; private set; }

        public void Activate() { }
        public void PumpTransport() { }
        public void BeforeAuthoritativeTick(uint executingTick) { }
        public void AfterAuthoritativeCommit(uint committedTick) { }
        public void PumpReplicatedClient(float frameDeltaTime) { }
        public void Dispose() => IsDisposed = true;
    }

    private sealed class TestFaultInjectionMetrics : INetworkFaultInjectionMetricsPort
    {
        private readonly NetworkProcessRole _role;

        public TestFaultInjectionMetrics(NetworkProcessRole role)
        {
            _role = role;
        }

        public NetworkFaultInjectionObservationSnapshot Capture()
        {
            var configuration = new NetworkFaultInjectionConfigurationSnapshot(
                "test/1",
                "normal",
                seed: 1,
                roundTripLatencyMilliseconds: 0,
                jitterMilliseconds: 0,
                packetLossPermille: 0,
                stateReorderPermille: 0);
            return new NetworkFaultInjectionObservationSnapshot(
                _role,
                in configuration,
                delayedInboundPacketCount: 0,
                droppedInboundPacketCount: 0,
                reorderedInboundStateDatagramCount: 0);
        }
    }
}
