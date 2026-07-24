using Ludots.Adapter.LiteNetLib;
using Ludots.Core.Networking.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.NetworkingAdapter;

[TestFixture]
public sealed class DeferredNetworkRuntimePortTests
{
    [Test]
    public void Runtime_IsCreatedOnceOnFirstUse_AndDisposedByOwner()
    {
        var inner = new TrackingRuntime(NetworkProcessRole.AuthoritativeServer);
        int factoryCalls = 0;
        var deferred = new DeferredNetworkRuntimePort(
            NetworkProcessRole.AuthoritativeServer,
            () =>
            {
                factoryCalls++;
                return inner;
            });

        Assert.That(factoryCalls, Is.Zero);
        deferred.PumpTransport();
        deferred.BeforeAuthoritativeTick(1);
        deferred.AfterAuthoritativeCommit(1);
        deferred.Dispose();
        deferred.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(factoryCalls, Is.EqualTo(1));
            Assert.That(inner.PumpCount, Is.EqualTo(1));
            Assert.That(inner.BeforeTick, Is.EqualTo(1));
            Assert.That(inner.AfterTick, Is.EqualTo(1));
            Assert.That(inner.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void FactoryRoleMismatch_DisposesRejectedRuntimeAndFailsExplicitly()
    {
        var inner = new TrackingRuntime(NetworkProcessRole.ReplicatedClient);
        var deferred = new DeferredNetworkRuntimePort(
            NetworkProcessRole.AuthoritativeServer,
            () => inner);

        Assert.That(
            deferred.PumpTransport,
            Throws.InvalidOperationException.With.Message.Contains("expected AuthoritativeServer"));
        Assert.That(inner.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void DisposeBeforeFirstUse_DoesNotCreateRuntime()
    {
        int factoryCalls = 0;
        var deferred = new DeferredNetworkRuntimePort(
            NetworkProcessRole.ReplicatedClient,
            () =>
            {
                factoryCalls++;
                return new TrackingRuntime(NetworkProcessRole.ReplicatedClient);
            });

        deferred.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(factoryCalls, Is.Zero);
            Assert.That(deferred.PumpTransport, Throws.InstanceOf<ObjectDisposedException>());
        });
    }

    private sealed class TrackingRuntime : INetworkRuntimePort
    {
        public TrackingRuntime(NetworkProcessRole role)
        {
            Role = role;
        }

        public NetworkProcessRole Role { get; }
        public int PumpCount { get; private set; }
        public uint BeforeTick { get; private set; }
        public uint AfterTick { get; private set; }
        public int DisposeCount { get; private set; }

        public void PumpTransport() => PumpCount++;

        public void BeforeAuthoritativeTick(uint executingTick) => BeforeTick = executingTick;

        public void AfterAuthoritativeCommit(uint committedTick) => AfterTick = committedTick;

        public void PumpReplicatedClient(float frameDeltaTime)
        {
        }

        public void Dispose() => DisposeCount++;
    }
}
