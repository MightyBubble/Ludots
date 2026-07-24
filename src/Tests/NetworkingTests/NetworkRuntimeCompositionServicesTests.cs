using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Transport;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class NetworkRuntimeCompositionServicesTests
{
    [Test]
    public void RuntimeObserver_TracksReconnectWindowWithoutAcceptingStaleEvents()
    {
        var observer = new NetworkRuntimeStateObserver(seatCapacity: 2);
        var seat = new SessionSeatBinding(0, 4, new PlayerId(1));
        var stale = new SessionSeatBinding(0, 3, new PlayerId(1));

        observer.OnServerSeatConnected(in seat, reconnected: false);
        Assert.That(observer.GetSeatState(0), Is.EqualTo(NetworkSeatConnectionState.Connected));
        observer.OnServerSeatDisconnected(in seat, TransportDisconnectReason.Timeout);
        Assert.That(observer.GetSeatState(0), Is.EqualTo(NetworkSeatConnectionState.AwaitingReconnect));
        Assert.That(
            () => observer.OnServerSeatReleased(in stale),
            Throws.InvalidOperationException.With.Message.Contains("stale"));
        observer.OnServerSeatReleased(in seat);

        Assert.Multiple(() =>
        {
            Assert.That(observer.GetSeatState(0), Is.EqualTo(NetworkSeatConnectionState.Empty));
            Assert.That(observer.TryGetSeatBinding(0, out _), Is.False);
        });
    }
}
