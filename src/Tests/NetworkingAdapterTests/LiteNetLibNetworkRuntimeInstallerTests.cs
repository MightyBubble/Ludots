using System.Diagnostics.CodeAnalysis;
using Arch.Core;
using Ludots.Adapter.LiteNetLib;
using Ludots.Core.Engine;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Transport;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.NetworkingAdapter;

[TestFixture]
public sealed class LiteNetLibNetworkRuntimeInstallerTests
{
    [Test]
    public void AuthoritativeComposition_MissingResolverOrFactoryFailsExplicitly()
    {
        var engine = new GameEngine();

        Assert.That(
            () => Resolve(engine),
            Throws.InvalidOperationException.With.Message.Contains(
                CoreServiceKeys.AuthoritativeSeatControllerResolver.Name));

        engine.SetService(
            CoreServiceKeys.AuthoritativeSeatControllerResolver,
            (IAuthoritativeSeatControllerResolver)new StubControllerResolver());

        Assert.That(
            () => Resolve(engine),
            Throws.InvalidOperationException.With.Message.Contains(
                CoreServiceKeys.AuthoritativeReplicationSeatRuntimeFactory.Name));
    }

    [Test]
    public void AuthoritativeComposition_UsesProvidedResolverAndFactory()
    {
        var engine = new GameEngine();
        var expectedResolver = new StubControllerResolver();
        var expectedFactory = new StubSeatFactory(
            seatCapacity: 150,
            globalEntityCapacity: 25_000,
            replicationEntityCapacityPerSeat: 2_048);
        engine.SetService(
            CoreServiceKeys.AuthoritativeSeatControllerResolver,
            (IAuthoritativeSeatControllerResolver)expectedResolver);
        engine.SetService(
            CoreServiceKeys.AuthoritativeReplicationSeatRuntimeFactory,
            (IAuthoritativeReplicationSeatRuntimeFactory)expectedFactory);

        LiteNetLibNetworkRuntimeInstaller.ResolveAuthoritativeComposition(
            engine,
            seatCapacity: 150,
            globalEntityCapacity: 25_000,
            replicationEntityCapacityPerSeat: 2_048,
            out IAuthoritativeSeatControllerResolver actualResolver,
            out IAuthoritativeReplicationSeatRuntimeFactory actualFactory);

        Assert.Multiple(() =>
        {
            Assert.That(actualResolver, Is.SameAs(expectedResolver));
            Assert.That(actualFactory, Is.SameAs(expectedFactory));
        });
    }

    [TestCase(149, 25_000, 2_048)]
    [TestCase(150, 24_999, 2_048)]
    [TestCase(150, 25_000, 2_047)]
    public void AuthoritativeComposition_RejectsFactoryCapacityMismatch(
        int seatCapacity,
        int globalEntityCapacity,
        int replicationEntityCapacityPerSeat)
    {
        var engine = new GameEngine();
        engine.SetService(
            CoreServiceKeys.AuthoritativeSeatControllerResolver,
            (IAuthoritativeSeatControllerResolver)new StubControllerResolver());
        engine.SetService(
            CoreServiceKeys.AuthoritativeReplicationSeatRuntimeFactory,
            (IAuthoritativeReplicationSeatRuntimeFactory)new StubSeatFactory(
                seatCapacity,
                globalEntityCapacity,
                replicationEntityCapacityPerSeat));

        Assert.That(
            () => Resolve(engine),
            Throws.InvalidOperationException.With.Message.Contains("capacities do not match"));
    }

    [Test]
    public void RuntimeObserver_MissingBridgeFailsExplicitly()
    {
        var engine = new GameEngine();
        var stateObserver = new NetworkRuntimeStateObserver(seatCapacity: 150);

        Assert.That(
            () => LiteNetLibNetworkRuntimeInstaller.ResolveNetworkRuntimeObserver(engine, stateObserver),
            Throws.InvalidOperationException.With.Message.Contains(
                CoreServiceKeys.NetworkRuntimeObserverBridge.Name));
    }

    [Test]
    public void RuntimeObserver_FansOutBridgeBeforeCommittingState()
    {
        var engine = new GameEngine();
        var bridge = new RecordingObserver();
        var stateObserver = new NetworkRuntimeStateObserver(seatCapacity: 150);
        bridge.StateObserver = stateObserver;
        engine.SetService(
            CoreServiceKeys.NetworkRuntimeObserverBridge,
            (INetworkRuntimeObserver)bridge);
        var seat = new SessionSeatBinding(slot: 0, generation: 1, playerId: new PlayerId(1));

        INetworkRuntimeObserver observer =
            LiteNetLibNetworkRuntimeInstaller.ResolveNetworkRuntimeObserver(engine, stateObserver);
        observer.OnServerSeatConnected(in seat, reconnected: false);

        Assert.Multiple(() =>
        {
            Assert.That(bridge.SeatStateObservedDuringCallback, Is.EqualTo(NetworkSeatConnectionState.Empty));
            Assert.That(stateObserver.GetSeatState(0), Is.EqualTo(NetworkSeatConnectionState.Connected));
            Assert.That(observer, Is.TypeOf<NetworkRuntimeObserverFanout>());
            var fanout = (NetworkRuntimeObserverFanout)observer;
            Assert.That(fanout.StateObserver, Is.SameAs(stateObserver));
            Assert.That(fanout.Bridge, Is.SameAs(bridge));
        });

        bridge.ThrowOnSeatEvent = true;
        Assert.That(
            () => observer.OnServerSeatDisconnected(in seat, TransportDisconnectReason.Timeout),
            Throws.InvalidOperationException.With.Message.EqualTo("bridge rejected seat event"));
        Assert.That(
            stateObserver.GetSeatState(0),
            Is.EqualTo(NetworkSeatConnectionState.Connected));
    }

    private static void Resolve(GameEngine engine)
    {
        LiteNetLibNetworkRuntimeInstaller.ResolveAuthoritativeComposition(
            engine,
            seatCapacity: 150,
            globalEntityCapacity: 25_000,
            replicationEntityCapacityPerSeat: 2_048,
            out _,
            out _);
    }

    private sealed class StubControllerResolver : IAuthoritativeSeatControllerResolver
    {
        public bool TryResolveController(in SessionSeatBinding seat, out Entity controller)
        {
            controller = Entity.Null;
            return false;
        }
    }

    private sealed class StubSeatFactory : IAuthoritativeReplicationSeatRuntimeFactory
    {
        public StubSeatFactory(
            int seatCapacity,
            int globalEntityCapacity,
            int replicationEntityCapacityPerSeat)
        {
            SeatCapacity = seatCapacity;
            GlobalEntityCapacity = globalEntityCapacity;
            ReplicationEntityCapacityPerSeat = replicationEntityCapacityPerSeat;
        }

        public int SeatCapacity { get; }

        public int GlobalEntityCapacity { get; }

        public int ReplicationEntityCapacityPerSeat { get; }

        public bool TryAcquire(
            in SessionSeatBinding seat,
            Entity viewer,
            [NotNullWhen(true)] out AuthoritativeReplicationSeatRuntime? runtime)
        {
            runtime = null;
            return false;
        }

        public bool TryRelease(
            in SessionSeatBinding seat,
            AuthoritativeReplicationSeatRuntime runtime) => false;
    }

    private sealed class RecordingObserver : INetworkRuntimeObserver
    {
        public NetworkRuntimeStateObserver? StateObserver { get; set; }

        public NetworkSeatConnectionState SeatStateObservedDuringCallback { get; private set; }

        public bool ThrowOnSeatEvent { get; set; }

        public void OnFault(in NetworkRuntimeFault fault)
        {
        }

        public void OnServerSeatConnected(in SessionSeatBinding seat, bool reconnected)
        {
            SeatStateObservedDuringCallback = StateObserver?.GetSeatState(seat.Slot) ??
                NetworkSeatConnectionState.Empty;
            ThrowIfRequested();
        }

        public void OnServerSeatDisconnected(
            in SessionSeatBinding seat,
            TransportDisconnectReason reason)
        {
            SeatStateObservedDuringCallback = StateObserver?.GetSeatState(seat.Slot) ??
                NetworkSeatConnectionState.Empty;
            ThrowIfRequested();
        }

        public void OnServerSeatReleased(in SessionSeatBinding seat)
        {
            SeatStateObservedDuringCallback = StateObserver?.GetSeatState(seat.Slot) ??
                NetworkSeatConnectionState.Empty;
            ThrowIfRequested();
        }

        public void OnClientHandshake(in SessionHandshakeResponse response)
        {
        }

        public void OnClientAdmission(in Ludots.Core.Networking.Commands.NetworkCommandAdmissionOutcome outcome)
        {
        }

        public void OnClientResyncRequired(in NetworkResyncRequired message)
        {
        }

        private void ThrowIfRequested()
        {
            if (ThrowOnSeatEvent)
            {
                throw new InvalidOperationException("bridge rejected seat event");
            }
        }
    }
}
